---
name: revit-harness-dialogs
description: |
  Handle Revit dialogs with the RevitHarness dialog watcher. Load this BEFORE launching,
  starting, or restarting Revit (Revit.exe, opening a model, running tests or commands that
  start Revit), and whenever a Revit dialog, warning, unsigned add-in prompt, or a stuck or
  timed-out revit-harness command is involved.
  Triggers: "launch revit", "start revit", "open revit", "revit dialog", "revit popup",
  "modal dialog", "revit is stuck", "unsigned add-in", "dialog-pending", "dialog-decide",
  "dialog watcher"
---

# RevitHarness Dialogs

A modal dialog blocks Revit and every `revit-harness` command with it. The dialog watcher
answers known dialogs from an allowlist and leaves unknown ones for you to decide. It does not
run on its own: start it before Revit and stop it when you are done.

## Before launching Revit

```bash
revit-harness dialog-watcher start
```

Start is idempotent: if a watcher is already running it reports its pid and starts nothing.
Do this BEFORE launching Revit, so the very first dialog (for example the unsigned add-in
prompt) is already covered. Revit is launched by the user or by you, never by the CLI, and the
watcher is never started at logon.

```bash
revit-harness dialog-watcher status   # pid, log directory, latest log; exit 1 if not running
revit-harness dialog-watcher stop
```

The watcher is installed by `revit-harness install`. If `start` says it is not installed, run
the install command first (see the `revit-harness-install` skill).

## Unknown dialogs

A dialog with no matching allowlist rule is left untouched and registered as pending. Check for
pending dialogs whenever a command seems stuck or times out.

```bash
revit-harness dialog-pending [--all]
revit-harness dialog-decide --id <pendingId> --button "<exact button name>" --by "<your name>" [--save-rule] [--timeout <ms>]
```

1. Read `title`, `bodySummary` and the screenshot at `screenshotPath`.
2. Pick one of the listed `buttons`, spelled exactly. A button that does not exist is rejected.
3. Run `dialog-decide` with `--by` set to the agent or model name (mandatory). The watcher
   presses the button; exit 0 = resolved, 1 = rejected, stale or failed, 2 = the watcher did
   not pick it up (start it and retry).

A dialog that disappears before the decision is marked `stale` and cannot be decided.

### Saving a rule

`--save-rule` also writes an allowlist rule so the same dialog is answered automatically next
time. Only reversible buttons are saved (OK, Cancel, Close); the button is still pressed for
any other choice, but no rule is written (add one by hand if you really want it). "Always Load"
and "Do Not Load" are never learned, and nothing is learned from a dialog covered by a
`noLearn` rule.

### Unsigned add-in dialog

"Security - Unsigned Add-in" is answered only by a rule with a `match.addIn` condition; a rule
with `addIn` never matches any other dialog. It is a plain string (not a pattern object),
compared case-insensitively. A value containing a path separator is compared with the dll path
only; any other value is compared with the add-in name and the dll file name.

- no `*`: exact match
- trailing `*` (for example `Contoso.*`): prefix match; any other `*` is literal, there is no regex
- a lone `*`: every unsigned add-in, including unknown ones, so use it deliberately

Add-ins without a matching rule become pending for you to decide. Example rule (adjust the
title and button labels to the Revit display language):

```json
{ "id": "known-addin", "enabled": true, "target": "watcher",
  "match": { "title": { "value": "Security - Unsigned Add-in", "mode": "equals", "ignoreCase": true },
             "addIn": "Contoso.*" },
  "action": { "buttonName": "Load Once" }, "noLearn": false, "createdBy": "manual" }
```

## Testing code that provokes dialogs

If you write a test that deliberately triggers a dialog, add a rule with `"noLearn": true` for
that dialog to `%LOCALAPPDATA%\RevitHarness\dialogs\allowlist.json` (then
`revit-harness reload-allowlist`). It is never auto-pressed or learned and always reaches the
caller as pending. Set it only on your own test targets, never on dialogs you did not cause.
The rule has the same shape as the example above, with `match.title` set to your test
dialog's title and `"noLearn": true`.

## When done

```bash
revit-harness dialog-watcher stop
```

Stopping leaves the dialogs folder (`allowlist.json`, logs, pending entries) in place.
