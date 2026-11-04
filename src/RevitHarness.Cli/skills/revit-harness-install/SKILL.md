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
revit-harness install --revit-version <year> [--skills-dir <path>]
```

- `--revit-version`: Revit version year (e.g., 2024) - required
- `--skills-dir`: where to install the SKILLs (default `%USERPROFILE%\.claude\skills`)

Re-running install replaces everything it installed. A running dialog watcher is stopped first. A skill folder with the same name that was not installed by `revit-harness` (it has no `.revit-harness` marker file) is never overwritten or removed; install fails and names it.

Example:
```bash
revit-harness install --revit-version 2024
revit-harness install --revit-version 2024 --skills-dir D:\my-skills
```

## Uninstall

```bash
revit-harness uninstall --revit-version <year> [--skills-dir <path>]
```

Removes the add-in for that version. The watcher and SKILLs are shared, so they are removed with the last installed version (pass the same `--skills-dir` as at install). The dialogs data folder (allowlist, logs, pending) is kept.

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

```
%APPDATA%\Autodesk\Revit\Addins\<year>\
├── RevitHarness.addin
└── RevitHarness\
    ├── RevitHarness.Addin.dll
    ├── RevitHarness.Contracts.dll
    └── Newtonsoft.Json.dll
%LOCALAPPDATA%\RevitHarness\
├── watcher\                 dialog watcher (see revit-harness-dialogs)
└── dialogs\                 allowlist, logs, pending (never removed by uninstall)
%USERPROFILE%\.claude\skills\<skill>\SKILL.md    or <--skills-dir>\<skill>\SKILL.md
```

## After Installation

Restart Revit to load the add-in. The IPC server starts automatically when Revit launches. Before launching Revit, start the dialog watcher (`revit-harness dialog-watcher start`, see the `revit-harness-dialogs` skill); it is never started automatically.
