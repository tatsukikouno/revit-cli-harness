# Sample target hot reload

Four standalone projects, outside the solution: `SampleTarget.Core` (netstandard2.0 helper), `SampleTarget.Friend` (per-year helper that reads an internal Core member through InternalsVisibleTo, so a net48 reload fails if the per-generation rename does not rewrite the attribute, and that references `RevitAPI` and `AdWindows` like a real helper),
`SampleTarget` (IExternalApplication, updater, event, ribbon), and `SampleTarget.Probe`
(IHarnessCommand with no reference to the target or Core). Do not install a SampleTarget `.addin`;
load it through the harness. Revit supplies the API assemblies and Newtonsoft.Json at runtime.

Build from the repository root in WSL, using a Windows temporary output directory:

```bash
dotnet.exe build samples/SampleTarget/SampleTarget -f net48 -p:BuildTag=v2 -p:NoWarn=NU1507 -o 'C:\Temp\sample-target\net48'
dotnet.exe build samples/SampleTarget/SampleTarget -f net8.0-windows -p:BuildTag=v2 -p:NoWarn=NU1507 -o 'C:\Temp\sample-target\net8'
dotnet.exe build samples/SampleTarget/SampleTarget -f net10.0-windows -p:BuildTag=v2 -p:NoWarn=NU1507 -o 'C:\Temp\sample-target\net10'
dotnet.exe build samples/SampleTarget/SampleTarget.Probe -f net48 -p:NoWarn=NU1507 -o 'C:\Temp\sample-probe\net48'
dotnet.exe build samples/SampleTarget/SampleTarget.Probe -f net8.0-windows -p:NoWarn=NU1507 -o 'C:\Temp\sample-probe\net8'
dotnet.exe build samples/SampleTarget/SampleTarget.Probe -f net10.0-windows -p:NoWarn=NU1507 -o 'C:\Temp\sample-probe\net10'
```

Core builds as a project reference for each target build; it can also be built directly with
`dotnet.exe build samples/SampleTarget/SampleTarget.Core -p:BuildTag=v2 -p:NoWarn=NU1507 -o <out-dir>`.
Use net48 for Revit 2024, net8.0-windows for 2025/2026, net10.0-windows for 2027.
Both target and Core embed BuildTag (default `dev`). Existing `.gitignore` rules exclude bin/obj.
Keep the probe output separate so it cannot introduce a second copy of the target or Core.

```bash
revit-harness reload --dll 'C:\path\SampleTarget.dll' --type SampleTarget.App
revit-harness target-status
revit-harness run-command --dll 'C:\probe\SampleTarget.Probe.dll' --type SampleTarget.Probe.ProbeCommand --args '{"op":"state"}'
revit-harness run-command --dll 'C:\probe\SampleTarget.Probe.dll' --type SampleTarget.Probe.ProbeCommand --args '{"op":"edit"}'
revit-harness run-command --dll 'C:\probe\SampleTarget.Probe.dll' --type SampleTarget.Probe.ProbeCommand --args '{"op":"ribbon"}'
revit-harness unload
```

Start with BuildTag=v1, exercise edit and the ribbon button, rebuild target and Core with v2 to the same output,
and reload again. `state` returns parsed records from
`%LOCALAPPDATA%\RevitHarness\sample-target\log.jsonl` and `updaterRegistered`.
Expect v1 shutdown before v2 startup; later updater/event/ribbon records must carry
`tag=v2, coreTag=v2`. Click the Hello button on the SampleTarget ribbon tab with a mouse (the Revit home screen has no ribbon, so open a project first) and read `state` afterwards for a `ribbon` record; the probe has no click op.
`ribbon` uses only public Revit API and should show one Hello button with the new text.
`edit` adds a Level to the active document; with no active document it creates and closes
an unsaved metric project. `clear-log` deletes the log. After unload, the updater must be
unregistered; editing must produce no target events/updater records, and the ribbon stub
must no longer invoke the target. Ribbon items remain visible because Revit cannot remove them.

Each `startup` is preceded by an `env` record that shows what an unmodified add-in sees:
`location` (the target's `Assembly.Location`, a shadow path), `settings` and `table` (files read beside the DLL,
`sample.settings.txt` and `data\table.csv`), `revitApi` and `adWindows` (where the helper's Revit types came
from; both must be the Revit install directory), and `vendorSchema`. The build also copies reference-assembly
stand-ins of `RevitAPI`, `RevitAPIUI` and `AdWindows` beside the target, like a CopyLocal build: they are
unusable, so a call into one fails if the harness ever loads the target's copy instead of Revit's.

`vendorSchema` is `skipped` unless a machine-local `vendor.txt` (one line, a vendor id; git-ignored) sits in the
build output. Then the target creates an Extensible Storage schema with that vendor id: `ok:<id>` when it matches
the installed harness manifest (`revit-harness install --vendor-id <id>`, then restart Revit), an `error:` otherwise.

Fixed updater IDs: AddInId `4dabf61b-74b3-4a77-bab5-98f56ac2cc30`,
updater GUID `57b074d0-f416-48e8-8c77-4aefb091ee01`.
