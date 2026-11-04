using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RevitHarness.Dialogs
{
    public sealed class DialogLogger
    {
        private readonly string? baseDirectory;

        public DialogLogger(string? baseDirectory = null) => this.baseDirectory = baseDirectory;

        public void Log(DialogLogRecord record)
        {
            try
            {
                var directory = DialogPaths.GetDirectory(baseDirectory);
                var path = Path.Combine(directory, "dialogs_" + record.Timestamp.UtcDateTime.ToString("yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");
                record.Timestamp = record.Timestamp.ToUniversalTime();
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
                using var hash = SHA256.Create();
                var key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                    Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
                // Append handles have independent positions; serialize writers across processes as well as threads.
                using var mutex = new Mutex(false, "RevitHarness.Dialogs." + key);
                var acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) return;
                    Directory.CreateDirectory(directory);
                    for (var attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                            stream.Write(bytes, 0, bytes.Length);
                            return;
                        }
                        catch (IOException) when (attempt < 4) { Thread.Sleep(20 * (attempt + 1)); }
                    }
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
            catch { /* Logging must never interrupt the host application. */ }
        }
    }
}
