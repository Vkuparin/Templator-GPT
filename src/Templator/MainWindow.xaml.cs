using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Templator.Core;
using Templator.Services;
using Templator.ViewModels;
using Templator.Views;

namespace Templator;

public partial class MainWindow : Window
{
    public Workspace Workspace { get; }
    private readonly MailHandoff mail;
    private bool refreshing;
    private TokenEditor? activeEditor;
    private Guid? editingTemplate;
    private Point dragStart;
    private bool dragged;
    private long lastDragScroll;

    public MainWindow(Workspace workspace, IMailPlatform? platform = null)
    {
        Workspace = workspace;
        mail = new MailHandoff(platform ?? new WindowsMailPlatform());
        InitializeComponent();
        WindowTheme.Apply(this);
        DataContext = workspace;
        WireEditor(ToEditor, t => t.To, (t, value) => t.To = value);
        WireEditor(CcEditor, t => t.Cc, (t, value) => t.Cc = value);
        WireEditor(SubjectEditor, t => t.Subject, (t, value) => t.Subject = value);
        WireEditor(BodyEditor, t => t.Body, (t, value) => t.Body = value);
        workspace.PreviewChanged += PreviewChanged;
        WindowPlacement.Restore(this, workspace.DataFolder);
        RefreshPreview();
    }

