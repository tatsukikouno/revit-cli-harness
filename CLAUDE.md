# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Revit 2024 harness add-in that enables external CLI/agent execution of tests, commands, and element inspection within a running Revit instance. Supports DLL hot-swapping via shadow copy + byte-array loading without restarting Revit.

### Key Architecture

- **IPC Communication**: Named Pipe (`\\.\pipe\RevitHarness2024`) with JSON messages and int32 LE length-prefix framing
- **Thread Safety**: All Revit API calls go through `ExternalEvent` → `HarnessEventHandler.Execute()` on Revit's main thread. A 100ms polling timer in `App.cs` raises the event when requests are pending.
- **Shadow Copy + Byte Load**: DLLs are copied to `%LOCALAPPDATA%\RevitHarness\shadow\{timestamp}\`, then loaded via `Assembly.Load(byte[])` to bypass the .NET assembly cache entirely. This enables true hot-reload of same-name/same-version assemblies. Old shadow dirs auto-clean (max 10 generations, >1 day old).
- **Mixed Frameworks**: Addin/Contracts/Lookup target .NET Framework 4.8 (Revit requirement); CLI targets .NET 8.0
- **Embedded Resources**: CLI embeds the Addin build output as EmbeddedResources for self-contained `install` deployment

### NuGet Packages

| Package | Purpose | Distribution |
|---------|---------|--------------|
| `RevitHarness.Contracts` | ITestEntryPoint, IHarnessCommand, HarnessCommandBase | NuGet.org |
| `RevitHarness.Cli` | CLI dotnet tool + embedded addin DLLs | NuGet.org (dotnet tool) |

## Build Commands

```bash
# Build the solution (x64 only - must use x64 platform)
dotnet build revit-cli-harness.sln -c Debug

# Build single project
dotnet build src/RevitHarness.Contracts
dotnet build src/RevitHarness.Addin
dotnet build src/RevitHarness.Cli

# Generate NuGet packages
dotnet pack src/RevitHarness.Contracts -c Release
dotnet pack src/RevitHarness.Cli -c Release
```

**Important**: Build the Addin project before the CLI project. The CLI embeds Addin build outputs as resources (see `RevitHarness.Cli.csproj` EmbeddedResource items with `Condition="Exists(...)"`). Building via the solution handles this dependency automatically.

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
├── RevitHarness.Lookup/             # net48 - Element inspection (RevitLookup-style)
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

lib/LookupEngine/                    # External library used by RevitHarness.Lookup
```

## Development Notes

- **Target Frameworks**: Addin/Contracts/Lookup = net48 (x64); CLI = net8.0
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
