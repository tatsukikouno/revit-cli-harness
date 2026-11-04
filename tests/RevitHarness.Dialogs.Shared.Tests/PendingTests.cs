using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using RevitHarness.Dialogs;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public sealed class PendingTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "RevitHarness.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static DialogInfo Dialog(string title = "Sample warning", string text = "Sample body", params (string n, string id)[] buttons) =>
            new DialogInfo
            {
                Title = title, Text = text, AutomationId = "TaskDialog",
                ButtonDetails = (buttons.Length == 0 ? new[] { ("OK", "CommandButton_1"), ("Overwrite", "CommandButton_1001") } : buttons)
                    .Select(b => new DialogButton { Name = b.Item1, AutomationId = b.Item2 }).ToList()
            };

        private PendingStore Store => new PendingStore(directory);

        private string Register(DialogInfo dialog, long hwnd = 10)
        {
            var decision = new DialogDecider(new DecisionContext { Pid = 1 }, directory)
                .Decide(dialog, DialogSource.Watcher, hwnd: hwnd, dialogPid: 7);
            return decision.PendingId!;
        }

        private AllowRule[] Rules() => AllowlistLoader.Load(directory).Rules.ToArray();

        [Fact]
        public void NonexistentButtonIsRejectedAndNothingIsQueued()
        {
            var id = Register(Dialog());
            var result = Store.RequestDecision(id, "Delete everything", null, "agent-x", false);
            Assert.False(result.Ok);
            Assert.Contains("button not in dialog", result.Error);
            Assert.Equal(PendingStatus.Pending, Store.Get(id)!.Status);
            Assert.False(Store.RequestDecision(id, "OK", "CommandButton_1001", "agent-x", false).Ok);
            Assert.False(Store.RequestDecision(id, null, "nope", "agent-x", false).Ok);
        }

        [Fact]
        public void DeciderNameIsRequired()
        {
            var id = Register(Dialog());
            Assert.False(Store.RequestDecision(id, "OK", null, " ", false).Ok);
            Assert.False(Store.RequestDecision(id, "OK", null, null, false).Ok);
        }

        [Fact]
        public void ValidDecisionIsQueuedThenCompletedAndLoggedAsExternal()
        {
            var id = Register(Dialog());
            var request = Store.RequestDecision(id, "OK", "CommandButton_1", "model-y", false);
            Assert.True(request.Ok);
            Assert.Equal(PendingStatus.DecisionRequested, Store.Get(id)!.Status);
            Assert.False(Store.RequestDecision(id, "OK", null, "model-y", false).Ok);

            Store.ClaimDecision(id);

            var done = Store.Complete(id, true)!;
            Assert.Equal(PendingStatus.Resolved, done.Status);
            Assert.Equal("model-y", done.Resolution!.By);
            Assert.Empty(Rules());
            var line = File.ReadAllLines(Directory.GetFiles(DialogPaths.GetDirectory(directory), "dialogs_*.jsonl").Single()).Last();
            var root = JsonDocument.Parse(line).RootElement;
            Assert.Equal("external", root.GetProperty("decidedBy").GetString());
            Assert.Equal("model-y", root.GetProperty("deciderName").GetString());
            Assert.Equal(id, root.GetProperty("pendingId").GetString());
            Assert.Equal("OK", root.GetProperty("pressedButton").GetProperty("name").GetString());
        }

        [Fact]
        public void SaveRuleKeepsReversibleButtonAndLaterDialogMatchesWithoutPending()
        {
            var id = Register(Dialog());
            Assert.True(Store.RequestDecision(id, "OK", null, "model-y", saveRule: true).Ok);
            Store.ClaimDecision(id);
            var done = Store.Complete(id, true)!;
            var rule = Assert.Single(Rules());
            Assert.Equal(done.Resolution!.RuleId, rule.Id);
            Assert.Equal("external:model-y", rule.CreatedBy);

            var again = new DialogDecider(new DecisionContext(), directory).Decide(Dialog(), DialogSource.Watcher);
            Assert.True(again.ShouldPress);
            Assert.Equal("rule", again.Record.DecidedBy);
        }

        [Fact]
        public void IrreversibleButtonIsPressedButNeverLearned()
        {
            var id = Register(Dialog());
            Assert.True(Store.RequestDecision(id, "Overwrite", null, "m", saveRule: true).Ok);
            Store.ClaimDecision(id);
            var done = Store.Complete(id, true)!;
            Assert.Equal(PendingStatus.Resolved, done.Status);
            Assert.Empty(Rules());
            Assert.Contains("not known to be reversible", done.Resolution!.RuleNotSavedReason);
            Assert.DoesNotContain("--force", done.Resolution.RuleNotSavedReason);
        }

        [Fact]
        public void DecisionRequestHasNoForceOverride()
        {
            Assert.Null(typeof(PendingDecisionRequest).GetProperty("Force"));
            Assert.DoesNotContain(typeof(PendingStore).GetMethod(nameof(PendingStore.RequestDecision))!.GetParameters(), p => p.Name == "force");
        }

        [Fact]
        public void NoLearnRuleBlocksSaving()
        {
            Assert.True(AllowlistStore.TryAddRule(new AllowRule
            {
                Id = "tester-target", Target = DialogTarget.Both, NoLearn = true,
                Match = new DialogMatch { Title = new TextPattern { Value = "Sample warning", Mode = TextPatternMode.Equals } },
                Action = new DialogAction { ButtonName = "OK" }
            }, directory));
            var id = Register(Dialog());
            Assert.True(Store.RequestDecision(id, "OK", null, "m", saveRule: true).Ok);
            Store.ClaimDecision(id);
            var done = Store.Complete(id, true)!;
            Assert.Equal(PendingStatus.Resolved, done.Status);
            Assert.Contains("noLearn", done.Resolution!.RuleNotSavedReason);
            Assert.Single(Rules());
        }

        [Fact]
        public void SecurityDialogChoicesAreNeverLearned()
        {
            var dialog = Dialog("Security - Unsigned Add-in", "Name: UnknownAddin\nPath: C:\\Temp\\UnknownAddin.dll",
                ("Always Load", "CommandButton_1001"), ("Load Once", "CommandButton_1002"), ("Do Not Load", "CommandButton_1003"));
            var id = Register(dialog);
            Assert.True(Store.RequestDecision(id, "Always Load", null, "m", saveRule: true).Ok);
            Store.ClaimDecision(id);
            var done = Store.Complete(id, true)!;
            Assert.Contains("security dialog", done.Resolution!.RuleNotSavedReason);
            Assert.Empty(Rules());
        }

        [Fact]
        public void FailedPressDoesNotSaveRuleAndMarksFailed()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", saveRule: true);
            Store.ClaimDecision(id);
            var done = Store.Complete(id, false, "button not found in the live window")!;
            Assert.Equal(PendingStatus.Failed, done.Status);
            Assert.Empty(Rules());
        }

        [Fact]
        public void StalePendingCannotBeDecided()
        {
            var id = Register(Dialog());
            Store.MarkStale(id, "dialog closed before a decision was applied");
            var result = Store.RequestDecision(id, "OK", null, "m", false);
            Assert.False(result.Ok);
            Assert.Contains("stale", result.Error);
            Assert.Empty(Store.List(true));
            Assert.Single(Store.List(false));
        }

        [Fact]
        public void RequestedDecisionForVanishedDialogBecomesStaleAndClearsRequest()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", false);
            Store.MarkStale(id, "gone");
            var stored = Store.Get(id)!;
            Assert.Equal(PendingStatus.Stale, stored.Status);
            Assert.Null(stored.Decision);
        }

        [Fact]
        public void WithdrawAfterClaimIsRefusedAndCompleteKeepsLogAndResult()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "model-a", saveRule: true);
            Assert.NotNull(Store.ClaimDecision(id));

            Assert.False(Store.TryWithdrawRequest(id));
            Assert.Equal(PendingStatus.Applying, Store.Get(id)!.Status);
            Assert.False(Store.RequestDecision(id, "Overwrite", null, "model-b", saveRule: true).Ok);

            var done = Store.Complete(id, true)!;
            Assert.Equal(PendingStatus.Resolved, done.Status);
            Assert.Equal("OK", done.Resolution!.PressedButton!.Name);
            Assert.Equal("model-a", done.Resolution.By);
            var line = File.ReadAllLines(Directory.GetFiles(DialogPaths.GetDirectory(directory), "dialogs_*.jsonl").Single()).Last();
            var root = JsonDocument.Parse(line).RootElement;
            Assert.Equal("model-a", root.GetProperty("deciderName").GetString());
            Assert.Equal("OK", root.GetProperty("pressedButton").GetProperty("name").GetString());
            Assert.Equal("learned-", Assert.Single(Rules()).Id.Substring(0, 8));
        }

        [Fact]
        public void WithdrawBeforeClaimStopsThePress()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", false);
            Assert.True(Store.TryWithdrawRequest(id));
            Assert.Null(Store.ClaimDecision(id));
            Assert.Equal(PendingStatus.Pending, Store.Get(id)!.Status);
        }

        [Fact]
        public async System.Threading.Tasks.Task WaitDoesNotWithdrawAClaimedRequestAndReportsItsResult()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", false);
            Store.ClaimDecision(id);
            var finisher = System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(200); Store.Complete(id, true); });
            var result = Store.WaitForResolution(id, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(5));
            await finisher;
            Assert.Equal(PendingStatus.Resolved, result!.Status);
        }

        [Fact]
        public void InvokeTimeoutIsNotAPressAndLearnsNothing()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", saveRule: true);
            Store.ClaimDecision(id);
            var error = InvokeGuard.Run(() => System.Threading.Thread.Sleep(2000), TimeSpan.FromMilliseconds(50));
            Assert.NotNull(error);
            var done = Store.Complete(id, error == null, error)!;
            Assert.Equal(PendingStatus.Failed, done.Status);
            Assert.Null(done.Resolution!.PressedButton);
            Assert.Empty(Rules());
        }

        [Fact]
        public void InvokeGuardReportsCompletionAndExceptions()
        {
            Assert.Null(InvokeGuard.Run(() => { }, TimeSpan.FromSeconds(5)));
            Assert.Equal("boom", InvokeGuard.Run(() => throw new InvalidOperationException("boom"), TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void CompleteWithoutClaimDoesNothing()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", saveRule: true);
            Assert.Equal(PendingStatus.DecisionRequested, Store.Complete(id, true)!.Status);
            Assert.Empty(Rules());
        }

        [Fact]
        public void WaitTimesOutAndWithdrawsRequestSoItCannotFireLater()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", false);
            Assert.Null(Store.WaitForResolution(id, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20)));
            var stored = Store.Get(id)!;
            Assert.Equal(PendingStatus.Pending, stored.Status);
            Assert.Null(stored.Decision);
        }

        [Fact]
        public async System.Threading.Tasks.Task WaitReturnsOnceWatcherCompletes()
        {
            var id = Register(Dialog());
            Store.RequestDecision(id, "OK", null, "m", false);
            var watcher = System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(80); Store.ClaimDecision(id); Store.Complete(id, true); });
            var result = Store.WaitForResolution(id, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(20));
            await watcher;
            Assert.Equal(PendingStatus.Resolved, result!.Status);
        }

        [Fact]
        public void WatcherEntrySupersedesApiEntryOfSameProcess()
        {
            var store = Store;
            var api = store.Register(PendingStore.From(Dialog(), DialogSource.Api, 7, null, null, null, null, null, "t"));
            Register(Dialog("Other"), 12);
            Assert.Equal(PendingStatus.Stale, store.Get(api.Id)!.Status);
        }

        [Fact]
        public void RuleAnsweredDialogSupersedesOnlyApiEntriesOfThatProcess()
        {
            var store = Store;
            var api = store.Register(PendingStore.From(Dialog(), DialogSource.Api, 7, null, null, null, null, null, "t"));
            var otherProcess = store.Register(PendingStore.From(Dialog(), DialogSource.Api, 8, null, null, null, null, null, "t"));
            store.SupersedeApiEntries(7, "watcher rule");
            Assert.Equal(PendingStatus.Stale, store.Get(api.Id)!.Status);
            Assert.Equal(PendingStatus.Pending, store.Get(otherProcess.Id)!.Status);
        }

        [Fact]
        public void ApiEntryWithoutWindowCannotBeDecided()
        {
            var api = Store.Register(PendingStore.From(Dialog(), DialogSource.Api, 7, null, null, null, null, null, "t"));
            Assert.Contains("no window handle", Store.RequestDecision(api.Id, "OK", null, "m", false).Error);
        }

        [Fact]
        public void PendingIdWithPathCharactersIsNotResolved() => Assert.Null(Store.Get("..\\allowlist"));
    }

    public sealed class UnsignedAddInTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "RevitHarness.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static DialogInfo Security(string addIn) => new DialogInfo
        {
            Title = "Security - Unsigned Add-in",
            AutomationId = "TaskDialog",
            Text = "The add-in is not signed.\nName: " + addIn + "\nPath: C:\\Addins\\" + addIn + "\\" + addIn + ".dll",
            ButtonDetails =
            {
                new DialogButton { Name = "Always Load", AutomationId = "CommandButton_1001" },
                new DialogButton { Name = "Load Once", AutomationId = "CommandButton_1002" },
                new DialogButton { Name = "Do Not Load", AutomationId = "CommandButton_1003" }
            }
        };

        private static AllowRule KnownRule(string addIn) => new AllowRule
        {
            Id = "known-" + addIn, Target = DialogTarget.Watcher,
            Match = new DialogMatch
            {
                Title = new TextPattern { Value = "Security - Unsigned Add-in", Mode = TextPatternMode.Equals },
                AutomationId = new TextPattern { Value = "TaskDialog", Mode = TextPatternMode.Equals },
                Buttons = new System.Collections.Generic.List<string> { "Do Not Load", "Load Once", "Always Load" },
                Text = new TextPattern { Value = "not signed", Mode = TextPatternMode.Contains },
                AddIn = addIn + ".dll"
            },
            Action = new DialogAction { ButtonName = "Load Once", ButtonAutomationId = "CommandButton_1002" }
        };

        private DialogDecision Decide(DialogInfo dialog) =>
            new DialogDecider(new DecisionContext { Pid = 1 }, directory).Decide(dialog, DialogSource.Watcher, hwnd: 3, dialogPid: 9);

        [Fact]
        public void ExtractsNameAndPathAndFileName()
        {
            var values = AddInInfo.Extract(Security("SampleAddin"));
            Assert.Contains("SampleAddin", values);
            Assert.Contains("C:\\Addins\\SampleAddin\\SampleAddin.dll", values);
            Assert.Contains("SampleAddin.dll", values);
            Assert.True(AddInInfo.IsUnsignedAddInDialog(Security("SampleAddin")));
            Assert.False(AddInInfo.IsUnsignedAddInDialog(new DialogInfo { Title = "Warning" }));
        }

        [Fact]
        public void KnownAddInGetsConfiguredButtonUnknownAddInBecomesPending()
        {
            Assert.True(AllowlistStore.TryAddRule(KnownRule("SampleAddin"), directory));
            var known = Decide(Security("SampleAddin"));
            Assert.True(known.ShouldPress);
            Assert.Equal("Load Once", known.Button!.Name);
            Assert.Equal("rule", known.Record.DecidedBy);

            var unknown = Decide(Security("OtherAddin"));
            Assert.False(unknown.ShouldPress);
            Assert.Equal("pending", unknown.Record.DecidedBy);
            var pending = new PendingStore(directory).Get(unknown.PendingId!)!;
            Assert.Contains("OtherAddin.dll", pending.AddIn!);
        }

        [Fact]
        public void RuleWithoutAddInConditionNeverAnswersSecurityDialog()
        {
            Assert.True(AllowlistStore.TryAddRule(new AllowRule
            {
                Id = "anything", Target = DialogTarget.Watcher,
                Match = new DialogMatch { Title = new TextPattern { Value = "Unsigned", Mode = TextPatternMode.Contains } },
                Action = new DialogAction { ButtonName = "Load Once" }
            }, directory));
            var result = Decide(Security("SampleAddin"));
            Assert.False(result.ShouldPress);
            Assert.Equal("pending", result.Record.DecidedBy);
        }

        [Fact]
        public void MatchingStaysStrictOnTitleAutomationIdButtonsAndBody()
        {
            Assert.True(AllowlistStore.TryAddRule(KnownRule("SampleAddin"), directory));
            var wrongTitle = Security("SampleAddin"); wrongTitle.Title = "Security - Unsigned Add-in (2)";
            var wrongAid = Security("SampleAddin"); wrongAid.AutomationId = "Other";
            var wrongButtons = Security("SampleAddin"); wrongButtons.ButtonDetails.RemoveAt(0);
            var wrongBody = Security("SampleAddin"); wrongBody.Text = wrongBody.Text!.Replace("not signed", "fine");
            foreach (var d in new[] { wrongTitle, wrongAid, wrongButtons, wrongBody })
                Assert.False(Decide(d).ShouldPress);
        }

        [Fact]
        public void AddInConditionMatchesPathFragment()
        {
            var rule = KnownRule("SampleAddin");
            rule.Match.AddIn = "C:\\Addins\\SampleAddin\\*";
            Assert.True(AllowlistStore.TryAddRule(rule, directory));
            Assert.True(Decide(Security("SampleAddin")).ShouldPress);
            Assert.False(Decide(Security("SampleAddinEvil")).ShouldPress);
        }

        private bool Presses(string addInCondition, string addIn)
        {
            var rule = KnownRule("x");
            rule.Match.AddIn = addInCondition;
            return DialogMatcher.FindFirstMatch(new[] { rule }, Security(addIn), DialogSource.Watcher) != null;
        }

        [Fact]
        public void AddInWithoutStarIsExactAndIgnoresCase()
        {
            Assert.True(Presses("SampleAddin", "SampleAddin"));
            Assert.True(Presses("sampleaddin", "SampleAddin"));
            Assert.False(Presses("Sample", "SampleAddin"));
            Assert.False(Presses("SampleAddin", "SampleAddin2"));
        }

        [Fact]
        public void AddInTrailingStarIsPrefixMatchIgnoringCase()
        {
            Assert.True(Presses("Contoso.*", "Contoso.Tools"));
            Assert.True(Presses("contoso.*", "Contoso.Tools"));
            Assert.False(Presses("Contoso.*", "Fabrikam.Tools"));
            Assert.False(Presses("Contoso.*", "ContosoTools"));
        }

        [Fact]
        public void LoneStarMatchesEveryAddIn()
        {
            Assert.True(Presses("*", "Contoso.Tools"));
            Assert.True(Presses("*", "Anything"));
        }

        [Fact]
        public void StarInTheMiddleIsLiteralNotWildcard()
        {
            Assert.False(Presses("Contoso.*.Tools", "Contoso.X.Tools"));
        }

        [Fact]
        public void NonMatchingAddInStaysPending()
        {
            var rule = KnownRule("x");
            rule.Match.AddIn = "Contoso.*";
            Assert.True(AllowlistStore.TryAddRule(rule, directory));
            Assert.True(Decide(Security("Contoso.Tools")).ShouldPress);
            var other = Decide(Security("Fabrikam.Tools"));
            Assert.False(other.ShouldPress);
            Assert.Equal("pending", other.Record.DecidedBy);
        }

        [Fact]
        public void SampleAllowlistInRepositoryParsesAndKnownAddInMatches()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "allowlist.sample.json");
            var file = JsonSerializer.Deserialize<AllowlistFile>(File.ReadAllText(Path.GetFullPath(path)))!;
            var rule = Assert.Single(file.Rules, r => r.Match.AddIn != null);
            Assert.Equal("Load Once", rule.Action.ButtonName);
            Assert.Same(rule, DialogMatcher.FindFirstMatch(new[] { rule }, Security("Contoso.Tools"), DialogSource.Watcher));
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { rule }, Security("OtherAddin"), DialogSource.Watcher));
        }
    }

    public sealed class ReviewFixTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "RevitHarness.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static DialogInfo Plain(string title = "Warning") => new DialogInfo
        {
            Title = title, Text = "Name: Contoso.Tools\nPath: C:\\Addins\\Contoso.Tools.dll", AutomationId = "TaskDialog",
            ButtonDetails = { new DialogButton { Name = "OK", AutomationId = "CommandButton_1" } }
        };

        private static AllowRule AddInRule(string addIn) => new AllowRule
        {
            Id = "addin-" + addIn, Target = DialogTarget.Both,
            Match = new DialogMatch { AddIn = addIn },
            Action = new DialogAction { ButtonName = "OK" }
        };

        [Theory]
        [InlineData("*")]
        [InlineData("Contoso.*")]
        [InlineData("Contoso.Tools")]
        [InlineData("C:\\Addins\\*")]
        public void AddInRuleNeverMatchesAnOrdinaryDialog(string addIn)
        {
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { AddInRule(addIn) }, Plain(), DialogSource.Watcher));
            var decision = new DialogDecider(new DecisionContext(), directory);
            Assert.True(AllowlistStore.TryAddRule(AddInRule(addIn), directory));
            var result = decision.Decide(Plain(), DialogSource.Watcher, hwnd: 5, dialogPid: 3);
            Assert.False(result.ShouldPress);
            Assert.Equal("pending", result.Record.DecidedBy);
        }

        [Fact]
        public void LoneStarStillMatchesAnUnsignedAddInDialog()
        {
            var security = Plain("Security - Unsigned Add-in");
            Assert.NotNull(DialogMatcher.FindFirstMatch(new[] { AddInRule("*") }, security, DialogSource.Watcher));
        }

        [Fact]
        public void SelfDeclaredNameCannotSatisfyAPathCondition()
        {
            var evil = Plain("Security - Unsigned Add-in");
            evil.Text = "Name: C:\\Addins\\Sample\\x\nPath: D:\\evil\\x.dll";
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { AddInRule("C:\\Addins\\Sample\\*") }, evil, DialogSource.Watcher));
            Assert.NotNull(DialogMatcher.FindFirstMatch(new[] { AddInRule("D:\\evil\\*") }, evil, DialogSource.Watcher));
        }

        private static AllowRule TitleRule(string id, bool noLearn) => new AllowRule
        {
            Id = id, Target = DialogTarget.Both, NoLearn = noLearn,
            Match = new DialogMatch { Title = new TextPattern { Value = "Warning", Mode = TextPatternMode.Equals } },
            Action = new DialogAction { ButtonName = "OK" }
        };

        [Fact]
        public void NoLearnRuleWinsOverAnEarlierRuleSoTheDialogStaysPending()
        {
            Assert.True(AllowlistStore.TryAddRule(TitleRule("broad", false), directory));
            Assert.True(AllowlistStore.TryAddRule(TitleRule("tester", true), directory));
            var result = new DialogDecider(new DecisionContext(), directory).Decide(Plain(), DialogSource.Watcher, hwnd: 5, dialogPid: 3);
            Assert.False(result.ShouldPress);
            Assert.Equal("pending", result.Record.DecidedBy);
            Assert.Contains("tester", result.Record.Note);
        }

        [Fact]
        public void MatchedRuleWhoseButtonIsMissingLeavesTheDialogPending()
        {
            var rule = TitleRule("wants-yes", false);
            rule.Action = new DialogAction { ButtonName = "Yes" };
            Assert.True(AllowlistStore.TryAddRule(rule, directory));
            var result = new DialogDecider(new DecisionContext(), directory).Decide(Plain(), DialogSource.Watcher, hwnd: 5, dialogPid: 3);
            Assert.False(result.ShouldPress);
            Assert.Equal("pending", result.Record.DecidedBy);
            Assert.NotNull(result.PendingId);
            Assert.Contains("does not apply", result.Record.Note);
            Assert.Null(new PendingStore(directory).Get(result.PendingId!)!.NoLearnRuleId);
        }

        private static DialogInfo Failure(string guid) => new DialogInfo
        {
            Source = DialogSource.Api, DialogId = "failure:" + guid, Title = "Revit warning", Text = "x", ProcessName = "Revit"
        };

        private static AllowRule FailureRule(string id, string guid, bool noLearn) => new AllowRule
        {
            Id = id, Target = DialogTarget.Api, NoLearn = noLearn,
            Match = new DialogMatch { DialogId = new TextPattern { Value = "failure:" + guid, Mode = TextPatternMode.Equals } },
            Action = new DialogAction { ButtonName = "OK" }
        };

        [Fact]
        public void WarningIsDeletedOnlyByANonNoLearnRule()
        {
            var g = Guid.NewGuid().ToString("D");
            Assert.Equal("del", DialogMatcher.FindWarningDeleteRule(new[] { FailureRule("del", g, false) }, Failure(g))!.Id);
            Assert.Null(DialogMatcher.FindWarningDeleteRule(new[] { FailureRule("keep", g, true) }, Failure(g)));
            Assert.Null(DialogMatcher.FindWarningDeleteRule(new[] { FailureRule("del", g, false), FailureRule("keep", g, true) }, Failure(g)));
            Assert.Null(DialogMatcher.FindWarningDeleteRule(new[] { TitleRule("title-only", false) }, Failure(g)));
        }

        [Fact]
        public void EmptyPatternValueNeverMatchesAndModeDefaultsToEquals()
        {
            var empty = TitleRule("empty", false);
            empty.Match.Title = new TextPattern { Value = "" };
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { empty }, Plain(), DialogSource.Watcher));
            var json = JsonSerializer.Deserialize<TextPattern>("{\"value\":\"War\"}")!;
            Assert.Equal(TextPatternMode.Equals, json.Mode);
            var partial = TitleRule("partial", false);
            partial.Match.Title = json;
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { partial }, Plain(), DialogSource.Watcher));
        }

        [Fact]
        public void NoLearnRuleWithoutDialogIdAlsoProtectsAWarning()
        {
            var g = Guid.NewGuid().ToString("D");
            var keep = new AllowRule
            {
                Id = "keep-by-text", Target = DialogTarget.Api, NoLearn = true,
                Match = new DialogMatch { Text = new TextPattern { Value = "x", Mode = TextPatternMode.Equals } },
                Action = new DialogAction { ButtonName = "OK" }
            };
            Assert.Null(DialogMatcher.FindWarningDeleteRule(new[] { FailureRule("del", g, false), keep }, Failure(g)));
        }

        [Fact]
        public void PathConditionRejectsParentDirectorySegments()
        {
            var sneaky = Plain("Security - Unsigned Add-in");
            sneaky.Text = "Name: X\nPath: C:\\Trusted\\..\\Public\\evil.dll";
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { AddInRule("C:\\Trusted\\*") }, sneaky, DialogSource.Watcher));
        }

        [Fact]
        public void WaitReturnsTheFinishedEntryInsteadOfReportingAWithdrawal()
        {
            var store = new PendingStore(directory);
            var id = new DialogDecider(new DecisionContext(), directory).Decide(Plain(), DialogSource.Watcher, hwnd: 5, dialogPid: 3).PendingId!;
            store.RequestDecision(id, "OK", null, "m", false);
            store.ClaimDecision(id);
            store.Complete(id, true);
            Assert.False(store.TryWithdrawRequest(id));
            Assert.Equal(PendingStatus.Resolved, store.WaitForResolution(id, TimeSpan.Zero, TimeSpan.FromMilliseconds(10))!.Status);
        }
    }
}
