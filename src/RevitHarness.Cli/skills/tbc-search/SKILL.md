---
name: tbc-search
description: |
  Search "The Building Coder" (TBC) blog for Revit API solutions and code examples.
  Use when you need to:
  (1) Find Revit API code examples or best practices
  (2) Research how to use specific Revit API classes or methods
  (3) Troubleshoot Revit API issues or migration problems
  (4) Understand Revit API behavior that is not well-documented
  Triggers: "search tbc", "building coder", "revit api example", "revit api how to", "revit api best practice"
---

# The Building Coder (TBC) Search

Search Jeremy Tammik's "The Building Coder" blog for Revit API programming guidance.
2,080+ articles from 2008 to present, written by a Principal Engineer on the Autodesk Revit team.

## Search Flow

### Step 1: Search the Index

Use WebFetch to find relevant articles from the TBC index:

```
WebFetch url="https://jeremytammik.github.io/tbc/a/index.html"
  prompt="Find articles related to [QUERY]. List the top 5 most relevant article filenames and titles from the post listing. Articles are listed with Nr, Date, Title (with link like 0199_family_api.htm), and Categories."
```

Tips:
- Search for Revit API class names (e.g., "FilteredElementCollector", "ExternalEvent")
- Search for operation names (e.g., "create wall", "modify parameter")
- Higher-numbered articles are newer and more relevant for recent Revit versions

### Step 2: Read Relevant Articles

For each promising article, fetch the full content:

```
WebFetch url="https://jeremytammik.github.io/tbc/a/{filename}"
  prompt="Extract the key Revit API techniques, code examples, and solutions relevant to [QUERY]. Focus on: (1) C# code snippets, (2) API class/method usage, (3) Important caveats."
```

Where `{filename}` is from the index (e.g., `0199_family_api.htm`).

### Step 3: Synthesize

After reading 1-3 articles, provide:
1. Direct answer with code examples
2. API classes/methods involved
3. Caveats and best practices
4. Source links to the original TBC articles

## Key Topic Areas

| Topic | What to search |
|-------|---------------|
| Element selection | FilteredElementCollector, element filtering |
| Family API | Creating/modifying families programmatically |
| External Events | Thread-safe API calls from external processes |
| Geometry | Element geometry access, intersection, analysis |
| Parameters | Reading/writing element parameters |
| Transactions | Transaction handling, sub-transactions |
| Dynamic Model Updater | Responding to model changes |
| DirectShape | Creating geometry without families |
| Materials | Material creation and assignment |
| Views | View creation, section views, schedules |
| MEP | Mechanical system API, connectors, routing |
| Design Automation | Forge/APS Design Automation for Revit |

## URL Format

- Index: `https://jeremytammik.github.io/tbc/a/index.html`
- Articles: `https://jeremytammik.github.io/tbc/a/{NNNN}_{slug}.htm`
