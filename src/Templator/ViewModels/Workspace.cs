using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using Templator.Core;

namespace Templator.ViewModels;

/// <summary>Owns editing state and persistence. Shell actions live in the view.</summary>
public sealed class Workspace : Observable, IDisposable
{
    private readonly TemplateStore store;
    private readonly StoreData data;
    private readonly DispatcherTimer saveTimer;
    private readonly HashSet<Variable> observedVariables = [];
    private bool syncing;
    private bool dirty;
    private Template? selected;
    private string search = "";
    public ObservableCollection<Template> Templates { get; }
    public ICollectionView Library { get; }
    public Settings Settings => data.Settings;
    public string DataFolder => store.Folder;
    public bool HasSelection => Selected != null;
    public string SaveStatus { get; private set => Set(ref field, value); } = "Saved locally";
    public string Notice { get; set => Set(ref field, value); } = "";
    public string Validation { get; private set => Set(ref field, value); } = "";
    public string DraftStatus { get; private set => Set(ref field, value); } = "";
    public bool CanOpen { get; private set => Set(ref field, value); }
    public string Progress { get; private set => Set(ref field, value); } = "";
    public int EditorMode { get; set => Set(ref field, value); }
    public string NewKey { get; set => Set(ref field, value); } = "";
    public MailDraft? Draft => Selected is null ? null : MailDraft.From(Selected);
    public event EventHandler? PreviewChanged;

    public string Search
    {
        get => search;
        set { Set(ref search, value); Library.Refresh(); }
    }

