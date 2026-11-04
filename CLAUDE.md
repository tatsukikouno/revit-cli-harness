# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Revit 2024/2025/2026/2027 harness add-in that enables external CLI/agent execution of tests, commands, and element inspection within a running Revit instance. Supports DLL hot-swapping via shadow copy + byte-array loading without restarting Revit.

### Key Architecture

- **IPC Communication**: Named Pipe (`\\.\pipe\RevitHarness2024`) with JSON messages and int32 LE length-prefix framing
- **Thread Safety**: All Revit API calls go through `ExternalEvent` → `HarnessEventHandler.Execute()` on Revit's main thread. A 100ms polling timer in `App.cs` raises the event when requests are pending.
- **Shadow Copy + Byte Load**: DLLs are copied to `%LOCALAPPDATA%\RevitHarness\shadow\{timestamp}\`, then loaded via `Assembly.Load(byte[])` to bypass the .NET assembly cache entirely. This enables true hot-reload of same-name/same-version assemblies. Old shadow dirs auto-clean (max 10 generations, >1 day old).
- **Mixed Frameworks**: Addin and Lookup both multi-target per Revit year via Configuration suffix (`R24`/`R25`/`R26`/`R27`) — `net48` for Revit 2024, `net8.0-windows` for Revit 2025/2026, `net10.0-windows` for Revit 2027. Contracts multi-targets `net48;net8.0-windows;net10.0-windows` directly (published NuGet package). CLI and DialogWatcher target .NET 10 (independent of Revit's runtime); the CLI targets `net10.0` (a `dotnet tool` cannot use a Windows-specific TFM).
- **Embedded Resources**: CLI embeds the Addin build output as EmbeddedResources for self-contained `install` deployment. The DialogWatcher build output and `skills/` ship as plain files next to the CLI dll in the nupkg (`tools/net10.0/any/watcher`, `/skills`), and `install` copies them to `%LOCALAPPDATA%\RevitHarness\watcher` and `%USERPROFILE%\.claude\skills` (or `--skills-dir`)

### NuGet Packages

| Package | Purpose | Distribution |
|---------|---------|--------------|
| `RevitHarness.Contracts` | ITestEntryPoint, IHarnessCommand, HarnessCommandBase | NuGet.org |
| `RevitHarness.Cli` | CLI dotnet tool + embedded addin DLLs | NuGet.org (dotnet tool) |

## Build Commands

```bash
# Build the Addin for a specific Revit year (only needed to build one year on its own; the CLI builds all years itself).
# -p:Platform=x64 is required for a direct project build to land in bin\x64\... — without it, the
# output path omits the x64 segment and the CLI's embedded-resource glob won't find it.
dotnet build src/RevitHarness.Addin -c "Debug R24" -p:Platform=x64    # Revit 2024 (net48)
dotnet build src/RevitHarness.Addin -c "Debug R25" -p:Platform=x64    # Revit 2025 (net8.0-windows)
dotnet build src/RevitHarness.Addin -c "Debug R26" -p:Platform=x64    # Revit 2026 (net8.0-windows)
dotnet build src/RevitHarness.Addin -c "Debug R27" -p:Platform=x64    # Revit 2027 (net10.0-windows)

# Build the solution (Platform=x64 is resolved automatically via the .sln's configuration mapping).
# Plain Debug/Release map Addin to its R24/Revit-2024 build, for backward compatibility.
dotnet build revit-cli-harness.sln -c Debug

# Build single project
dotnet build src/RevitHarness.Contracts
dotnet build src/RevitHarness.Addin -c "Debug R24" -p:Platform=x64
dotnet build src/RevitHarness.Cli

