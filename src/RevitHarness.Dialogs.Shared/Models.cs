using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitHarness.Dialogs
{
    public sealed class CamelCaseEnumConverter : JsonStringEnumConverter
    {
        public CamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
    }

    [JsonConverter(typeof(CamelCaseEnumConverter))]
    public enum DialogSource { Api, Watcher }

    [JsonConverter(typeof(CamelCaseEnumConverter))]
    public enum DialogTarget { Api, Watcher, Both }

    [JsonConverter(typeof(CamelCaseEnumConverter))]
    public enum TextPatternMode { Contains, Equals, Regex }

    public sealed class AllowlistFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;
        [JsonPropertyName("rules")]
        public List<AllowRule> Rules { get; set; } = new List<AllowRule>();
    }

    public sealed class AllowRule
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
        [JsonPropertyName("target")]
        public DialogTarget Target { get; set; }
        [JsonPropertyName("match")]
        public DialogMatch Match { get; set; } = new DialogMatch();
        [JsonPropertyName("action")]
        public DialogAction Action { get; set; } = new DialogAction();
        [JsonPropertyName("note")]
        public string? Note { get; set; }
        [JsonPropertyName("noLearn")]
        public bool NoLearn { get; set; }
        [JsonPropertyName("key")]
        public DialogKey? Key { get; set; }
        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("createdBy")]
        public string? CreatedBy { get; set; }
        [JsonPropertyName("sourceLogRef")]
        public string? SourceLogRef { get; set; }
    }

    public sealed class DialogMatch
    {
        [JsonPropertyName("dialogId")]
        public TextPattern? DialogId { get; set; }
        [JsonPropertyName("title")]
        public TextPattern? Title { get; set; }
        [JsonPropertyName("text")]
        public TextPattern? Text { get; set; }
        [JsonPropertyName("processName")]
        public TextPattern? ProcessName { get; set; }
        [JsonPropertyName("automationId")]
        public TextPattern? AutomationId { get; set; }
        // Exact button-name set of the dialog (order-insensitive).
        [JsonPropertyName("buttons")]
        public List<string>? Buttons { get; set; }
        // Condition on the add-in name or path extracted from the body of a "Security - Unsigned Add-in" dialog.
        [JsonPropertyName("addIn")]
        public string? AddIn { get; set; }
    }

    public sealed class TextPattern
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
        [JsonPropertyName("mode")]
        public TextPatternMode Mode { get; set; } = TextPatternMode.Equals;
        [JsonPropertyName("ignoreCase")]
        public bool IgnoreCase { get; set; } = true;
    }

    public sealed class DialogAction
    {
        [JsonPropertyName("resultCode")]
        public string? ResultCode { get; set; }
        [JsonPropertyName("buttonName")]
        public string? ButtonName { get; set; }
        [JsonPropertyName("buttonAutomationId")]
        public string? ButtonAutomationId { get; set; }
    }

    public sealed class DialogInfo
    {
        [JsonPropertyName("source")]
        public DialogSource Source { get; set; }
        [JsonPropertyName("dialogId")]
        public string? DialogId { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        [JsonPropertyName("processName")]
        public string? ProcessName { get; set; }
        [JsonPropertyName("automationId")]
        public string? AutomationId { get; set; }
        [JsonPropertyName("buttons")]
        public List<string> Buttons { get; set; } = new List<string>();
        [JsonPropertyName("buttonDetails")]
        public List<DialogButton> ButtonDetails { get; set; } = new List<DialogButton>();
    }

    public sealed class DialogButton
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("automationId")]
        public string? AutomationId { get; set; }
    }

    public sealed class DialogLogRecord
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        [JsonPropertyName("source")]
        public DialogSource Source { get; set; }
        [JsonPropertyName("dialog")]
        public DialogInfo Dialog { get; set; } = new DialogInfo();
        [JsonPropertyName("matchedRuleId")]
        public string? MatchedRuleId { get; set; }
        [JsonPropertyName("actionTaken")]
        public string? ActionTaken { get; set; }
        [JsonPropertyName("screenshotPath")]
        public string? ScreenshotPath { get; set; }
        [JsonPropertyName("dumpPath")]
        public string? DumpPath { get; set; }
        [JsonPropertyName("logId")]
        public string LogId { get; set; } = Guid.NewGuid().ToString("N");
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "decision";
        [JsonPropertyName("pid")]
        public int? Pid { get; set; }
        [JsonPropertyName("revitVersion")]
        public string? RevitVersion { get; set; }
        [JsonPropertyName("openDocument")]
        public string? OpenDocument { get; set; }
        [JsonPropertyName("key")]
        public DialogKey? Key { get; set; }
        [JsonPropertyName("decidedBy")]
        public string? DecidedBy { get; set; }
        [JsonPropertyName("pressedButton")]
        public DialogButton? PressedButton { get; set; }
        [JsonPropertyName("learnedRuleId")]
        public string? LearnedRuleId { get; set; }
        [JsonPropertyName("note")]
        public string? Note { get; set; }
        [JsonPropertyName("taskId")]
        public string? TaskId { get; set; }
        [JsonPropertyName("harnessCommandId")]
        public string? HarnessCommandId { get; set; }
        [JsonPropertyName("deciderName")]
        public string? DeciderName { get; set; }
        [JsonPropertyName("pendingId")]
        public string? PendingId { get; set; }
        [JsonPropertyName("outcome")]
        public DialogOutcome? Outcome { get; set; }
    }

    public sealed class DialogOutcome
    {
        [JsonPropertyName("closed")]
        public bool Closed { get; set; }
        [JsonPropertyName("nextDialogTitle")]
        public string? NextDialogTitle { get; set; }
    }

}
