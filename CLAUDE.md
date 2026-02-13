# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Revit 2024 harness add-in project that enables external CLI/agent execution of tests and arbitrary code within a running Revit instance. The system allows DLL hot-swapping via shadow copying while Revit is running.

### Key Architecture

- **IPC Communication**: Named Pipe (`\\.\pipe\RevitHarness2024`) for CLI ⇄ Revit communication
- **Thread Safety**: All Revit API calls must go through `ExternalEvent` to run on Revit's main thread
- **Shadow Copy**: DLLs are copied to `%LOCALAPPDATA%\RevitHarness\shadow\{timestamp}\` before loading to avoid file locks

### NuGet Packages

| Package | Purpose | Distribution |
|---------|---------|--------------|
| `RevitHarness.Contracts` | Developer references ITestEntryPoint etc. | NuGet.org |
| `RevitHarness.Cli` | CLI + embedded addin | dotnet tool |

## Build Commands

```bash
# Build the solution (x64 only)
dotnet build revit-cli-harness.sln -c Debug
dotnet build revit-cli-harness.sln -c Release

# Build single project
dotnet build src/RevitHarness.Contracts
dotnet build src/RevitHarness.Addin
dotnet build src/RevitHarness.Cli

# Generate NuGet packages
dotnet pack src/RevitHarness.Contracts
dotnet pack src/RevitHarness.Cli
```

## Development Notes

- **Target Framework**: .NET Framework 4.8 (x64 only)
- **Revit References**: RevitAPI.dll and RevitAPIUI.dll from `C:\Program Files\Autodesk\Revit 2024\`
- **Namespaces**:
  - `RevitHarness.Contracts` - Shared interfaces
  - `RevitHarness.Addin` - Revit add-in
  - `RevitHarness.Cli` - CLI tool
- **Debug**: Set to launch `C:\Program Files\Autodesk\Revit 2024\Revit.exe`

## Project Structure

```
revit-cli-harness/
├── src/
│   ├── RevitHarness.Contracts/     # Shared interfaces (NuGet package)
│   │   ├── ITestEntryPoint.cs
│   │   ├── IHarnessCommand.cs
│   │   └── RevitHarness.Contracts.csproj
│   │
│   ├── RevitHarness.Addin/         # Revit add-in
│   │   ├── App.cs                  # IExternalApplication implementation
│   │   ├── Ipc/
│   │   │   ├── IpcMessages.cs      # Request/Response message classes
│   │   │   ├── IpcServer.cs        # Named Pipe server
│   │   │   ├── HarnessEventHandler.cs  # ExternalEvent handler
│   │   │   └── ShadowCopier.cs     # DLL shadow copy mechanism
│   │   ├── RevitHarness.addin      # Add-in manifest
│   │   └── RevitHarness.Addin.csproj
│   │
│   └── RevitHarness.Cli/           # CLI tool (dotnet tool)
│       ├── Program.cs              # Main entry point
│       ├── IpcClient.cs            # Named Pipe client
│       ├── IpcMessages.cs          # Message classes for CLI
│       ├── Installation/
│       │   └── RevitInstaller.cs   # Addin deployment
│       └── RevitHarness.Cli.csproj
│
├── Directory.Build.props           # Common build settings
├── Directory.Packages.props        # Centralized package versions
└── revit-cli-harness.sln
```

## CLI Usage

```bash
# Install CLI globally
dotnet tool install -g RevitHarness.Cli

# Deploy addin to Revit
revit-harness install --revit-version 2024

# Check installation status
revit-harness status

# Check connection to Revit
revit-harness ping

# Run tests
revit-harness run-tests --dll "path/to/tests.dll" --type "Company.Tests.EntryPoint" --filter "Smoke"

# Run external command
revit-harness run-command --dll "path/to/command.dll" --type "Company.MyCommand"

# Post a registered Revit command
revit-harness post-command --id "CustomCtrl_%CustomCtrl_%Add-Ins%Addin%Command"

# Stop IPC server (Revit continues running)
revit-harness shutdown

# Uninstall addin
revit-harness uninstall --revit-version 2024
```

## Installation Paths

```
%APPDATA%\Autodesk\Revit\Addins\2024\
├── RevitHarness.addin              # Manifest
└── RevitHarness/
    ├── RevitHarness.Addin.dll
    ├── RevitHarness.Contracts.dll
    └── Newtonsoft.Json.dll
```

## IPC Protocol

JSON messages with length-prefix framing (int32 LE). Operations:
- `ping`: Health check, returns Revit version/process info
- `runTests`: Shadow-copy DLL, load via reflection, execute `ITestEntryPoint`
- `runCommand`: Execute `IHarnessCommand` or `IExternalCommand` via reflection
- `postCommand`: Post a registered Revit command by ID
- `shutdown`: Stop IPC server

Exit codes: 0=success, 1=failure, 2=connection/timeout error

## Implementing Test Entry Points

To create a test that can be executed via the harness, install `RevitHarness.Contracts` and implement `ITestEntryPoint`:

```csharp
using RevitHarness.Contracts;

public class MyTestEntryPoint : ITestEntryPoint
{
    public string Run(UIApplication uiapp, IDictionary<string, object> args)
    {
        // Your test logic here
        return JsonConvert.SerializeObject(new {
            passed = true,
            summary = "All tests passed"
        });
    }
}
```

## Implementing Commands

For commands, implement `IHarnessCommand`:

```csharp
using RevitHarness.Contracts;

public class MyCommand : IHarnessCommand
{
    public string Execute(UIApplication uiapp)
    {
        // Your command logic here
        return JsonConvert.SerializeObject(new {
            result = "Command completed"
        });
    }
}
```

## Logs

Logs are written to: `%LOCALAPPDATA%\RevitHarness\logs\harness_{date}.log`
