# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Revit 2024/2025/2026 harness add-in that enables external CLI/agent execution of tests, commands, and element inspection within a running Revit instance. Supports DLL hot-swapping via shadow copy + byte-array loading without restarting Revit.

### Key Architecture

- **IPC Communication**: Named Pipe (`\\.\pipe\RevitHarness2024`) with JSON messages and int32 LE length-prefix framing
- **Thread Safety**: All Revit API calls go through `ExternalEvent` → `HarnessEventHandler.Execute()` on Revit's main thread. A 100ms polling timer in `App.cs` raises the event when requests are pending.
- **Shadow Copy + Byte Load**: DLLs are copied to `%LOCALAPPDATA%\RevitHarness\shadow\{timestamp}\`, then loaded via `Assembly.Load(byte[])` to bypass the .NET assembly cache entirely. This enables true hot-reload of same-name/same-version assemblies. Old shadow dirs auto-clean (max 10 generations, >1 day old).
- **Mixed Frameworks**: Addin and Lookup both multi-target per Revit year via Configuration suffix (`R24`/`R25`/`R26`) — `net48` for Revit 2024, `net8.0-windows` for Revit 2025/2026. Contracts multi-targets `net48;net8.0-windows` directly (published NuGet package). CLI targets `net8.0` (a `dotnet tool` cannot use a Windows-specific TFM).
- **Embedded Resources**: CLI embeds the Addin build output as EmbeddedResources for self-contained `install` deployment

### NuGet Packages

| Package | Purpose | Distribution |
|---------|---------|--------------|
| `RevitHarness.Contracts` | ITestEntryPoint, IHarnessCommand, HarnessCommandBase | NuGet.org |
| `RevitHarness.Cli` | CLI dotnet tool + embedded addin DLLs | NuGet.org (dotnet tool) |

## Build Commands

```bash
# Build the Addin for a specific Revit year (must be built before the CLI, once per supported year).
# -p:Platform=x64 is required for a direct project build to land in bin\x64\... — without it, the
# output path omits the x64 segment and the CLI's embedded-resource glob won't find it.
dotnet build src/RevitHarness.Addin -c "Debug R24" -p:Platform=x64    # Revit 2024 (net48)
dotnet build src/RevitHarness.Addin -c "Debug R25" -p:Platform=x64    # Revit 2025 (net8.0-windows)
dotnet build src/RevitHarness.Addin -c "Debug R26" -p:Platform=x64    # Revit 2026 (net8.0-windows)

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

**Important**: Build the Addin project for all three configurations (`Debug R24`/`Debug R25`/`Debug R26`, or the `Release` equivalents) as **separate `dotnet build` invocations, before** building the CLI project or the solution. The CLI embeds each year's Addin build output as a version-prefixed resource (see `RevitHarness.Cli.csproj` `EmbeddedResource` items with `Condition="Exists(...)"`), and `RevitInstaller` selects the matching set at install time based on `--revit-version`. That `Exists(...)` condition is evaluated once, up front, and is *not* re-checked after a same-invocation dependency build finishes — so even a single `dotnet build revit-cli-harness.sln` run will silently embed nothing for whichever year's Addin output doesn't already exist on disk before that invocation starts (this is a pre-existing characteristic of the embedding mechanism, not specific to R25/R26). Always build R24/R25/R26 as their own prior commands first.

## Project Structure

```
src/
├── RevitHarness.Contracts/          # net48 - Shared interfaces (NuGet package)
│   ├── ITestEntryPoint.cs           # Test entry point interface
│   ├── IHarnessCommand.cs           # CLI-only command interface
│   └── HarnessCommandBase.cs        # Dual-use: CLI (IHarnessCommand) + Ribbon (IExternalCommand)
│
├── RevitHarness.Addin/              # net48 - Revit add-in (UseWPF=true)
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
├── RevitHarness.Lookup/             # net48 (R24) / net8.0-windows (R25/R26) - Element inspection (RevitLookup-style)
│   ├── ElementInspector.cs          # Main inspection entry point
│   ├── InspectOptions.cs            # Depth, filters, member visibility options
│   └── Descriptors/                 # Type-specific value formatters (Element, XYZ, Curve, etc.)
│
└── RevitHarness.Cli/                # net8.0 - CLI tool (dotnet tool)
    ├── Program.cs                   # Command routing (manual arg parsing, no library)
    ├── IpcClient.cs                 # Named Pipe client
    ├── IpcMessages.cs               # Message classes (separate copy from Addin)
    └── Installation/
        └── RevitInstaller.cs        # Extracts embedded resources to Revit addins folder

lib/LookupEngine/                    # External library used by RevitHarness.Lookup, vendored per-TFM (net48/, net8.0-windows/)
```

## Development Notes

- **Target Frameworks**: Addin/Lookup = net48 (R24) / net8.0-windows (R25, R26); Contracts = net48;net8.0-windows; CLI = net8.0
- **LookupEngine vendoring**: `lib/LookupEngine/{net48,net8.0-windows}/` holds `LookupEngine.dll` + `LookupEngine.Abstractions.dll`, extracted from an installed RevitLookup add-in for the matching Revit year (not published to NuGet.org, so there's no package to reference). To refresh, copy the two DLLs from `%PROGRAMDATA%\Autodesk\Revit\Addins\{year}\RevitLookup\` into the matching TFM subfolder.
- **Revit References**: RevitAPI.dll and RevitAPIUI.dll from `C:\Program Files\Autodesk\Revit 2024\` (referenced with `Private=False`)
- **C# Language Version**: 9.0 with nullable enabled, warnings as errors (see `Directory.Build.props`)
- **Central Package Management**: Versions in `Directory.Packages.props`
- **Debug**: Addin project configured to launch Revit.exe directly
- **IpcMessages duplication**: Both Addin and CLI have their own `IpcMessages.cs` because they target different frameworks

## IPC Protocol

Operations dispatched in `HarnessEventHandler.ProcessRequest()`:
- `ping` — Health check, returns Revit version/process info
- `runTests` — Shadow-copy DLL, load via reflection, execute `ITestEntryPoint.Run()`
- `runCommand` — Execute `IHarnessCommand` or `IExternalCommand` via `CommandExecutor`
- `lookupElement` — Inspect element by ElementId using `ElementInspector`
- `shutdown` — Stop IPC server (Revit continues running)

Exit codes: 0=success, 1=failure (`ok=false` or `passed=false`), 2=connection/timeout error

## CLI Usage

```bash
revit-harness install --revit-version 2024 [--force]
revit-harness uninstall --revit-version 2024
revit-harness status [--revit-version 2024]
revit-harness ping
revit-harness run-tests --dll <path> --type <typename> [--filter <filter>] [--timeout <ms>]
revit-harness run-command --dll <path> --type <typename> [--args '{"key":"value"}'] [--timeout <ms>]
revit-harness post-command --id <commandId>
revit-harness lookup --id <elementId> [--depth <n>] [--max-members <n>] [--include-fields] [--include-private] [--include-static] [--filter <name>]
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

## Logs

`%LOCALAPPDATA%\RevitHarness\logs\harness_{date}.log`
