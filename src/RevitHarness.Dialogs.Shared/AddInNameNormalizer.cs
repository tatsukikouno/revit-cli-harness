using System;
using System.Text.RegularExpressions;

namespace RevitHarness.Dialogs
{
    public static class AddInNameNormalizer
    {
        private static readonly Regex QuotedToken = new Regex(
            "\"(?<name>[^\"\\r\\n\\u0085\\u2028\\u2029]*)\"|“(?<name>[^”\\r\\n\\u0085\\u2028\\u2029]*)”|‘(?<name>[^’\\r\\n\\u0085\\u2028\\u2029]*)’|「(?<name>[^」\\r\\n\\u0085\\u2028\\u2029]*)」",
            RegexOptions.CultureInvariant);

        public static string Normalize(DialogInfo dialog)
        {
            var title = (dialog.Title ?? string.Empty).Trim();
            if (!title.StartsWith("External Tool", StringComparison.OrdinalIgnoreCase) &&
                !title.StartsWith("外部ツール", StringComparison.OrdinalIgnoreCase))
                return DialogKey.NormalizeText(dialog.Text);

            var text = QuotedToken.Replace(dialog.Text ?? string.Empty, match =>
            {
                var name = match.Groups["name"].Value;
                if (name.Length < 1 || name.Length > 200 || name.IndexOfAny(new[] { '\\', '/' }) >= 0 ||
                    name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".addin", StringComparison.OrdinalIgnoreCase))
                    return match.Value;

                return match.Value.Substring(0, 1) + "<addin>" + match.Value.Substring(match.Value.Length - 1);
            });
            return DialogKey.NormalizeText(text);
        }
    }
}
