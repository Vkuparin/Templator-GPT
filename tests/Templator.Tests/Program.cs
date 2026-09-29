using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Templator.Core;

var passed = 0;
var failed = 0;
var root = Path.Combine(Path.GetTempPath(), "Templator-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected <{expected}>, got <{actual}>.");
}
void True(bool condition) { if (!condition) throw new Exception("Condition was false."); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
Template Template(string body = "") => new() { To = "", Cc = "", Body = body };
TemplateStore Store() => new(Path.Combine(root, Guid.NewGuid().ToString("N")));
StoreData Data(Template template) => new() { Templates = [template] };

Check("Detect variables across all four fields, once, in first-use order", () =>
{
    var t = Template("{{body}} {{ to }} {{cc}} {{body}}"); t.To = "{{to}}"; t.Cc = "{{cc}}"; t.Subject = "{{subject}}";
    VariableSyntax.Synchronize(t); Equal("to,cc,subject,body", string.Join(',', t.Variables.Select(v => v.Key)));
});
Check("Keys are ASCII, case-sensitive, and strictly anchored", () =>
{
    var t = Template("{{ID}} {{id}} {{a_b9}} {{bad-key}} {{ä}}"); VariableSyntax.Synchronize(t);
    Equal(3, t.Variables.Count); True(!VariableSyntax.KeyPattern().IsMatch("key\n"));
});
Check("Unused definitions stay available for reuse and undo", () =>
{
    var t = Template("{{foo}}"); VariableSyntax.Synchronize(t); t.Body = ""; VariableSyntax.Synchronize(t); Equal(1, t.Variables.Count); True(!t.Variables[0].Referenced);
});
Check("Preserve customized and filled unused definitions without blocking", () =>
{
    var t = Template("{{foo}} {{bar}}"); VariableSyntax.Synchronize(t);
    t.Variables[0].Customized = true; t.Variables[1].Value = "remember me";
    t.Body = ""; VariableSyntax.Synchronize(t); Equal(2, t.Variables.Count); Equal(0, VariableSyntax.Missing(t).Count);
    True(t.Variables.All(v => !v.Referenced));
});
Check("Preserve metadata and values during edits", () =>
{
    var t = Template("{{foo}}"); VariableSyntax.Synchronize(t); var v = t.Variables[0]; v.Label = "My field"; v.Example = "Example"; v.Value = "Value"; v.Required = false;
    t.Body += " {{bar}}"; VariableSyntax.Synchronize(t); True(ReferenceEquals(v, t.Variables[0])); Equal("Value", v.Value); Equal("My field", v.Label);
});
Check("Required whitespace is missing; optional blank becomes empty", () =>
{
    var t = Template("{{foo}}/{{bar}}"); VariableSyntax.Synchronize(t); t.Variables[0].Value = "  "; t.Variables[1].Required = false;
    Equal("{{foo}}/", VariableSyntax.Render(t.Body, t)); Equal(1, VariableSyntax.Missing(t).Count);
});
Check("Rendering does not recursively expand values", () =>
{
    var t = Template("{{foo}} {{bar}}"); VariableSyntax.Synchronize(t); t.Variables[0].Value = "{{bar}}"; t.Variables[1].Value = "Ääni 🌿";
    Equal("{{bar}} Ääni 🌿", VariableSyntax.Render(t.Body, t));
});
Check("Segments carry missing-state metadata for previews", () =>
{
    var t = Template("Hi {{name}}."); VariableSyntax.Synchronize(t); var segments = VariableSyntax.Segments(t.Body, t).ToArray();
    Equal(3, segments.Length); True(segments[1].Missing); Equal("name", segments[1].Key);
});
Check("Starter templates contain no saved identity or recipients", () =>
{
    foreach (var fi in new[] { false, true })
    {
        var t = Samples.Sap(fi); Equal(0, t.Values.Count); True(t.Variables.All(v => v.Value == ""));
        True(!t.Variables.Single(v => v.Key == "cc_list").Required); Equal(fi ? "fi" : "en", t.Language);
    }
});
Check("Recipients normalize commas/semicolons", () => Equal("a@example.com,b@example.com", MailDraft.Addresses(" a@example.com ; b@example.com, ")));
Check("Empty recipients block both URI and MIME", () =>
{
    var d = new MailDraft("", "", "Hello", "Body"); Throws<FormatException>(() => d.Mailto()); Throws<FormatException>(() => d.Eml());
});
Check("Cc-only draft is valid and has empty mailto path", () =>
{
    var d = new MailDraft("", "a@example.com", "Hi", "Text"); True(d.Mailto().StartsWith("mailto:?subject=")); True(!d.Eml().Contains("\r\nTo:"));
});
Check("Empty Cc is omitted", () => True(!new MailDraft("a@example.com", "", "Hi", "").Mailto().Contains("&cc=")));
Check("Malformed addresses and display names rejected", () =>
{
    foreach (var s in new[] { "not-an-email", "Person <a@example.com>", "ä@example.com", "a@example.com\r\nBcc: victim@example.com", "a@example.com\t" })
        Throws<FormatException>(() => MailDraft.Addresses(s));
});
Check("Subject control-character injection blocked", () =>
{
    var d = new MailDraft("a@example.com", "", "Hi\r\nBcc: a@example.com", ""); Throws<FormatException>(() => d.Eml()); Throws<FormatException>(() => d.Mailto());
});
Check("URI preserves Finnish, emoji, ampersands, plus, and question marks", () =>
{
    var d = new MailDraft("a+tag@example.com,b@example.com", "c@example.com", "Ää öö + & ? 🌿", "Hyvää\npäivää & 10% +");
    var uri = d.Mailto(); True(uri.Contains("%2C")); True(uri.Contains("%2B")); True(uri.Contains("%20")); True(!uri.Contains('+'));
    var query = uri[(uri.IndexOf('?') + 1)..].Split('&').Select(s => s.Split('=', 2)).ToDictionary(s => s[0], s => Uri.UnescapeDataString(s[1]));
    Equal(d.Subject, query["subject"]); Equal(MailDraft.NormalizeLines(d.Body), query["body"]); Equal(d.Cc, query["cc"]);
});
Check("All newline variants normalize to CRLF", () => Equal("a\r\nb\r\nc\r\nd", MailDraft.NormalizeLines("a\nb\rc\r\nd")));
Check("Headers-only URI omits body", () => True(!new MailDraft("a@example.com", "", "Hi", "body").Mailto(false).Contains("body=")));
Check("Full text remains available even without valid recipients", () => True(new MailDraft("", "", "Subject", "Body").FullText().Contains("Subject: Subject\r\n\r\nBody")));
Check("MIME round trip, Unicode words and long lines", () =>
{
    var subject = string.Concat(Enumerable.Repeat("Hyvää päivää 🌿 漢字 ", 30));
    var body = string.Concat(Enumerable.Repeat("Ääkköset\n🦊\r\n", 50));
    var eml = new MailDraft("a@example.com,b@example.com", "c@example.com", subject, body).Eml();
    var sections = eml.Split("\r\n\r\n", 2);
    Equal(MailDraft.NormalizeLines(body), Encoding.UTF8.GetString(Convert.FromBase64String(sections[1])));
    var matches = Regex.Matches(sections[0], @"=\?UTF-8\?B\?([^?]+)\?=");
    var strict = new UTF8Encoding(false, true);
    Equal(subject, string.Concat(matches.Select(m => strict.GetString(Convert.FromBase64String(m.Groups[1].Value)))));
    True(matches.All(m => m.Length <= 75)); True(eml.Split("\r\n").All(line => line.Length <= 78));
    True(eml.StartsWith("X-Unsent: 1\r\n"));
});
Check("MIME empty subject/body are valid", () => True(new MailDraft("a@example.com", "", "", "").Eml().Contains("Subject: \r\n")));
Check("Missing store seeds two recipes", () => Equal(2, Store().Load().Data.Templates.Count));
Check("Save/reload persists Unicode and all fields", () =>
{
    var store = Store(); var t = Samples.Sap(true); t.Variables[0].Label = "Sähköposti"; t.Values["recipient"] = "a@example.com";
    store.Save(Data(t)); var loaded = store.Load().Data.Templates[0]; Equal(t.Body, loaded.Body); Equal("Sähköposti", loaded.Variables[0].Label); Equal("a@example.com", loaded.Variables[0].Value);
});
Check("Atomic repeated saves retain the prior version", () =>
{
    var store = Store(); var t = Template("one"); var data = Data(t); store.Save(data); t.Body = "two"; store.Save(data); t.Body = "three"; store.Save(data);
    Equal("three", store.Load().Data.Templates[0].Body); Equal("two", TemplateStore.Read(store.FilePath + ".bak").Templates[0].Body);
    Equal(0, Directory.GetFiles(store.Folder, "*.tmp").Length);
});
Check("Corrupt store preserved byte-for-byte, empty recovery", () =>
{
    var store = Store(); Directory.CreateDirectory(store.Folder); File.WriteAllText(store.FilePath, "broken { ä");
    var loaded = store.Load(); Equal(0, loaded.Data.Templates.Count); True(loaded.Notice != null);
    Equal("broken { ä", File.ReadAllText(Directory.GetFiles(store.Folder, "*.bad-*").Single()));
});
Check("Unknown future version is untouched", () =>
{
    var store = Store(); Directory.CreateDirectory(store.Folder); File.WriteAllText(store.FilePath, "{\"version\":99}");
    Throws<NotSupportedException>(() => store.Load()); Equal("{\"version\":99}", File.ReadAllText(store.FilePath));
});
Check("Null definitions and invalid shape recover safely", () =>
{
    foreach (var json in new[] { "null", "{\"templates\":null}", "{\"settings\":null}", "{\"templates\":[null]}", "{\"settings\":{\"mailtoLengthThreshold\":0}}" })
    {
        var store = Store(); Directory.CreateDirectory(store.Folder); File.WriteAllText(store.FilePath, json);
        True(store.Load().Notice != null);
    }
});
Check("Duplicate IDs and keys rejected", () =>
{
    var t = Template(); Throws<InvalidDataException>(() => TemplateStore.Validate(new StoreData { Templates = [t, t] }));
    t.Variables.Add(new Variable { Key = "same" }); t.Variables.Add(new Variable { Key = "same" }); Throws<InvalidDataException>(() => TemplateStore.Validate(Data(t)));
});
Check("Failed validation cannot replace existing good store", () =>
{
    var store = Store(); var data = Data(Template("good")); store.Save(data); var before = File.ReadAllText(store.FilePath);
    data.Settings.MailtoLengthThreshold = -1; Throws<InvalidDataException>(() => store.Save(data)); Equal(before, File.ReadAllText(store.FilePath));
});
Check("File lock prevents concurrent writers and releases cleanly", () =>
{
    var store = Store(); using (var held = store.AcquireLock()) Throws<IOException>(() => store.AcquireLock());
    using var next = store.AcquireLock(); True(next.CanWrite);
});
Check("Duplicate is independent and has a fresh identity", () =>
{
    var original = Samples.Sap(); original.Values["recipient"] = "a@example.com";
    var copy = TemplateStore.Clone(original); True(original.Id != copy.Id); Equal("a@example.com", copy.Variables[0].Value);
    copy.Variables[0].Label = "Changed"; copy.Values["recipient"] = "b@example.com";
    True(original.Variables[0].Label != copy.Variables[0].Label); Equal("a@example.com", original.Values["recipient"]);
});
Check("Import parsing does not alter its source", () =>
{
    var store = Store(); store.Save(Data(Samples.Sap())); var before = File.ReadAllText(store.FilePath);
    var imported = TemplateStore.Read(store.FilePath); imported.Templates[0].Name = "edited"; Equal(before, File.ReadAllText(store.FilePath));
});
Check("Source field size limit prevents a destructive save", () =>
{
    var store = Store(); var data = Data(Template("short")); store.Save(data);
    data.Templates[0].Body = new string('x', 200001); Throws<InvalidDataException>(() => store.Save(data)); Equal("short", store.Load().Data.Templates[0].Body);
});
Check("Serialized document excludes transient preview values", () =>
{
    var t = Samples.Sap(); t.Variables[0].Value = "transient";
    var json = JsonSerializer.Serialize(Data(t), TemplateStore.Json); True(!json.Contains("transient")); True(!json.Contains("referenced"));
});
Check("Friendly names generate safe, unique internal keys", () =>
{
    Equal("customer_name", VariableNaming.NewKey("Customer name", []));
    Equal("paivamaara", VariableNaming.NewKey("Päivämäärä", []));
    Equal("customer_name_3", VariableNaming.NewKey("Customer name", ["customer_name", "customer_name_2"]));
    Equal("field", VariableNaming.NewKey("项目", []));
});
Console.WriteLine($"\n{passed} passed; {failed} failed. Test data: {root}");
return failed == 0 ? 0 : 1;
