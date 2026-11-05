using System;
using System.IO;
using RevitHarness.Dialogs;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public sealed class AddInNameTests : IDisposable
    {
        private const string MissingAssemblyTitle = "外部ツール - 見つからないアドイン アセンブリ";
        private readonly string directory = Path.Combine(Path.GetTempPath(), "RevitHarness.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static DialogInfo Dialog(string title, string text) => new DialogInfo
        {
            Title = title, Text = text, AutomationId = "TaskDialog",
            ButtonDetails = { new DialogButton { Name = "OK", AutomationId = "IDOK" } }
        };

        private static string MissingAssembly(string name, string path = @"C:\Addins\Sample.dll") =>
            $"アセンブリ “{path}” が存在しないため、アドイン “{name}” の初期化に失敗しました。";

        private DialogDecider Decider() => new DialogDecider(new DecisionContext { Pid = 42 }, directory);

        private AllowRule Learn(DialogInfo dialog)
        {
            var first = Decider().Decide(dialog, DialogSource.Watcher, hwnd: 5);
            Assert.False(first.ShouldPress);
            Assert.NotNull(first.PendingId);
            var store = new PendingStore(directory);
            Assert.True(store.RequestDecision(first.PendingId!, "OK", "IDOK", "test-agent", saveRule: true).Ok);
            Assert.NotNull(store.ClaimDecision(first.PendingId!));
            var done = store.Complete(first.PendingId!, true)!;
            Assert.Equal(PendingStatus.Resolved, done.Status);
            var rule = Assert.Single(AllowlistLoader.Load(directory).Rules);
            Assert.Equal(done.Resolution!.RuleId, rule.Id);
            Assert.Equal(DialogKey.From(dialog), rule.Key);
            return rule;
        }

        [Theory]
        [InlineData(MissingAssemblyTitle, "アセンブリ “C:\\Addins\\Sample.dll” が存在しないため、アドイン “{0}” の初期化に失敗しました。")]
        [InlineData("外部ツール", "トランザクション属性が割り当てられていないため、アドイン \"{0}\" を実行できませんでした。詳細を確認してください。")]
        [InlineData("External Tools - Missing Assembly", "Assembly \"C:\\Addins\\Sample.dll\" does not exist; add-in \"{0}\" failed to initialize.")]
        [InlineData("External Tool - Transaction", "Add-in ‘{0}’ cannot execute because no transaction attribute is assigned.")]
        public void LearnedRuleMatchesAnotherAddInAndPendingEntriesStaySeparate(string title, string body)
        {
            var first = Dialog(title, string.Format(body, "SampleAlpha"));
            var second = Dialog(title, string.Format(body, "SampleBeta"));
            Assert.Equal(DialogKey.From(first), DialogKey.From(second));

            var decider = Decider();
            var firstPending = decider.Decide(first, DialogSource.Watcher, hwnd: 5);
            var secondPending = decider.Decide(second, DialogSource.Watcher, hwnd: 5);
            Assert.NotEqual(firstPending.PendingId, secondPending.PendingId);
            Assert.Equal(2, new PendingStore(directory).List(true).Count);
            var reseen = decider.Decide(first, DialogSource.Watcher, hwnd: 5);
            Assert.Equal(firstPending.PendingId, reseen.PendingId);

            var rule = Learn(first);
            var result = decider.Decide(second, DialogSource.Watcher);
            Assert.True(result.ShouldPress);
            Assert.Equal("OK", result.Button!.Name);
            Assert.Equal("rule", result.Record.DecidedBy);
            Assert.Equal(rule.Id, result.Record.MatchedRuleId);
            Assert.Null(result.PendingId);
        }

        [Theory]
        [InlineData("title")]
        [InlineData("path")]
        [InlineData("sentence")]
        [InlineData("buttons")]
        public void LearnedRuleKeepsOtherDialogDifferencesDistinct(string difference)
        {
            var title = difference == "title" ? "RevitHarness - x" : MissingAssemblyTitle;
            var first = Dialog(title, MissingAssembly("SampleAlpha"));
            var second = Dialog(title, MissingAssembly("SampleBeta"));
            if (difference == "path") second.Text = MissingAssembly("SampleBeta", @"C:\Addins\Other.dll");
            if (difference == "sentence") second.Text += " 詳細を確認してください。";
            if (difference == "buttons") second.ButtonDetails.Add(new DialogButton { Name = "Cancel", AutomationId = "IDCANCEL" });

            Assert.NotEqual(DialogKey.From(first), DialogKey.From(second));
            Learn(first);
            var result = Decider().Decide(second, DialogSource.Watcher, hwnd: 5);
            Assert.False(result.ShouldPress);
            Assert.Equal("pending", result.Record.DecidedBy);
            Assert.Equal("unknown", result.Record.MatchedRuleId);
        }

        [Theory]
        [InlineData("\"Sample\"", "\"<addin>\"")]
        [InlineData("“Sample”", "“<addin>”")]
        [InlineData("‘Sample’", "‘<addin>’")]
        [InlineData("「Sample」", "「<addin>」")]
        public void SupportedQuotesPreserveSurroundingText(string token, string normalized) =>
            Assert.Equal("Add-in " + normalized + " failed.",
                AddInNameNormalizer.Normalize(Dialog("  eXtErNaL tOoLs  ", "  Add-in  " + token + " failed.\n")));

        [Theory]
        [InlineData("\"C:\\Addins\\Sample.dll\"")]
        [InlineData("“/opt/addins/Sample.dll”")]
        [InlineData("‘Sample.DLL’")]
        [InlineData("「Sample.ExE」")]
        [InlineData("\"Sample.ADDIN\"")]
        [InlineData("\"relative\\name\"")]
        [InlineData("“relative/name”")]
        [InlineData("\"\"")]
        [InlineData("\"Sample\nAlpha\"")]
        [InlineData("“Sample\rAlpha”")]
        [InlineData("「Sample\u2028Alpha」")]
        public void IneligibleTokensAreOnlyWhitespaceNormalized(string token)
        {
            var body = "Add-in " + token + " failed.";
            Assert.Equal(DialogKey.NormalizeText(body), AddInNameNormalizer.Normalize(Dialog(MissingAssemblyTitle, body)));
        }

        [Fact]
        public void NameLengthIsLimitedBeforeWhitespaceNormalization()
        {
            var accepted = "add-in \"" + new string('a', 200) + "\"";
            var rejected = "add-in \"" + new string('a', 201) + "\"";
            Assert.Equal("add-in \"<addin>\"", AddInNameNormalizer.Normalize(Dialog("External Tool", accepted)));
            Assert.Equal(rejected, AddInNameNormalizer.Normalize(Dialog("External Tool", rejected)));
        }

        [Fact]
        public void OnlyTheTokenAfterAddInIsTheName()
        {
            string Body(string name, string detail) => $"Add-in “{name}” failed: “{detail}”.";
            var first = Dialog(MissingAssemblyTitle, Body("SampleAlpha", "permission denied"));
            Assert.Equal(DialogKey.From(first), DialogKey.From(Dialog(MissingAssemblyTitle, Body("SampleBeta", "permission denied"))));
            Assert.NotEqual(DialogKey.From(first), DialogKey.From(Dialog(MissingAssemblyTitle, Body("SampleBeta", "invalid configuration"))));
        }

        [Fact]
        public void PendingDecisionIsRejectedWhenAnotherAddInTookTheWindow()
        {
            var alpha = Dialog(MissingAssemblyTitle, MissingAssembly("SampleAlpha"));
            var beta = Dialog(MissingAssemblyTitle, MissingAssembly("SampleBeta"));
            Assert.False(DialogKey.SameBody(alpha, beta));
            Assert.True(DialogKey.SameBody(alpha, Dialog(MissingAssemblyTitle, "  " + MissingAssembly("SampleAlpha") + "\n")));
        }

        [Fact]
        public void MissingAssemblyPathStaysVerbatimWhileNameIsReplaced() =>
            Assert.Equal(MissingAssembly("<addin>"),
                AddInNameNormalizer.Normalize(Dialog(MissingAssemblyTitle, MissingAssembly("SampleAlpha"))));
    }
}
