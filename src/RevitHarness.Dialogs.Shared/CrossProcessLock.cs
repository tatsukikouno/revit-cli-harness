using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RevitHarness.Dialogs
{
    internal sealed class CrossProcessLock : IDisposable
    {
        private readonly Mutex mutex;
        private bool held;

        private CrossProcessLock(Mutex mutex, bool held) { this.mutex = mutex; this.held = held; }

        public bool Held => held;

        public static CrossProcessLock Acquire(string path, TimeSpan timeout)
        {
            using var sha = SHA256.Create();
            var key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
            var mutex = new Mutex(false, "RevitHarness.Dialogs.Store." + key);
            var acquired = false;
            try { acquired = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { acquired = true; }
            return new CrossProcessLock(mutex, acquired);
        }

        public void Dispose()
        {
            if (held) { held = false; try { mutex.ReleaseMutex(); } catch (ApplicationException) { } }
            mutex.Dispose();
        }
    }
}
