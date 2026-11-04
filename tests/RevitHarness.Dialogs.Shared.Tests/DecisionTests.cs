using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitHarness.Dialogs;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public sealed class DecisionTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "RevitHarness.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static DialogInfo Dialog(string title = "Warning", string text = "Something  happened", params (string n, string id)[] buttons) =>
            new DialogInfo
            {
                Title = title, Text = text, AutomationId = "TaskDialog",
                ButtonDetails = (buttons.Length == 0 ? new[] { ("OK", "IDOK"), ("Delete", "CommandLink1") } : buttons)
                    .Select(b => new DialogButton { Name = b.Item1, AutomationId = b.Item2 }).ToList()
            };

        private DialogDecider Decider() => new DialogDecider(new DecisionContext { Pid = 42, TaskId = "T1" }, directory);

        private AllowRule[] Rules() => AllowlistLoader.Load(directory).Rules.ToArray();

        [Fact]
        public void ButtonOrderAndWhitespaceDoNotChangeKey()
        {
            var a = DialogKey.From(Dialog(text: "a  b\n c"));
            var b = DialogKey.From(new DialogInfo
            {
                Title = "Warning", Text = "a b c", AutomationId = "TaskDialog",
                ButtonDetails = Dialog().ButtonDetails.AsEnumerable().Reverse().ToList()
            });
            Assert.Equal(a, b);
        }

        [Theory]
        [InlineData("OK", "IDOK", true)]
        [InlineData("Cancel", "IDCANCEL", true)]
        [InlineData("&Close", null, true)]
        [InlineData("キャンセル", null, true)]
        [InlineData(null, "IDCLOSE", true)]
        [InlineData("OK", "CommandLink1", false)]
        [InlineData("Delete", "IDOK", false)]
        [InlineData("閉じる(C)", "CommandButton_8", true)]
        [InlineData("Delete", "CommandButton_8", false)]
        [InlineData("閉じる", "CommandButton_1001", false)]
        [InlineData("Save", null, false)]
        [InlineData("Overwrite", "IDYES", false)]
        [InlineData(null, null, false)]
        public void ReversibilityIsDecidedByAllowSets(string? name, string? id, bool expected) =>
            Assert.Equal(expected, Reversibility.IsReversible(new DialogButton { Name = name, AutomationId = id }));

        [Fact]
        public void ActionReversibilityChecksResultCode()
        {
            Assert.True(Reversibility.IsReversible(new DialogAction { ResultCode = "Cancel" }));
            Assert.False(Reversibility.IsReversible(new DialogAction { ResultCode = "Yes" }));
            Assert.False(Reversibility.IsReversible(new DialogAction { ResultCode = "Cancel", ButtonName = "Delete" }));
        }

        [Fact]
        public void LogContainsRequiredFieldsAndOutcomeLine()
        {
            var decider = Decider();
            var decision = decider.Decide(Dialog(), DialogSource.Api, "shot.png");
            decider.LogOutcome(decision, true, "Next");
            var lines = File.ReadAllLines(Directory.GetFiles(DialogPaths.GetDirectory(directory), "dialogs_*.jsonl").Single());
            Assert.Equal(2, lines.Length);
            var root = JsonDocument.Parse(lines[0]).RootElement;
            Assert.Equal(42, root.GetProperty("pid").GetInt32());
            Assert.Equal("T1", root.GetProperty("taskId").GetString());
            Assert.Equal("pending", root.GetProperty("decidedBy").GetString());
            Assert.Equal(decision.PendingId, root.GetProperty("pendingId").GetString());
            Assert.Equal("unknown", root.GetProperty("matchedRuleId").GetString());
            Assert.Equal("shot.png", root.GetProperty("screenshotPath").GetString());
            Assert.Equal("api", root.GetProperty("source").GetString());
            Assert.True(root.TryGetProperty("key", out _));
            var outcome = JsonDocument.Parse(lines[1]).RootElement;
            Assert.Equal("outcome", outcome.GetProperty("kind").GetString());
            Assert.Equal("Next", outcome.GetProperty("outcome").GetProperty("nextDialogTitle").GetString());
        }

        [Fact]
        public void UnknownDialogPressesNothingAndRegistersPending()
        {
            var result = Decider().Decide(Dialog(), DialogSource.Watcher, "s.png", "d.json", hwnd: 99, dialogPid: 7);
            Assert.False(result.ShouldPress);
            Assert.Equal("pending", result.Record.DecidedBy);
            Assert.Equal("none", result.Record.ActionTaken);
            var pending = Assert.Single(new PendingStore(directory).List(true));
            Assert.Equal(result.PendingId, pending.Id);
            Assert.Equal(7, pending.Pid);
            Assert.Equal(99, pending.Hwnd);
            Assert.Equal("Warning", pending.Title);
            Assert.Equal("TaskDialog", pending.AutomationId);
            Assert.Equal("s.png", pending.ScreenshotPath);
            Assert.Equal("d.json", pending.DumpPath);
            Assert.Equal(new[] { "OK", "Delete" }, pending.Buttons.Select(b => b.Name));
            Assert.Empty(Rules());
        }

        [Fact]
        public void SameDialogSeenTwiceReusesOnePendingEntry()
        {
            var decider = Decider();
            decider.Decide(Dialog(), DialogSource.Watcher, hwnd: 5, dialogPid: 7);
            decider.Decide(Dialog(), DialogSource.Watcher, hwnd: 5, dialogPid: 7);
            Assert.Single(new PendingStore(directory).List(true));
        }

        [Fact]
        public void NoLearnRuleMatchedDialogIsPendingNotPressed()
        {
            var rule = new AllowRule
            {
                Id = "under-test", Target = DialogTarget.Both, NoLearn = true,
                Match = new DialogMatch { Title = new TextPattern { Value = "Warning", Mode = TextPatternMode.Equals } },
                Action = new DialogAction { ButtonName = "Delete" }
            };
            Assert.True(AllowlistStore.TryAddRule(rule, directory));
            var result = Decider().Decide(Dialog(), DialogSource.Api);
            Assert.False(result.ShouldPress);
            Assert.Equal("under-test", result.Record.MatchedRuleId);
            Assert.Equal("pending", result.Record.DecidedBy);
            Assert.Equal("under-test", new PendingStore(directory).Get(result.PendingId!)!.NoLearnRuleId);
        }

        [Fact]
        public async Task ConcurrentAddsFromManyWritersAllSurviveAndFileStaysValid()
        {
            await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() =>
                Assert.True(AllowlistStore.TryAddRule(new AllowRule { Id = "r" + i, Match = new DialogMatch { Title = new TextPattern { Value = "t" + i } } }, directory)))));
            var result = AllowlistLoader.Load(directory);
            Assert.Null(result.Error);
            Assert.Equal(40, result.Rules.Select(r => r.Id).Distinct().Count());
            Assert.Empty(Directory.GetFiles(DialogPaths.GetDirectory(directory), "*.tmp"));
        }

        [Fact]
        public void StoreSupportsRemoveAndUpdateAndRefusesToOverwriteCorruptFile()
        {
            Assert.True(AllowlistStore.TryAddRule(new AllowRule { Id = "a" }, directory));
            Assert.True(AllowlistStore.TryUpdateRule("a", r => r.Enabled = false, directory));
            Assert.False(Rules().Single().Enabled);
            Assert.True(AllowlistStore.TryRemoveRule("a", directory));
            Assert.Empty(Rules());
            File.WriteAllText(DialogPaths.GetAllowlistPath(directory), "{broken");
            Assert.False(AllowlistStore.TryAddRule(new AllowRule { Id = "b" }, directory));
            Assert.Equal("{broken", File.ReadAllText(DialogPaths.GetAllowlistPath(directory)));
        }

    }
}
