#nullable enable

using Newtonsoft.Json;
using System.Collections.Generic;

namespace RevitHarness.Cli
{
    /// <summary>
    /// IPC request message structure.
    /// </summary>
    public class IpcRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("op")]
        public string Op { get; set; } = string.Empty;

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonProperty("payload")]
        public Dictionary<string, object>? Payload { get; set; }
    }

    /// <summary>
    /// IPC response message structure.
    /// </summary>
    public class IpcResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("error")]
        public IpcError? Error { get; set; }

        [JsonProperty("result")]
        public object? Result { get; set; }

        [JsonProperty("logs")]
        public List<string>? Logs { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// Error information in IPC response.
    /// </summary>
    public class IpcError
    {
        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("details")]
        public object? Details { get; set; }
    }

    /// <summary>
    /// Operation names.
    /// </summary>
    public static class Operations
    {
        public const string Ping = "ping";
        public const string RunTests = "runTests";
        public const string RunCommand = "runCommand";
        public const string Shutdown = "shutdown";
        public const string LookupElement = "lookupElement";
    }
}
