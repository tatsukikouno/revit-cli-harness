#nullable enable

using RevitHarness.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitHarness.Addin.DialogHandling
{
    public static class DialogLogReader
    {
        public const int MaxLines = 1000;

        // Newest-last list of up to `count` raw JSONL lines, read from the most recent daily files.
        public static List<string> ReadRecent(int count, string? baseDirectory = null)
        {
            count = Math.Max(1, Math.Min(count, MaxLines));
            var lines = new List<string>();
            var directory = DialogPaths.GetDirectory(baseDirectory);
            if (!Directory.Exists(directory)) return lines;
            var files = Directory.GetFiles(directory, "dialogs_*.jsonl").OrderByDescending(f => f, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var chunk = new List<string>();
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null) if (line.Length > 0) chunk.Add(line);
                }
                catch (IOException) { continue; }
                lines.InsertRange(0, chunk.Skip(Math.Max(0, chunk.Count - (count - lines.Count))));
                if (lines.Count >= count) break;
            }
            return lines;
        }
    }
}