    private void PreviewChanged(object? sender, EventArgs e) => RefreshPreview();
    private void WireEditor(TokenEditor editor, Func<Template, string> read, Action<Template, string> write)
    {
        editor.SourceChanged += (_, _) =>
        {
            if (!refreshing && Workspace.Selected is { } template && read(template) != editor.Source)
                write(template, editor.Source);
        };
        editor.GotKeyboardFocus += (_, _) => activeEditor = editor;
        editor.VariableActivated += EditValue;
        editor.Message += text => Workspace.Notice = text;
    }
    private void RefreshPreview()
    {
        refreshing = true;
        try
        {
            var template = Workspace.Selected;
            if (editingTemplate != template?.Id) { activeEditor = BodyEditor; editingTemplate = template?.Id; }
            ToEditor.LoadTemplate(template, template?.To ?? "");
            CcEditor.LoadTemplate(template, template?.Cc ?? "");
            SubjectEditor.LoadTemplate(template, template?.Subject ?? "");
            BodyEditor.LoadTemplate(template, template?.Body ?? "");
            EnglishRadio.IsChecked = Workspace.Selected?.Language == "en";
            FinnishRadio.IsChecked = Workspace.Selected?.Language == "fi";
        }
        finally { refreshing = false; }
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException or FormatException)
        { Workspace.Notice = e.Message; }
    }

    private bool Confirm(string title, string text, string action)
    {
        var dialog = new ChoiceDialog(this, title, text, new DialogChoice("confirm", action, "This changes your local workspace."));
        return dialog.ShowDialog() == true && dialog.Choice == "confirm";
    }

    private void NewTemplate(object sender, RoutedEventArgs e) { Workspace.NewTemplate(); NameEditor.Focus(); NameEditor.SelectAll(); }
    private void Duplicate(object sender, RoutedEventArgs e) => Workspace.Duplicate();
    private void MoveUp(object sender, RoutedEventArgs e) => Workspace.Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Workspace.Move(1);
    private void DeleteTemplate(object sender, RoutedEventArgs e)
    {
        if (Workspace.Selected != null && Confirm("Delete this template?", $"“{Workspace.Selected.Name}” and its saved values will be removed. Export a backup first if you need to keep them.", "Delete template")) Workspace.Delete();
    }
    private void ResetValues(object sender, RoutedEventArgs e)
    {
        Workspace.Reset();
    }
    private void UndoReset(object sender, RoutedEventArgs e) => Workspace.UndoReset();
    private void AddVariable(object sender, RoutedEventArgs e)
    {
        if (Workspace.Selected is not { } template) return;
        var dialog = new VariableDialog(this, template);
        if (dialog.ShowDialog() == true) Workspace.AddVariable(dialog.VariableName, dialog.Example, dialog.Required);
    }
    private void EditVariable(object sender, RoutedEventArgs e)
    {
        if (Workspace.Selected is not { } template || sender is not Button { Tag: Variable variable }) return;
        var dialog = new VariableDialog(this, template, variable);
        if (dialog.ShowDialog() != true) return;
        if (dialog.DeleteRequested)
        {
            if (Confirm("Delete this variable?", $"“{variable.Label}” will be removed from the variable list and everywhere it appears in this template. Its saved value will also be cleared.", "Delete variable")) Workspace.DeleteVariable(variable);
            return;
        }
        variable.Label = dialog.VariableName; variable.Example = dialog.Example; variable.Required = dialog.Required;
    }
    private void EditValue(string key)
    {
        var variable = Workspace.Selected?.Variables.FirstOrDefault(v => v.Key == key);
        if (variable == null) return;
        var dialog = new VariableValueDialog(this, variable);
        if (dialog.ShowDialog() == true) variable.Value = dialog.Value;
    }
    private void InsertVariable(object sender, RoutedEventArgs e)
    {
        if (!dragged && sender is Button { Tag: Variable variable }) (activeEditor ?? BodyEditor).InsertVariable(variable.Key);
    }
    private void ChipMouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this); dragged = false;
    }
    private void ChipMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragged || sender is not Button { Tag: Variable variable } button || Workspace.Selected is not { } template) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragged = true; e.Handled = true;
        DragDrop.DoDragDrop(button, TokenEditor.DragData(template, variable), DragDropEffects.Copy);
        // A release after a drag must not also insert through Button.Click.
        Dispatcher.BeginInvoke(() => dragged = false, System.Windows.Threading.DispatcherPriority.Background);
    }
    private void ScrollWhileDragging(object sender, DragEventArgs e)
    {
        if (sender is not ScrollViewer scroll || !BodyEditor.CanDrop(e.Data) || Environment.TickCount64 - lastDragScroll < 40) return;
        var y = e.GetPosition(scroll).Y;
        if (y < 32) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 16);
        else if (y > scroll.ActualHeight - 32) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 16);
        lastDragScroll = Environment.TickCount64;
    }
    private void LanguageChanged(object sender, RoutedEventArgs e)
    {
        if (!refreshing && Workspace.Selected != null) Workspace.Selected.Language = ReferenceEquals(sender, FinnishRadio) ? "fi" : "en";
    }
    private void DismissNotice(object sender, RoutedEventArgs e) => Workspace.Notice = "";
    private void CopyBody(object sender, RoutedEventArgs e)
    {
        if (Workspace.Draft is { } draft) Workspace.Notice = mail.Copy(MailDraft.NormalizeLines(draft.Body));
    }
    private void CopyFull(object sender, RoutedEventArgs e)
    {
        if (Workspace.Draft is { } draft) Workspace.Notice = mail.Copy(draft.FullText());
    }
    private void Settings(object sender, RoutedEventArgs e)
    {
        if (new SettingsWindow(this, Workspace.Settings, Workspace.DataFolder).ShowDialog() == true)
        { Workspace.Changed(); Workspace.Flush(); }
    }
    private void Import(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Import templates as copies", Filter = "Templator backup (*.json;*.bak)|*.json;*.bak|All files|*.*" };
        if (picker.ShowDialog(this) == true) Run(() => Workspace.Import(picker.FileName));
    }
    private void Export(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = "Export library including saved values", Filter = "Templator backup (*.json)|*.json", FileName = "templator-backup-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json", DefaultExt = ".json" };
        if (picker.ShowDialog(this) == true) Run(() =>
        {
            if (Path.GetFullPath(picker.FileName).Equals(Path.Combine(Workspace.DataFolder, "templates.json"), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a backup location outside the active store file.");
            Workspace.Export(picker.FileName); Workspace.Notice = "Library exported, including saved field values.";
        });
    }

    private void OpenDraft(object sender, RoutedEventArgs e) => Run(() =>
    {
        Workspace.Refresh();
        if (!Workspace.CanOpen || Workspace.Draft is not { } draft)
        { Workspace.Notice = "Draft not opened. " + Workspace.Validation; return; }
        if (!Workspace.Flush()) return;
        if (draft.Mailto().Length <= Workspace.Settings.MailtoLengthThreshold)
        { Workspace.Notice = mail.Open(draft); return; }
        var headersFit = draft.Mailto(false).Length <= Workspace.Settings.MailtoLengthThreshold;
        var dialog = new ChoiceDialog(this, "This draft needs a little more room.",
            "Long mail links can be truncated by Windows or your mail app. Choose how to carry the full message across.",
            new("clipboard", "Copy body + open draft", headersFit ? "Recommended · paste the message into the opened draft." : "Unavailable: even the headers exceed your URL threshold.", headersFit),
            new("eml", "Save an .eml draft", "Preserves the full message. Editing support varies by mail client."),
            new("anyway", "Open full link anyway", "The message may be truncated. Check every field before sending."));
        if (dialog.ShowDialog() != true) return;
        switch (dialog.Choice)
        {
            case "clipboard": Workspace.Notice = mail.Open(draft, headersOnly: true); break;
            case "anyway": Workspace.Notice = mail.Open(draft); break;
            case "eml":
                var picker = new SaveFileDialog { Title = "Save email draft", Filter = "Email draft (*.eml)|*.eml", FileName = "draft-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".eml", DefaultExt = ".eml" };
                if (picker.ShowDialog(this) == true)
                {
                    Workspace.Notice = mail.SaveFile(draft, picker.FileName);
                }
                break;
        }
    });

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.F: SearchBox.Focus(); SearchBox.SelectAll(); break;
            case Key.N: NewTemplate(this, new RoutedEventArgs()); break;
            case Key.S: Workspace.Flush(); break;
            case Key.Enter: OpenDraft(this, new RoutedEventArgs()); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!Workspace.Flush() && !Confirm("Changes could not be saved", "Keep this window open to retry saving or export a backup. Close only if you want to discard unsaved changes.", "Discard changes and close"))
        { e.Cancel = true; return; }
        WindowPlacement.Save(this, Workspace.DataFolder);
        Workspace.PreviewChanged -= PreviewChanged;
        Workspace.Dispose();
    }
}
