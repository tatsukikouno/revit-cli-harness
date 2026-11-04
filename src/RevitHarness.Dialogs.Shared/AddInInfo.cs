using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitHarness.Dialogs
{
    public sealed class AddInIdentity
    {
        public IReadOnlyList<string> Names { get; }
        public IReadOnlyList<string> Paths { get; }
        public IReadOnlyList<string> Files { get; }
        public IEnumerable<string> NamesAndFiles => Names.Concat(Files);

        public AddInIdentity(IReadOnlyList<string> names, IReadOnlyList<string> paths, IReadOnlyList<string> files)
        {
            Names = names;
            Paths = paths;
            Files = files;
        }
    }

    // Reads the add-in identity out of the body of Revit's "Security - Unsigned Add-in" dialog.
    public static class AddInInfo
    {
        private static readonly string[] TitleMarkers = { "Unsigned Add-in", "署名なしのアドイン", "署名されていないアドイン" };

        private const string NameLabels = @"add-?in\s*name|name|アドイン名|名前";
        private const string PathLabels = @"path|location|assembly|file|パス|場所";

        private static readonly Regex NameLine = new Regex(
            @"^\s*(?:" + NameLabels + @")\s*[:：]\s*(?<v>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static readonly Regex PathLine = new Regex(
            @"^\s*(?:" + PathLabels + @")\s*[:：]\s*(?<v>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static readonly Regex DllPath = new Regex(
            @"(?:[A-Za-z]:\\|\\\\)[^\r\n""<>|]*?\.dll|[\w.\-]+\.dll",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public static bool IsUnsignedAddInDialog(DialogInfo dialog)
        {
            var title = dialog.Title ?? string.Empty;
            return TitleMarkers.Any(m => title.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Names come only from name-labelled lines. Paths and file names come from the other lines, so the add-in's
        // self-declared name can never satisfy a path condition.
        public static AddInIdentity Identify(DialogInfo dialog)
        {
            var names = new List<string>();
            var paths = new List<string>();
            var files = new List<string>();
            try
            {
                foreach (var line in (dialog.Text ?? string.Empty).Split('\n'))
                {
                    var nameMatch = NameLine.Match(line.TrimEnd('\r'));
                    if (nameMatch.Success) { names.Add(nameMatch.Groups["v"].Value.Trim()); continue; }
                    var labelled = PathLine.Match(line.TrimEnd('\r'));
                    if (labelled.Success) AddPathOrFile(labelled.Groups["v"].Value.Trim(), paths, files);
                    foreach (Match m in DllPath.Matches(line)) AddPathOrFile(m.Value.Trim(), paths, files);
                }
            }
            catch (RegexMatchTimeoutException) { }
            return new AddInIdentity(Clean(names), Clean(paths), Clean(files));
        }

        // Every identifying string, for display in a pending entry.
        public static IReadOnlyList<string> Extract(DialogInfo dialog)
        {
            var identity = Identify(dialog);
            return Clean(identity.Names.Concat(identity.Paths).Concat(identity.Files));
        }

        private static void AddPathOrFile(string value, List<string> paths, List<string> files)
        {
            var slash = value.LastIndexOfAny(new[] { '\\', '/' });
            if (slash < 0) { files.Add(value); return; }
            paths.Add(value);
            files.Add(value.Substring(slash + 1));
        }

        private static List<string> Clean(IEnumerable<string> values) =>
            values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
