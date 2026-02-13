---
name: revit-harness-ping
description: |
  Check connection to running Revit instance via RevitHarness IPC. Use when user wants to:
  (1) Verify Revit is running and harness is ready
  (2) Get Revit version and process info
  (3) Debug connection issues
  Triggers: "ping revit", "check revit connection", "is revit running", "harness status"
---

# RevitHarness Ping

## Usage

```bash
revit-harness ping
```

## Response

Success response includes:
- `revitVersion`: Revit version number
- `processId`: Revit process ID
- `activeDocumentTitle`: Currently open document (if any)

Example output:
```json
{
  "id": "...",
  "ok": true,
  "result": {
    "revitVersion": "2024",
    "processId": 12345,
    "activeDocumentTitle": "Project1.rvt"
  }
}
```

## Exit Codes

- `0`: Success - Revit is running and harness is ready
- `2`: Connection error - Revit not running or harness not loaded

## Troubleshooting

If ping fails:
1. Ensure Revit is running
2. Verify harness is installed: `revit-harness status`
3. Restart Revit if harness was just installed
