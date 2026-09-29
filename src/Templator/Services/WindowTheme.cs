using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Templator.Services;

internal static class WindowTheme
{
    public static void Apply(Window window) => window.SourceInitialized += (_, _) =>
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int));
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
