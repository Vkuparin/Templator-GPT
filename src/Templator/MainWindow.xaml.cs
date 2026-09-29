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

    public MainWindow(Workspace workspace, IMailPlatform? platform = null)
    {
        Workspace = workspace;
        mail = new MailHandoff(platform ?? new WindowsMailPlatform());
        InitializeComponent();
        WindowTheme.Apply(this);
        DataContext = workspace;
        workspace.PreviewChanged += PreviewChanged;
        WindowPlacement.Restore(this, workspace.DataFolder);
        RefreshPreview();
    }

    private void PreviewChanged(object? sender, EventArgs e) => RefreshPreview();
    private void RefreshPreview()
    {
        refreshing = true;
        try
        {
            BodyPreview.Render(Workspace.Selected);
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

    private void NewTemplate(object sender, RoutedEventArgs e) { Workspace.NewTemplate(); NameEditor.Focus(); }
    private void Duplicate(object sender, RoutedEventArgs e) => Workspace.Duplicate();
    private void MoveUp(object sender, RoutedEventArgs e) => Workspace.Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Workspace.Move(1);
    private void DeleteTemplate(object sender, RoutedEventArgs e)
    {
        if (Workspace.Selected != null && Confirm("Delete this template?", $"“{Workspace.Selected.Name}” and its saved values will be removed. Export a backup first if you need to keep them.", "Delete template")) Workspace.Delete();
    }
    private void ResetValues(object sender, RoutedEventArgs e)
    {
        if (Workspace.HasSelection && Confirm("Start with a clean form?", "Clear all saved values for this template. Its text and field definitions will stay.", "Reset values")) Workspace.Reset();
    }
    private void AddVariable(object sender, RoutedEventArgs e) => Workspace.AddVariable();
    private void DeleteVariable(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Variable variable } && Confirm("Remove this variable?", $"Remove {variable.Key} and its saved value. If its placeholder is still used, a new definition will be created.", "Remove variable")) Workspace.DeleteVariable(variable);
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
        if (!Workspace.CanOpen || Workspace.Draft is not { } draft) return;
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
            case Key.N: Workspace.NewTemplate(); break;
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
