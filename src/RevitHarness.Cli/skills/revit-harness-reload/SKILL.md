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
2027 = net10.0-windows. Place the target's own helper DLLs beside it; Revit API references
must use Private=False. Load the target only through the harness, without its own `.addin`.
Rebuild, reload, then check target-status and exercise commands/events.

Reload calls old OnShutdown, disposes the old generation, shadow-copies, then calls new
OnStartup. Shutdown exceptions produce warnings and reload continues; startup exceptions
fail the request and leave the target Unloaded. Check `warnings` and (net8+) `previousReleased`.
Status includes generation, path/type, loadedAt, runtime, working set, generation counts,
old live ALC count (null on net48), and ribbonSlots. `status` checks installation instead.

Use `TargetRibbon.AddPushButton(panel, name, text, commandType)` in target OnStartup.
The same panel.Name/name reuses a button, updates text, and rebinds to the current generation.
There are at most eight distinct slots. The harness DLL owns the permanent command stubs.

The harness `.addin` is the thin loader. A successful reload saves path/type to
`%LOCALAPPDATA%\RevitHarness\target.json`; Revit startup restores it (failure is non-fatal).
Unload calls OnShutdown, disposes the generation, and deletes this file. Ribbon items remain.

Target OnShutdown must unregister updaters, unsubscribe events, dispose ExternalEvent and
timers, and stop background work. net8+ uses collectible ALCs; retained references can
prevent release. net48 old generations remain until Revit exits. Native DLLs are unsupported;
byte-loaded assemblies have empty Assembly.Location. See `samples/SampleTarget/README.md`
for an independent probe that verifies target and helper BuildTags.

Exit codes: 0 success, 1 operation failed, 2 connection/timeout error.