# Generate NuGet packages
dotnet pack src/RevitHarness.Contracts -c Release
dotnet pack src/RevitHarness.Cli -c Release
```

**Important**: `RevitHarness.Cli` builds the Addin for `R24`/`R25`/`R26`/`R27` and `RevitHarness.DialogWatcher` itself (target `BundleAddinAndWatcher` in `RevitHarness.Cli.csproj`) and embeds the results, so `dotnet build src/RevitHarness.Cli` and a plain `dotnet pack src/RevitHarness.Cli -c Release` need no earlier build step. Each year restores into its own `obj\R24|R25|R26|R27` directory (`src/RevitHarness.Addin/Directory.Build.props`), and the target removes the inherited `TargetFramework` global property: `dotnet pack` re-enters the CLI project with `TargetFramework=net10.0`, which otherwise overrides the Addin's per-year TFM and fails with NETSDK1136.

## Project Structure

```
src/
├── RevitHarness.Contracts/          # net48;net8.0-windows;net10.0-windows - Shared interfaces (NuGet package)
│   ├── ITestEntryPoint.cs           # Test entry point interface
│   ├── IHarnessCommand.cs           # CLI-only command interface
│   └── HarnessCommandBase.cs        # Dual-use: CLI (IHarnessCommand) + Ribbon (IExternalCommand)
│
├── RevitHarness.Addin/              # net48 (R24) / net8.0-windows (R25/R26) / net10.0-windows (R27) - Revit add-in (UseWPF=true)
│   ├── App.cs                       # IExternalApplication: starts IpcServer, ExternalEvent, ribbon
│   ├── Commands/
│   │   ├── CommandExecutor.cs       # Shadow copy + reflection execution engine
│   │   ├── AssemblyScanner.cs       # Discovers IHarnessCommand/IExternalCommand types in DLLs
│   │   └── OpenAddinManagerCommand.cs
│   ├── Ipc/
│   │   ├── IpcMessages.cs           # Request/Response/Operations/ErrorCodes
│   │   ├── IpcServer.cs             # Named Pipe server with concurrent request queue
│   │   ├── HarnessEventHandler.cs   # Dispatches IPC ops on Revit main thread
│   │   └── ShadowCopier.cs          # Shadow copy + byte-array assembly loading
│   └── UI/
│       └── AddinManagerWindow.xaml   # GUI for browsing DLLs and executing commands
│
├── RevitHarness.Lookup/             # net48 (R24) / net8.0-windows (R25/R26) / net10.0-windows (R27) - Element inspection (RevitLookup-style)
│   ├── ElementInspector.cs          # Main inspection entry point
│   ├── InspectOptions.cs            # Depth, filters, member visibility options
│   └── Descriptors/                 # Type-specific value formatters (Element, XYZ, Curve, etc.)
│
└── RevitHarness.Cli/                # net10.0 - CLI tool (dotnet tool)
    ├── Program.cs                   # Command routing (manual arg parsing, no library)
    ├── IpcClient.cs                 # Named Pipe client
    ├── IpcMessages.cs               # Message classes (separate copy from Addin)
    └── Installation/
        └── RevitInstaller.cs        # Extracts embedded resources to Revit addins folder

