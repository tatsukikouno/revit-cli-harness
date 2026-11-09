---
name: revit-harness-run-tests
description: |
  Execute tests in Revit context via RevitHarness. Use when user wants to:
  (1) Run Revit API tests inside running Revit instance
  (2) Execute ITestEntryPoint implementations
  (3) Run automated tests that require Revit context
  Triggers: "run revit tests", "execute tests in revit", "test revit plugin"
---

# RevitHarness Run Tests

## Usage

```bash
revit-harness.exe run-tests --dll <path> --type <typename> [--filter <filter>] [--timeout <ms>]
```

> From WSL call `revit-harness.exe` (the `.exe` form also works on Windows). `--dll` takes a Windows path (`C:\...`); `/mnt/c/...` is not accepted.

## Parameters

- `--dll`: Path to test DLL (required)
- `--type`: Full type name implementing `ITestEntryPoint` (required)
- `--filter`: Test filter string passed to entry point (optional)
- `--timeout`: Timeout in milliseconds (default: 300000)

## Example

```bash
revit-harness.exe run-tests \
  --dll "C:\Projects\MyTests\bin\Debug\MyTests.dll" \
  --type "MyCompany.Tests.RevitTestEntryPoint" \
  --filter "Smoke"
```

## Implementing ITestEntryPoint

```csharp
using RevitHarness.Contracts;

public class MyTestEntryPoint : ITestEntryPoint
{
    public string Run(UIApplication uiapp, IDictionary<string, object> args)
    {
        var filter = args.TryGetValue("filter", out var f) ? f?.ToString() : null;

        // Run tests...

        return JsonConvert.SerializeObject(new {
            passed = true,
            summary = "All tests passed"
        });
    }
}
```

## Response

The result must include a `passed` boolean field:
```json
{
  "ok": true,
  "result": {
    "passed": true,
    "summary": { ... }
  }
}
```

## Exit Codes

- `0`: Success and all tests passed
- `1`: Tests failed or execution error
- `2`: Connection error
