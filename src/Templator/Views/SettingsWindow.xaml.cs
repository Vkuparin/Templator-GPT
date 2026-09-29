using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Templator.Core;

namespace Templator.Views;

public partial class SettingsWindow : Window
{
    private readonly Settings settings;
    private readonly string folder;
    public SettingsWindow(Window owner, Settings settings, string folder)
    {
        InitializeComponent(); Owner = owner; this.settings = settings; this.folder = folder;
        Templator.Services.WindowTheme.Apply(this);
        DefaultTo.Text = settings.DefaultTo; DefaultCc.Text = settings.DefaultCc;
        Threshold.Text = settings.MailtoLengthThreshold.ToString(); FolderText.Text = folder;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Threshold.Text, out var threshold) || threshold is < 256 or > 30000)
        { ErrorText.Text = "Choose a whole number from 256 to 30,000."; return; }
        try { _ = MailDraft.Addresses(DefaultTo.Text); _ = MailDraft.Addresses(DefaultCc.Text); }
        catch (FormatException error) { ErrorText.Text = error.Message; return; }
        settings.DefaultTo = DefaultTo.Text.Trim(); settings.DefaultCc = DefaultCc.Text.Trim(); settings.MailtoLengthThreshold = threshold;
        DialogResult = true;
    }

    private void OpenFolder(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { ErrorText.Text = error.Message; }
    }
}
