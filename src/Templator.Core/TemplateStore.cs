using System.Text.Json;

namespace Templator.Core;

public sealed record LoadResult(StoreData Data, string? Notice = null);

public sealed class TemplateStore(string folder)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
    };
    public string Folder { get; } = Path.GetFullPath(folder);
    public string FilePath => Path.Combine(Folder, "templates.json");
    public FileStream AcquireLock()
    {
        Directory.CreateDirectory(Folder);
        return new FileStream(Path.Combine(Folder, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public LoadResult Load(string? appVersion = null)
    {
        if (!File.Exists(FilePath)) return new(new StoreData { Templates = [Samples.Sap(), Samples.Sap(true)] });
        StoreData data;
        try { data = Read(FilePath); }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            // Access and future-version errors must never reset the store.
            var backup = FilePath + ".bad-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
            File.Move(FilePath, backup);
            return new(new StoreData(), $"Invalid data preserved at {backup}. Import a backup to recover.");
        }
        // A validated, byte-for-byte snapshot precedes this version's first save.
        // Unlike the rolling .bak, later autosaves can never replace this snapshot.
        if (appVersion != null) PreserveVersionBackup(appVersion);
        return new(data);
    }

    private void PreserveVersionBackup(string version)
    {
        if (!System.Version.TryParse(version, out var parsed)) throw new ArgumentException("Invalid app version.", nameof(version));
        var folder = Path.Combine(Folder, "backups");
        var destination = Path.Combine(folder, $"templates-before-{parsed}.json");
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(folder);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = File.ReadAllBytes(FilePath);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static StoreData Read(string path)
    {
        if (new FileInfo(path).Length > 10 * 1024 * 1024) throw new InvalidDataException("Store exceeds the 10 MB limit.");
        var data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty document.");
        Validate(data);
        foreach (var template in data.Templates)
        {
            foreach (var variable in template.Variables) variable.Value = template.Values.GetValueOrDefault(variable.Key, "");
            VariableSyntax.Synchronize(template);
        }
        return data;
    }

    public static void Validate(StoreData data)
    {
        if (data.Version != 1) throw new NotSupportedException($"Store version {data.Version} is not supported. Original file was left untouched.");
        if (data.Templates == null || data.Settings == null || data.Templates.Count > 1000)
            throw new InvalidDataException("Invalid store structure (maximum 1,000 templates).");
        if (data.Settings.DefaultTo == null || data.Settings.DefaultCc == null || data.Settings.MailtoLengthThreshold is < 256 or > 30000)
            throw new InvalidDataException("Invalid settings; URL threshold must be 256–30,000.");
        var ids = new HashSet<Guid>();
        foreach (var t in data.Templates)
        {
            if (t == null || t.Id == Guid.Empty || !ids.Add(t.Id) || t.Name == null || t.Subject == null ||
                t.Body == null || t.To == null || t.Cc == null || t.Language is not ("en" or "fi") ||
                t.Variables == null || t.Values == null || t.Variables.Count > 500 ||
                new[] { t.Name, t.Subject, t.Body, t.To, t.Cc }.Any(s => s.Length > 200000))
                throw new InvalidDataException("Invalid template structure or size.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in t.Variables)
                if (v == null || v.Key == null || !VariableSyntax.KeyPattern().IsMatch(v.Key) || !keys.Add(v.Key) ||
                    v.Label == null || v.Example == null || v.Value == null)
                    throw new InvalidDataException("Invalid or duplicate variable definition.");
            if (t.Values.Any(pair => !VariableSyntax.KeyPattern().IsMatch(pair.Key) || pair.Value == null))
                throw new InvalidDataException("Invalid saved values.");
        }
    }

    public void Save(StoreData data) => Write(FilePath, data);

    public static void Write(string path, StoreData data)
    {
        Validate(data);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        if (bytes.Length > 10 * 1024 * 1024) throw new InvalidDataException("Store exceeds the 10 MB limit; export or remove unused templates.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Template Clone(Template source)
    {
        var clone = JsonSerializer.Deserialize<Template>(JsonSerializer.Serialize(source, Json), Json)!;
        clone.Id = Guid.NewGuid();
        foreach (var v in clone.Variables) v.Value = clone.Values.GetValueOrDefault(v.Key, "");
        VariableSyntax.Synchronize(clone);
        return clone;
    }
}
