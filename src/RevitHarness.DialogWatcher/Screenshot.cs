using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RevitHarness.DialogWatcher
{
    public static class Screenshot
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

        public static string? Capture(IntPtr hwnd, string directory, string name)
        {
            try
            {
                if (!GetWindowRect(hwnd, out var r)) return null;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return null;
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, name + ".png");
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        var hdc = g.GetHdc();
                        bool ok;
                        try { ok = PrintWindow(hwnd, hdc, 2 /* PW_RENDERFULLCONTENT */); }
                        finally { g.ReleaseHdc(hdc); }
                        if (!ok) g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
                    }
                    bmp.Save(path, ImageFormat.Png);
                }
                return path;
            }
            catch { return null; }
        }
    }
}