    public Template? Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            Detach();
            selected = value;
            if (selected != null)
            {
                selected.PropertyChanged += TemplateChanged;
                selected.Variables.CollectionChanged += VariablesChanged;
                Sync();
            }
            Notify(); Notify(nameof(HasSelection));
            Refresh();
        }
    }

    public Workspace(TemplateStore store, LoadResult loaded)
    {
        this.store = store;
        data = loaded.Data;
        Notice = loaded.Notice ?? "";
        Templates = new(data.Templates);
        Library = CollectionViewSource.GetDefaultView(Templates);
        Library.Filter = item => item is Template t &&
            (t.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) || t.Subject.Contains(Search, StringComparison.OrdinalIgnoreCase));
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        saveTimer.Tick += (_, _) => Flush();
        Selected = Templates.FirstOrDefault();
    }

    private void TemplateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (syncing) return;
        Sync(); Library.Refresh(); Changed();
    }

    private void VariablesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ObserveVariables();
        if (!syncing) Changed();
    }

    private void ObserveVariables()
    {
        if (Selected == null) return;
        foreach (var old in observedVariables.Where(v => !Selected.Variables.Contains(v)).ToArray())
        {
            old.PropertyChanged -= VariableChanged; observedVariables.Remove(old);
        }
        foreach (var variable in Selected.Variables)
            if (observedVariables.Add(variable)) variable.PropertyChanged += VariableChanged;
    }

    private void VariableChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (syncing || Selected == null || sender is not Variable variable || args.PropertyName == nameof(Variable.Referenced)) return;
        if (args.PropertyName == nameof(Variable.Value)) Selected.Values[variable.Key] = variable.Value;
        else variable.Customized = true;
        Changed();
    }

    private void Sync()
    {
        if (Selected == null) return;
        syncing = true;
        try { VariableSyntax.Synchronize(Selected); ObserveVariables(); }
        finally { syncing = false; }
    }

    public void Refresh()
    {
        if (Selected == null)
        {
            Validation = "Choose a template to start.";
            CanOpen = false; DraftStatus = ""; Progress = "";
        }
        else
        {
            var missing = VariableSyntax.Missing(Selected);
            Validation = missing.Count > 0 ? "Complete: " + string.Join(", ", missing) : Draft!.ValidationError() ?? "Ready for your review.";
            CanOpen = missing.Count == 0 && Draft!.ValidationError() == null;
            var total = Selected.Variables.Count(v => v.Referenced && v.Required);
            Progress = $"{total - missing.Count} / {total} required fields";
            DraftStatus = CanOpen ? $"{Draft!.Mailto().Length:N0} / {Settings.MailtoLengthThreshold:N0} URL characters" : "Draft stays on this device";
        }
        Notify(nameof(Draft)); PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Changed()
    {
        dirty = true; SaveStatus = "Saving…";
        saveTimer.Stop(); saveTimer.Start(); Refresh();
    }

    public bool Flush()
    {
        saveTimer.Stop();
        if (!dirty) return true;
        try
        {
            data.Templates = Templates.ToList(); store.Save(data);
            dirty = false; SaveStatus = "Saved locally"; return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            SaveStatus = "Not saved — Ctrl+S to retry";
            Notice = "Your changes are still in memory. " + e.Message; return false;
        }
    }

    public void NewTemplate()
    {
        if (Templates.Count >= 1000) { Notice = "Your library has reached the 1,000-template limit."; return; }
        Search = "";
        var template = new Template { Body = "Hello {{customer_name}},\n\n{{message}}\n\nBest regards,\n{{sender_name}}", Subject = "{{subject}}" };
        VariableSyntax.Synchronize(template);
        foreach (var v in template.Variables)
        {
            if (v.Key == "cc_list") { v.Required = false; v.Customized = true; }
            var value = v.Key switch { "recipient" => Settings.DefaultTo, "cc_list" => Settings.DefaultCc, _ => "" };
            v.Value = value; template.Values[v.Key] = value;
        }
        Templates.Add(template); Selected = template; EditorMode = 1; Changed();
    }

    public void Duplicate()
    {
        if (Selected == null) return;
        if (Templates.Count >= 1000) { Notice = "Your library has reached the 1,000-template limit."; return; }
        var clone = TemplateStore.Clone(Selected); clone.Name += " · copy";
        Search = ""; Templates.Insert(Templates.IndexOf(Selected) + 1, clone); Selected = clone; Changed();
    }

    public void Delete()
    {
        if (Selected == null) return;
        var index = Templates.IndexOf(Selected); var old = Selected;
        Selected = null; Templates.Remove(old);
        Selected = Templates.ElementAtOrDefault(Math.Min(index, Templates.Count - 1)); Changed();
    }

    public void Move(int direction)
    {
        if (Selected == null) return;
        var index = Templates.IndexOf(Selected); var target = index + direction;
        if (target < 0 || target >= Templates.Count) return;
        Templates.Move(index, target); Changed();
    }

    public void Reset()
    {
        if (Selected == null) return;
        foreach (var variable in Selected.Variables) variable.Value = "";
        Selected.Values.Clear(); Sync(); Changed();
    }

    public void AddVariable()
    {
        if (Selected == null) return;
        if (Selected.Variables.Count >= 500) { Notice = "This template has reached the 500-variable limit."; return; }
        var key = NewKey.Trim(); var match = VariableSyntax.Pattern().Match(key);
        if (match.Success && match.Length == key.Length) key = match.Groups[1].Value;
        if (!VariableSyntax.KeyPattern().IsMatch(key)) { Notice = "Use letters, digits, and underscores for a variable key."; return; }
        if (Selected.Variables.Any(v => v.Key == key)) { Notice = "That variable already exists."; return; }
        Selected.Variables.Add(new Variable { Key = key, Label = VariableSyntax.Label(key), Customized = true });
        NewKey = ""; Notice = "Variable added. Insert {{" + key + "}} into a template field to use it."; Changed();
    }

    public void DeleteVariable(Variable variable)
    {
        if (Selected == null) return;
        Selected.Values.Remove(variable.Key); Selected.Variables.Remove(variable);
        Sync(); Changed();
        Notice = Selected.Variables.Any(v => v.Key == variable.Key)
            ? "The variable is still referenced, so a fresh definition was created. Remove its placeholder to remove it completely."
            : "Variable removed.";
    }

    public void Import(string path)
    {
        var incoming = TemplateStore.Read(path);
        if (Templates.Count + incoming.Templates.Count > 1000) throw new InvalidDataException("Import would exceed 1,000 templates.");
        var copies = incoming.Templates.Select(TemplateStore.Clone).ToList();
        var merged = new StoreData { Settings = Settings, Templates = Templates.Concat(copies).ToList() };
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(merged, TemplateStore.Json).Length > 10 * 1024 * 1024)
            throw new InvalidDataException("Import would exceed the 10 MB store limit.");
        foreach (var copy in copies) Templates.Add(copy);
        Search = ""; Selected = copies.FirstOrDefault() ?? Selected; Changed();
        Notice = $"Imported {copies.Count} template(s) as independent copies. Existing settings were kept.";
    }

    public void Export(string path) => TemplateStore.Write(path, new StoreData { Settings = Settings, Templates = Templates.ToList() });

    private void Detach()
    {
        if (selected != null)
        {
            selected.PropertyChanged -= TemplateChanged;
            selected.Variables.CollectionChanged -= VariablesChanged;
        }
        foreach (var variable in observedVariables) variable.PropertyChanged -= VariableChanged;
        observedVariables.Clear();
    }

    public void Dispose() { saveTimer.Stop(); Detach(); }
}
