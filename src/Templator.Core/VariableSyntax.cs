using System.Text.RegularExpressions;

namespace Templator.Core;

public sealed record TextSegment(string Text, string? Key, bool Missing);

public static partial class VariableSyntax
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();
    [GeneratedRegex(@"\A[A-Za-z0-9_]+\z", RegexOptions.CultureInvariant)]
    public static partial Regex KeyPattern();

    public static IEnumerable<string> Keys(Template template) =>
        new[] { template.To, template.Cc, template.Subject, template.Body }
            .SelectMany(text => Pattern().Matches(text).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);

    public static string Label(string key) => char.ToUpperInvariant(key[0]) + key[1..].Replace('_', ' ');

    public static void Synchronize(Template template)
    {
        var orderedKeys = Keys(template).ToArray();
        var keys = orderedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in orderedKeys)
            if (!template.Variables.Any(v => v.Key == key))
                template.Variables.Add(new Variable
                {
                    Key = key, Label = Label(key), Value = template.Values.GetValueOrDefault(key, "")
                });
        // The field library is independent from its uses in the message. Keeping
        // unused fields also lets undo restore a removed chip with its identity.
        foreach (var variable in template.Variables)
            variable.Referenced = keys.Contains(variable.Key);
    }

    public static IEnumerable<TextSegment> Segments(string source, Template template)
    {
        var offset = 0;
        foreach (Match match in Pattern().Matches(source))
        {
            if (match.Index > offset) yield return new(source[offset..match.Index], null, false);
            var key = match.Groups[1].Value;
            var variable = template.Variables.FirstOrDefault(v => v.Key == key);
            var value = variable?.Value ?? "";
            var missing = variable?.Required != false && string.IsNullOrWhiteSpace(value);
            yield return new(missing ? "{{" + key + "}}" : value, key, missing);
            offset = match.Index + match.Length;
        }
        if (offset < source.Length) yield return new(source[offset..], null, false);
    }

    public static string Render(string source, Template template) =>
        string.Concat(Segments(source, template).Select(s => s.Text));

    public static IReadOnlyList<string> Missing(Template template) => template.Variables
        .Where(v => v.Referenced && v.Required && string.IsNullOrWhiteSpace(v.Value))
        .Select(v => string.IsNullOrWhiteSpace(v.Label) ? v.Key : v.Label).ToList();
}
