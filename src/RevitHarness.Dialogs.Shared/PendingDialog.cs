using System;
using System.Text.Json.Serialization;

namespace RevitHarness.Dialogs
{
    public static class PendingStatus
    {
        // Waiting for an external decision.
        public const string Pending = "pending";
        // A decision was requested through the CLI and waits for the watcher to press the button.
        public const string DecisionRequested = "decisionRequested";
        // The watcher claimed the request and is pressing the button. A caller can no longer withdraw it.
        public const string Applying = "applying";
        public const string Resolved = "resolved";
        // The dialog disappeared (or its process died) before a decision was applied.
        public const string Stale = "stale";
        public const string Failed = "failed";
    }

    public sealed class PendingDecisionRequest
    {
        [JsonPropertyName("button")]
        public DialogButton Button { get; set; } = new DialogButton();
        [JsonPropertyName("by")]
        public string By { get; set; } = string.Empty;
        [JsonPropertyName("saveRule")]
        public bool SaveRule { get; set; }
        [JsonPropertyName("requestedAt")]
        public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class PendingResolution
    {
        [JsonPropertyName("decidedBy")]
        public string DecidedBy { get; set; } = "external";
        [JsonPropertyName("by")]
        public string? By { get; set; }
        [JsonPropertyName("pressedButton")]
        public DialogButton? PressedButton { get; set; }
        [JsonPropertyName("ruleId")]
        public string? RuleId { get; set; }
        [JsonPropertyName("ruleNotSavedReason")]
        public string? RuleNotSavedReason { get; set; }
        [JsonPropertyName("note")]
        public string? Note { get; set; }
        [JsonPropertyName("at")]
        public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    }

    // One unknown dialog waiting for an external decision. Stored as dialogs\pending\<id>.json.
    public sealed class PendingDialog
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        [JsonPropertyName("status")]
        public string Status { get; set; } = PendingStatus.Pending;
        [JsonPropertyName("source")]
        public DialogSource Source { get; set; }
        [JsonPropertyName("pid")]
        public int? Pid { get; set; }
        [JsonPropertyName("hwnd")]
        public long? Hwnd { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("automationId")]
        public string? AutomationId { get; set; }
        [JsonPropertyName("buttons")]
        public System.Collections.Generic.List<DialogButton> Buttons { get; set; } = new System.Collections.Generic.List<DialogButton>();
        [JsonPropertyName("bodySummary")]
        public string? BodySummary { get; set; }
        [JsonPropertyName("addIn")]
        public System.Collections.Generic.List<string>? AddIn { get; set; }
        [JsonPropertyName("screenshotPath")]
        public string? ScreenshotPath { get; set; }
        [JsonPropertyName("dumpPath")]
        public string? DumpPath { get; set; }
        [JsonPropertyName("noLearnRuleId")]
        public string? NoLearnRuleId { get; set; }
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
        [JsonPropertyName("logId")]
        public string? LogId { get; set; }
        [JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        [JsonPropertyName("updatedAt")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        [JsonPropertyName("dialog")]
        public DialogInfo Dialog { get; set; } = new DialogInfo();
        [JsonPropertyName("key")]
        public DialogKey? Key { get; set; }
        [JsonPropertyName("decision")]
        public PendingDecisionRequest? Decision { get; set; }
        [JsonPropertyName("resolution")]
        public PendingResolution? Resolution { get; set; }
        [JsonPropertyName("staleReason")]
        public string? StaleReason { get; set; }

        public bool IsOpen => Status == PendingStatus.Pending || Status == PendingStatus.DecisionRequested || Status == PendingStatus.Applying;
    }
}
