#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace RevitHarness.Addin.Ipc
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

        public T? GetPayloadValue<T>(string key)
        {
            if (Payload == null || !Payload.TryGetValue(key, out var value))
                return default;

            if (value is T typedValue)
                return typedValue;

            try
            {
                return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
            }
            catch
            {
                return default;
            }
        }
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

        public static IpcResponse Success(string id, object? result, long durationMs, List<string>? logs = null)
        {
            return new IpcResponse
            {
                Id = id,
                Ok = true,
                Result = result,
                DurationMs = durationMs,
                Logs = logs
            };
        }

        public static IpcResponse Failure(string id, string code, string message, long durationMs, object? details = null, List<string>? logs = null)
        {
            return new IpcResponse
            {
                Id = id,
                Ok = false,
                Error = new IpcError
                {
                    Code = code,
                    Message = message,
                    Details = details
                },
                DurationMs = durationMs,
                Logs = logs
            };
        }
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
    /// Error codes defined in requirements.
    /// </summary>
    public static class ErrorCodes
    {
        public const string HarnessNotReady = "HARNESS_NOT_READY";
        public const string InvalidRequest = "INVALID_REQUEST";
        public const string AssemblyNotFound = "ASSEMBLY_NOT_FOUND";
        public const string TypeNotFound = "TYPE_NOT_FOUND";
        public const string EntrypointFailed = "ENTRYPOINT_FAILED";
        public const string RevitApiError = "REVIT_API_ERROR";
        public const string Timeout = "TIMEOUT";
        public const string InternalError = "INTERNAL_ERROR";
        public const string ElementNotFound = "ELEMENT_NOT_FOUND";
        public const string NoActiveDocument = "NO_ACTIVE_DOCUMENT";
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

    /// <summary>
    /// Result for ping operation.
    /// </summary>
    public class PingResult
    {
        [JsonProperty("revitVersion")]
        public string RevitVersion { get; set; } = string.Empty;

        [JsonProperty("addinVersion")]
        public string AddinVersion { get; set; } = string.Empty;

        [JsonProperty("processId")]
        public int ProcessId { get; set; }

        [JsonProperty("activeDocumentTitle")]
        public string? ActiveDocumentTitle { get; set; }
    }

    /// <summary>
    /// Result for runTests operation.
    /// </summary>
    public class RunTestsResult
    {
        [JsonProperty("passed")]
        public bool Passed { get; set; }

        [JsonProperty("summary")]
        public object? Summary { get; set; }

        [JsonProperty("artifactsDir")]
        public string? ArtifactsDir { get; set; }
    }

    /// <summary>
    /// Result for runCommand operation.
    /// </summary>
    public class RunCommandResult
    {
        [JsonProperty("result")]
        public string Result { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string? Message { get; set; }
    }
}
