/* ===================================
   NativeChrome.cs
   -----------------------------------
   - Asks DWM for Windows 11 rounded corners and a border colour on our borderless windows.
   - Hardware-composited, unlike AllowsTransparency + DropShadowEffect.
   =================================== */

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HibernateScheduler;

internal static class NativeChrome
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    // COLORREF is 0x00BBGGRR; matches the Stroke brush #34373E
    private const int BorderColour = 0x003E3734;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var corner = DWMWCP_ROUND;
            var border = BorderColour;

            // Both fail quietly on Windows 10, which just keeps square corners
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        };
    }
}
