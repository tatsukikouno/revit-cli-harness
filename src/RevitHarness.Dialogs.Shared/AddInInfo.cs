using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitHarness.Dialogs
{
    // Reads the add-in identity out of the body of Revit's "Security - Unsigned Add-in" dialog.
    public static class AddInInfo
    {
        private static readonly string[] TitleMarkers = { "Unsigned Add-in", "署名なしのアドイン", "署名されていないアドイン" };

        private static readonly Regex LabelledValue = new Regex(
            @"^\s*(?:add-?in\s*name|name|アドイン名|名前|path|location|assembly|file|パス|場所)\s*[:：]\s*(?<v>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static readonly Regex DllPath = new Regex(
            @"(?:[A-Za-z]:\\|\\\\)[^\r\n""<>|]*?\.dll|[\w.\-]+\.dll",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public static bool IsUnsignedAddInDialog(DialogInfo dialog)
        {
            var title = dialog.Title ?? string.Empty;
            return TitleMarkers.Any(m => title.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Every identifying string found in the body: labelled name/path values, full dll paths and bare dll file names.
        public static IReadOnlyList<string> Extract(DialogInfo dialog)
        {
            var text = dialog.Text ?? string.Empty;
            var found = new List<string>();
            try
            {
                foreach (Match m in LabelledValue.Matches(text)) found.Add(m.Groups["v"].Value.Trim());
                foreach (Match m in DllPath.Matches(text))
                {
                    var path = m.Value.Trim();
                    found.Add(path);
                    var slash = path.LastIndexOfAny(new[] { '\\', '/' });
                    if (slash >= 0) found.Add(path.Substring(slash + 1));
                }
            }
            catch (RegexMatchTimeoutException) { }
            return found.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
