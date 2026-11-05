---
name: revit-harness-reload
description: |
  Hot reload an IExternalApplication target add-in without restarting Revit.
  Use for rebuilding a resident add-in with ribbon buttons, updaters, or events,
  checking its current generation, or unloading it.
  Triggers: "reload revit addin", "hot reload target", "unload target"
---

# RevitHarness Target Reload

```bash
revit-harness reload --dll <absolute-path> --type <IExternalApplication-type> [--timeout <ms>]
revit-harness target-status
revit-harness unload [--timeout <ms>]
```

Build for the running Revit: 2024 = net48, 2025/2026 = net8.0-windows,
2027 = net10.0-windows, into an output directory that is not a Revit Addins folder.
Load the target only through the harness. Rebuild, reload, then check target-status and exercise
commands/events.

Each reload copies the whole directory that holds the DLL (all files, subdirectories included) and loads
the target by path from that copy. `Assembly.Location` is the shadow path and files beside the DLL are
there, so unmodified `Assembly.Location` code works. Files the add-in writes beside its DLL are discarded
on the next reload: persistent state belongs under `%APPDATA%` / `%LOCALAPPDATA%`. A DLL beside the target
whose name also exists in the Revit install directory (RevitAPI, AdWindows, Newtonsoft.Json, ...) is
Revit's and is never loaded from the target.

If a build emits the target's own `.addin` into an Addins folder, run
`revit-harness prepare-target --addin <path>` (parks it as `.addin.parked`, nothing is deleted; takes effect at
the next Revit start). If the target uses Extensible Storage with a vendor id, install the harness with
`--vendor-id <id>` and restart Revit once (see the `revit-harness-install` SKILL).

Reload calls old OnShutdown, disposes the old generation, shadow-copies, then calls new
OnStartup. Shutdown exceptions produce warnings and reload continues; startup exceptions
fail the request and leave the target Unloaded. Check `warnings` and (net8+) `previousReleased`.
Status includes generation, path/type, loadedAt, runtime, working set, generation counts,
old live ALC count (null on net48), and ribbonSlots. `status` checks installation instead.

Use `TargetRibbon.AddPushButton(panel, name, text, commandType)` in target OnStartup.
A button with the same name on the same panel is reused: updates text, and rebinds to the current generation.
There are at most eight distinct slots. The harness DLL owns the permanent command stubs.

The harness `.addin` is the thin loader. A successful reload saves path/type to
`%LOCALAPPDATA%\RevitHarness\target.json`; Revit startup restores it (failure is non-fatal).
Unload calls OnShutdown, disposes the generation, and deletes this file. Ribbon items remain.

Target OnShutdown must unregister updaters, unsubscribe events, dispose ExternalEvent and
timers, and stop background work. net8+ uses collectible ALCs; retained references can
prevent release. net48 old generations remain until Revit exits. Native DLLs are unsupported.
See `samples/SampleTarget/README.md` for an independent probe that verifies target and helper BuildTags.

Exit codes: 0 success, 1 operation failed, 2 CONNECTION_FAILED (Revit not running / harness not loaded),
3 HARNESS_NOT_READY (add-in still starting: retry), 4 TIMEOUT, 5 DIALOG_BLOCKED (a dialog is pending:
`dialog-pending`, `dialog-decide`). For 2 to 5 the first stderr line is `revit-harness: <TOKEN>: <message>`.
