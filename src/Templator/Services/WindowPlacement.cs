using System.IO;
using System.Text.Json;
using System.Windows;

namespace Templator.Services;

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    public static void Restore(Window window, string folder)
    {
        try
        {
            var path = Path.Combine(folder, "window-state.json");
            if (!File.Exists(path)) return;
            var state = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path));
            if (state == null || new[] { state.Left, state.Top, state.Width, state.Height }.Any(v => !double.IsFinite(v))) return;
            var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var width = Math.Clamp(state.Width, window.MinWidth, Math.Max(window.MinWidth, desktop.Width));
            var height = Math.Clamp(state.Height, window.MinHeight, Math.Max(window.MinHeight, desktop.Height));
            var bounds = new Rect(state.Left, state.Top, width, height);
            if (desktop.Contains(bounds))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = state.Left; window.Top = state.Top;
            }
            window.Width = width; window.Height = height;
            if (state.Maximized) window.WindowState = WindowState.Maximized;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }

    public static void Save(Window window, string folder)
    {
        try
        {
            var bounds = window.RestoreBounds;
            if (bounds.IsEmpty) return;
            var state = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized);
            File.WriteAllText(Path.Combine(folder, "window-state.json"), JsonSerializer.Serialize(state));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