lib/LookupEngine/                    # External library used by RevitHarness.Lookup, vendored per-TFM (net48/, net8.0-windows/, net10.0-windows/)
```

## Development Notes

- **Target Frameworks**: Addin/Lookup = net48 (R24) / net8.0-windows (R25, R26) / net10.0-windows (R27); Contracts = net48;net8.0-windows;net10.0-windows; CLI = net10.0; DialogWatcher = net10.0-windows
- **LookupEngine vendoring**: `lib/LookupEngine/{net48,net8.0-windows,net10.0-windows}/` holds `LookupEngine.dll` + `LookupEngine.Abstractions.dll`, extracted from the RevitLookup release for the matching Revit year (2024: 2024.3.x, 2025/2026: 2026.1.x, 2027: 2027.0.4) (not published to NuGet.org, so there's no package to reference). To refresh, copy the two DLLs from `%PROGRAMDATA%\Autodesk\Revit\Addins\{year}\RevitLookup\` into the matching TFM subfolder.
- **Revit References**: `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` NuGet reference assemblies per year (see `Directory.Packages.props`)
- **C# Language Version**: 9.0 with nullable enabled, warnings as errors (see `Directory.Build.props`)
- **Central Package Management**: Versions in `Directory.Packages.props`
- **Debug**: Addin project configured to launch Revit.exe directly
- **IpcMessages duplication**: Both Addin and CLI have their own `IpcMessages.cs` because they target different frameworks

## Design Policy

- Do not keep unnecessary options, fallbacks, or backward-compatibility shims. When a spec changes, delete the old mechanism instead of keeping it behind an option.

## IPC Protocol

Operations dispatched in `HarnessEventHandler.ProcessRequest()`:
- `ping` — Health check, returns Revit version/process info
- `runTests` — Shadow-copy DLL, load via reflection, execute `ITestEntryPoint.Run()`
- `runCommand` — Execute `IHarnessCommand` or `IExternalCommand` via `CommandExecutor`
- `lookupElement` — Inspect element by ElementId using `ElementInspector`
- `shutdown` — Stop IPC server (Revit continues running)
- `dialog-log` — Return recent dialog JSONL records (`count`); handled off the main thread
- `reload-allowlist` — Validate the dialog allowlist; handled off the main thread

Exit codes: 0=success, 1=failure (`ok=false` or `passed=false`), 2=connection/timeout error

## CLI Usage

```bash
revit-harness install --revit-version 2024 [--skills-dir <path>]
revit-harness uninstall --revit-version 2024 [--skills-dir <path>]
revit-harness status [--revit-version 2024]
revit-harness ping
revit-harness run-tests --dll <path> --type <typename> [--filter <filter>] [--timeout <ms>]
revit-harness run-command --dll <path> --type <typename> [--args '{"key":"value"}'] [--timeout <ms>]
revit-harness post-command --id <commandId>
revit-harness lookup --id <elementId> [--depth <n>] [--max-members <n>] [--include-fields] [--include-private] [--include-static] [--filter <name>]
revit-harness dialog-log [--count <n>]
revit-harness reload-allowlist
revit-harness dialog-watcher start|stop|status
revit-harness dialog-pending [--all]
revit-harness dialog-decide --id <pendingId> --button <name> --by <decider> [--button-id <automationId>] [--save-rule] [--timeout <ms>]
revit-harness shutdown
```

## Implementing Tests and Commands

```bash
dotnet add package RevitHarness.Contracts
```

Three patterns available:
- **`ITestEntryPoint`** — For tests: `string Run(UIApplication uiapp, IDictionary<string, object> args)` — return JSON with `passed` field
- **`IHarnessCommand`** — CLI-only commands: `string Execute(UIApplication uiapp, IDictionary<string, object> args)`
- **`HarnessCommandBase`** — Dual-use class that works from both CLI (`IHarnessCommand`) and Revit ribbon (`IExternalCommand`) via a single `Execute` override

## Dialog Auto-Handling

`App.OnStartup` subscribes `DialogHooks` (`src/RevitHarness.Addin/DialogHandling/`) to `UIControlledApplication.DialogBoxShowing` and `ControlledApplication.FailuresProcessing`; `OnShutdown` unsubscribes. Decisions go through `RevitHarness.Dialogs.Shared` (`DialogDecider`). The harness never calls an AI: unknown dialogs become pending entries that an external AI/caller resolves via `dialog-pending` / `dialog-decide`. Warnings are deleted only when an allowlist rule with a `dialogId` pattern matches `failure:<guid>`. The IPC ops `dialog-log` and `reload-allowlist` are answered on the pipe thread (`IpcServer.DirectHandler`), not through the ExternalEvent, so they work while a modal dialog blocks Revit. Revit-free logic (`DialogInfoBuilder`, `DialogSettings`, `DialogLogReader`) is compiled into `tests/RevitHarness.Dialogs.Shared.Tests` via linked files.

## Pending dialogs: how an AI driving RevitHarness resolves unknown dialogs

The harness never calls an AI itself. A dialog with no matching allowlist rule (or one matching a `noLearn` rule) is left untouched, dumped (UIA tree plus screenshot) and registered as a pending entry under `%LOCALAPPDATA%\RevitHarness\dialogs\pending\<id>.json`; the JSONL log gets `decidedBy="pending"`. The caller (an AI agent or a person) decides:

```bash
revit-harness dialog-pending [--all]      # open entries (id, title, automationId, buttons, bodySummary, screenshotPath, dumpPath, status)
revit-harness dialog-decide --id <pendingId> --button "<exact button name>" --by "<agent or model name>" [--save-rule] [--timeout 10000]
```

Check for pending dialogs whenever a command seems stuck or times out (a modal dialog blocks Revit), read `bodySummary` and the screenshot, then press one of the listed buttons. A nonexistent button is rejected. The watcher (`revit-harness dialog-watcher start`) must be running: it presses the button by UI Automation and completes the entry, which the CLI waits for (exit 0 resolved, 1 rejected/stale/failed, 2 watcher did not pick it up; the request is then withdrawn). `--by` is mandatory and lands in the log as `decidedBy="external"`, `deciderName=<by>`. A dialog that disappears before the decision is marked `stale` and cannot be decided.

`--save-rule` also writes an allowlist rule, but only for buttons known to be reversible (OK, Cancel, Close); a destructive button is pressed once and never learned, write that rule by hand if you really want it. A dialog covered by a `noLearn` rule is never saved, and neither is anything on the unsigned add-in security dialog.

**Testers**: mark the dialogs your test deliberately provokes with a rule that has `"noLearn": true`, so they are never auto-pressed or learned and always reach the caller as pending (see `samples/allowlist.sample.json`).

## Unsigned add-in security dialog

`match.addIn` is a plain string, not a pattern object, and applies only to the "Security - Unsigned Add-in" dialog: a rule with `addIn` never matches any other dialog. It is compared ordinal and case-insensitively. A value with a path separator is compared with the dll path only; any other value is compared with the add-in name and the dll file name, so an add-in cannot impersonate a path through its self-declared name. Without `*` it must match exactly. A trailing `*` (for example `Contoso.*`) makes it a prefix match; any other `*` is literal and there is no regex. A lone `*` matches every unsigned add-in, so it loads unknown unsigned add-ins too. For the security dialog only a rule with an `addIn` condition can answer, so known add-ins get the configured button (for example "Load Once") and every other add-in becomes pending. "Always Load" and "Do Not Load" are never learned. Rules may also use `match.automationId` and `match.buttons` (exact button-name set) next to `title` and `text`. A text pattern needs a non-empty `value`; an omitted `mode` means `equals`. A matching `noLearn` rule always wins over earlier rules, and a matching rule whose action does not apply to the dialog leaves it pending. See `samples/allowlist.sample.json`.

## Dialog Watcher (FlaUI)

`src/RevitHarness.DialogWatcher` is an on-demand exe (net10.0-windows, needs the .NET 10 Desktop Runtime, no Revit dependency) that watches Revit.exe top-level windows and Windows error reporting dialogs (WerFault) through UI Automation (WindowOpened event plus a 300 ms poll). Known dialogs are answered from the allowlist (`%LOCALAPPDATA%\RevitHarness\dialogs\allowlist.json`) by pressing the button with the UIA Invoke pattern (never the real mouse). Unknown dialogs get a UIA tree dump in `dialogs\dumps\`, a screenshot in `dialogs\screenshots\` and a JSONL log record plus a pending entry; nothing is pressed. `install` and `uninstall` refuse to run while Revit.exe is running (close Revit first), and `install`, `uninstall` and `dialog-watcher start|stop` take a cross-process lock and fail with a message when another one is running. `dialog-watcher start` fails with a message naming the .NET Desktop Runtime to install when it is missing.

```bash
dotnet build src/RevitHarness.DialogWatcher -c Debug -p:Platform=x64   # only needed when building from source
revit-harness dialog-watcher start     # idempotent: no second instance; prints pid and log location
revit-harness dialog-watcher status    # exit 1 when not running
revit-harness dialog-watcher stop
RevitHarness.DialogWatcher.exe --once --dry-run              # one pass, log only
RevitHarness.DialogWatcher.exe --selftest                    # fake WinForms dialog, needs an interactive desktop
```

`revit-harness install` copies the watcher to `%LOCALAPPDATA%\RevitHarness\watcher\`. It is never started automatically and never resident by itself: start it before launching Revit and stop it when done (the `revit-harness-dialogs` SKILL tells an AI agent to do exactly that). Install stops a running watcher before replacing it.

Exe options: `--once`, `--dry-run`, `--pid <revit pid>`. The allowlist, pending entries and logs always live under `%LOCALAPPDATA%\RevitHarness\dialogs`. Stop with Ctrl+C or by creating `dialogs\watcher.stop`. A session-local named mutex (`Local\`) prevents a second watcher per Windows session; the pending-store and log mutexes are session-local too, so run the watcher, add-in and CLI in the same session.

## Logs

`%LOCALAPPDATA%\RevitHarness\logs\harness_{date}.log`
