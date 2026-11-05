# Hot reload for real resident add-ins: design

Goal: a real resident `IExternalApplication` (helper DLLs, Revit API references, Extensible Storage,
files beside its DLL, a build that emits its own `.addin`) loads, reloads and unloads without restarting
Revit and without changing the add-in's source. Each section compares the alternatives and picks one.

## 1. Reference resolution and the net48 rename

**What failed.** `Generation.Load` decided per DLL in the target directory whether the target owns it or
shares an already-loaded copy. The test was "an assembly of that name is loaded and at least as new". Two
leaks followed.

- A Revit-family DLL that sits beside the target (a `CopyLocal` build copies `RevitAPIIFC`, `AdWindows`,
  `UIFramework`, `Newtonsoft.Json`, ...) but is not loaded yet at reload time became a private copy.
  On net48 it was renamed `X_gN`; on net8+ it loaded into the generation's context. Types crossing the
  boundary (`RevitAPI` returns an `RevitAPIIFC` type) then belonged to two different assemblies.
- A copy that is older than the target's own copy, or a first-time load, was owned for the same reason.

Alternatives:

| Option | Verdict |
|---|---|
| Keep the version heuristic and extend the host list by hand | Rejected. The list is never complete; every Revit year ships a different set. |
| Share everything already loaded in the process | Rejected. Old generations of the target are loaded in the process too (net8+ collectible contexts are enumerable). |
| Resolve by one rule: **a file with the same name in the Revit install directory is Revit's** | **Chosen.** It is what a normal `.addin` load does (Revit's copy wins), and it needs no list. |

The resolver (`ReferenceResolver.Plan`) classifies every managed DLL of the shadow directory, in this order:

