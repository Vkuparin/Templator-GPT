using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Templator;
using Templator.Core;
using Templator.ViewModels;
using Templator.Views;

internal static class EditorChecks
{
    public static void Run(MainWindow window, Workspace workspace, Application app,
        Action<string, Action> check, Action pump, Action<Window, string> capture, string output)
    {
        var original = workspace.Selected!;
        workspace.NewTemplate();
        var template = workspace.Selected!;
        var body = (TokenEditor)window.FindName("BodyEditor");
        var subject = (TokenEditor)window.FindName("SubjectEditor");
        var to = (TokenEditor)window.FindName("ToEditor");
        var cc = (TokenEditor)window.FindName("CcEditor");
        pump();
        void Replace(TokenEditor editor, string text) { editor.SelectAll(); editor.ReplaceSelection(text); pump(); }
        Variable? field = null;

        check("New templates start as blank editable emails, without technical fields", () =>
            Require(template.Variables.Count == 0 && template.Subject == "" && template.Body == ""));
        check("All four email fields edit the template directly", () =>
        {
            Replace(to, "team@example.com"); Replace(cc, "boss@example.com");
            Replace(subject, "An editable subject"); Replace(body, "Hello, world.");
            Require(template.To == "team@example.com" && template.Cc == "boss@example.com" &&
                template.Subject == "An editable subject" && template.Body == "Hello, world.");
        });
        check("The New variable dialog creates a field by ordinary name", () =>
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                var dialog = app.Windows.OfType<VariableDialog>().Single();
                dialog.NameInput.Text = "Customer name"; dialog.ExampleInput.Text = "Acme Oy";
                capture(dialog, Path.Combine(output, "create-variable.png"));
                Descendants<Button>(dialog).Single(b => Equals(b.Content, "Create variable")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.ApplicationIdle);
            ((Button)window.FindName("AddVariableButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            field = template.Variables.Single();
            Require(field.Label == "Customer name" && field.Key == "customer_name" && !field.Referenced && workspace.CanOpen);
        });
        check("Dropping a palette variable inserts a semantic chip between words", () =>
        {
            var run = ((Paragraph)body.Document.Blocks.FirstBlock).Inlines.OfType<Run>().Single();
            var position = run.ContentStart.GetPositionAtOffset(7)!;
            var rectangle = position.GetCharacterRect(LogicalDirection.Forward);
            Require(body.DropVariableAt(TokenEditor.DragData(template, field!), new Point(rectangle.Left + 0.1, rectangle.Top + rectangle.Height / 2))); pump();
            Require(template.Body == "Hello, {{customer_name}}world.", template.Body);
            var chip = Chips(body).Single();
            Require(chip.Child is Button { IsEnabled: true } button && ((TextBlock)button.Content).Text == "Customer name");
            Require(!workspace.CanOpen);
        });
        check("Filling a chip preserves its identity, caret, document, and undo", () =>
        {
            var document = body.Document;
            var caret = document.ContentStart.GetOffsetToPosition(body.CaretPosition);
            field!.Value = "Acme Oy"; pump();
            Require(ReferenceEquals(document, body.Document));
            Require(caret == document.ContentStart.GetOffsetToPosition(body.CaretPosition));
            Require(template.Body == "Hello, {{customer_name}}world." && workspace.Draft!.Body == "Hello, Acme Oyworld.");
            Require(body.CanUndo && workspace.CanOpen);
        });
        check("Undo and redo restore chip identity without losing its definition", () =>
        {
            body.Undo(); pump(); Require(template.Body == "Hello, world.", template.Body);
            Require(template.Variables.Contains(field!) && !field!.Referenced);
            body.Redo(); pump(); Require(template.Body == "Hello, {{customer_name}}world.", template.Body);
            Require(Chips(body).Single().Child is Button { Tag: "customer_name" });
        });
        check("An undo-restored chip still opens the value editor", () =>
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                var dialog = app.Windows.OfType<VariableValueDialog>().Single();
                dialog.ValueInput.Text = "New customer Oy";
                Descendants<Button>(dialog).Single(b => Equals(b.Content, "Use value")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.ApplicationIdle);
            ((Button)Chips(body).Single().Child).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(field!.Value == "New customer Oy" && workspace.Draft!.Body.Contains("New customer Oy"));
        });
        check("Deleting and undoing a selected chip is atomic", () =>
        {
            var chip = Chips(body).Single(); body.Selection.Select(chip.ElementStart, chip.ElementEnd);
            body.Selection.Text = ""; pump(); Require(template.Body == "Hello, world.", template.Body);
            body.Undo(); pump(); Require(template.Body == "Hello, {{customer_name}}world.", template.Body);
        });
        check("Internal copy/paste retains chips while external text is resolved", () =>
        {
            body.SelectAll(); var data = body.SelectionData();
            Require((string)data.GetData(DataFormats.UnicodeText) == "Hello, New customer Oyworld.");
            body.ReplaceSelection(""); body.PasteData(data); pump();
            Require(template.Body == "Hello, {{customer_name}}world.", template.Body);
            Require(Chips(body).Count() == 1);
        });
        check("Backspace at a chip boundary removes it as one unit and undo restores it", () =>
        {
            var chip = Chips(body).Single(); body.CaretPosition = chip.ElementEnd.GetInsertionPosition(LogicalDirection.Forward)!;
            EditingCommands.Backspace.Execute(null, body); pump();
            Require(template.Body == "Hello, world.", template.Body);
            body.Undo(); pump(); Require(template.Body == "Hello, {{customer_name}}world.", template.Body);
        });
        check("Partial text selection copies only the selected characters", () =>
        {
            var first = ((Paragraph)body.Document.Blocks.FirstBlock).Inlines.OfType<Run>().First();
            body.Selection.Select(first.ContentStart, first.ContentStart.GetPositionAtOffset(5)!);
            Require((string)body.SelectionData().GetData(DataFormats.UnicodeText) == "Hello");
        });
        check("External paste uses only plain text and normalizes header newlines", () =>
        {
            var data = new DataObject(); data.SetText("Finnish äö 🌿\r\nsecond line"); data.SetData(DataFormats.Rtf, "{\\rtf1 UNWANTED}");
            body.SelectAll(); body.PasteData(data); pump();
            Require(template.Body == "Finnish äö 🌿\nsecond line", template.Body);
            subject.SelectAll(); subject.PasteData(data); pump();
            Require(template.Subject == "Finnish äö 🌿 second line", template.Subject);
            Replace(body, "Hello, world."); Replace(subject, "Subject");
        });
        check("Paragraph and soft line breaks survive the document round trip", () =>
        {
            Replace(body, "First");
            var next = body.CaretPosition.InsertParagraphBreak();
            new Run("Second", next); pump();
            Require(template.Body == "First\nSecond", template.Body);
            body.CaretPosition = body.Document.ContentEnd;
            new LineBreak(body.CaretPosition); pump();
            Require(template.Body == "First\nSecond\n", template.Body);
            var saved = template.Body; workspace.Selected = original; workspace.Selected = template; pump();
            Require(body.Source == saved, body.Source);
        });
        check("Clicking a palette name inserts at the last email cursor position", () =>
        {
            Replace(body, "Before after");
            var run = ((Paragraph)body.Document.Blocks.FirstBlock).Inlines.OfType<Run>().Single();
            body.CaretPosition = run.ContentStart.GetPositionAtOffset(7)!; body.Focus(); pump();
            var palette = (ItemsControl)window.FindName("ValueInputs");
            var button = Descendants<Button>(palette).Single(b => ReferenceEquals(b.Tag, field) && b.Content is StackPanel);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump();
            Require(template.Body == "Before {{customer_name}}after", template.Body);
        });
        check("The same field can be dropped into subject and both recipient fields", () =>
        {
            var data = TokenEditor.DragData(template, field!);
            foreach (var editor in new[] { subject, to, cc })
            { editor.SelectAll(); editor.ReplaceSelection(""); Require(editor.DropVariable(data, editor.Document.ContentStart)); }
            pump();
            Require(template.To == "{{customer_name}}" && template.Cc == "{{customer_name}}" && template.Subject == "{{customer_name}}");
            field!.Value = "person@example.com"; pump();
            Require(workspace.CanOpen && workspace.Draft!.To == "person@example.com" && workspace.Draft.Subject == "person@example.com");
        });
        check("Renaming a field updates all labels without changing stored references", () =>
        {
            field!.Label = "Client"; field.Value = ""; pump();
            foreach (var editor in new[] { body, subject, to, cc })
                Require(Chips(editor).All(c => c.Child is Button button && ((TextBlock)button.Content).Text == "Client"));
            Require(template.Body.Contains("{{customer_name}}"));
            field.Value = "person@example.com";
        });
        check("Template switches reject stale drags and clear undo history", () =>
        {
            var data = TokenEditor.DragData(original, original.Variables[0]);
            var before = template.Body;
            Require(!body.DropVariable(data)); Require(template.Body == before);
            workspace.Selected = original; workspace.Selected = template; pump();
            Require(!body.CanUndo);
        });
        check("Cross-template paste uses visible text instead of foreign field keys", () =>
        {
            body.SelectAll(); var copied = body.SelectionData();
            workspace.Selected = original; var old = original.Body;
            body.SelectAll(); body.PasteData(copied); pump();
            Require(original.Body == "Before person@example.comafter", original.Body);
            original.Body = old; workspace.Selected = template; pump();
        });
        check("Stored templates keep semantic references after visual editing", () =>
        {
            Require(workspace.Flush());
            var loaded = TemplateStore.Read(Path.Combine(workspace.DataFolder, "templates.json")).Templates.Single(t => t.Id == template.Id);
            Require(loaded.Body == template.Body && loaded.Subject == "{{customer_name}}" && loaded.Values["customer_name"] == "person@example.com");
        });
        check("Legacy placeholder whitespace does not reset the document on value edits", () =>
        {
            template.Body = "Hello {{  customer_name  }}"; pump();
            var document = body.Document; field!.Value = "updated@example.com"; pump();
            Require(ReferenceEquals(document, body.Document) && template.Body == "Hello {{  customer_name  }}");
            template.Body = "Before {{customer_name}}after";
        });
        check("Deleting a variable removes all its chips and rejects stale payloads", () =>
        {
            var drag = TokenEditor.DragData(template, field!);
            workspace.DeleteVariable(field!); pump();
            Require(template.Variables.Count == 0 && template.Values.Count == 0 && template.Body == "Before after");
            Require(template.To == "" && template.Cc == "" && template.Subject == "" && !body.DropVariable(drag));
        });
        workspace.Delete(); workspace.Selected = original; workspace.Notice = ""; pump();
    }

    private static IEnumerable<InlineUIContainer> Chips(TokenEditor editor) => editor.Document.Blocks.OfType<Paragraph>()
        .SelectMany(p => Inlines(p.Inlines)).OfType<InlineUIContainer>();
    private static IEnumerable<Inline> Inlines(InlineCollection collection)
    {
        foreach (var item in collection)
        {
            if (item is Span span) { foreach (var child in Inlines(span.Inlines)) yield return child; }
            else yield return item;
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Require(bool value, string? detail = null) { if (!value) throw new Exception(detail ?? "Editor assertion failed"); }
}
