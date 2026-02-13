---
name: revit-harness-run-command
description: |
  Execute custom commands in Revit context via RevitHarness. Use when user wants to:
  (1) Run IHarnessCommand implementations in Revit
  (2) Execute arbitrary code with Revit API access
  (3) Automate Revit operations via external CLI/agent
  Triggers: "run revit command", "execute command in revit", "run harness command"
---

# RevitHarness Run Command

## Usage

```bash
revit-harness run-command --dll <path> --type <typename> [--args <json>] [--timeout <ms>]
```

## Parameters

- `--dll`: Path to command DLL (required)
- `--type`: Full type name implementing `IHarnessCommand` (required)
- `--args`: Arguments as JSON string (optional)
- `--timeout`: Timeout in milliseconds (default: 300000)

## Examples

Basic:
```bash
revit-harness run-command \
  --dll "C:\MyAddin\bin\Debug\MyAddin.dll" \
  --type "MyCompany.Commands.ExportWalls"
```

With arguments:
```bash
revit-harness run-command \
  --dll "C:\MyAddin\bin\Debug\MyAddin.dll" \
  --type "MyCompany.Commands.ExportWalls" \
  --args "{\"outputPath\":\"C:\\output.json\",\"includeHidden\":false}"
```

## Implementing IHarnessCommand

```csharp
using RevitHarness.Contracts;

public class ExportWalls : IHarnessCommand
{
    public string Execute(UIApplication uiapp, IDictionary<string, object> args)
    {
        var outputPath = args.TryGetValue("outputPath", out var p) ? p?.ToString() : "output.json";
        var doc = uiapp.ActiveUIDocument?.Document;

        // Your Revit API code here...

        return JsonConvert.SerializeObject(new {
            result = "Exported 42 walls",
            outputPath = outputPath
        });
    }
}
```

## DLL Hot-Swapping

The harness uses shadow copying, allowing DLL updates without restarting Revit:
1. Build your DLL
2. Run the command - harness copies DLL to shadow directory
3. Modify and rebuild
4. Run again - new version is loaded

## Exit Codes

- `0`: Success
- `1`: Command failed
- `2`: Connection error
