#nullable enable

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace RevitHarness.Addin.DialogHandling
{
    /// <summary>
    /// Best-effort capture of Revit's main window. Returns null on any failure.
    /// </summary>
    internal static class RevitWindowCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        public static string? Capture(string directory, string logId)
        {
            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle == IntPtr.Zero || !GetWindowRect(handle, out var r)) return null;
                int width = r.Right - r.Left, height = r.Bottom - r.Top;
                if (width <= 0 || height <= 0 || width > 16000 || height > 16000) return null;
                var folder = Path.Combine(directory, "screenshots");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "_" + logId + ".png");
                using var bitmap = new Bitmap(width, height);
                using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(width, height));
                bitmap.Save(path, ImageFormat.Png);
                return path;
            }
            catch { return null; }
        }
    }
}
