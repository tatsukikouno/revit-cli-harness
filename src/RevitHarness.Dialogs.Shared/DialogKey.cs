using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RevitHarness.Dialogs
{
    public sealed class DialogKey : IEquatable<DialogKey>
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("automationId")]
        public string AutomationId { get; set; } = string.Empty;
        [JsonPropertyName("buttons")]
        public List<string> Buttons { get; set; } = new List<string>();
        [JsonPropertyName("textHash")]
        public string TextHash { get; set; } = string.Empty;

        public static DialogKey From(DialogInfo dialog)
        {
            var buttons = dialog.ButtonDetails.Count > 0
                ? dialog.ButtonDetails.Select(b => Part(b.Name) + "|" + Part(b.AutomationId))
                : dialog.Buttons.Select(b => Part(b) + "|");
            return new DialogKey
            {
                Title = Part(dialog.Title),
                AutomationId = Part(dialog.AutomationId),
                Buttons = buttons.OrderBy(b => b, StringComparer.Ordinal).ToList(),
                TextHash = Hash(AddInNameNormalizer.Normalize(dialog))
            };
        }

        public static string NormalizeText(string? text) => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

        // A learned rule ignores the add-in name; a pending entry and its decision must still name the very dialog that was reviewed.
        public static bool SameBody(DialogInfo a, DialogInfo b) => NormalizeText(a.Text) == NormalizeText(b.Text);

        private static string Part(string? value) => (value ?? string.Empty).Trim();

        private static string Hash(string value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        public string Fingerprint()
        {
            using var sha = SHA256.Create();
            var canonical = string.Join("\n", new[] { Title, AutomationId, TextHash }.Concat(Buttons));
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        }

        public bool Equals(DialogKey? other) => other != null && Title == other.Title &&
            AutomationId == other.AutomationId && TextHash == other.TextHash && Buttons.SequenceEqual(other.Buttons);

        public override bool Equals(object? obj) => Equals(obj as DialogKey);
        public override int GetHashCode() => Fingerprint().GetHashCode();
    }
}
