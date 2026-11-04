using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevitHarness.Dialogs;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public sealed class DialogsTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "RevitHarness.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private string WriteAllowlist(string json)
        {
            var path = DialogPaths.GetAllowlistPath(directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void SchemaRoundtripAndLoadPreserveCamelCaseStringsAndDefaults()
        {
            const string json = "{\"version\":1,\"rules\":[{\"id\":\"close\",\"target\":\"both\",\"match\":{\"title\":{\"value\":\"Warning\",\"mode\":\"equals\"}},\"action\":{\"resultCode\":\"Cancel\",\"buttonName\":\"Cancel\",\"buttonAutomationId\":\"2\"},\"note\":\"Known warning\"}]}";
            WriteAllowlist(json);
            var result = AllowlistLoader.Load(directory);
            Assert.Null(result.Error);
            var rule = Assert.Single(result.Rules);
            Assert.True(rule.Enabled);
            Assert.True(rule.Match.Title!.IgnoreCase);
            Assert.Equal(DialogTarget.Both, rule.Target);
            Assert.Equal("Cancel", rule.Action.ResultCode);
            var serialized = JsonSerializer.Serialize(new AllowlistFile { Rules = result.Rules.ToList() });
            using var document = JsonDocument.Parse(serialized);
            var item = document.RootElement.GetProperty("rules")[0];
            Assert.Equal("both", item.GetProperty("target").GetString());
            Assert.Equal("equals", item.GetProperty("match").GetProperty("title").GetProperty("mode").GetString());
            var copy = JsonSerializer.Deserialize<AllowlistFile>(serialized)!;
            Assert.Equal("Known warning", copy.Rules[0].Note);
            Assert.Equal("2", copy.Rules[0].Action.ButtonAutomationId);
            Assert.Equal("Cancel", copy.Rules[0].Action.ButtonName);
        }

        [Fact]
        public void MissingFileIsEmptyAndDoesNotCreateDirectory()
        {
            var result = AllowlistLoader.Load(directory);
            Assert.Empty(result.Rules);
            Assert.Null(result.Error);
            Assert.False(Directory.Exists(directory));
        }

        [Theory]
        [InlineData("{broken")]
        [InlineData("null")]
        [InlineData("{\"rules\":[{\"target\":\"unknown\"}]}")]
        public void MalformedFileReportsErrorAndReturnsEmpty(string json)
        {
            WriteAllowlist(json);
            string? error = null;
            var result = AllowlistLoader.Load(directory, message => error = message);
            Assert.Empty(result.Rules);
            Assert.False(string.IsNullOrEmpty(result.Error));
            Assert.Equal(result.Error, error);
            Assert.Empty(AllowlistLoader.Load(directory, _ => throw new InvalidOperationException()).Rules);
        }

        [Fact]
        public void LoadWhileWriterHoldsFileOpen()
        {
            var path = WriteAllowlist("{\"version\":1,\"rules\":[]}");
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            Assert.Null(AllowlistLoader.Load(directory).Error);
        }

        [Fact]
        public void IoErrorIsReported()
        {
            var path = WriteAllowlist("{}");
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            string? error = null;
            var result = AllowlistLoader.Load(directory, message => error = message);
            Assert.Empty(result.Rules);
            Assert.NotNull(error);
            Assert.Equal(error, result.Error);
        }

        private static AllowRule Rule() => new AllowRule
        {
            Id = "first", Target = DialogTarget.Both,
            Match = new DialogMatch { Title = new TextPattern { Value = "warning" } }
        };

        [Theory]
        [InlineData(DialogTarget.Api, DialogSource.Api, true)]
        [InlineData(DialogTarget.Api, DialogSource.Watcher, false)]
        [InlineData(DialogTarget.Watcher, DialogSource.Api, false)]
        [InlineData(DialogTarget.Watcher, DialogSource.Watcher, true)]
        [InlineData(DialogTarget.Both, DialogSource.Api, true)]
        [InlineData(DialogTarget.Both, DialogSource.Watcher, true)]
        public void TargetFiltersSources(DialogTarget target, DialogSource source, bool expected)
        {
            var rule = Rule();
            rule.Target = target;
            var dialog = new DialogInfo { Title = "Warning", Source = source };
            Assert.Equal(expected, DialogMatcher.FindFirstMatch(new[] { rule }, dialog, source) != null);
            rule.Enabled = false;
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { rule }, dialog, source));
        }

        [Theory]
        [InlineData(TextPatternMode.Contains, "arn", true, true)]
        [InlineData(TextPatternMode.Contains, "ARN", false, false)]
        [InlineData(TextPatternMode.Equals, "warning", true, true)]
        [InlineData(TextPatternMode.Equals, "warning", false, false)]
        [InlineData(TextPatternMode.Equals, "warn", true, false)]
        [InlineData(TextPatternMode.Regex, "^warn.*$", true, true)]
        [InlineData(TextPatternMode.Regex, "^warn.*$", false, false)]
        [InlineData(TextPatternMode.Regex, "[", true, false)]
        public void PatternModesMatch(TextPatternMode mode, string value, bool ignoreCase, bool expected)
        {
            var rule = Rule();
            rule.Match.Title = new TextPattern { Mode = mode, Value = value, IgnoreCase = ignoreCase };
            Assert.Equal(expected, DialogMatcher.FindFirstMatch(new[] { rule }, new DialogInfo { Title = "Warning" }, DialogSource.Api) != null);
        }

        [Fact]
        public void ConditionsAreAndedAndEmptyMatchNeverMatches()
        {
            var rule = Rule();
            rule.Match.DialogId = new TextPattern { Value = "confirm" };
            rule.Match.Text = new TextPattern { Value = "save" };
            rule.Match.ProcessName = new TextPattern { Value = "revit", Mode = TextPatternMode.Equals };
            var dialog = new DialogInfo { DialogId = "confirm", Title = "Warning", Text = "Save changes", ProcessName = "Revit" };
            Assert.Same(rule, DialogMatcher.FindFirstMatch(new[] { rule }, dialog, DialogSource.Api));
            dialog.Text = "Other";
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { rule }, dialog, DialogSource.Api));
            dialog.Text = null;
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { rule }, dialog, DialogSource.Api));
            rule.Match = new DialogMatch();
            Assert.Null(DialogMatcher.FindFirstMatch(new[] { rule }, dialog, DialogSource.Api));
        }

        [Fact]
        public void ReturnsFirstMatchingRuleInOrder()
        {
            var first = Rule();
            var second = Rule();
            second.Id = "second";
            var dialog = new DialogInfo { Title = "Warning" };
            Assert.Same(first, DialogMatcher.FindFirstMatch(new[] { first, second }, dialog, DialogSource.Api));
            Assert.Same(second, DialogMatcher.FindFirstMatch(new[] { second, first }, dialog, DialogSource.Api));
        }

        [Fact]
        public async Task ConcurrentLogAppendsProduceOneValidLinePerRecord()
        {
            var timestamp = new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.FromHours(-2));
            await Task.WhenAll(Enumerable.Range(0, 300).Select(index => Task.Run(() =>
                new DialogLogger(directory).Log(new DialogLogRecord
                {
                    Timestamp = timestamp, Source = DialogSource.Watcher,
                    Dialog = new DialogInfo { Source = DialogSource.Watcher, Title = "Warning\n日本語", Buttons = { "OK" } },
                    MatchedRuleId = index.ToString(), ActionTaken = "clicked", ScreenshotPath = "screen.png", DumpPath = "dump.json"
                }))));
            var files = Directory.GetFiles(DialogPaths.GetDirectory(directory));
            Assert.EndsWith("dialogs_20260103.jsonl", Assert.Single(files));
            var lines = File.ReadAllLines(files[0], Encoding.UTF8);
            Assert.Equal(300, lines.Length);
            var ids = lines.Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var record = document.RootElement;
                Assert.Equal("watcher", record.GetProperty("source").GetString());
                Assert.EndsWith("+00:00", record.GetProperty("timestamp").GetString());
                Assert.Equal("Warning\n日本語", record.GetProperty("dialog").GetProperty("title").GetString());
                Assert.Equal("clicked", record.GetProperty("actionTaken").GetString());
                Assert.Equal("screen.png", record.GetProperty("screenshotPath").GetString());
                Assert.Equal("dump.json", record.GetProperty("dumpPath").GetString());
                return record.GetProperty("matchedRuleId").GetString();
            }).ToArray();
            Assert.Equal(300, ids.Distinct().Count());
        }

        [Fact]
        public void LoggerSwallowsIoErrors()
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "file");
            File.WriteAllText(file, "occupied");
            new DialogLogger(file).Log(new DialogLogRecord());
        }
    }
}
