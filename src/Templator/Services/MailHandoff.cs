using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Templator.Core;

namespace Templator.Services;

public interface IMailPlatform
{
    void Copy(string text);
    void Open(string target);
}

public sealed class WindowsMailPlatform : IMailPlatform
{
    public void Copy(string text) => Clipboard.SetDataObject(text, true);
    public void Open(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}

public sealed class MailHandoff(IMailPlatform platform)
{
    public string Copy(string text)
    {
        try { platform.Copy(text); return "Copied to clipboard."; }
        catch (ExternalException) { return "Clipboard is busy. Your draft is safe here; try copying again."; }
    }

    public string Open(MailDraft draft, bool headersOnly = false)
    {
        if (headersOnly)
        {
            try { platform.Copy(MailDraft.NormalizeLines(draft.Body)); }
            catch (ExternalException) { return "Clipboard is busy. Draft was not opened; try again or save an .eml file."; }
        }
        try
        {
            platform.Open(draft.Mailto(!headersOnly));
            return headersOnly ? "Draft requested. Paste the copied body, then review in your mail app."
                : "Draft requested. Edit here and choose Open draft again whenever you need a fresh copy. Review in your mail app before sending.";
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return "Windows could not open a mail handler. Check your default mail app. " + Copy(draft.Body);
        }
    }

    public string OpenFile(string path)
    {
        try { platform.Open(path); return "Draft saved and opened. Editing support depends on your mail app."; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        { return "Draft saved, but Windows could not open its handler. Open the .eml file from your mail app."; }
    }

    public string SaveFile(MailDraft draft, string path)
    {
        File.WriteAllText(path, draft.Eml(), new UTF8Encoding(false));
        return OpenFile(path);
    }
}
