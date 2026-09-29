using System.IO;
using System.Windows;
using Templator.Core;
using Templator.ViewModels;

namespace Templator;

public partial class App : Application
{
    private FileStream? storeLock;
    private string dataFolder = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        dataFolder = Environment.GetEnvironmentVariable("TEMPLATOR_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Templator-GPT");
        DispatcherUnhandledException += (_, args) => Log(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => Log(args.Exception);
        try
        {
            var store = new TemplateStore(dataFolder);
            try { storeLock = store.AcquireLock(); }
            catch (IOException error)
            { throw new IOException("The workspace may already be open in another Templator window. Close it and try again. " + error.Message, error); }
            var version = typeof(App).Assembly.GetName().Version!.ToString(3);
            var workspace = new Workspace(store, store.Load(version));
            MainWindow = new MainWindow(workspace);
            MainWindow.Show();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(error.Message, "Templator could not open the workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    private void Log(Exception? error)
    {
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.AppendAllText(Path.Combine(dataFolder, "crash.log"), $"{DateTimeOffset.Now:O}\n{error}\n\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    protected override void OnExit(ExitEventArgs e) { storeLock?.Dispose(); base.OnExit(e); }
}
