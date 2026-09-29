using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Templator.Core;

namespace Templator.Views;

public sealed record VariableDrag(Guid TemplateId, string Key);
public sealed record EditorClipboard(Guid TemplateId, string Source);

/// <summary>
/// Plain-text email editor with atomic, semantic variable chips. The document is
/// a projection of template source; visible values are never written over keys.
/// </summary>
public sealed class TokenEditor : RichTextBox
{
    public const string VariableFormat = "Templator.Variable.v1";
    public const string FragmentFormat = "Templator.Fragment.v1";
    private Template? template;
    private bool loading;
    public bool SingleLine { get; set; }
    public event EventHandler? SourceChanged;
    public event Action<string>? VariableActivated;
    public event Action<string>? Message;
    public string Source => Extract(Document.ContentStart, Document.ContentEnd, false);

    public TokenEditor()
    {
        IsReadOnly = false; IsDocumentEnabled = true; AllowDrop = true;
        IsInactiveSelectionHighlightEnabled = true;
        BorderThickness = new Thickness(0, 0, 0, 1);
        BorderBrush = new SolidColorBrush(Color.FromRgb(219, 223, 213));
        Padding = new Thickness(2, 6, 2, 6); MinHeight = 38;
        Background = Brushes.Transparent;
        Foreground = new SolidColorBrush(Color.FromRgb(39, 49, 47));
        CaretBrush = Foreground;
        SelectionBrush = new SolidColorBrush(Color.FromRgb(161, 207, 181));
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 15;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document = NewDocument();
        TextChanged += (_, _) => { if (!loading) SourceChanged?.Invoke(this, EventArgs.Empty); };
        SizeChanged += (_, _) => RefreshChips();
        GotKeyboardFocus += (_, _) => BorderBrush = new SolidColorBrush(Color.FromRgb(86, 141, 106));
        LostKeyboardFocus += (_, _) => BorderBrush = new SolidColorBrush(Color.FromRgb(219, 223, 213));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { CopySelection(false); e.Handled = true; }, CanCopy));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => { CopySelection(true); e.Handled = true; }, CanCopy));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => { PasteClipboard(); e.Handled = true; }, (_, e) => { e.CanExecute = true; e.Handled = true; }));
        foreach (var command in new[]
        {
            EditingCommands.ToggleBold, EditingCommands.ToggleItalic, EditingCommands.ToggleUnderline,
            EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering, EditingCommands.IncreaseFontSize,
            EditingCommands.DecreaseFontSize, EditingCommands.AlignLeft, EditingCommands.AlignCenter,
            EditingCommands.AlignRight, EditingCommands.AlignJustify, EditingCommands.IncreaseIndentation,
            EditingCommands.DecreaseIndentation
        }) CommandBindings.Add(new CommandBinding(command, (_, e) => e.Handled = true,
            (_, e) => { e.CanExecute = false; e.Handled = true; }));
        // Never allow WPF to deserialize foreign rich content or embedded UI.
        DataObject.AddPastingHandler(this, (_, e) => e.CancelCommand());
        // Use a routed handler: WPF undo can recreate an inline control without
        // restoring CLR event subscriptions attached to that control.
        AddHandler(Button.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is Button { Tag: string key }) { VariableActivated?.Invoke(key); e.Handled = true; }
        }));
    }

    private FlowDocument NewDocument() => new(new Paragraph { Margin = new Thickness(0), LineHeight = SingleLine ? double.NaN : 27 })
    { PagePadding = new Thickness(0), FontFamily = FontFamily, FontSize = FontSize };

    public void LoadTemplate(Template? current, string source)
    {
        source = Normalize(source);
        var canonical = VariableSyntax.Pattern().Replace(source, match => "{{" + match.Groups[1].Value + "}}");
        var switched = template?.Id != current?.Id;
        template = current;
        // Updating a field value or saving a keystroke must not reset selection or undo.
        if (!switched && Source == canonical) { RefreshChips(); return; }
        loading = true;
        try
        {
            IsUndoEnabled = false;
            Document = NewDocument();
            var paragraph = (Paragraph)Document.Blocks.FirstBlock;
            AppendSource(paragraph.Inlines, source);
            CaretPosition = Document.ContentStart;
            IsUndoEnabled = true;
        }
        finally { loading = false; }
    }

    public void InsertVariable(string key, TextPointer? position = null)
    {
        if (template == null || !template.Variables.Any(v => v.Key == key)) return;
        if (position != null) Selection.Select(position, position);
        ReplaceSelection("{{" + key + "}}");
    }

    public void ReplaceSelection(string source)
    {
        source = Normalize(source);
        if (SingleLine) source = source.Replace('\n', ' ');
        if (Source.Length + source.Length - Extract(Selection.Start, Selection.End, false).Length > 200000)
        { Message?.Invoke("This field has reached its 200,000-character limit."); return; }
        BeginChange();
        try
        {
            Selection.Text = "";
            var position = Selection.Start.GetInsertionPosition(LogicalDirection.Forward);
            if (position == null) return;
            foreach (var part in Parts(source))
            {
                Inline inline = part.Key == null ? new Run(part.Text, position) : new InlineUIContainer(CreateChip(part.Key), position) { BaselineAlignment = BaselineAlignment.Center };
                position = inline.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
                if (position == null) break;
            }
            if (position != null) Selection.Select(position, position);
        }
        finally { EndChange(); }
        Focus();
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    private static IEnumerable<(string Text, string? Key)> Parts(string source)
    {
        var offset = 0;
        foreach (System.Text.RegularExpressions.Match match in VariableSyntax.Pattern().Matches(source))
        {
            if (match.Index > offset) yield return (source[offset..match.Index], null);
            yield return ("", match.Groups[1].Value);
            offset = match.Index + match.Length;
        }
        if (offset < source.Length) yield return (source[offset..], null);
    }

    private void AppendSource(InlineCollection inlines, string source)
    {
        foreach (var part in Parts(source))
            inlines.Add(part.Key == null ? new Run(part.Text) : new InlineUIContainer(CreateChip(part.Key)) { BaselineAlignment = BaselineAlignment.Center });
    }

    private Button CreateChip(string key)
    {
        var button = new Button { Tag = key, Focusable = false, Cursor = Cursors.Hand, Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(1, 0, 1, 0) };
        UpdateChip(button);
        return button;
    }

    private void UpdateChip(Button button)
    {
        var key = (string)button.Tag;
        var variable = template?.Variables.FirstOrDefault(v => v.Key == key);
        var label = string.IsNullOrWhiteSpace(variable?.Label) ? VariableSyntax.Label(key) : variable.Label;
        var empty = string.IsNullOrWhiteSpace(variable?.Value);
        var missing = empty && variable?.Required != false;
        button.Content = new TextBlock
        {
            Text = empty ? label : variable!.Value, TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily, FontSize = FontSize, FontWeight = FontWeight,
            MaxWidth = Math.Max(80, ActualWidth - 42)
        };
        button.Background = new SolidColorBrush(missing ? Color.FromRgb(249, 229, 194) : Color.FromRgb(224, 237, 224));
        button.Foreground = new SolidColorBrush(missing ? Color.FromRgb(113, 76, 28) : Color.FromRgb(38, 81, 52));
        button.BorderBrush = new SolidColorBrush(missing ? Color.FromRgb(216, 187, 143) : Color.FromRgb(186, 211, 188));
        button.ToolTip = label + " · click to change its value";
        AutomationProperties.SetName(button, label + ": " + (empty ? "Not filled" : variable!.Value));
    }

    private void RefreshChips()
    {
        foreach (var paragraph in Document.Blocks.OfType<Paragraph>())
            foreach (var inline in Descendants(paragraph.Inlines))
                if (inline is InlineUIContainer { Child: Button button }) UpdateChip(button);
    }

    private static IEnumerable<Inline> Descendants(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            { foreach (var child in Descendants(span.Inlines)) yield return child; }
            else yield return inline;
        }
    }

    private string Extract(TextPointer start, TextPointer end, bool resolved)
    {
        var result = new StringBuilder();
        var paragraphs = Document.Blocks.OfType<Paragraph>().ToArray();
        for (var index = 0; index < paragraphs.Length; index++)
        {
            var paragraph = paragraphs[index];
            foreach (var inline in Descendants(paragraph.Inlines))
            {
                if (start.CompareTo(inline.ElementEnd) >= 0 || end.CompareTo(inline.ElementStart) <= 0) continue;
                switch (inline)
                {
                    case Run run:
                        var from = start.CompareTo(run.ContentStart) > 0 ? start : run.ContentStart;
                        var to = end.CompareTo(run.ContentEnd) < 0 ? end : run.ContentEnd;
                        if (from.CompareTo(to) < 0) result.Append(new TextRange(from, to).Text);
                        break;
                    case LineBreak: result.Append('\n'); break;
                    case InlineUIContainer { Child: Button { Tag: string key } }:
                        result.Append(resolved ? template?.Variables.FirstOrDefault(v => v.Key == key)?.Value ?? "" : "{{" + key + "}}");
                        break;
                }
            }
            if (index < paragraphs.Length - 1 && start.CompareTo(paragraph.ContentEnd) <= 0 && end.CompareTo(paragraphs[index + 1].ContentStart) >= 0)
                result.Append('\n');
        }
        return Normalize(result.ToString());
    }

    public IDataObject SelectionData()
    {
        var data = new DataObject();
        data.SetText(Extract(Selection.Start, Selection.End, true));
        if (template != null) data.SetData(FragmentFormat, JsonSerializer.Serialize(new EditorClipboard(template.Id, Extract(Selection.Start, Selection.End, false))), false);
        return data;
    }

    public void PasteData(IDataObject data)
    {
        if (template != null && ReadPayload<EditorClipboard>(data, FragmentFormat) is { } fragment && fragment.TemplateId == template.Id && fragment.Source != null && fragment.Source.Length <= 200000)
        {
            // Definitions may have been deleted since copying. Do not resurrect them.
            var keys = VariableSyntax.Pattern().Matches(fragment.Source).Select(m => m.Groups[1].Value);
            if (keys.All(key => template.Variables.Any(v => v.Key == key))) { ReplaceSelection(fragment.Source); return; }
        }
        if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text) ReplaceSelection(text);
    }

    private void CopySelection(bool cut)
    {
        try
        {
            Clipboard.SetDataObject(SelectionData(), true);
            if (cut) Selection.Text = "";
        }
        catch (ExternalException) { Message?.Invoke("Clipboard is busy. Try again; your text is still here."); }
    }
    private void PasteClipboard()
    {
        try { if (Clipboard.GetDataObject() is { } data) PasteData(data); }
        catch (ExternalException) { Message?.Invoke("Clipboard is busy. Please try again."); }
    }
    private void CanCopy(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = !Selection.IsEmpty; e.Handled = true; }

    public static IDataObject DragData(Template template, Variable variable)
    {
        var data = new DataObject();
        data.SetData(VariableFormat, JsonSerializer.Serialize(new VariableDrag(template.Id, variable.Key)), false);
        return data;
    }
    private static T? ReadPayload<T>(IDataObject data, string format) where T : class
    {
        if (!data.GetDataPresent(format, false) || data.GetData(format, false) is not string json || json.Length > 1200000) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return null; }
    }
    public bool CanDrop(IDataObject data) => template != null && ReadPayload<VariableDrag>(data, VariableFormat) is { } payload &&
        payload.TemplateId == template.Id && template.Variables.Any(v => v.Key == payload.Key);

    public bool DropVariable(IDataObject data, TextPointer? position = null)
    {
        if (!CanDrop(data)) return false;
        var payload = ReadPayload<VariableDrag>(data, VariableFormat)!;
        InsertVariable(payload.Key, position ?? CaretPosition);
        return true;
    }
    protected override void OnPreviewDragEnter(DragEventArgs e) => ShowDrop(e);
    protected override void OnPreviewDragOver(DragEventArgs e) => ShowDrop(e);
    private void ShowDrop(DragEventArgs e)
    {
        e.Handled = true; e.Effects = CanDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects == DragDropEffects.None) return;
        Focus();
        var position = GetPositionFromPoint(e.GetPosition(this), true);
        if (position != null) CaretPosition = position;
    }
    protected override void OnPreviewDrop(DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DropVariableAt(e.Data, e.GetPosition(this)) ? DragDropEffects.Copy : DragDropEffects.None;
    }
    public bool DropVariableAt(IDataObject data, Point point) => DropVariable(data, GetPositionFromPoint(point, true));
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (SingleLine && e.Key == Key.Enter) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
}
