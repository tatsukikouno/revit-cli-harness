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
revit-harness install --revit-version <year> [--vendor-id <id>] [--skills-dir <path>]
```

- `--revit-version`: Revit version year (2024, 2025, 2026 or 2027) - required
- `--vendor-id`: the `<VendorId>` written into the installed `RevitHarness.addin`. Extensible Storage schemas gated by a vendor id must match the id of the running add-in, and code hot-loaded by the harness runs as the harness add-in. A reinstall without the flag keeps the installed id; `status` shows it. Revit reads the manifest at startup, so restart Revit after the id changes. Never commit a real vendor id
- `--skills-dir`: where to install the SKILLs (default `%USERPROFILE%\.claude\skills`)

Install and uninstall refuse to run while Revit is running: close Revit first. Re-running install replaces everything it installed. A running dialog watcher is stopped first. A skill folder with the same name that was not installed by `revit-harness` (it has no `.revit-harness` marker file) is never overwritten or removed; install fails and names it.

Example:
```bash
revit-harness install --revit-version 2024
revit-harness install --revit-version 2024 --skills-dir D:\my-skills
```

## Park a target's own `.addin`

```bash
revit-harness prepare-target --addin <path-to-the-target.addin> [--restore]
```

Renames `X.addin` to `X.addin.parked` so Revit (which reads only `*.addin`) does not load the target itself next to the harness. Nothing is deleted. Run it again after a build re-emits the manifest. It takes effect at the next Revit start. `--restore` puts the file back and never overwrites an existing manifest.

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
