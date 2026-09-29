using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Templator.Core;

public static partial class VariableNaming
{
    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separators();

    public static string NewKey(string label, IEnumerable<string> existing)
    {
        var simple = string.Concat(label.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        var stem = Separators().Replace(simple.ToLowerInvariant(), "_").Trim('_');
        if (stem.Length == 0) stem = "field";
        var keys = existing.ToHashSet(StringComparer.Ordinal);
        var key = stem;
        for (var suffix = 2; keys.Contains(key); suffix++) key = stem + "_" + suffix;
        return key;
    }
}
