#nullable enable

using RevitHarness.Addin.Ipc;
using RevitHarness.Dialogs;
using System.Collections.Generic;
using System.Diagnostics;

namespace RevitHarness.Addin.DialogHandling
{
    /// <summary>
    /// dialog-log and reload-allowlist only read files, so they run on the pipe thread and stay available while a dialog blocks Revit.
    /// </summary>
    public static class DialogIpcHandler
    {
        public const int DefaultCount = 50;

        public static IpcResponse? TryHandle(IpcRequest request)
        {
            switch (request.Op)
            {
                case Operations.DialogLog: return DialogLog(request);
                case Operations.ReloadAllowlist: return ReloadAllowlist(request);
                default: return null;
            }
        }

        private static IpcResponse DialogLog(IpcRequest request)
        {
            var sw = Stopwatch.StartNew();
            var count = request.GetPayloadValue<int?>("count") ?? DefaultCount;
            var lines = DialogLogReader.ReadRecent(count);
            return IpcResponse.Success(request.Id, new { count = lines.Count, path = DialogPaths.GetDirectory(), records = lines }, sw.ElapsedMilliseconds);
        }

        // Rules are read from disk on every decision, so this validates the file and reports what the next dialog will see.
        private static IpcResponse ReloadAllowlist(IpcRequest request)
        {
            var sw = Stopwatch.StartNew();
            var result = AllowlistLoader.Load();
            var path = DialogPaths.GetAllowlistPath();
            if (result.Error != null)
                return IpcResponse.Failure(request.Id, ErrorCodes.InvalidRequest, "allowlist unreadable: " + result.Error, sw.ElapsedMilliseconds, new { path });
            var ids = new List<string>();
            foreach (var rule in result.Rules) ids.Add(rule.Id);
            return IpcResponse.Success(request.Id, new { path, ruleCount = ids.Count, ruleIds = ids }, sw.ElapsedMilliseconds);
        }
    }
}
