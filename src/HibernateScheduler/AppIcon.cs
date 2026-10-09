/* ===================================
   AppIcon.cs
   -----------------------------------
   - Draws the crescent-moon icon at runtime for the tray and window title bars.
   =================================== */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HibernateScheduler;

internal static class AppIcon
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(bool active)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            using var fill = new SolidBrush(active ? System.Drawing.Color.FromArgb(255, 190, 60) : System.Drawing.Color.FromArgb(150, 150, 150));
            g.FillEllipse(fill, 2, 2, 28, 28);

            // Punch out an offset circle with SourceCopy to leave an anti-aliased crescent
            g.CompositingMode = CompositingMode.SourceCopy;
            using var clear = new SolidBrush(System.Drawing.Color.Transparent);
            g.FillEllipse(clear, 11, -3, 25, 25);
        }

        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    public static ImageSource CreateImage()
    {
        using var icon = Create(true);
        var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        return src;
    }
}
