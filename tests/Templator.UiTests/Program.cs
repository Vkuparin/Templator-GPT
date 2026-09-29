using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Templator;
using Templator.Core;
using Templator.Services;
using Templator.ViewModels;
using Templator.Views;

internal static class Program
{
    private static int passed;
    private static readonly List<string> BindingErrors = [];

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui");
        Directory.CreateDirectory(output);
        var folder = Path.Combine(output, "test-data-" + Guid.NewGuid().ToString("N"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Templator;component/Themes/WorkspaceTheme.xaml", UriKind.Relative) });
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingListener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var store = new TemplateStore(folder);
        var platform = new FakePlatform();
        MainWindow? window = null;
        try
        {
            using var held = store.AcquireLock();
            var workspace = new Workspace(store, store.Load());
            window = new MainWindow(workspace, platform) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            window.Show(); Pump();
            Check("WPF loads templates and blocks incomplete draft", () => Require(workspace.Templates.Count == 2 && !Find<Button>(window, "OpenDraftButton").IsEnabled));
            Check("Actual form bindings update values and preview", () =>
            {
                var fields = Descendants<TextBox>(Find<ItemsControl>(window, "ValueInputs")).ToList();
                Require(fields.Count == 6);
                foreach (var field in fields)
                {
                    var variable = (Variable)field.DataContext;
                    field.Text = variable.Key switch
                    {
                        "recipient" => "sap-team@example.com", "customer_name" => "Nordic Components Oy",
                        "order_number" => "45002816", "delivery_date" => "30 November 2026",
                        "sender_name" => "Alex Niemi", _ => ""
                    };
                }
                Pump();
                Require(workspace.CanOpen && Find<Button>(window, "OpenDraftButton").IsEnabled);
                Require(workspace.Draft!.Body.Contains("Nordic Components Oy"));
            });
            workspace.Flush();
            Capture(window, Path.Combine(output, "compose.png"));
            Check("Open draft delegates a URI to a fake handler only", () =>
            {
                Find<Button>(window, "OpenDraftButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(platform.Opened.Single().StartsWith("mailto:sap-team%40example.com"));
                workspace.Notice = "";
            });
            EditorChecks.Run(window, workspace, app, Check, Pump, Capture, output);
            Capture(window, Path.Combine(output, "editor.png"));
            Check("Language selection updates metadata without translating content", () =>
            {
                var body = workspace.Selected!.Body;
                Find<RadioButton>(window, "FinnishRadio").IsChecked = true;
                Require(workspace.Selected.Language == "fi" && workspace.Selected.Body == body);
                Find<RadioButton>(window, "EnglishRadio").IsChecked = true;
            });
            Check("Custom definitions, metadata, and unreferenced state persist", () =>
            {
                workspace.AddVariable("Cost center", "1002");
                var v = workspace.Selected!.Variables.Single(v => v.Key == "cost_center");
                v.Label = "Cost center"; v.Example = "1002"; v.Required = true;
                Require(!v.Referenced && workspace.CanOpen); Require(workspace.Flush());
                var loaded = store.Load().Data.Templates[0].Variables.Single(v => v.Key == "cost_center");
                Require(loaded.Customized && loaded.Label == "Cost center" && loaded.Example == "1002");
            });
            Check("Duplicate and reorder preserve data and selection", () =>
            {
                var id = workspace.Selected!.Id; workspace.Duplicate(); var clone = workspace.Selected!;
                Require(clone.Id != id && clone.Values["customer_name"] == "Nordic Components Oy");
                workspace.Move(-1); Require(workspace.Templates[0] == clone); workspace.Delete();
                Require(workspace.Templates.Count == 2);
            });
            Check("Search filters name and subject", () =>
            {
                workspace.Search = "Projektin"; Require(workspace.Library.Cast<Template>().Count() == 1);
                workspace.Search = "unfindable"; Require(workspace.Library.IsEmpty); workspace.Search = "";
                workspace.Selected = workspace.Templates[0];
            });
            Check("Import merges fresh identities and preserves settings", () =>
            {
                var path = Path.Combine(output, "backup.json"); workspace.Export(path);
                workspace.Settings.DefaultTo = "local@example.com";
                workspace.Import(path); Require(workspace.Templates.Count == 4);
                Require(workspace.Templates.Select(t => t.Id).Distinct().Count() == 4);
                Require(workspace.Settings.DefaultTo == "local@example.com");
                var before = workspace.Templates.Count;
                var bad = Path.Combine(output, "invalid.json"); File.WriteAllText(bad, "{");
                try { workspace.Import(bad); throw new Exception("Invalid import succeeded"); }
                catch (System.Text.Json.JsonException) { }
                Require(workspace.Templates.Count == before);
            });
            Check("Reset values clears memory, disk, and the draft guard", () =>
            {
                workspace.Reset(); Require(workspace.Selected!.Values.Count == 0 && !workspace.CanOpen);
                Require(workspace.Flush());
                var loaded = store.Load().Data.Templates.Single(t => t.Id == workspace.Selected.Id);
                Require(loaded.Values.Count == 0 && loaded.Variables.All(v => v.Value == ""));
            });
            Check("Defaults prefill only new templates", () =>
            {
                workspace.NewTemplate(); Require(workspace.Selected!.To == "local@example.com" && workspace.Selected.Variables.Count == 0);
                workspace.Reset(); Require(workspace.Selected.Values.Count == 0);
            });
            Check("Failed save preserves the old file and allows retry", () =>
            {
                Require(workspace.Flush()); var before = File.ReadAllText(store.FilePath);
                workspace.Selected!.Name = "Unsaved change";
                using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                { Require(!workspace.Flush()); Require(workspace.SaveStatus.StartsWith("Not saved")); }
                Require(File.ReadAllText(store.FilePath) == before); Require(workspace.Flush());
                Require(store.Load().Data.Templates.Any(t => t.Name == "Unsaved change"));
            });
            Check("Save/restart restores last-used fields", () =>
            {
                workspace.Selected = workspace.Templates[0]; workspace.Selected.Variables[0].Value = "roundtrip@example.com";
                Require(workspace.Flush());
                using var reopened = new Workspace(store, store.Load());
                Require(reopened.Selected!.Variables[0].Value == "roundtrip@example.com");
            });
            Check("Store size validation surfaces as unsaved state without crashing", () =>
            {
                var original = workspace.Selected!.Body;
                workspace.Selected.Body = new string('x', 200001);
                Require(!workspace.Flush() && workspace.SaveStatus.StartsWith("Not saved"));
                workspace.Selected.Body = original; Require(workspace.Flush());
            });
            Check("Handler failure attempts clipboard recovery", () =>
            {
                var fake = new FakePlatform { OpenFails = true };
                var text = new MailHandoff(fake).Open(new MailDraft("a@example.com", "", "Hi", "Body"));
                Require(text.Contains("could not open") && fake.Copied.Single() == "Body");
            });
            Check("Clipboard failure prevents headers-only launch", () =>
            {
                var fake = new FakePlatform { CopyFails = true };
                var text = new MailHandoff(fake).Open(new MailDraft("a@example.com", "", "Hi", "Body"), true);
                Require(fake.Opened.Count == 0 && text.Contains("Clipboard is busy"));
            });
            Check("Headers-only fallback copies body without duplicate headers", () =>
            {
                var fake = new FakePlatform(); new MailHandoff(fake).Open(new MailDraft("a@example.com", "", "Hi", "Body"), true);
                Require(fake.Copied.Single() == "Body" && !fake.Opened.Single().Contains("body="));
            });
            Check("Actual long-draft dialog chooses the clipboard fallback", () =>
            {
                workspace.Settings.MailtoLengthThreshold = 256;
                var before = platform.Opened.Count;
                ChooseNext(app, "clipboard", d => Capture(d, Path.Combine(output, "long-draft.png")));
                Find<Button>(window, "OpenDraftButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(platform.Opened.Count == before + 1 && !platform.Opened.Last().Contains("&body="));
                Require(platform.Copied.Last() == MailDraft.NormalizeLines(workspace.Draft!.Body));
            });
            Check("Oversize headers disable clipboard option; explicit full-link choice works", () =>
            {
                var subject = workspace.Selected!.Subject;
                workspace.Selected.Subject = new string('x', 400);
                ChooseNext(app, "anyway", d => Require(!Descendants<Button>(d).Single(b => Equals(b.Tag, "clipboard")).IsEnabled));
                Find<Button>(window, "OpenDraftButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(platform.Opened.Last().Contains("&body="));
                workspace.Selected.Subject = subject; workspace.Settings.MailtoLengthThreshold = 1800;
            });
            Check("EML fallback writes complete data even if its handler fails", () =>
            {
                var path = Path.Combine(output, "test-draft.eml");
                var draft = new MailDraft("a@example.com", "", "Päivää 🌿", "Hyvää päivää\n" + new string('x', 6000));
                var result = new MailHandoff(new FakePlatform { OpenFails = true }).SaveFile(draft, path);
                Require(File.ReadAllText(path) == draft.Eml() && result.Contains("Draft saved"));
            });
            Check("Settings dialog rejects invalid input and saves valid values", () =>
            {
                var settings = new Settings();
                var dialog = new SettingsWindow(window, settings, folder);
                Dispatcher.CurrentDispatcher.BeginInvoke(() =>
                {
                    var save = Descendants<Button>(dialog).Single(b => Equals(b.Content, "Save settings"));
                    Find<TextBox>(dialog, "Threshold").Text = "-1";
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(settings.MailtoLengthThreshold == 1800 && Find<TextBlock>(dialog, "ErrorText").Text.Length > 0);
                    Find<TextBox>(dialog, "Threshold").Text = "2000";
                    Find<TextBox>(dialog, "DefaultTo").Text = "team@example.com";
                    Capture(dialog, Path.Combine(output, "settings.png"));
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }, DispatcherPriority.ApplicationIdle);
                Require(dialog.ShowDialog() == true && settings.MailtoLengthThreshold == 2000 && settings.DefaultTo == "team@example.com");
            });
            workspace.Notice = ""; workspace.Selected = workspace.Templates[0];
            window.Width = 1050; window.Height = 680; Pump();
            Capture(window, Path.Combine(output, "compact.png"));
            Check("Minimum window keeps action and preview in bounds", () =>
            {
                var button = Find<Button>(window, "OpenDraftButton");
                var position = button.TranslatePoint(new Point(), (UIElement)window.Content);
                Require(position.X >= 0 && position.Y >= 0 && position.X + button.ActualWidth <= ((FrameworkElement)window.Content).ActualWidth + 1);
            });
            Check("Deleting the final template presents an empty workspace", () =>
            {
                while (workspace.Templates.Count > 0) { workspace.Selected = workspace.Templates[0]; workspace.Delete(); }
                Pump(); Require(!workspace.HasSelection && !Find<Button>(window, "OpenDraftButton").IsEnabled);
                Require(workspace.Flush() && store.Load().Data.Templates.Count == 0);
                Capture(window, Path.Combine(output, "empty.png"));
            });
            Check("No WPF binding errors", () => Require(BindingErrors.Count == 0, string.Join("\n", BindingErrors)));
            window.Close(); app.Shutdown();
            Console.WriteLine($"{passed} UI/integration checks passed. Renders: {output}");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e); File.WriteAllText(Path.Combine(output, "failure.txt"), e.ToString());
            window?.Close(); app.Shutdown(); return 1;
        }
    }

    private static void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    private static void Require(bool condition, string? detail = null) { if (!condition) throw new Exception(detail ?? "Assertion failed"); }
    private static T Find<T>(FrameworkElement window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void ChooseNext(Application app, string choice, Action<ChoiceDialog>? inspect = null) =>
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            var dialog = app.Windows.OfType<ChoiceDialog>().Single();
            inspect?.Invoke(dialog);
            Descendants<Button>(dialog).Single(b => Equals(b.Tag, choice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Capture(Window window, string path)
    {
        Pump(); window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var width = (int)(content.ActualWidth + content.Margin.Left + content.Margin.Right);
        var height = (int)(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class BindingListener : TraceListener
    {
        public override void Write(string? message) { if (message != null) BindingErrors.Add(message); }
        public override void WriteLine(string? message) { if (message != null) BindingErrors.Add(message); }
    }
    private sealed class FakePlatform : IMailPlatform
    {
        public List<string> Opened { get; } = [];
        public List<string> Copied { get; } = [];
        public bool OpenFails { get; init; }
        public bool CopyFails { get; init; }
        public void Copy(string text) { if (CopyFails) throw new ExternalException(); Copied.Add(text); }
        public void Open(string target) { if (OpenFails) throw new Win32Exception(); Opened.Add(target); }
    }
}
