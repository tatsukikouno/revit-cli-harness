# RevitHarness

[![NuGet RevitHarness.Cli](https://img.shields.io/nuget/v/RevitHarness.Cli.svg?label=RevitHarness.Cli)](https://www.nuget.org/packages/RevitHarness.Cli/)
[![NuGet RevitHarness.Contracts](https://img.shields.io/nuget/v/RevitHarness.Contracts.svg?label=RevitHarness.Contracts)](https://www.nuget.org/packages/RevitHarness.Contracts/)

A harness add-in for Revit 2024 that enables CLI and agent-driven test execution, command execution, and element inspection against a running Revit instance. Supports DLL hot-swapping without restarting Revit.

[日本語版 README](README.ja.md)

## Features

- **Named Pipe IPC** — Fast JSON-based communication between CLI and Revit
- **Shadow Copy + Byte Load** — DLLs are shadow-copied and loaded from byte arrays, bypassing file locks and the .NET assembly cache for seamless iterative development
- **Element Lookup** — RevitLookup-style element inspection from the CLI
- **Addin Manager UI** — A Revit ribbon button that opens a GUI to browse DLLs, scan for commands, and execute them
- **HarnessCommandBase** — Write a single class that works both from the CLI (`IHarnessCommand`) and as a Revit ribbon button (`IExternalCommand`)

## Installation

### CLI Tool

```bash
dotnet tool install -g RevitHarness.Cli
```

### Deploying the Add-in

```bash
# Install
revit-harness install --revit-version 2024

# Reinstall (overwrite with --force)
revit-harness install --revit-version 2024 --force

# Check status
revit-harness status

# Uninstall
revit-harness uninstall --revit-version 2024
```

> Close Revit before installing or uninstalling. Restart Revit after installation to load the add-in.

### Installation Path

```
%APPDATA%\Autodesk\Revit\Addins\2024\
├── RevitHarness.addin              # Manifest
└── RevitHarness\
    ├── RevitHarness.Addin.dll
    ├── RevitHarness.Contracts.dll
    ├── RevitHarness.Lookup.dll
    ├── LookupEngine.dll
    ├── Newtonsoft.Json.dll
    └── ...
```

## CLI Commands

### ping — Health Check

```bash
revit-harness ping
```

Returns Revit version, process ID, and active document name.

### run-tests — Execute Tests

```bash
revit-harness run-tests --dll "path/to/tests.dll" --type "Company.Tests.EntryPoint" [--filter "Smoke"] [--timeout 300000]
```

Executes an `ITestEntryPoint` implementation from the specified DLL. The exit code is determined by the `passed` field in the result JSON.

### run-command — Execute a Command

```bash
revit-harness run-command --dll "path/to/command.dll" --type "Company.MyCommand" [--args '{"key":"value"}'] [--timeout 300000]
```

Executes a type implementing `IHarnessCommand` or `IExternalCommand`.

### post-command — Post a Registered Revit Command

```bash
revit-harness post-command --id "CustomCtrl_%CustomCtrl_%Add-Ins%Addin%Command"
```

Triggers a registered Revit ribbon command by its command ID.

### lookup — Element Inspection

```bash
revit-harness lookup --id 12345 [--depth 1] [--max-members 200] [--include-fields] [--include-private] [--include-static] [--filter "Name"]
```

RevitLookup-style element inspection from the CLI. Outputs property and method return values as JSON.

### shutdown — Stop the IPC Server

```bash
revit-harness shutdown
```

Stops the IPC server. Revit continues running.

### Other

```bash
revit-harness version   # Show version
revit-harness help      # Show help
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Failure (`ok=false` or `passed=false`) |
| 2 | Connection / infrastructure error |

## Addin Manager UI

After launching Revit, access it from the **RevitHarness** ribbon tab → **Addin Manager** button.

1. **Browse** to select a DLL
2. **Scan** to list types implementing `IHarnessCommand` / `IExternalCommand`
3. Select a type and click **Execute**
4. Results are displayed in the text area at the bottom

## Implementing Tests and Commands

### Contracts Package

```bash
dotnet add package RevitHarness.Contracts
```

### ITestEntryPoint — For Tests

```csharp
using RevitHarness.Contracts;

public class MyTestEntryPoint : ITestEntryPoint
{
    public string Run(UIApplication uiapp, IDictionary<string, object> args)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        // Test logic
        return JsonConvert.SerializeObject(new { passed = true, summary = "All tests passed" });
    }
}
```

### IHarnessCommand — For Commands (CLI Only)

```csharp
using RevitHarness.Contracts;

public class MyCommand : IHarnessCommand
{
    public string Execute(UIApplication uiapp, IDictionary<string, object> args)
    {
        // Command logic
        return JsonConvert.SerializeObject(new { result = "done" });
    }
}
```

### HarnessCommandBase — CLI + Ribbon Dual Use

Use this when you want a single class that can be executed both from the CLI (`revit-harness run-command`) and as a Revit ribbon button (`IExternalCommand`).

```csharp
using RevitHarness.Contracts;

[Transaction(TransactionMode.Manual)]
public class MyDualCommand : HarnessCommandBase
{
    public override string Execute(UIApplication uiapp, IDictionary<string, object> args)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        // Works from both CLI and ribbon
        return JsonConvert.SerializeObject(new { result = "done" });
    }
}
```

## IPC Protocol

JSON messages over Named Pipe `\\.\pipe\RevitHarness2024` with length-prefix framing (int32 LE).

| Operation | Description |
|-----------|-------------|
| `ping` | Health check |
| `runTests` | Execute `ITestEntryPoint` |
| `runCommand` | Execute `IHarnessCommand` / `IExternalCommand` |
| `postCommand` | Post a registered Revit command |
| `lookupElement` | Element inspection |
| `shutdown` | Stop the IPC server |

## Development

### Build

```bash
# Entire solution
dotnet build revit-cli-harness.sln -c Debug

# Generate NuGet packages
dotnet pack src/RevitHarness.Contracts -c Release
dotnet pack src/RevitHarness.Cli -c Release
```

### Project Structure

```
revit-cli-harness/
├── src/
│   ├── RevitHarness.Contracts/     # Shared interfaces (NuGet: RevitHarness.Contracts)
│   │   ├── ITestEntryPoint.cs
│   │   ├── IHarnessCommand.cs
│   │   └── HarnessCommandBase.cs
│   ├── RevitHarness.Addin/         # Revit add-in
│   │   ├── App.cs                  # IExternalApplication + ribbon setup
│   │   ├── Commands/
│   │   │   ├── CommandExecutor.cs          # Shadow copy + reflection execution engine
│   │   │   ├── AssemblyScanner.cs          # Discovers command types in a DLL
│   │   │   └── OpenAddinManagerCommand.cs  # IExternalCommand for the ribbon button
│   │   ├── Ipc/
│   │   │   ├── IpcMessages.cs
│   │   │   ├── IpcServer.cs
│   │   │   ├── HarnessEventHandler.cs
│   │   │   └── ShadowCopier.cs
│   │   └── UI/
│   │       ├── AddinManagerWindow.xaml
│   │       └── AddinManagerWindow.xaml.cs
│   ├── RevitHarness.Lookup/        # Element inspection
│   └── RevitHarness.Cli/           # CLI tool (NuGet: RevitHarness.Cli)
├── lib/LookupEngine/               # LookupEngine library
├── Directory.Build.props            # Common build settings & version
└── Directory.Packages.props         # Centralized package versions
```

### Logs

```
%LOCALAPPDATA%\RevitHarness\logs\harness_{date}.log
```

### Shadow Copy Directory

```
%LOCALAPPDATA%\RevitHarness\shadow\{timestamp}\
```

Old directories are automatically cleaned up (max 10 generations, deleted after 1 day).

## License

MIT. See [LICENSE](LICENSE).

The element inspection feature (`lookup`) uses the `LookupEngine` and `LookupEngine.Abstractions` libraries from [RevitLookup](https://github.com/lookup-foundation/RevitLookup) (MIT, Copyright (c) Lookup Foundation and Contributors). They are redistributed unmodified as vendored binaries. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); the CLI package and the installed add-in folder include it.
