using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RevitHarness.Dialogs
{
    public sealed class AllowlistLoadResult
    {
        public IReadOnlyList<AllowRule> Rules { get; }
        public string? Error { get; }

        public AllowlistLoadResult(IReadOnlyList<AllowRule> rules, string? error = null)
        {
            Rules = rules;
            Error = error;
        }
    }

    public static class AllowlistLoader
    {
        public static AllowlistLoadResult Load(string? baseDirectory = null, Action<string>? reportError = null, string? allowlistPath = null)
        {
            try
            {
                using var stream = new FileStream(allowlistPath ?? DialogPaths.GetAllowlistPath(baseDirectory),
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var file = JsonSerializer.Deserialize<AllowlistFile>(stream);
                if (file == null) throw new JsonException("The allowlist must be a JSON object.");
                return new AllowlistLoadResult(file.Rules ?? new List<AllowRule>());
            }
            catch (FileNotFoundException) { return new AllowlistLoadResult(Array.Empty<AllowRule>()); }
            catch (DirectoryNotFoundException) { return new AllowlistLoadResult(Array.Empty<AllowRule>()); }
            catch (Exception exception)
            {
                try { reportError?.Invoke(exception.Message); }
                catch { /* Diagnostics must not interrupt dialog handling. */ }
                return new AllowlistLoadResult(Array.Empty<AllowRule>(), exception.Message);
            }
        }
    }
}
