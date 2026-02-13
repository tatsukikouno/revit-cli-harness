---
name: revit-harness-install
description: |
  Install or uninstall RevitHarness add-in to Revit. Use when user wants to:
  (1) Install the harness add-in to a specific Revit version
  (2) Uninstall the harness add-in from Revit
  (3) Check installation status
  Triggers: "install harness", "setup revit harness", "uninstall harness", "harness status"
---

# RevitHarness Installation

## Install

```bash
revit-harness install --revit-version <year> [--force]
```

- `--revit-version`: Revit version year (e.g., 2024) - required
- `--force`: Force reinstall if already installed

Example:
```bash
revit-harness install --revit-version 2024
revit-harness install --revit-version 2024 --force
```

## Uninstall

```bash
revit-harness uninstall --revit-version <year>
```

Example:
```bash
revit-harness uninstall --revit-version 2024
```

## Status

```bash
revit-harness status [--revit-version <year>]
```

Shows installation status. If `--revit-version` is omitted, shows all detected versions.

## Installation Paths

Files are installed to:
```
%APPDATA%\Autodesk\Revit\Addins\<year>\
├── RevitHarness.addin
└── RevitHarness\
    ├── RevitHarness.Addin.dll
    ├── RevitHarness.Contracts.dll
    └── Newtonsoft.Json.dll
```

## After Installation

Restart Revit to load the add-in. The IPC server starts automatically when Revit launches.
