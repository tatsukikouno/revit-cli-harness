# LookupEngine Revit 2025/2026対応 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Note for this repo's maintainer workflow:** per this user's standing instructions, default execution is direct implementation in the main session with TDD-style build verification, task-by-task, without spawning a fresh subagent per task. Use `subagent-driven-development`/`executing-plans` only if explicitly requested.

**Goal:** `lookupElement` を Revit 2024/2025/2026 のすべてで動作させる(現状はR25/R26で `NOT_SUPPORTED` を返すガード状態)。

**Architecture:** `RevitHarness.Lookup` を `RevitHarness.Addin` と対称な Configuration サフィックス方式(`R24`/`R25`/`R26`)でマルチターゲット化する。R24は既存のnet48 LookupEngineバイナリ、R25/R26は新たにvendorするnet8.0-windows LookupEngineバイナリ(実インストール済みRevitLookup 2026アドインから抽出、Revit APIに依存しないため2025/2026で共用)を参照する。`RevitHarness.Addin` 側の `LOOKUP_SUPPORTED` コンパイルシンボルによるゲートを完全に削除し、全バージョンで同じ実装を使う。

**Tech Stack:** .NET SDK (net48 / net8.0-windows), MSBuild Central Package Management, LookupEngine (vendored binary, MIT license)

## Global Constraints

