#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitHarness.Cli
{
    /// <summary>Why a request never produced an operation result. Each kind has its own exit code and stderr token.</summary>
    public enum FailureKind
    {
        ConnectionFailed,
        HarnessNotReady,
        Timeout,
        DialogBlocked
    }

    /// <summary>Thrown by the pipe client for every way the harness could not be asked or did not answer.</summary>
    public sealed class HarnessFailureException : Exception
    {
        public HarnessFailureException(FailureKind kind, string message, Exception? inner = null) : base(message, inner)
        {
            Kind = kind;
        }

        public FailureKind Kind { get; }
    }

    public readonly struct FailureReport
    {
        public FailureReport(int exitCode, string token, string message)
        {
            ExitCode = exitCode;
            Token = token;
            Message = message;
        }

        public int ExitCode { get; }
        public string Token { get; }
        public string Message { get; }

        /// <summary>The first stderr line: stable and machine-greppable.</summary>
        public string StderrLine => $"revit-harness: {Token}: {Message}";
    }

    public static class HarnessFailure
    {
        public const int ConnectionFailedExit = 2;
        public const int HarnessNotReadyExit = 3;
        public const int TimeoutExit = 4;
        public const int DialogBlockedExit = 5;

        /// <summary>
        /// A request that timed out while a dialog is pending is dialog-blocked: a modal dialog stops Revit's main thread.
        /// </summary>
        public static FailureReport Describe(HarnessFailureException failure, IReadOnlyCollection<string> openPendingDialogIds)
        {
            if (failure.Kind == FailureKind.Timeout && openPendingDialogIds.Count > 0)
                return new FailureReport(DialogBlockedExit, "DIALOG_BLOCKED",
                    $"Revit did not answer and {openPendingDialogIds.Count} dialog(s) are pending (ids: {string.Join(", ", openPendingDialogIds)}). Run 'revit-harness dialog-pending', then 'revit-harness dialog-decide'. {failure.Message}");

            switch (failure.Kind)
            {
                case FailureKind.ConnectionFailed:
                    return new FailureReport(ConnectionFailedExit, "CONNECTION_FAILED", failure.Message);
                case FailureKind.HarnessNotReady:
                    return new FailureReport(HarnessNotReadyExit, "HARNESS_NOT_READY", failure.Message);
                case FailureKind.Timeout:
                    return new FailureReport(TimeoutExit, "TIMEOUT", failure.Message);
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
        }

        /// <summary>
        /// Pending entries that can block the connected Revit: an entry whose process has exited (a crash, or a dialog
        /// closed while no watcher ran) is stale and never explains a timeout.
        /// </summary>
        public static IReadOnlyCollection<string> LiveDialogIds(IEnumerable<(string Id, int? Pid)> open, Func<int, bool> isAlive) =>
            open.Where(d => d.Pid == null || isAlive(d.Pid.Value)).Select(d => d.Id).ToArray();
    }
}
