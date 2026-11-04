using System;
using System.IO;

namespace RevitHarness.Dialogs
{
    public static class DialogPaths
    {
        public static string GetDirectory(string? baseDirectory = null) => Path.Combine(
            baseDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitHarness", "dialogs");

        public static string GetPendingDirectory(string? baseDirectory = null) =>
            Path.Combine(GetDirectory(baseDirectory), "pending");

        public static string GetAllowlistPath(string? baseDirectory = null) =>
            Path.Combine(GetDirectory(baseDirectory), "allowlist.json");
    }
}