- 対応バージョンは2024/2025/2026の3つ(既存のAddin/Contracts/CLIと同じ)。
- LookupEngineはNuGet.orgに未公開・GitHub Releaseにもバイナリ成果物がないため、実インストール済みRevitLookupアドインからDLLを抜き出してvendorする方式を維持する(既存net48版と同じ手法)。
- R25/R26は同一のnet8.0-windows LookupEngineバイナリを共用する(本機にインストール済みのRevit 2026用RevitLookupアドイン `C:\ProgramData\Autodesk\Revit\Addins\2026\RevitLookup\` から抽出。アセンブリバージョン `2026.1.1.0`)。LookupEngineはRevit APIに依存しない純粋な.NETリフレクションエンジンのため、年度非依存で共用できる。
- 検証はビルド成功確認を基本とする。実機Revit起動によるライブ検証は、本機にRevit 2024/2026とそれぞれ対応するRevitHarnessが実インストール済みであるため一部可能だが、Revitの起動はユーザーの作業を中断しうる操作なので**必ずユーザーに確認してから**実行する(Task 6のStep 2)。Revit 2025は本機に未インストールのため実機検証はスコープ外。
- 参照する既存ファイルの内容(このプラン作成時点の実体):
  - `src/RevitHarness.Addin/RevitHarness.Addin.csproj` は `RevitYearSuffix` プロパティ(`R24`/`R25`/`R26`)を計算し、`TargetFramework`・`StartProgram`等をそれで分岐するパターンを既に使っている。`RevitHarness.Lookup` も同じ `RevitYearSuffix` パターンを踏襲する。
  - `Directory.Packages.props` は `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` のバージョンを `$(Configuration.EndsWith('R24'/'R25'/'R26'))` 条件で既に分岐済み(15-25行目)。`RevitHarness.Lookup` が同じConfiguration命名を使えば、変更なしでこの中央バージョン管理の恩恵を受けられる。

---

### Task 1: net8.0-windows LookupEngineバイナリのvendoring、`lib/LookupEngine` のTFM別再編

**Files:**
- Move: `lib/LookupEngine/LookupEngine.dll` → `lib/LookupEngine/net48/LookupEngine.dll`
- Move: `lib/LookupEngine/LookupEngine.Abstractions.dll` → `lib/LookupEngine/net48/LookupEngine.Abstractions.dll`
- Create: `lib/LookupEngine/net8.0-windows/LookupEngine.dll`
- Create: `lib/LookupEngine/net8.0-windows/LookupEngine.Abstractions.dll`

**Interfaces:**
- Consumes: なし(独立したタスク)
- Produces: `lib/LookupEngine/net48/*.dll` と `lib/LookupEngine/net8.0-windows/*.dll` の2系統のパス。Task 2の `RevitHarness.Lookup.csproj` がこれらを `HintPath` で参照する。

- [ ] **Step 1: 現状のフラットな配置を確認**

Run: `ls -la lib/LookupEngine/`
Expected: `LICENSE`・`LookupEngine.dll`・`LookupEngine.Abstractions.dll` がサブフォルダなしで直下にある。

- [ ] **Step 2: TFM別サブフォルダを作成し、既存net48バイナリを移動**

```bash
mkdir -p lib/LookupEngine/net48 lib/LookupEngine/net8.0-windows
git mv lib/LookupEngine/LookupEngine.dll lib/LookupEngine/net48/LookupEngine.dll
git mv lib/LookupEngine/LookupEngine.Abstractions.dll lib/LookupEngine/net48/LookupEngine.Abstractions.dll
```

- [ ] **Step 3: net8.0-windows版バイナリを、実インストール済みのRevit 2026用RevitLookupアドインから抽出**

```bash
cp "/c/ProgramData/Autodesk/Revit/Addins/2026/RevitLookup/LookupEngine.dll" lib/LookupEngine/net8.0-windows/LookupEngine.dll
cp "/c/ProgramData/Autodesk/Revit/Addins/2026/RevitLookup/LookupEngine.Abstractions.dll" lib/LookupEngine/net8.0-windows/LookupEngine.Abstractions.dll
git add lib/LookupEngine/net8.0-windows/LookupEngine.dll lib/LookupEngine/net8.0-windows/LookupEngine.Abstractions.dll
```

Note: この抽出元パスはこのプラン作成時点(2026-07-15)の本機の実インストール状態に基づく。もし将来別のマシンでこのタスクを実行する場合や、RevitLookupのバージョンが更新されている場合は、`%PROGRAMDATA%\Autodesk\Revit\Addins\{対象年度}\RevitLookup\` 配下の同名2ファイルを使う(年度非依存のバイナリなので、2025/2026どちらのRevitLookupインストールから採取しても構わない)。

- [ ] **Step 4: ファイルサイズで抽出の整合性を確認**

Run: `ls -la lib/LookupEngine/net48/ lib/LookupEngine/net8.0-windows/`
Expected:
- `lib/LookupEngine/net48/LookupEngine.dll` = 292664 bytes、`LookupEngine.Abstractions.dll` = 281400 bytes(移動のみなのでサイズ不変)
- `lib/LookupEngine/net8.0-windows/LookupEngine.dll` = 127288 bytes、`LookupEngine.Abstractions.dll` = 115512 bytes(コピー元と一致)

サイズが一致しない場合はコピーが壊れているか、コピー元インストールの内容が変わっている。再度Step 3から実行し、サイズを再確認してから先に進む。

- [ ] **Step 5: コミット**

```bash
git add lib/LookupEngine
git commit -m "Vendor net8.0-windows LookupEngine binaries, split lib/LookupEngine by TFM"
```

---

### Task 2: `RevitHarness.Lookup.csproj` をAddinと対称なConfiguration方式でマルチターゲット化

**Files:**
- Modify: `src/RevitHarness.Lookup/RevitHarness.Lookup.csproj`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: Task 1の `lib/LookupEngine/net48/*.dll`・`lib/LookupEngine/net8.0-windows/*.dll`
- Produces: `Configuration` が `Debug R24`/`Release R24`/`Debug R25`/`Release R25`/`Debug R26`/`Release R26` のいずれかのとき対応する `TargetFramework`(`net48` or `net8.0-windows`)でビルドされる `RevitHarness.Lookup.dll`。Task 3で `RevitHarness.Addin` から無条件に `ProjectReference` される。

- [ ] **Step 1: `Release R25` が現状ビルドできないことを確認**

Run: `dotnet build src/RevitHarness.Lookup -c "Release R25"`
Expected: FAIL(`Release R25` が `RevitHarness.Lookup.csproj` の `Configurations` に存在しないため)

- [ ] **Step 2: `RevitHarness.Lookup.csproj` を以下の内容に置き換える**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <Configurations>Debug R24;Release R24;Debug R25;Release R25;Debug R26;Release R26</Configurations>

    <!-- Mirrors RevitHarness.Addin.csproj's RevitYearSuffix pattern -->
    <RevitYearSuffix Condition="$(Configuration.EndsWith('R24'))">R24</RevitYearSuffix>
    <RevitYearSuffix Condition="$(Configuration.EndsWith('R25'))">R25</RevitYearSuffix>
    <RevitYearSuffix Condition="$(Configuration.EndsWith('R26'))">R26</RevitYearSuffix>
    <RevitYearSuffix Condition="'$(RevitYearSuffix)'==''">R24</RevitYearSuffix>

    <TargetFramework Condition="'$(RevitYearSuffix)'=='R24'">net48</TargetFramework>
    <TargetFramework Condition="'$(RevitYearSuffix)'=='R25' Or '$(RevitYearSuffix)'=='R26'">net8.0-windows</TargetFramework>

    <RootNamespace>RevitHarness.Lookup</RootNamespace>
    <AssemblyName>RevitHarness.Lookup</AssemblyName>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <!-- LookupEngine binaries are vendored per-TFM under lib/LookupEngine (see CLAUDE.md for provenance/refresh) -->
  <ItemGroup Condition="'$(RevitYearSuffix)'=='R24'">
    <Reference Include="LookupEngine">
      <HintPath>..\..\lib\LookupEngine\net48\LookupEngine.dll</HintPath>
    </Reference>
    <Reference Include="LookupEngine.Abstractions">
      <HintPath>..\..\lib\LookupEngine\net48\LookupEngine.Abstractions.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup Condition="'$(RevitYearSuffix)'=='R25' Or '$(RevitYearSuffix)'=='R26'">
    <Reference Include="LookupEngine">
      <HintPath>..\..\lib\LookupEngine\net8.0-windows\LookupEngine.dll</HintPath>
    </Reference>
    <Reference Include="LookupEngine.Abstractions">
      <HintPath>..\..\lib\LookupEngine\net8.0-windows\LookupEngine.Abstractions.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" PrivateAssets="all" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Note: `VersionOverride` の明示ピンは削除した。`Directory.Packages.props` の既存Configuration条件(次のStepで参照を更新)がこのプロジェクトにもそのまま適用される。

- [ ] **Step 3: `Directory.Packages.props` のコメントを更新(挙動は変更しない)**

`Directory.Packages.props` の17-19行目のコメントを以下に置き換える(バージョン分岐のロジック自体は変更なし):

```xml
    <!-- Revit API reference assemblies (compile-time only; Revit supplies the real DLLs at runtime) -->
    <!-- Used by RevitHarness.Addin / RevitHarness.Lookup, selected by Configuration suffix (R24/R25/R26); R24 is also the
         fallback for a plain "Debug"/"Release" Configuration with no R2x suffix -->
```

- [ ] **Step 4: R24が引き続きビルドできることを確認(回帰確認)**

Run: `dotnet build src/RevitHarness.Lookup -c "Release R24"`
Expected: PASS

- [ ] **Step 5: R25/R26が新しくビルドできることを確認**

Run:
```bash
dotnet build src/RevitHarness.Lookup -c "Release R25"
dotnet build src/RevitHarness.Lookup -c "Release R26"
```
Expected: 両方PASS。`bin\x64\Release R25\net8.0-windows\RevitHarness.Lookup.dll` および `bin\x64\Release R26\net8.0-windows\RevitHarness.Lookup.dll` が生成される。

- [ ] **Step 6: コミット**

```bash
git add src/RevitHarness.Lookup/RevitHarness.Lookup.csproj Directory.Packages.props
git commit -m "Multi-target RevitHarness.Lookup for Revit 2024/2025/2026"
```

---

### Task 3: `LOOKUP_SUPPORTED` ゲートを削除し、`lookupElement` を全バージョンで有効化

**Files:**
- Modify: `src/RevitHarness.Addin/RevitHarness.Addin.csproj`
- Modify: `src/RevitHarness.Addin/Ipc/HarnessEventHandler.cs`
- Modify: `src/RevitHarness.Addin/Ipc/IpcMessages.cs`

**Interfaces:**
- Consumes: Task 2の `RevitHarness.Lookup`(R24/R25/R26すべてでビルド可能)
- Produces: `RevitHarness.Addin` のR24/R25/R26すべてで `ElementInspector` を使った `lookupElement` が動作する。`LOOKUP_SUPPORTED`・`ErrorCodes.NotSupported` は完全に削除される。

- [ ] **Step 1: 現状のゲート箇所を確認(ベースライン)**

Run: `grep -rn "LOOKUP_SUPPORTED\|NotSupported" src/RevitHarness.Addin/`
Expected: `RevitHarness.Addin.csproj`(`DefineConstants`とProjectReferenceのCondition)、`HarnessEventHandler.cs`(`#if`/`#else`/`#endif` 3箇所と `ErrorCodes.NotSupported` 使用箇所)、`IpcMessages.cs`(`NotSupported` 定数定義)がヒットする。

- [ ] **Step 2: `RevitHarness.Addin.csproj` を編集**

26-29行目のブロック:
```xml
  <!-- lookupElement is only available on the net48 (Revit 2024) build; see HarnessEventHandler.cs -->
  <PropertyGroup Condition="'$(RevitYearSuffix)'=='R24'">
    <DefineConstants>$(DefineConstants);LOOKUP_SUPPORTED</DefineConstants>
  </PropertyGroup>

```
を削除する(このブロックごと除去)。

39-42行目の `ItemGroup`:
```xml
  <ItemGroup>
    <ProjectReference Include="..\RevitHarness.Contracts\RevitHarness.Contracts.csproj" />
    <ProjectReference Include="..\RevitHarness.Lookup\RevitHarness.Lookup.csproj" Condition="'$(RevitYearSuffix)'=='R24'" />
  </ItemGroup>
```
を以下に置き換える(Lookupの参照を無条件化):
```xml
  <ItemGroup>
    <ProjectReference Include="..\RevitHarness.Contracts\RevitHarness.Contracts.csproj" />
    <ProjectReference Include="..\RevitHarness.Lookup\RevitHarness.Lookup.csproj" />
  </ItemGroup>
```

- [ ] **Step 3: `HarnessEventHandler.cs` の `using` ブロックを編集**

1-15行目:
```csharp
#nullable enable

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitHarness.Addin.Commands;
using RevitHarness.Contracts;
#if LOOKUP_SUPPORTED
using RevitHarness.Lookup;
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
```
を以下に置き換える:
```csharp
#nullable enable

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitHarness.Addin.Commands;
using RevitHarness.Contracts;
using RevitHarness.Lookup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
```

- [ ] **Step 4: `HandleLookupElement` メソッドを編集**

既存の323-383行目(`HandleLookupElement` メソッド全体)を以下に置き換える(`#if`/`#else`/`#endif` を除去し、対応版の実装のみを残す):

```csharp
        private IpcResponse HandleLookupElement(IpcRequest request, Stopwatch sw)
        {
            var elementId = request.GetPayloadValue<int?>("elementId");
            if (!elementId.HasValue)
            {
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.InvalidRequest,
                    "elementId is required",
                    sw.ElapsedMilliseconds);
            }

            var options = new InspectOptions
            {
                MaxDepth = request.GetPayloadValue<int?>("maxDepth") ?? 0,
                MaxMembers = request.GetPayloadValue<int?>("maxMembers") ?? 200,
                IncludeFields = request.GetPayloadValue<bool?>("includeFields") ?? false,
                IncludePrivateMembers = request.GetPayloadValue<bool?>("includePrivateMembers") ?? false,
                IncludeStaticMembers = request.GetPayloadValue<bool?>("includeStaticMembers") ?? false,
                NameFilter = request.GetPayloadValue<string>("nameFilter")
            };

            try
            {
                Log($"Looking up element: {elementId.Value}");
                var inspector = new ElementInspector();
                var result = inspector.Inspect(_uiapp!, elementId.Value, options);

                if (result.ErrorDetail != null)
                {
                    Log($"Lookup error: {result.ErrorDetail.Code} - {result.ErrorDetail.Message}");
                    return IpcResponse.Failure(
                        request.Id,
                        result.ErrorDetail.Code,
                        result.ErrorDetail.Message,
                        sw.ElapsedMilliseconds);
                }

                Log($"Lookup completed: {result.MemberCount} members found");
                return IpcResponse.Success(request.Id, result, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log($"Lookup failed: {ex}");
                return IpcResponse.Failure(
                    request.Id,
                    ErrorCodes.RevitApiError,
                    ex.Message,
                    sw.ElapsedMilliseconds,
                    new { stackTrace = ex.ToString() });
            }
        }
```

- [ ] **Step 5: `IpcMessages.cs` から未使用になった `NotSupported` 定数を削除**

`ErrorCodes` クラス(116-129行目)から `public const string NotSupported = "NOT_SUPPORTED";` の行を削除する。

- [ ] **Step 6: R24/R25/R26すべてでビルドが通ることを確認**

Run:
```bash
dotnet build src/RevitHarness.Addin -c "Release R24" -p:Platform=x64
dotnet build src/RevitHarness.Addin -c "Release R25" -p:Platform=x64
dotnet build src/RevitHarness.Addin -c "Release R26" -p:Platform=x64
```
Expected: 3つともPASS。R25/R26は以前の `NOT_SUPPORTED` スタブではなく、`ElementInspector` を使った実装でコンパイルされる。

- [ ] **Step 7: コミット**

```bash
git add src/RevitHarness.Addin/RevitHarness.Addin.csproj src/RevitHarness.Addin/Ipc/HarnessEventHandler.cs src/RevitHarness.Addin/Ipc/IpcMessages.cs
git commit -m "Enable lookupElement on Revit 2025/2026, remove LOOKUP_SUPPORTED gate"
```

---

### Task 4: ソリューションファイルの `RevitHarness.Lookup` 構成マッピングを修正

**Files:**
- Modify: `revit-cli-harness.sln`

**Interfaces:**
- Consumes: Task 2で追加した `RevitHarness.Lookup.csproj` の `Configurations`
- Produces: `dotnet build revit-cli-harness.sln -c "Release R25"` がLookupもR25(net8.0-windows)としてビルドする(現状は常にplain `Release|x64`=net48にフォールバックしている)。

- [ ] **Step 1: 現状の(誤った)マッピングを確認**

Run: `grep -n "D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A" revit-cli-harness.sln`
Expected: `.Debug R25|x64.ActiveCfg = Debug|x64` のように、R25/R26行が `R25`/`R26` サフィックスなしの `Debug|x64`/`Release|x64` にマッピングされている。

- [ ] **Step 2: `revit-cli-harness.sln` の該当ブロックを置き換える**

現在の60-71行目:
```
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug|x64.ActiveCfg = Debug|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug|x64.Build.0 = Debug|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R25|x64.ActiveCfg = Debug|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R25|x64.Build.0 = Debug|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R26|x64.ActiveCfg = Debug|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R26|x64.Build.0 = Debug|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release|x64.ActiveCfg = Release|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release|x64.Build.0 = Release|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R25|x64.ActiveCfg = Release|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R25|x64.Build.0 = Release|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R26|x64.ActiveCfg = Release|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R26|x64.Build.0 = Release|x64
```
を以下に置き換える:
```
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug|x64.ActiveCfg = Debug R24|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug|x64.Build.0 = Debug R24|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R25|x64.ActiveCfg = Debug R25|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R25|x64.Build.0 = Debug R25|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R26|x64.ActiveCfg = Debug R26|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Debug R26|x64.Build.0 = Debug R26|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release|x64.ActiveCfg = Release R24|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release|x64.Build.0 = Release R24|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R25|x64.ActiveCfg = Release R25|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R25|x64.Build.0 = Release R25|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R26|x64.ActiveCfg = Release R26|x64
		{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}.Release R26|x64.Build.0 = Release R26|x64
```

- [ ] **Step 3: ソリューションビルドで確認**

Run:
```bash
dotnet build revit-cli-harness.sln -c Release
dotnet build revit-cli-harness.sln -c "Release R25"
dotnet build revit-cli-harness.sln -c "Release R26"
```
Expected: 3つともPASS。`ls "src/RevitHarness.Lookup/bin/x64/Release R25/net8.0-windows/RevitHarness.Lookup.dll"` が存在することを確認し、Lookupが確かにnet8.0-windowsとしてビルドされたことを確認する。

- [ ] **Step 4: コミット**

```bash
git add revit-cli-harness.sln
git commit -m "Map RevitHarness.Lookup to per-year build configurations in the solution"
```

---

### Task 5: `CLAUDE.md` のドキュメント更新

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:** なし(ドキュメントのみ)

- [ ] **Step 1: Project Overview(7行目)を編集**

現在:
```
A Revit 2024/2025/2026 harness add-in that enables external CLI/agent execution of tests, commands, and (Revit 2024 only) element inspection within a running Revit instance. Supports DLL hot-swapping via shadow copy + byte-array loading without restarting Revit.
```
を以下に置き換える:
```
A Revit 2024/2025/2026 harness add-in that enables external CLI/agent execution of tests, commands, and element inspection within a running Revit instance. Supports DLL hot-swapping via shadow copy + byte-array loading without restarting Revit.
```

- [ ] **Step 2: Key Architecture(14行目)を編集**

現在:
```
- **Mixed Frameworks**: Addin multi-targets per Revit year via Configuration suffix (`R24`/`R25`/`R26`) — `net48` for Revit 2024, `net8.0-windows` for Revit 2025/2026. Contracts multi-targets `net48;net8.0-windows` directly (published NuGet package). Lookup stays `net48`-only (Revit 2024 only, see `lookupElement` note below). CLI targets `net8.0` (a `dotnet tool` cannot use a Windows-specific TFM).
```
を以下に置き換える:
```
- **Mixed Frameworks**: Addin and Lookup both multi-target per Revit year via Configuration suffix (`R24`/`R25`/`R26`) — `net48` for Revit 2024, `net8.0-windows` for Revit 2025/2026. Contracts multi-targets `net48;net8.0-windows` directly (published NuGet package). CLI targets `net8.0` (a `dotnet tool` cannot use a Windows-specific TFM).
```

- [ ] **Step 3: Build Commandsセクションの`lookupElement`注記(48行目)を編集**

現在の48行目末尾の一文:
```
`lookupElement` is only available in the R24 (Revit 2024, net48) build — R25/R26 builds return a `NOT_SUPPORTED` error for that operation because the `RevitHarness.Lookup` dependency (`lib/LookupEngine/*.dll`) is net48-only.
```
を削除する(`lookupElement` は全バージョンで動作するため、この制約の記述自体が不要になる)。

- [ ] **Step 4: Project Structure(73-76行目)を編集**

現在:
```
├── RevitHarness.Lookup/             # net48 - Element inspection (RevitLookup-style)
│   ├── ElementInspector.cs          # Main inspection entry point
│   ├── InspectOptions.cs            # Depth, filters, member visibility options
│   └── Descriptors/                 # Type-specific value formatters (Element, XYZ, Curve, etc.)
```
を以下に置き換える:
```
├── RevitHarness.Lookup/             # net48 (R24) / net8.0-windows (R25/R26) - Element inspection (RevitLookup-style)
│   ├── ElementInspector.cs          # Main inspection entry point
│   ├── InspectOptions.cs            # Depth, filters, member visibility options
│   └── Descriptors/                 # Type-specific value formatters (Element, XYZ, Curve, etc.)
```

- [ ] **Step 5: `lib/LookupEngine/` コメント(85行目)を編集**

現在:
```
lib/LookupEngine/                    # External library used by RevitHarness.Lookup
```
を以下に置き換える(他のツリー行と同じ1行コメントの体裁を維持する):
```
lib/LookupEngine/                    # External library used by RevitHarness.Lookup, vendored per-TFM (net48/, net8.0-windows/)
```

- [ ] **Step 6: Development Notes(90行目)を編集し、vendoringのリフレッシュ手順を追記**

現在:
```
- **Target Frameworks**: Addin/Contracts/Lookup = net48 (x64); CLI = net8.0
```
を以下に置き換える:
```
- **Target Frameworks**: Addin/Lookup = net48 (R24) / net8.0-windows (R25, R26); Contracts = net48;net8.0-windows; CLI = net8.0
- **LookupEngine vendoring**: `lib/LookupEngine/{net48,net8.0-windows}/` holds `LookupEngine.dll` + `LookupEngine.Abstractions.dll`, extracted from an installed RevitLookup add-in for the matching Revit year (not published to NuGet.org, so there's no package to reference). To refresh, copy the two DLLs from `%PROGRAMDATA%\Autodesk\Revit\Addins\{year}\RevitLookup\` into the matching TFM subfolder.
```

- [ ] **Step 7: コミット**

```bash
git add CLAUDE.md
git commit -m "Document Lookup's Revit 2024/2025/2026 support in CLAUDE.md"
```

---

### Task 6: 全構成ビルドの最終確認、および任意の実機確認

**Files:** なし(検証のみ)

**Interfaces:**
- Consumes: Task 1〜5のすべての成果物
- Produces: なし(このプランの最終タスク)

- [ ] **Step 1: 全構成・全プロジェクトのビルド確認**

Run:
```bash
dotnet build src/RevitHarness.Contracts -c Release
dotnet build src/RevitHarness.Lookup -c "Release R24"
dotnet build src/RevitHarness.Lookup -c "Release R25"
dotnet build src/RevitHarness.Lookup -c "Release R26"
dotnet build src/RevitHarness.Addin -c "Release R24" -p:Platform=x64
dotnet build src/RevitHarness.Addin -c "Release R25" -p:Platform=x64
dotnet build src/RevitHarness.Addin -c "Release R26" -p:Platform=x64
dotnet build revit-cli-harness.sln -c Release
dotnet pack src/RevitHarness.Cli -c Release --no-build -o /tmp/nupkg-final
```
Expected: すべてPASS。

- [ ] **Step 2(任意・要ユーザー確認): 実機Revitでのライブ検証**

これはRevitをインストール・再起動を伴う操作であり、ユーザーの作業を中断しうるため、**実行前に必ずユーザーに確認を取ること**。本機にはRevit 2024・2026とそれぞれ対応するRevitHarnessが実インストール済みなので、以下の手順で実際に動作確認できる:

```bash
dotnet run --project src/RevitHarness.Cli -- install --revit-version 2024 --force
dotnet run --project src/RevitHarness.Cli -- install --revit-version 2026 --force
```

その後、Revit 2024またはRevit 2026を起動し、何らかの要素を1つ選択してIDを控えたうえで:
```bash
dotnet run --project src/RevitHarness.Cli -- lookup --id <elementId>
```
Expected: `NOT_SUPPORTED` エラーではなく、`members`を含む実際のインスペクション結果が返る。

Revit 2025は本機に未インストールのため、このライブ検証はスコープ外(R25はR26と同一バイナリを使うため、上記R26でのライブ確認とTask 1〜3のビルド成功をもって間接的に担保する)。

- [ ] **Step 3: 最終状態の確認**

Run: `git log --oneline -6`
Expected: Task 1〜5の6コミット(このTaskは検証のみでコミットを作らない)が新しいCLAUDE.md/sln/csproj/lib変更として並んでいる。
