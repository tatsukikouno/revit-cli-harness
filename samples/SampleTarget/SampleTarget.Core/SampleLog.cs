using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SampleTarget.Core
{
    public static class BuildInfo
    {
        public static string Tag => typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "BuildTag").Value ?? "";
    }

    public static class SampleLog
    {
        public static void Write(string kind, string tag, string coreTag)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitHarness", "sample-target");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "log.jsonl"),
                "{\"kind\":" + Quote(kind) + ",\"tag\":" + Quote(tag) + ",\"coreTag\":" + Quote(coreTag) + "}\n",
                new UTF8Encoding(false));
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (var c in value)
            {
                if (c == '"' || c == '\\') result.Append('\\').Append(c);
                else if (c < ' ') result.Append("\\u").Append(((int)c).ToString("x4"));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }
    }
}
