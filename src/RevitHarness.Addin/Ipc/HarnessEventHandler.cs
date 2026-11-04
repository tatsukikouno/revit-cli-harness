#nullable enable

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitHarness.Addin.Commands;
using RevitHarness.Addin.DialogHandling;
using RevitHarness.Contracts;
using RevitHarness.Lookup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// External event handler for executing operations on Revit's main thread.
    /// </summary>
    public class HarnessEventHandler : IExternalEventHandler
    {
        private readonly IpcServer _server;
        private readonly ShadowCopier _shadowCopier;
        private readonly CommandExecutor _commandExecutor;
        private readonly List<string> _logs;
        private readonly Action<string> _log;
        private UIApplication? _uiapp;

        private const string TestEntryPointInterfaceName = "RevitHarness.Contracts.ITestEntryPoint";

        public HarnessEventHandler(IpcServer server, Action<string> log)
        {
            _server = server;
            _shadowCopier = new ShadowCopier(log);
            _commandExecutor = new CommandExecutor(_shadowCopier, log);
            _logs = new List<string>();
            _log = log;

        }

        public string GetName() => "RevitHarnessEventHandler";

        public void Execute(UIApplication app)
        {
            _uiapp = app;
            DialogRuntime.SetSession(app.Application?.VersionNumber, app.ActiveUIDocument?.Document?.Title);

            // Process all pending requests
            while (_server.TryDequeueRequest(out var item))
            {
                var (request, callback) = item;
                var sw = Stopwatch.StartNew();
                _logs.Clear();

                try
                {
                    var response = ProcessRequest(request, sw);
                    response.Logs = new List<string>(_logs);
                    callback(response);
                }
                catch (Exception ex)
                {
                    _log($"Error processing request: {ex}");
                    _logs.Add($"Exception: {ex}");
                    callback(IpcResponse.Failure(
                        request.Id,
                        ErrorCodes.InternalError,
                        ex.Message,
                        sw.ElapsedMilliseconds,
                        new { stackTrace = ex.StackTrace },
                        new List<string>(_logs)));
                }
            }
        }

        private IpcResponse ProcessRequest(IpcRequest request, Stopwatch sw)
        {
            Log($"Processing: {request.Op}");

            switch (request.Op)
            {
                case Operations.Ping:
                    return HandlePing(request, sw);

                case Operations.RunTests:
                    return WithDialogContext(request, () => HandleRunTests(request, sw));

                case Operations.RunCommand:
                    return WithDialogContext(request, () => HandleRunCommand(request, sw));

                case Operations.Reload:
                    return HandleReload(request, sw);

                case Operations.Unload:
                    return HandleTargetOp(request, sw, host => host.Unload());

                case Operations.TargetStatus:
                    return HandleTargetOp(request, sw, host => host.Status());

                case Operations.Shutdown:
                    return HandleShutdown(request, sw);

                case Operations.LookupElement:
                    return HandleLookupElement(request, sw);

                default:
                    return IpcResponse.Failure(
                        request.Id,
                        ErrorCodes.InvalidRequest,
                        $"Unknown operation: {request.Op}",
                        sw.ElapsedMilliseconds);
            }
        }

        // Tags dialog log records raised while this request runs with the command id (CLI-supplied, else the request id).
        private static IpcResponse WithDialogContext(IpcRequest request, Func<IpcResponse> run)
        {
            var commandId = request.GetPayloadValue<string>("commandId");
            DialogRuntime.SetCommand(
                string.IsNullOrEmpty(commandId) ? request.Id : commandId,
                request.GetPayloadValue<string>("taskId"));
            try { return run(); }
            finally { DialogRuntime.SetCommand(null, null); }
        }

        private IpcResponse HandlePing(IpcRequest request, Stopwatch sw)
        {
            var result = new PingResult
            {
                RevitVersion = _uiapp?.Application?.VersionNumber ?? "Unknown",
                AddinVersion = typeof(HarnessEventHandler).Assembly.GetName().Version?.ToString() ?? "Unknown",
                ProcessId = Process.GetCurrentProcess().Id,
                ActiveDocumentTitle = _uiapp?.ActiveUIDocument?.Document?.Title
            };

            return IpcResponse.Success(request.Id, result, sw.ElapsedMilliseconds);
        }

        private IpcResponse HandleRunTests(IpcRequest request, Stopwatch sw)
        {
            var assemblyPath = request.GetPayloadValue<string>("assemblyPath");
            var entryType = request.GetPayloadValue<string>("entryType");
            var args = request.GetPayloadValue<Dictionary<string, object>>("args") ?? new Dictionary<string, object>();

            if (string.IsNullOrEmpty(assemblyPath))
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.InvalidRequest,
                    "assemblyPath is required",
                    sw.ElapsedMilliseconds);
            }

            if (string.IsNullOrEmpty(entryType))
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.InvalidRequest,
                    "entryType is required",
                    sw.ElapsedMilliseconds);
            }

            if (!File.Exists(assemblyPath))
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.AssemblyNotFound,
                    $"Assembly not found: {assemblyPath}",
                    sw.ElapsedMilliseconds);
            }

            try
            {
                using var lease = AssemblyLease.Open(_shadowCopier, assemblyPath!, Log);
                var assembly = lease.Assembly;

                // Find the entry type
                var type = assembly.GetType(entryType);
                if (type == null)
                {
                    return IpcResponse.Failure(
                        request.Id,
                        ErrorCodes.TypeNotFound,
                        $"Type not found: {entryType}",
                        sw.ElapsedMilliseconds);
                }
                Log($"Entry type found: {type.FullName}");

                // Check if type implements ITestEntryPoint.
                // Use name-based check to handle assembly identity mismatches when
                // RevitHarness.Contracts is loaded in multiple contexts (e.g., by AddIn Manager).
                if (!ReflectionHelper.ImplementsInterfaceByName(type, TestEntryPointInterfaceName))
                {
                    return IpcResponse.Failure(
                        request.Id,
                        ErrorCodes.TypeNotFound,
                        $"Type {entryType} does not implement ITestEntryPoint",
                        sw.ElapsedMilliseconds);
                }

                // Create instance and run
                Log("Creating instance");
                var instance = Activator.CreateInstance(type)!;
                Log("Running tests");

                string? resultJson;
                if (instance is ITestEntryPoint testEntry)
                {
                    resultJson = testEntry.Run(_uiapp!, args);
                }
                else
                {
                    // Fallback: invoke via reflection to handle cross-context type identity mismatch
                    Log("Direct cast failed (assembly context mismatch), using reflection");
                    var runMethod = ReflectionHelper.FindMethod(
                        type,
                        "Run",
                        new[] { typeof(UIApplication), typeof(IDictionary<string, object>) });
                    if (runMethod == null)
                    {
                        return IpcResponse.Failure(
                            request.Id,
                            ErrorCodes.TypeNotFound,
                            $"Type {entryType} declares ITestEntryPoint but Run method not found",
                            sw.ElapsedMilliseconds);
                    }
                    resultJson = (string?)runMethod.Invoke(instance, new object[] { _uiapp!, args });
                }
                Log("Tests completed");

                // Parse result to extract passed status
                var resultObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultJson ?? "{}");
                var passed = false;
                if (resultObj != null && resultObj.TryGetValue("passed", out var passedValue))
                {
                    if (passedValue is bool b)
                        passed = b;
                    else if (bool.TryParse(passedValue?.ToString(), out var parsedPassed))
                        passed = parsedPassed;
                }

                var result = new RunTestsResult
                {
                    Passed = passed,
                    Summary = resultObj
                };

                // Cleanup old shadow directories
                _shadowCopier.Cleanup();

                return IpcResponse.Success(request.Id, result, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log($"Test execution failed: {ex}");
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.EntrypointFailed,
                    ex.Message,
                    sw.ElapsedMilliseconds,
                    new { stackTrace = ex.ToString() });
            }
        }

        private IpcResponse HandleRunCommand(IpcRequest request, Stopwatch sw)
        {
            var assemblyPath = request.GetPayloadValue<string>("assemblyPath");
            var commandType = request.GetPayloadValue<string>("commandType");
            var args = request.GetPayloadValue<Dictionary<string, object>>("args") ?? new Dictionary<string, object>();

            if (string.IsNullOrEmpty(assemblyPath))
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.InvalidRequest,
                    "assemblyPath is required",
                    sw.ElapsedMilliseconds);
            }

            if (string.IsNullOrEmpty(commandType))
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.InvalidRequest,
                    "commandType is required",
                    sw.ElapsedMilliseconds);
            }

            if (!File.Exists(assemblyPath))
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.AssemblyNotFound,
                    $"Assembly not found: {assemblyPath}",
                    sw.ElapsedMilliseconds);
            }

            var result = _commandExecutor.ExecuteCommand(_uiapp!, assemblyPath!, commandType!, args);

            if (result.Success)
            {
                var response = new RunCommandResult
                {
                    Result = "Succeeded",
                    Message = result.ResultText
                };
                return IpcResponse.Success(request.Id, response, sw.ElapsedMilliseconds);
            }
            else
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.RevitApiError,
                    result.ErrorMessage ?? "Unknown error",
                    sw.ElapsedMilliseconds,
                    result.StackTrace != null ? new { stackTrace = result.StackTrace } : null);
            }
        }

        private IpcResponse HandleReload(IpcRequest request, Stopwatch sw)
        {
            var assemblyPath = request.GetPayloadValue<string>("assemblyPath");
            var entryType = request.GetPayloadValue<string>("entryType");
            if (string.IsNullOrEmpty(assemblyPath) || string.IsNullOrEmpty(entryType))
                return IpcResponse.Failure(request.Id, ErrorCodes.InvalidRequest, "assemblyPath and entryType are required", sw.ElapsedMilliseconds);
            if (!File.Exists(assemblyPath))
                return IpcResponse.Failure(request.Id, ErrorCodes.AssemblyNotFound, $"Assembly not found: {assemblyPath}", sw.ElapsedMilliseconds);
            return HandleTargetOp(request, sw, host => host.Reload(assemblyPath!, entryType!));
        }

        private IpcResponse HandleTargetOp(IpcRequest request, Stopwatch sw, Func<TargetHost, object> run)
        {
            try
            {
                var host = TargetHost.Instance ?? throw new InvalidOperationException("Target host is not initialized");
                return IpcResponse.Success(request.Id, run(host), sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log($"{request.Op} failed: {ex}");
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.EntrypointFailed,
                    ex.Message,
                    sw.ElapsedMilliseconds,
                    new { stackTrace = ex.ToString() });
            }
        }

        private IpcResponse HandleLookupElement(IpcRequest request, Stopwatch sw)
        {
            var elementId = request.GetPayloadValue<int?>("elementId");
            if (!elementId.HasValue)
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.InvalidRequest,
                    "elementId is required",
                    sw.ElapsedMilliseconds);
            }

            var options = new InspectOptions
            {
                MaxDepth = request.GetPayloadValue<int?>("maxDepth") ?? 0,
                MaxMembers = request.GetPayloadValue<int?>("maxMembers") ?? 200,
                IncludeFields = request.GetPayloadValue<bool?>("includeFields") ?? false,
                IncludePrivateMembers = request.GetPayloadValue<bool?>("includePrivateMembers") ?? false,
                IncludeStaticMembers = request.GetPayloadValue<bool?>("includeStaticMembers") ?? false,
                NameFilter = request.GetPayloadValue<string>("nameFilter")
            };

            try
            {
                Log($"Looking up element: {elementId.Value}");
                var inspector = new ElementInspector();
                var result = inspector.Inspect(_uiapp!, elementId.Value, options);

                if (result.ErrorDetail != null)
                {
                    Log($"Lookup error: {result.ErrorDetail.Code} - {result.ErrorDetail.Message}");
                    return IpcResponse.Failure(
                        request.Id,
                        result.ErrorDetail.Code,
                        result.ErrorDetail.Message,
                        sw.ElapsedMilliseconds);
                }

                Log($"Lookup completed: {result.MemberCount} members found");
                return IpcResponse.Success(request.Id, result, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log($"Lookup failed: {ex}");
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.RevitApiError,
                    ex.Message,
                    sw.ElapsedMilliseconds,
                    new { stackTrace = ex.ToString() });
            }
        }

        private IpcResponse HandleShutdown(IpcRequest request, Stopwatch sw)
        {
            Log("Shutdown requested");
            // The actual shutdown will be handled by the App class
            return IpcResponse.Success(request.Id, new { shutting_down = true }, sw.ElapsedMilliseconds);
        }

        private void Log(string message)
        {
            _logs.Add(message);
            _log(message);
        }
    }
}
