using RevitHarness.Cli;
using System;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public class ExitCodeTests
    {
        private static readonly string[] NoDialogs = Array.Empty<string>();

        [Theory]
        [InlineData(FailureKind.ConnectionFailed, 2, "CONNECTION_FAILED")]
        [InlineData(FailureKind.HarnessNotReady, 3, "HARNESS_NOT_READY")]
        [InlineData(FailureKind.Timeout, 4, "TIMEOUT")]
        public void EachKindHasItsOwnExitCodeAndStderrToken(FailureKind kind, int exit, string token)
        {
            var report = HarnessFailure.Describe(new HarnessFailureException(kind, "why"), NoDialogs);

            Assert.Equal(exit, report.ExitCode);
            Assert.Equal(token, report.Token);
            Assert.Equal($"revit-harness: {token}: why", report.StderrLine);
        }

        [Fact]
        public void TimeoutWithAPendingDialogIsDialogBlockedAndNamesTheDialogs()
        {
            var report = HarnessFailure.Describe(new HarnessFailureException(FailureKind.Timeout, "no answer"), new[] { "abc123", "def456" });

            Assert.Equal(5, report.ExitCode);
            Assert.StartsWith("revit-harness: DIALOG_BLOCKED:", report.StderrLine);
            Assert.Contains("abc123, def456", report.Message);
        }

        [Fact]
        public void PendingDialogsDoNotChangeConnectionFailure()
        {
            var pending = new[] { "abc123" };

            Assert.Equal(2, HarnessFailure.Describe(new HarnessFailureException(FailureKind.ConnectionFailed, "x"), pending).ExitCode);
            Assert.Equal(3, HarnessFailure.Describe(new HarnessFailureException(FailureKind.HarnessNotReady, "x"), NoDialogs).ExitCode);
        }

        [Fact]
        public void StillStartingWithAPendingDialogIsDialogBlocked()
        {
            var report = HarnessFailure.Describe(new HarnessFailureException(FailureKind.HarnessNotReady, "starting"), new[] { "abc123" });

            Assert.Equal(5, report.ExitCode);
        }

        [Fact]
        public void OnlyPendingDialogsOfRunningProcessesCountAsBlocking()
        {
            var open = new (string Id, int? Pid)[] { ("live", 100), ("exited", 200), ("unknown-pid", null) };

            var ids = HarnessFailure.LiveDialogIds(open, pid => pid == 100);

            Assert.Equal(new[] { "live" }, ids);
        }
    }
}
