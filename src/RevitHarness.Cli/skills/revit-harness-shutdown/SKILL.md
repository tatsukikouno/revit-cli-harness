---
name: revit-harness-shutdown
description: |
  Stop the RevitHarness IPC server. Use when user wants to:
  (1) Stop the harness without closing Revit
  (2) Disable external CLI access to Revit
  (3) Clean up IPC resources
  Triggers: "shutdown harness", "stop harness server", "disable harness"
---

# RevitHarness Shutdown

## Usage

```bash
revit-harness shutdown
```

## Behavior

- Stops the IPC named pipe server
- Revit continues running normally
- Harness will restart when Revit is restarted

## Response

```json
{
  "ok": true,
  "result": {
    "shutting_down": true
  }
}
```

## Exit Codes

- `0`: Shutdown initiated
- `2`: Connection error (harness may already be stopped)

## Note

To re-enable the harness without restarting Revit, you would need to reload the add-in (not currently supported - requires Revit restart).