1. The entry assembly is owned.
2. A name in the host set (`RevitHarness.Contracts`, `RevitAPI`, `RevitAPIUI`) is shared.
3. A name that also exists in the Revit install directory is Revit's: shared if the copy loaded from the Revit
   directory is running, otherwise loaded from the Revit directory on demand. The target's copy is not loaded.
   One exception, kept on purpose: when the target ships a higher assembly version than Revit's file, the target
   needs that version (Revit's older one would fail with `MissingMethodException`), so it owns it like any helper.
   A copy another add-in loaded privately from elsewhere is never taken for Revit's.
4. A name some other add-in already loaded, at least as new as the target's copy, is shared. Names the
   harness has owned before are excluded, so an old generation never hosts a new one.
5. Everything else is owned by the generation.

**Is the rename still needed?** On net8+ no: every generation is its own collectible `AssemblyLoadContext`
and static references bind inside it. On net48 yes. There is one binder, no unloadable context, and a
second `AppDomain` cannot pass `UIApplication` across. Loading a second copy of the same identity needs
distinct identities, so helpers are renamed per generation with Cecil (the entry assembly keeps its name
because WPF pack URIs and `Type.GetType("...,Asm")` use it). The rename touches only helpers the resolver
classified as owned, so a renamed copy of a Revit assembly can no longer appear.

## 2. File location

Byte loading leaves `Assembly.Location` empty, and real add-ins use it to find files beside the DLL.

| Option | Verdict |
|---|---|
| Contracts API (`TargetContext.OriginalDirectory`) given to `OnStartup` | Rejected as the primary story. The add-in must change every `Location` call. |
| `AssemblyResolve` / `CodeBase` mapping | Rejected. `CodeBase` of a byte-loaded assembly points at the loader; mapping hides the problem and breaks `Location` users anyway. |
| **Load by path from a complete shadow copy of the target directory** | **Chosen.** `Location`, `CodeBase`, and relative files work unmodified. |

Shadow directories are per Revit process (`shadow\<pid>`; a later start deletes the roots of exited processes).
On net48 every generation stays loaded until Revit exits, so its shadow copy stays too; net8+ deletes a copy once
its context is collected, keeping the newest three. The shadow directory is now a full recursive copy of the directory that holds the target DLL (every file,
not only `*.dll`, `*.pdb`, `*.config`). The generation loads by path from it:
`AssemblyLoadContext.LoadFromAssemblyPath` on net8+, `Assembly.LoadFile` on net48 (unlike `LoadFrom`, it
loads a second copy of an identity from another path). On net48 the Cecil rewrite is written back into the
shadow directory before `LoadFile`.

What the add-in must know, and this is the whole list:

- `Assembly.Location` is the **shadow** path. Files it reads beside its DLL are there (they were copied).
- Files it **writes** beside its DLL land in the shadow directory and are discarded on the next reload.
  Persistent state belongs under `%APPDATA%` / `%LOCALAPPDATA%`.
- Satellite resource assemblies in subdirectories are copied but not bound by the generation; neutral
  resources embedded in the DLL work.

No Contracts path API is added: it would be a second story for the same need.

## 3. The `.addin` a build emits

Revit loads every `.addin` it finds in the Addins folders at startup. A product build that drops its own
manifest there makes Revit load the target itself, and the harness would then load a second copy (duplicate
ribbon items, updaters, events). The harness `.addin` must be the only active one.

Chosen: build to an output directory outside the Addins folders, and where a build writes into an Addins
folder, park the manifest with one explicit command:

```
revit-harness prepare-target --addin <path-to-.addin>            # renames X.addin to X.addin.parked
revit-harness prepare-target --addin <path-to-.addin> --restore  # puts it back
```

Parking is a rename in place: Revit only reads `*.addin`. The command never deletes anything, never
touches project sources or the build output, and is idempotent (run it again after a build re-emits the
file; the fresh manifest replaces the parked one). `--restore` refuses to overwrite a manifest that exists.
Revit reads manifests at startup, so parking takes effect on the next Revit start. A helper earns its keep
here because the alternative is hand-renaming a file the build keeps recreating.

## 4. VendorId

Extensible Storage schemas created with `SetVendorId` must match the `<VendorId>` of the add-in that is
running. Code hot-loaded by the harness runs as the harness add-in, so the harness manifest's VendorId is
the identity.

Chosen: `revit-harness install --revit-version <year> --vendor-id <id>` writes the id into the installed
`RevitHarness.addin` (the manifest is edited as XML, so the id is escaped). A reinstall without the flag
keeps the VendorId already installed; the packaged default is used only on a first install. `status`
prints the installed VendorId. Nothing is committed per user, and the product `.addin` is never edited.

Revit reads the manifest at startup, so **a Revit restart is required when the VendorId changes**.
`install` already refuses to run while Revit is running, so the restart is part of the flow.

## 5. Exit codes

Exit 2 used to mean "connection or timeout or anything else" and the JSON said `HARNESS_NOT_READY` for
all of it. Chosen: one table, distinct exit codes, and a stable first stderr line
`revit-harness: <TOKEN>: <message>` (the JSON on stdout carries the same token in `error.code`).

| Exit | Token | Meaning |
|---|---|---|
| 0 | | success |
| 1 | `FAILED` or the operation's own error code | the operation ran and failed |
| 2 | `CONNECTION_FAILED` | no pipe, or the pipe broke: Revit is not running or the harness add-in is not loaded |
| 3 | `HARNESS_NOT_READY` | connected, but the add-in is still starting (it answers every request except `dialog-log` / `reload-allowlist` with this until `OnStartup` and the saved-target restore finish) |
| 4 | `TIMEOUT` | Revit did not answer in time and no dialog is pending |
| 5 | `DIALOG_BLOCKED` | Revit did not answer in time and at least one dialog is pending; stderr lists the ids |

## What this design deletes

- The version heuristic as the only sharing test (replaced by the Revit-directory rule).
- Byte loading on both runtimes (replaced by path loading from a full shadow copy).
- The blanket exit 2 and the `HARNESS_NOT_READY` label on timeouts.
- `docs/superpowers` (the finished multi-version plan) and the stale `PostCommand` note in the sample README.
