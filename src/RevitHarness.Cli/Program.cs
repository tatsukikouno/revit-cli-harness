#nullable enable

using Newtonsoft.Json;
using RevitHarness.Cli.Installation;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitHarness.Cli
{
    /// <summary>
    /// CLI for communicating with Revit harness.
    /// </summary>
    internal class Program
    {
        // Exit codes
        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;
        private const int ExitConnectionError = 2;

        private static async Task<int> Main(string[] args)
        {
            // Set console encoding to UTF-8 for proper Japanese character display
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            if (args.Length == 0)
            {
                PrintUsage();
                return ExitFailure;
            }

            var command = args[0].ToLowerInvariant();

            try
            {
                switch (command)
                {
                    // Installation commands
                    case "install":
                        return RunInstall(args);

                    case "uninstall":
                        return RunUninstall(args);

                    case "status":
                        return RunStatus(args);

                    // IPC commands
                    case "ping":
                        return await RunPingAsync();

                    case "run-tests":
                        return await RunTestsAsync(args);

                    case "run-command":
                        return await RunCommandAsync(args);

                    case "shutdown":
                        return await RunShutdownAsync();

                    case "lookup":
                        return await RunLookupAsync(args);

                    case "dialog-log":
                        return await RunDialogLogAsync(args);

                    case "dialog-pending":
                        return RunDialogPending(args);

                    case "dialog-decide":
                        return RunDialogDecide(args);

                    case "dialog-watcher":
                        return RunDialogWatcher(args);

                    case "reload-allowlist":
                        return await RunReloadAllowlistAsync();

                    case "--help":
                    case "-h":
                    case "help":
                        PrintUsage();
                        return ExitSuccess;

                    case "--version":
                    case "-v":
                    case "version":
                        PrintVersion();
                        return ExitSuccess;

                    default:
                        Console.Error.WriteLine($"Unknown command: {command}");
                        PrintUsage();
                        return ExitFailure;
                }
            }
            catch (TimeoutException ex)
            {
                Console.Error.WriteLine($"Connection error: {ex.Message}");
                var errorResponse = new IpcResponse
                {
                    Id = string.Empty,
                    Ok = false,
                    Error = new IpcError
                    {
                        Code = "HARNESS_NOT_READY",
                        Message = ex.Message
                    }
                };
                Console.WriteLine(JsonConvert.SerializeObject(errorResponse, Formatting.Indented));
                return ExitConnectionError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                var errorResponse = new IpcResponse
                {
                    Id = string.Empty,
                    Ok = false,
                    Error = new IpcError
                    {
                        Code = "INTERNAL_ERROR",
                        Message = ex.Message
                    }
                };
                Console.WriteLine(JsonConvert.SerializeObject(errorResponse, Formatting.Indented));
                return ExitConnectionError;
            }
        }

        private static void PrintVersion()
        {
            var version = typeof(Program).Assembly.GetName().Version;
            Console.WriteLine($"revit-harness version {version}");
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine(@"Usage: revit-harness <command> [options]

Installation Commands:
  install                       Install the add-in to Revit, the dialog watcher to
                                %LOCALAPPDATA%\RevitHarness\watcher and the SKILLs
    --revit-version <year>      Revit version (e.g., 2024, 2025, 2026) (required)
    --skills-dir <path>         SKILL folder (optional, default: %USERPROFILE%\.claude\skills)
  uninstall                     Remove the add-in; the last uninstall also removes the watcher
                                and SKILLs (the dialogs data folder is kept)
    --revit-version <year>      Revit version (e.g., 2024, 2025, 2026) (required)
    --skills-dir <path>         SKILL folder used at install (optional)
  status                        Show installation status
    --revit-version <year>      Revit version (optional, shows all if omitted)

IPC Commands:
  ping                          Check connection to Revit harness
  run-tests                     Execute tests via the harness
    --dll <path>                Path to the test DLL (required)
    --type <typename>           Entry point type name (required)
    --filter <filter>           Test filter (optional)
    --timeout <ms>              Timeout in milliseconds (optional, default: 300000)
  run-command                   Execute an IHarnessCommand (loads DLL)
    --dll <path>                Path to the command DLL (required)
    --type <typename>           Command type name (required)
    --args <json>               Arguments as JSON (optional, e.g., '{""key"":""value""}')
    --timeout <ms>              Timeout in milliseconds (optional, default: 300000)
  shutdown                      Stop the IPC server (Revit continues running)
  dialog-log                    Show recent dialog auto-handling records (JSONL)
    --count <n>                 Number of records (optional, default: 50)
  dialog-pending                List unknown dialogs waiting for an external decision (JSON)
    --all                       Include resolved and stale entries
  dialog-decide                 Press an existing button of a pending dialog (via the DialogWatcher)
    --id <pendingId>            Pending dialog id from dialog-pending (required)
    --button <name>             Button name exactly as listed (or --button-id <automationId>)
    --by <name>                 Who decided, e.g. the agent or model name (required)
    --save-rule                 Also save an allowlist rule (reversible buttons only)
    --timeout <ms>              Wait for the watcher (optional, default: 10000)
  dialog-watcher start|stop|status
                                Start (idempotent), stop or inspect the dialog watcher
  reload-allowlist              Re-read and validate the dialog allowlist
  lookup                        Inspect a Revit element by ElementId
    --id <elementId>            Element ID to inspect (required)
    --depth <n>                 Max decomposition depth (default: 0)
    --max-members <n>           Max members to return (default: 200)
    --include-fields            Include field members
    --include-private           Include private members
    --include-static            Include static members
    --filter <name>             Filter members by name (substring match)
    --timeout <ms>              Timeout in milliseconds (optional, default: 300000)

Exit codes:
  0 = Success (ok=true, and passed=true for run-tests)
  1 = Failure (ok=false or passed=false)
  2 = Connection/infrastructure error");
        }

        #region Installation Commands

        private static int RunInstall(string[] args)
        {
            int? revitVersion = null;
            string? skillsDir = null;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--revit-version":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var v))
                            revitVersion = v;
                        break;
                    case "--skills-dir":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Error: --skills-dir needs a path");
                            return ExitFailure;
                        }
                        skillsDir = Path.GetFullPath(args[++i]);
                        break;
                }
            }

            if (!revitVersion.HasValue)
            {
                Console.Error.WriteLine("Error: --revit-version is required");
                return ExitFailure;
            }

            Console.WriteLine($"Installing RevitHarness for Revit {revitVersion}...");

            var result = new RevitInstaller(InstallLayout.Create(skillsDir)).Install(revitVersion.Value);

            if (result.Success)
            {
                Console.WriteLine(result.Message);
                Console.WriteLine($"Installation folder: {result.InstallationFolder}");
                if (result.InstalledFiles.Count > 0)
                {
                    Console.WriteLine("Installed files:");
                    foreach (var file in result.InstalledFiles)
                    {
                        Console.WriteLine($"  - {file}");
                    }
                }
                return ExitSuccess;
            }
            else
            {
                Console.Error.WriteLine(result.Message);
                return ExitFailure;
            }
        }

        private static int RunUninstall(string[] args)
        {
            int? revitVersion = null;
            string? skillsDir = null;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--revit-version":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var v))
                            revitVersion = v;
                        break;
                    case "--skills-dir":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Error: --skills-dir needs a path");
                            return ExitFailure;
                        }
                        skillsDir = Path.GetFullPath(args[++i]);
                        break;
                }
            }

            if (!revitVersion.HasValue)
            {
                Console.Error.WriteLine("Error: --revit-version is required");
                return ExitFailure;
            }

            Console.WriteLine($"Uninstalling RevitHarness from Revit {revitVersion}...");

            var result = new RevitInstaller(InstallLayout.Create(skillsDir)).Uninstall(revitVersion.Value);

            if (result.Success)
            {
                Console.WriteLine(result.Message);
                if (result.RemovedFiles.Count > 0)
                {
                    Console.WriteLine("Removed:");
                    foreach (var file in result.RemovedFiles)
                    {
                        Console.WriteLine($"  - {file}");
                    }
                }
                return ExitSuccess;
            }
            else
            {
                Console.Error.WriteLine(result.Message);
                return ExitFailure;
            }
        }

        private static int RunStatus(string[] args)
        {
            int? revitVersion = null;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--revit-version":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var v))
                            revitVersion = v;
                        break;
                }
            }

            var installer = new RevitInstaller(InstallLayout.Create(null));
            if (revitVersion.HasValue)
            {
                // Show status for specific version
                PrintStatusForVersion(installer, revitVersion.Value);
            }
            else
            {
                // Show status for all detected Revit versions
                var versions = installer.GetInstalledRevitVersions();

                if (versions.Count == 0)
                {
                    Console.WriteLine("No Revit addins folders found.");
                    return ExitSuccess;
                }

                Console.WriteLine("RevitHarness Installation Status:");
                Console.WriteLine(new string('=', 50));

                foreach (var version in versions)
                {
                    PrintStatusForVersion(installer, version);
                    Console.WriteLine();
                }
            }

            return ExitSuccess;
        }

        private static void PrintStatusForVersion(RevitInstaller installer, int revitVersion)
        {
            var status = installer.GetStatus(revitVersion);

            var statusText = status.IsInstalled ? "INSTALLED" : "NOT INSTALLED";
            Console.WriteLine($"Revit {revitVersion}: {statusText}");

            if (status.AddinsFolderExists)
            {
                Console.WriteLine($"  Addins folder: {status.AddinsFolder}");

                if (status.IsInstalled)
                {
                    Console.WriteLine($"  Installation: {status.InstallationFolder}");
                    Console.WriteLine($"  Files: {string.Join(", ", status.InstalledFiles)}");
                }
            }
            else
            {
                Console.WriteLine($"  Addins folder does not exist");
            }
        }

        #endregion

        #region IPC Commands

        private static async Task<int> RunPingAsync()
        {
            using var client = new IpcClient();
            await client.ConnectAsync();

            var request = new IpcRequest
            {
                Id = Guid.NewGuid().ToString(),
                Op = Operations.Ping,
                Timestamp = DateTime.UtcNow.ToString("O")
            };

            var response = await client.SendRequestAsync(request);
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            return response.Ok ? ExitSuccess : ExitFailure;
        }

        private static async Task<int> RunDialogLogAsync(string[] args)
        {
            int count = 50;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("--count", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out var c))
                    count = c;
            }

            using var client = new IpcClient();
            await client.ConnectAsync();

            var response = await client.SendRequestAsync(new IpcRequest
            {
                Id = Guid.NewGuid().ToString(),
                Op = Operations.DialogLog,
                Timestamp = DateTime.UtcNow.ToString("O"),
                Payload = new Dictionary<string, object> { ["count"] = count }
            });
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            return response.Ok ? ExitSuccess : ExitFailure;
        }

        private static readonly System.Text.Json.JsonSerializerOptions DialogJson = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static int RunDialogPending(string[] args)
        {
            var all = args.Skip(1).Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
            var items = new RevitHarness.Dialogs.PendingStore().List(openOnly: !all);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ok = true, count = items.Count, pending = items }, DialogJson));
            return ExitSuccess;
        }

        private static int RunDialogDecide(string[] args)
        {
            string? id = null, button = null, buttonId = null, by = null;
            bool saveRule = false;
            int timeoutMs = 10000;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--id": id = i + 1 < args.Length ? args[++i] : null; break;
                    case "--button": button = i + 1 < args.Length ? args[++i] : null; break;
                    case "--button-id": buttonId = i + 1 < args.Length ? args[++i] : null; break;
                    case "--by": by = i + 1 < args.Length ? args[++i] : null; break;
                    case "--save-rule": saveRule = true; break;
                    case "--timeout":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out timeoutMs) || timeoutMs < 0)
                            return PrintDialogUsageError("--timeout needs a non-negative integer (milliseconds)");
                        break;
                    default: return PrintDialogUsageError("unknown option: " + args[i]);
                }
            }

            object Print(bool ok, string? error, RevitHarness.Dialogs.PendingDialog? pending) => new { ok, error, pending };
            if (string.IsNullOrWhiteSpace(id))
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(Print(false, "--id is required", null), DialogJson));
                return ExitFailure;
            }

            var store = new RevitHarness.Dialogs.PendingStore();
            var request = store.RequestDecision(id!, button, buttonId, by, saveRule);
            if (!request.Ok)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(Print(false, request.Error, request.Pending), DialogJson));
                return ExitFailure;
            }

            var result = store.WaitForResolution(id!, TimeSpan.FromMilliseconds(timeoutMs));
            if (result == null)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(Print(false,
                    "the DialogWatcher did not apply the decision in time (is it running? revit-harness dialog-watcher start); request withdrawn",
                    store.Get(id!)), DialogJson));
                return ExitConnectionError;
            }

            var ok = result.Status == RevitHarness.Dialogs.PendingStatus.Resolved;
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                Print(ok, ok ? null : result.StaleReason ?? result.Resolution?.Note ?? result.Status, result), DialogJson));
            return ok ? ExitSuccess : ExitFailure;
        }

        private static int RunDialogWatcher(string[] args)
        {
            var verb = args.Length == 2 ? args[1].ToLowerInvariant() : null;
            if (verb != "start" && verb != "stop" && verb != "status")
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ok = false, error = "usage: revit-harness dialog-watcher start|stop|status" }, DialogJson));
                return ExitFailure;
            }

            var watcher = new WatcherControl(InstallLayout.Create(null));
            var result = verb == "start" ? watcher.Start()
                : verb == "stop" ? watcher.Stop()
                : watcher.Status();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = result.Ok,
                message = result.Message,
                running = result.State.Running,
                pid = result.State.Pid,
                logDirectory = result.State.LogDirectory,
                latestLog = result.State.LatestLog
            }, DialogJson));
            return result.Ok ? ExitSuccess : ExitFailure;
        }

        private static int PrintDialogUsageError(string error)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ok = false, error, pending = (object?)null }, DialogJson));
            return ExitFailure;
        }

        private static async Task<int> RunReloadAllowlistAsync()
        {
            using var client = new IpcClient();
            await client.ConnectAsync();

            var response = await client.SendRequestAsync(new IpcRequest
            {
                Id = Guid.NewGuid().ToString(),
                Op = Operations.ReloadAllowlist,
                Timestamp = DateTime.UtcNow.ToString("O")
            });
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            return response.Ok ? ExitSuccess : ExitFailure;
        }

        private static async Task<int> RunTestsAsync(string[] args)
        {
            string? dll = null;
            string? type = null;
            string? filter = null;
            int timeout = 300000;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--dll":
                        if (i + 1 < args.Length) dll = args[++i];
                        break;
                    case "--type":
                        if (i + 1 < args.Length) type = args[++i];
                        break;
                    case "--filter":
                        if (i + 1 < args.Length) filter = args[++i];
                        break;
                    case "--timeout":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var t)) timeout = t;
                        break;
                }
            }

            if (string.IsNullOrEmpty(dll))
            {
                Console.Error.WriteLine("Error: --dll is required");
                return ExitFailure;
            }

            if (string.IsNullOrEmpty(type))
            {
                Console.Error.WriteLine("Error: --type is required");
                return ExitFailure;
            }

            using var client = new IpcClient();
            await client.ConnectAsync();

            var payload = new Dictionary<string, object>
            {
                ["assemblyPath"] = dll!,
                ["entryType"] = type!,
                ["timeoutMs"] = timeout,
                ["commandId"] = Environment.GetEnvironmentVariable("REVIT_HARNESS_COMMAND_ID") ?? Guid.NewGuid().ToString("N"),
                ["taskId"] = Environment.GetEnvironmentVariable("REVIT_HARNESS_TASK_ID") ?? string.Empty
            };

            if (!string.IsNullOrEmpty(filter))
            {
                payload["args"] = new Dictionary<string, object> { ["filter"] = filter! };
            }

            var request = new IpcRequest
            {
                Id = Guid.NewGuid().ToString(),
                Op = Operations.RunTests,
                Timestamp = DateTime.UtcNow.ToString("O"),
                Payload = payload
            };

            var response = await client.SendRequestAsync(request, timeout + 10000);
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            if (!response.Ok)
                return ExitFailure;

            // Check if tests passed
            var resultJson = JsonConvert.SerializeObject(response.Result);
            var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(resultJson);
            if (result != null && result.TryGetValue("passed", out var passed))
            {
                if (passed is bool b && b)
                    return ExitSuccess;
                if (bool.TryParse(passed?.ToString(), out var parsedPassed) && parsedPassed)
                    return ExitSuccess;
            }

            return ExitFailure;
        }

        private static async Task<int> RunCommandAsync(string[] args)
        {
            string? dll = null;
            string? type = null;
            string? argsJson = null;
            int timeout = 300000;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--dll":
                        if (i + 1 < args.Length) dll = args[++i];
                        break;
                    case "--type":
                        if (i + 1 < args.Length) type = args[++i];
                        break;
                    case "--args":
                        if (i + 1 < args.Length) argsJson = args[++i];
                        break;
                    case "--timeout":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var t)) timeout = t;
                        break;
                }
            }

            if (string.IsNullOrEmpty(dll))
            {
                Console.Error.WriteLine("Error: --dll is required");
                return ExitFailure;
            }

            if (string.IsNullOrEmpty(type))
            {
                Console.Error.WriteLine("Error: --type is required");
                return ExitFailure;
            }

            using var client = new IpcClient();
            await client.ConnectAsync();

            var payload = new Dictionary<string, object>
            {
                ["assemblyPath"] = dll!,
                ["commandType"] = type!,
                ["timeoutMs"] = timeout,
                ["commandId"] = Environment.GetEnvironmentVariable("REVIT_HARNESS_COMMAND_ID") ?? Guid.NewGuid().ToString("N"),
                ["taskId"] = Environment.GetEnvironmentVariable("REVIT_HARNESS_TASK_ID") ?? string.Empty
            };

            if (!string.IsNullOrEmpty(argsJson))
            {
                try
                {
                    var parsedArgs = JsonConvert.DeserializeObject<Dictionary<string, object>>(argsJson);
                    if (parsedArgs != null)
                    {
                        payload["args"] = parsedArgs;
                    }
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"Error: Invalid JSON for --args: {ex.Message}");
                    return ExitFailure;
                }
            }

            var request = new IpcRequest
            {
                Id = Guid.NewGuid().ToString(),
                Op = Operations.RunCommand,
                Timestamp = DateTime.UtcNow.ToString("O"),
                Payload = payload
            };

            var response = await client.SendRequestAsync(request, timeout + 10000);
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            return response.Ok ? ExitSuccess : ExitFailure;
        }

        private static async Task<int> RunShutdownAsync()
        {
            using var client = new IpcClient();
            await client.ConnectAsync();

            var request = new IpcRequest
            {
                Id = Guid.NewGuid().ToString(),
                Op = Operations.Shutdown,
                Timestamp = DateTime.UtcNow.ToString("O")
            };

            var response = await client.SendRequestAsync(request);
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            return response.Ok ? ExitSuccess : ExitFailure;
        }

        private static async Task<int> RunLookupAsync(string[] args)
        {
            int? elementId = null;
            int depth = 0;
            int maxMembers = 200;
            bool includeFields = false;
            bool includePrivate = false;
            bool includeStatic = false;
            string? nameFilter = null;
            int timeout = 300000;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--id":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var id))
                            elementId = id;
                        break;
                    case "--depth":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var d))
                            depth = d;
                        break;
                    case "--max-members":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var mm))
                            maxMembers = mm;
                        break;
                    case "--include-fields":
                        includeFields = true;
                        break;
                    case "--include-private":
                        includePrivate = true;
                        break;
                    case "--include-static":
                        includeStatic = true;
                        break;
                    case "--filter":
                        if (i + 1 < args.Length) nameFilter = args[++i];
                        break;
                    case "--timeout":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var t))
                            timeout = t;
                        break;
                }
            }

            if (!elementId.HasValue)
            {
                Console.Error.WriteLine("Error: --id is required");
                return ExitFailure;
            }

            using var client = new IpcClient();
            await client.ConnectAsync();

            var payload = new Dictionary<string, object>
            {
                ["elementId"] = elementId.Value,
                ["maxDepth"] = depth,
                ["maxMembers"] = maxMembers,
                ["includeFields"] = includeFields,
                ["includePrivateMembers"] = includePrivate,
                ["includeStaticMembers"] = includeStatic
            };

            if (!string.IsNullOrEmpty(nameFilter))
            {
                payload["nameFilter"] = nameFilter!;
            }

            var request = new IpcRequest
            {
                Id = Guid.NewGuid().ToString(),
                Op = Operations.LookupElement,
                Timestamp = DateTime.UtcNow.ToString("O"),
                Payload = payload
            };

            var response = await client.SendRequestAsync(request, timeout + 10000);
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            return response.Ok ? ExitSuccess : ExitFailure;
        }

        #endregion
    }
}
