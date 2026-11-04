# Third-Party Notices

RevitHarness (MIT, see `LICENSE`) redistributes the third-party components listed below.

## LookupEngine and LookupEngine.Abstractions

- **Used by:** `RevitHarness.Lookup` (the `lookupElement` / `revit-harness lookup` feature). The DLLs are vendored in `lib/LookupEngine/` and are included in the add-in files that `RevitHarness.Cli` embeds and installs.
- **Upstream project:** RevitLookup, https://github.com/lookup-foundation/RevitLookup. The libraries live in `source/LookupEngine` of that repository.
- **License:** MIT, Copyright (c) Lookup Foundation and Contributors
- **Vendored binaries (unmodified):**

| Target framework | Files | File version | Upstream commit (from `ProductVersion`) |
|------------------|-------|--------------|------------------------------------------|
| net48 | `LookupEngine.dll`, `LookupEngine.Abstractions.dll` | 2024.3.1.0 | `7a9e96555d0d4906855967430c622a97ca8cb8fc` |
| net8.0-windows | `LookupEngine.dll`, `LookupEngine.Abstractions.dll` | 2026.1.1.0 | `7a9e96555d0d4906855967430c622a97ca8cb8fc` |

- **Obtained from:** the binaries shipped with an installed RevitLookup add-in (`%ProgramData%\Autodesk\Revit\Addins\<year>\RevitLookup`), not rebuilt from source. File versions and commit hashes were read from the DLLs' version resources. The net48 set is used by Revit 2024 builds and the net8.0-windows set by Revit 2025/2026 builds.

### License text

```text
The MIT License (MIT)

Copyright (c) Lookup Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
