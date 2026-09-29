using System.Net.Mail;
using System.Text;

namespace Templator.Core;

public sealed record MailDraft(string To, string Cc, string Subject, string Body)
{
    public static MailDraft From(Template template) => new(
        VariableSyntax.Render(template.To, template), VariableSyntax.Render(template.Cc, template),
        VariableSyntax.Render(template.Subject, template), VariableSyntax.Render(template.Body, template));

    public static string NormalizeLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    public static string Addresses(string input)
    {
        if (input.Any(char.IsControl)) throw new FormatException("Recipients cannot contain line breaks or control characters.");
        var addresses = input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var address in addresses)
            if (address.Length > 254 || address.Any(c => c > 127) || !MailAddress.TryCreate(address, out var parsed) ||
                parsed.Address != address || !address.Contains('@'))
                throw new FormatException($"Use plain email addresses, separated by commas. Check: {address}");
        return string.Join(',', addresses);
    }

    public string? ValidationError()
    {
        try
        {
            var to = Addresses(To);
            var cc = Addresses(Cc);
            if (to.Length == 0 && cc.Length == 0) return "No recipient — add a To or Cc address.";
            if (Subject.Any(char.IsControl)) return "The subject must be a single line without control characters.";
            return null;
        }
        catch (FormatException e) { return e.Message; }
    }

    public string Mailto(bool includeBody = true)
    {
        var error = ValidationError();
        if (error != null) throw new FormatException(error);
        var result = "mailto:" + Uri.EscapeDataString(Addresses(To)) + "?subject=" + Uri.EscapeDataString(Subject);
        var cc = Addresses(Cc);
        if (cc.Length > 0) result += "&cc=" + Uri.EscapeDataString(cc);
        if (includeBody) result += "&body=" + Uri.EscapeDataString(NormalizeLines(Body));
        return result;
    }

    public string FullText() => $"To: {To}\r\nCc: {Cc}\r\nSubject: {Subject}\r\n\r\n{NormalizeLines(Body)}";

    public string Eml()
    {
        var error = ValidationError();
        if (error != null) throw new FormatException(error);
        var builder = new StringBuilder("X-Unsent: 1\r\nMIME-Version: 1.0\r\n");
        foreach (var (name, value) in new[] { ("To", Addresses(To)), ("Cc", Addresses(Cc)) })
            if (value.Length > 0) builder.Append(name).Append(": ").Append(value.Replace(",", ",\r\n ")).Append("\r\n");
        builder.Append("Subject: ").Append(EncodedSubject(Subject)).Append("\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(NormalizeLines(Body)));
        for (var i = 0; i < body.Length; i += 76) builder.Append(body.AsSpan(i, Math.Min(76, body.Length - i))).Append("\r\n");
        return builder.ToString();
    }

    private static string EncodedSubject(string subject)
    {
        var words = new List<string>();
        var chunk = new StringBuilder();
        var bytes = 0;
        void Flush()
        {
            if (chunk.Length == 0) return;
            words.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString())) + "?=");
            chunk.Clear(); bytes = 0;
        }
        foreach (var rune in subject.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 42) Flush();
            chunk.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        Flush();
        return string.Join("\r\n ", words);
    }
}
