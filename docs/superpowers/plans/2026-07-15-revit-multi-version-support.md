# Revit 2024/2025/2026 マルチバージョン対応 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RevitHarness.Addin/Contracts/Cli を Revit 2024(net48)・2025/2026(net8.0-windows) の3バージョンすべてでビルド・インストールできるようにする。

**Architecture:** Addin/Lookupは Nice3point 流の Configuration 名(R24/R25/R26)によるマルチターゲット、Contracts(公開NuGetパッケージ)は本物の `TargetFrameworks` マルチターゲットを使う。`lookupElement`(RevitHarness.Lookup依存)はnet48専用バイナリに依存しているため2025/2026では非対応とし、コンパイル時シンボルで分岐する。CLIは3バージョン分のAddinビルドを個別リソース名で埋め込み、インストール時に指定バージョンに対応する分だけ展開する。

**Tech Stack:** .NET SDK (net48 / net8.0-windows / net8.0), MSBuild Central Package Management, Nice3point.Revit.Api.* NuGetパッケージ

## Global Constraints

- 対応バージョンは2024/2025/2026の3つ。2024の既存動作は変更しない。
- Named Pipe名は現状の `"RevitHarness2024"` から変更しない(複数バージョン同時起動はサポート対象外)。
- Nice3point.Revit.Api.RevitAPI / RevitAPIUI のバージョン: 2024→`2024.3.40`、2025→`2025.4.0`、2026→`2026.4.0`。
- `RevitHarness.Lookup`(要素インスペクタ、`lib/LookupEngine/*.dll`に依存)は`.NETFramework,Version=v4.8`専用ビルドしか存在しないため、Revit 2025/2026では`lookupElement`操作を非サポートとし、`{ "ok": false, "error": { "code": "NOT_SUPPORTED", "message": "lookupElement not supported on this Revit version yet" } }` を返す。`ping`/`runTests`/`runCommand`は全バージョンで動作する。
- 検証は `dotnet build`/`dotnet pack` の成功確認までとする。実機Revit 2025/2026での起動・install/ping等の動作確認はスコープ外。

---

### Task 1: RevitHarness.Contracts のマルチターゲット化(NuGetパッケージ)

**Files:**
- Modify: `src/RevitHarness.Contracts/RevitHarness.Contracts.csproj`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: なし(独立したタスク)
- Produces: `RevitHarness.Contracts.dll` が `net48` と `net8.0-windows` の両方でビルドされる。以降のタスク(Addin R25/R26ビルド)はこの成果物に依存する。

- [ ] **Step 1: 現状で `net8.0-windows` ビルドが存在しないことを確認**

Run: `dotnet build src/RevitHarness.Contracts -c Release -f net8.0-windows`
Expected: FAIL (`error NETSDK1013` または `error MSB4126` 等、`net8.0-windows` が `TargetFramework` に存在しないためのエラー)

- [ ] **Step 2: `Directory.Packages.props` に central version override を有効化**

`Directory.Packages.props` の `<PropertyGroup>` に1行追加:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageVersionOverrideEnabled>true</CentralPackageVersionOverrideEnabled>
  </PropertyGroup>
  ...
```

- [ ] **Step 3: `RevitHarness.Contracts.csproj` を `TargetFrameworks` マルチターゲットに変更**

`src/RevitHarness.Contracts/RevitHarness.Contracts.csproj` を以下の内容に置き換える:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net48;net8.0-windows</TargetFrameworks>
    <RootNamespace>RevitHarness.Contracts</RootNamespace>
    <AssemblyName>RevitHarness.Contracts</AssemblyName>

    <!-- NuGet Package Settings -->
    <PackageId>RevitHarness.Contracts</PackageId>
    <Description>Shared interfaces for RevitHarness - ITestEntryPoint and IHarnessCommand</Description>
    <PackageTags>revit;testing;harness;contracts</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- net48 build targets Revit 2024's API version -->
  <ItemGroup Condition="'$(TargetFramework)'=='net48'">
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" VersionOverride="2024.3.40" PrivateAssets="all" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" VersionOverride="2024.3.40" PrivateAssets="all" />
  </ItemGroup>

  <!-- net8.0-windows build is shared by Revit 2025/2026; pinned to the newer API version -->
  <ItemGroup Condition="'$(TargetFramework)'=='net8.0-windows'">
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" VersionOverride="2026.4.0" PrivateAssets="all" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" VersionOverride="2026.4.0" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

Note: `AppendTargetFrameworkToOutputPath` は設定しない(既定のtrueのまま)。マルチターゲット時は `bin\x64\{Configuration}\{TargetFramework}\` に分かれて出力されるのが正しい状態(Addin側のProjectReferenceはビルド中のプロジェクトのTFMに一致する方を自動選択して参照する)。

- [ ] **Step 4: 両方のTFMでビルドが通ることを確認**

Run: `dotnet build src/RevitHarness.Contracts -c Release`
Expected: PASS。出力に `net48/RevitHarness.Contracts.dll` と `net8.0-windows/RevitHarness.Contracts.dll` の両方が生成される。もし `VersionOverride` 関連のエラー(NU1010等)が出た場合は、`CentralPackageVersionOverrideEnabled` が正しく設定されているか確認して修正する。

- [ ] **Step 5: pack が通ることを確認**

Run: `dotnet pack src/RevitHarness.Contracts -c Release -o /tmp/nupkg-check`
Expected: PASS。生成された `.nupkg` を展開すると `lib/net48/RevitHarness.Contracts.dll` と `lib/net8.0-windows/RevitHarness.Contracts.dll` の両方が含まれる。

- [ ] **Step 6: コミット**

```bash
git add src/RevitHarness.Contracts/RevitHarness.Contracts.csproj Directory.Packages.props
git commit -m "Multi-target RevitHarness.Contracts for net48 and net8.0-windows"
```

---

### Task 2: RevitHarness.Addin のマルチバージョンビルド構成

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/RevitHarness.Addin/RevitHarness.Addin.csproj`

**Interfaces:**
- Consumes: Task 1で作成した `RevitHarness.Contracts` の `net48`/`net8.0-windows` 両ビルド
- Produces: `Configuration` が `Debug R24`/`Release R24`/`Debug R25`/`Release R25`/`Debug R26`/`Release R26` のいずれかのとき、対応する `TargetFramework`(`net48` or `net8.0-windows`)でビルドされる `RevitHarness.Addin.dll`。`DefineConstants` に `LOOKUP_SUPPORTED` がR24構成でのみ定義される(Task 3で使用)。

- [ ] **Step 1: 現状で `Release R25` 構成が存在しないことを確認**

Run: `dotnet build src/RevitHarness.Addin -c "Release R25"`
Expected: FAIL(`Release R25` が `Configurations` に存在しないため、`Debug` にフォールバックするか `MSB4126` 相当のエラーになる)

- [ ] **Step 2: `Directory.Packages.props` にConfiguration条件付きのバージョンを追加**

`Directory.Packages.props` の該当箇所を以下に置き換える:

```xml
  <ItemGroup>
    <!-- JSON serialization -->
    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />

    <!-- Revit API reference assemblies (compile-time only; Revit supplies the real DLLs at runtime) -->
    <!-- Used by RevitHarness.Addin / RevitHarness.Lookup, selected by Configuration suffix (R24/R25/R26) -->
    <PackageVersion Include="Nice3point.Revit.Api.RevitAPI" Version="2024.3.40" Condition="$(Configuration.EndsWith('R24'))" />
    <PackageVersion Include="Nice3point.Revit.Api.RevitAPI" Version="2025.4.0" Condition="$(Configuration.EndsWith('R25'))" />
    <PackageVersion Include="Nice3point.Revit.Api.RevitAPI" Version="2026.4.0" Condition="$(Configuration.EndsWith('R26'))" />
    <PackageVersion Include="Nice3point.Revit.Api.RevitAPIUI" Version="2024.3.40" Condition="$(Configuration.EndsWith('R24'))" />
    <PackageVersion Include="Nice3point.Revit.Api.RevitAPIUI" Version="2025.4.0" Condition="$(Configuration.EndsWith('R25'))" />
    <PackageVersion Include="Nice3point.Revit.Api.RevitAPIUI" Version="2026.4.0" Condition="$(Configuration.EndsWith('R26'))" />
  </ItemGroup>
```

- [ ] **Step 3: `RevitHarness.Addin.csproj` をマルチバージョン構成に変更**

`src/RevitHarness.Addin/RevitHarness.Addin.csproj` を以下の内容に置き換える:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <Configurations>Debug R24;Release R24;Debug R25;Release R25;Debug R26;Release R26</Configurations>
    <TargetFramework Condition="$(Configuration.EndsWith('R24'))">net48</TargetFramework>
    <TargetFramework Condition="$(Configuration.EndsWith('R25')) Or $(Configuration.EndsWith('R26'))">net8.0-windows</TargetFramework>
    <RootNamespace>RevitHarness.Addin</RootNamespace>
    <AssemblyName>RevitHarness.Addin</AssemblyName>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <!-- lookupElement is only available on the net48 (Revit 2024) build; see Task 3 -->
  <PropertyGroup Condition="$(Configuration.EndsWith('R24'))">
    <DefineConstants>$(DefineConstants);LOOKUP_SUPPORTED</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="PresentationFramework" />
    <Reference Include="PresentationCore" />
    <Reference Include="WindowsBase" />
    <Reference Include="System.Xaml" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RevitHarness.Contracts\RevitHarness.Contracts.csproj" />
    <ProjectReference Include="..\RevitHarness.Lookup\RevitHarness.Lookup.csproj" Condition="$(Configuration.EndsWith('R24'))" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" PrivateAssets="all" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <None Include="RevitHarness.addin" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <!-- Debug settings -->
  <PropertyGroup>
    <StartAction>Program</StartAction>
    <StartProgram Condition="$(Configuration.EndsWith('R24'))">C:\Program Files\Autodesk\Revit 2024\Revit.exe</StartProgram>
    <StartProgram Condition="$(Configuration.EndsWith('R25'))">C:\Program Files\Autodesk\Revit 2025\Revit.exe</StartProgram>
    <StartProgram Condition="$(Configuration.EndsWith('R26'))">C:\Program Files\Autodesk\Revit 2026\Revit.exe</StartProgram>
  </PropertyGroup>
</Project>
```

Note: この時点ではまだ `HarnessEventHandler.cs` が `LOOKUP_SUPPORTED` を見ていないため、`Release R25`/`Release R26` はまだビルドが失敗する(`RevitHarness.Lookup` を参照するコードがR25/R26でもコンパイルされ、Lookupプロジェクト参照が無いためエラーになる)。これはTask 3で解消する。

- [ ] **Step 4: `Release R24` がビルドできることを確認(既存動作の回帰確認)**

Run: `dotnet build src/RevitHarness.Addin -c "Release R24"`
Expected: PASS(2024/net48の既存ビルドが壊れていないことを確認)

- [ ] **Step 5: コミット**

```bash
git add Directory.Packages.props src/RevitHarness.Addin/RevitHarness.Addin.csproj
git commit -m "Add per-Revit-year build configurations to RevitHarness.Addin"
```

---

### Task 3: `lookupElement` のバージョン別コンパイル分岐

**Files:**
- Modify: `src/RevitHarness.Addin/Ipc/HarnessEventHandler.cs`
- Modify: `src/RevitHarness.Addin/Ipc/IpcMessages.cs`

**Interfaces:**
- Consumes: Task 2の `LOOKUP_SUPPORTED` コンパイルシンボル(R24構成でのみ定義)
- Produces: R24ビルドでは既存どおり `ElementInspector` を使った `lookupElement` が動作。R25/R26ビルドでは `ErrorCodes.NotSupported`("NOT_SUPPORTED")を返す。

- [ ] **Step 1: `Release R25` がまだビルド失敗することを確認**

Run: `dotnet build src/RevitHarness.Addin -c "Release R25"`
Expected: FAIL(`using RevitHarness.Lookup;` や `InspectOptions`/`ElementInspector` の型が見つからないエラー。R25構成では `RevitHarness.Lookup` プロジェクト参照が無いため)

- [ ] **Step 2: `IpcMessages.cs` に `NotSupported` エラーコードを追加**

`src/RevitHarness.Addin/Ipc/IpcMessages.cs` の `ErrorCodes` クラスに1行追加:

```csharp
    public static class ErrorCodes
    {
        public const string HarnessNotReady = "HARNESS_NOT_READY";
        public const string InvalidRequest = "INVALID_REQUEST";
        public const string AssemblyNotFound = "ASSEMBLY_NOT_FOUND";
        public const string TypeNotFound = "TYPE_NOT_FOUND";
        public const string EntrypointFailed = "ENTRYPOINT_FAILED";
        public const string RevitApiError = "REVIT_API_ERROR";
        public const string Timeout = "TIMEOUT";
        public const string InternalError = "INTERNAL_ERROR";
        public const string ElementNotFound = "ELEMENT_NOT_FOUND";
        public const string NoActiveDocument = "NO_ACTIVE_DOCUMENT";
        public const string NotSupported = "NOT_SUPPORTED";
    }
```

- [ ] **Step 3: `HarnessEventHandler.cs` の `using` とメソッド本体を条件分岐**

`src/RevitHarness.Addin/Ipc/HarnessEventHandler.cs` の先頭 `using` ブロック(1〜13行目)を以下に置き換える:

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

続けて `HandleLookupElement` メソッド全体(既存の321〜372行目)を以下に置き換える:

```csharp
        private IpcResponse HandleLookupElement(IpcRequest request, Stopwatch sw)
        {
#if LOOKUP_SUPPORTED
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
#else
            Log("lookupElement is not supported on this Revit version");
            return IpcResponse.Failure(
                request.Id,
                ErrorCodes.NotSupported,
                "lookupElement not supported on this Revit version yet",
                sw.ElapsedMilliseconds);
#endif
        }
```

- [ ] **Step 4: R24/R25/R26すべてでビルドが通ることを確認**

Run:
```bash
dotnet build src/RevitHarness.Addin -c "Release R24"
dotnet build src/RevitHarness.Addin -c "Release R25"
dotnet build src/RevitHarness.Addin -c "Release R26"
```
Expected: 3つともPASS。R24は`ElementInspector`を使った実装、R25/R26は`NOT_SUPPORTED`を返す実装でコンパイルされる。

- [ ] **Step 5: コミット**

```bash
git add src/RevitHarness.Addin/Ipc/HarnessEventHandler.cs src/RevitHarness.Addin/Ipc/IpcMessages.cs
git commit -m "Gate lookupElement behind LOOKUP_SUPPORTED for net48-only LookupEngine dependency"
```

---

### Task 4: ソリューションファイルにマルチバージョン構成を追加

**Files:**
- Modify: `revit-cli-harness.sln`

**Interfaces:**
- Consumes: Task 2で追加した `RevitHarness.Addin.csproj` の `Configurations`
- Produces: Visual Studio / `dotnet build revit-cli-harness.sln` から `Debug R25|x64` 等の構成を選択可能にする。既存の `Debug|x64`/`Release|x64` はAddinを引き続き `Debug R24`/`Release R24` にマッピングし、後方互換を保つ。

- [ ] **Step 1: 現状で `Release R25|x64` ソリューション構成が存在しないことを確認**

Run: `dotnet build revit-cli-harness.sln -c "Release R25"`
Expected: FAIL または警告付きで無視される(ソリューションレベルの構成が存在しないため)

- [ ] **Step 2: `revit-cli-harness.sln` を以下の内容に置き換える**

```
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.12.35527.113
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "RevitHarness.Contracts", "src\RevitHarness.Contracts\RevitHarness.Contracts.csproj", "{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "RevitHarness.Addin", "src\RevitHarness.Addin\RevitHarness.Addin.csproj", "{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "RevitHarness.Cli", "src\RevitHarness.Cli\RevitHarness.Cli.csproj", "{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "RevitHarness.Lookup", "src\RevitHarness.Lookup\RevitHarness.Lookup.csproj", "{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|x64 = Debug|x64
		Debug R25|x64 = Debug R25|x64
		Debug R26|x64 = Debug R26|x64
		Release|x64 = Release|x64
		Release R25|x64 = Release R25|x64
		Release R26|x64 = Release R26|x64
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Debug|x64.ActiveCfg = Debug|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Debug|x64.Build.0 = Debug|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Debug R25|x64.ActiveCfg = Debug|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Debug R25|x64.Build.0 = Debug|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Debug R26|x64.ActiveCfg = Debug|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Debug R26|x64.Build.0 = Debug|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Release|x64.ActiveCfg = Release|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Release|x64.Build.0 = Release|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Release R25|x64.ActiveCfg = Release|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Release R25|x64.Build.0 = Release|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Release R26|x64.ActiveCfg = Release|x64
		{C1E2F3A4-5B6D-7E8F-9A0B-1C2D3E4F5A6B}.Release R26|x64.Build.0 = Release|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Debug|x64.ActiveCfg = Debug R24|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Debug|x64.Build.0 = Debug R24|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Debug R25|x64.ActiveCfg = Debug R25|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Debug R25|x64.Build.0 = Debug R25|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Debug R26|x64.ActiveCfg = Debug R26|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Debug R26|x64.Build.0 = Debug R26|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Release|x64.ActiveCfg = Release R24|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Release|x64.Build.0 = Release R24|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Release R25|x64.ActiveCfg = Release R25|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Release R25|x64.Build.0 = Release R25|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Release R26|x64.ActiveCfg = Release R26|x64
		{05450411-AEC1-4FC1-B1FC-E07EAF12EC9E}.Release R26|x64.Build.0 = Release R26|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Debug|x64.ActiveCfg = Debug|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Debug|x64.Build.0 = Debug|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Debug R25|x64.ActiveCfg = Debug|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Debug R25|x64.Build.0 = Debug|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Debug R26|x64.ActiveCfg = Debug|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Debug R26|x64.Build.0 = Debug|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Release|x64.ActiveCfg = Release|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Release|x64.Build.0 = Release|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Release R25|x64.ActiveCfg = Release|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Release R25|x64.Build.0 = Release|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Release R26|x64.ActiveCfg = Release|x64
		{B2A4F8E1-3C5D-4F2E-9A1B-6C8D7E0F2A3B}.Release R26|x64.Build.0 = Release|x64
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
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
	GlobalSection(ExtensibilityGlobals) = postSolution
		SolutionGuid = {5B858F2F-0ECF-4035-AF99-5039CF29879F}
	EndGlobalSection
EndGlobal
```

- [ ] **Step 3: 新旧の構成でソリュションビルドが通ることを確認**

Run:
```bash
dotnet build revit-cli-harness.sln -c Release
dotnet build revit-cli-harness.sln -c "Release R25"
dotnet build revit-cli-harness.sln -c "Release R26"
```
Expected: 3つともPASS。1つ目はAddinがR24(net48)としてビルドされ、既存CIコマンドと完全互換であることを確認する。

- [ ] **Step 4: コミット**

```bash
git add revit-cli-harness.sln
git commit -m "Add Revit 2025/2026 solution configurations"
```

---

### Task 5: CLIのマルチバージョン埋め込み・インストーラー対応

**Files:**
- Modify: `src/RevitHarness.Cli/RevitHarness.Cli.csproj`
- Modify: `src/RevitHarness.Cli/Installation/RevitInstaller.cs`
- Modify: `src/RevitHarness.Cli/Program.cs:131,134`

**Interfaces:**
- Consumes: Task 2〜4で作成した `bin\x64\{Configuration} R24\`/`R25\`/`R26\` の3つのAddinビルド出力
- Produces: `RevitInstaller.Install(int revitVersion, bool force)` は `revitVersion` に応じたバージョン別リソースを展開し、2024/2025/2026以外では失敗する `InstallResult` を返す。

- [ ] **Step 1: `RevitHarness.Cli.csproj` の埋め込みリソースをバージョン別ワイルドカードに変更**

`src/RevitHarness.Cli/RevitHarness.Cli.csproj` の「Embed addin files as resources」`ItemGroup`(既存27〜41行目)を以下に置き換える:

```xml
  <!-- Embed addin files as resources, one set per supported Revit year -->
  <ItemGroup>
    <EmbeddedResource Include="..\RevitHarness.Addin\bin\x64\$(Configuration) R24\*.dll"
                      Exclude="..\RevitHarness.Addin\bin\x64\$(Configuration) R24\RevitAPI.dll;..\RevitHarness.Addin\bin\x64\$(Configuration) R24\RevitAPIUI.dll"
                      LogicalName="2024/%(Filename)%(Extension)"
                      Condition="Exists('..\RevitHarness.Addin\bin\x64\$(Configuration) R24')" />
    <EmbeddedResource Include="..\RevitHarness.Addin\bin\x64\$(Configuration) R25\*.dll"
                      Exclude="..\RevitHarness.Addin\bin\x64\$(Configuration) R25\RevitAPI.dll;..\RevitHarness.Addin\bin\x64\$(Configuration) R25\RevitAPIUI.dll"
                      LogicalName="2025/%(Filename)%(Extension)"
                      Condition="Exists('..\RevitHarness.Addin\bin\x64\$(Configuration) R25')" />
    <EmbeddedResource Include="..\RevitHarness.Addin\bin\x64\$(Configuration) R26\*.dll"
                      Exclude="..\RevitHarness.Addin\bin\x64\$(Configuration) R26\RevitAPI.dll;..\RevitHarness.Addin\bin\x64\$(Configuration) R26\RevitAPIUI.dll"
                      LogicalName="2026/%(Filename)%(Extension)"
                      Condition="Exists('..\RevitHarness.Addin\bin\x64\$(Configuration) R26')" />
    <EmbeddedResource Include="..\RevitHarness.Addin\RevitHarness.addin" Link="Resources\RevitHarness.addin" LogicalName="RevitHarness.addin" />
  </ItemGroup>
```

Note: `$(Configuration)` はCLI自身の構成(`Debug`/`Release`)。Addin側のビルドは常に `{Debug|Release} R2x` という命名になるため、`$(Configuration) R24` のように連結すれば一致する。`Exclude` はRevit本体が供給する `RevitAPI.dll`/`RevitAPIUI.dll`(コンパイル時参照専用)を誤って同梱しないための安全策。実際にこれらのDLLがビルド出力フォルダにコピーされるかはStep 3で確認し、コピーされていなければ `Exclude` は単なる無害な保険として残す。

- [ ] **Step 2: `RevitInstaller.cs` をバージョン別動的抽出に変更**

`src/RevitHarness.Cli/Installation/RevitInstaller.cs` の冒頭、クラス定義直後(既存14〜32行目、`EmbeddedResources` 静的配列)を以下に置き換える:

```csharp
    public static class RevitInstaller
    {
        private static readonly int[] SupportedRevitVersions = { 2024, 2025, 2026 };

        private const string AddinManifestResourceName = "RevitHarness.addin";
```

続けて `Install` メソッド(既存121〜193行目)を以下に置き換える:

```csharp
        /// <summary>
        /// Installs the RevitHarness add-in for a specific Revit version.
        /// </summary>
        public static InstallResult Install(int revitVersion, bool force = false)
        {
            var result = new InstallResult { RevitVersion = revitVersion };

            if (Array.IndexOf(SupportedRevitVersions, revitVersion) < 0)
            {
                result.Success = false;
                result.Message = $"Revit {revitVersion} is not supported by this CLI build. Supported versions: {string.Join(", ", SupportedRevitVersions)}";
                return result;
            }

            try
            {
                var addinsFolder = GetAddinsFolder(revitVersion);
                var installFolder = GetInstallationFolder(revitVersion);
                var manifestPath = GetAddinManifestPath(revitVersion);

                // Check if addins folder exists
                if (!Directory.Exists(addinsFolder))
                {
                    result.Success = false;
                    result.Message = $"Revit {revitVersion} addins folder does not exist: {addinsFolder}";
                    return result;
                }

                // Check if already installed
                if (IsInstalled(revitVersion) && !force)
                {
                    result.Success = false;
                    result.Message = $"RevitHarness is already installed for Revit {revitVersion}. Use --force to reinstall.";
                    return result;
                }

                var assembly = Assembly.GetExecutingAssembly();
                var prefix = revitVersion + "/";
                var versionedResourceNames = Array.FindAll(
                    assembly.GetManifestResourceNames(),
                    name => name.StartsWith(prefix, StringComparison.Ordinal));

                if (versionedResourceNames.Length == 0)
                {
                    result.Success = false;
                    result.Message = $"No embedded add-in files found for Revit {revitVersion} in this CLI build.";
                    return result;
                }

                // Create installation folder
                Directory.CreateDirectory(installFolder);

                var extractedFiles = new List<string>();

                foreach (var resourceName in versionedResourceNames)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                        continue;

                    var fileName = resourceName.Substring(prefix.Length);
                    var destPath = Path.Combine(installFolder, fileName);

                    using var fileStream = File.Create(destPath);
                    stream.CopyTo(fileStream);
                    extractedFiles.Add(destPath);
                }

                using (var manifestStream = assembly.GetManifestResourceStream(AddinManifestResourceName))
                {
                    if (manifestStream != null)
                    {
                        using var manifestFileStream = File.Create(manifestPath);
                        manifestStream.CopyTo(manifestFileStream);
                        extractedFiles.Add(manifestPath);
                    }
                }

                result.Success = true;
                result.Message = $"RevitHarness installed successfully for Revit {revitVersion}";
                result.InstalledFiles = extractedFiles;
                result.InstallationFolder = installFolder;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Installation failed: {ex.Message}";
            }

            return result;
        }
```

`IsInstalled`/`GetStatus`/`Uninstall`/`GetInstalledRevitVersions` はそのまま変更しない(バージョン非依存のファイル操作のため)。

- [ ] **Step 3: `Program.cs` のヘルプテキストを更新**

`src/RevitHarness.Cli/Program.cs:131` と `:134` の `--revit-version <year>      Revit version (e.g., 2024) (required)` を、それぞれ `--revit-version <year>      Revit version (e.g., 2024, 2025, 2026) (required)` に変更する。

- [ ] **Step 4: 3バージョン分のAddinビルド + CLIビルドが通ることを確認**

Run:
```bash
dotnet build src/RevitHarness.Addin -c "Release R24"
dotnet build src/RevitHarness.Addin -c "Release R25"
dotnet build src/RevitHarness.Addin -c "Release R26"
dotnet build src/RevitHarness.Cli -c Release
```
Expected: 4つともPASS。CLIのビルドログに `2024/RevitHarness.Addin.dll` 等、バージョンプレフィックス付きの埋め込みリソースが含まれることを確認する(`dotnet build -v:detailed` や `ildasm`/`dotnet-resx` 等でGetManifestResourceNames相当を確認してもよい)。

- [ ] **Step 5: pack が通ることを確認**

Run: `dotnet pack src/RevitHarness.Cli -c Release -o /tmp/nupkg-check --no-build`
Expected: PASS

- [ ] **Step 6: コミット**

```bash
git add src/RevitHarness.Cli/RevitHarness.Cli.csproj src/RevitHarness.Cli/Installation/RevitInstaller.cs src/RevitHarness.Cli/Program.cs
git commit -m "Support installing RevitHarness for Revit 2024/2025/2026 from a single CLI build"
```

---

### Task 6: ドキュメント・CI更新と最終確認

**Files:**
- Modify: `CLAUDE.md`
- Modify: `.github/workflows/publish.yml`

**Interfaces:**
- Consumes: Task 1〜5のすべての成果物
- Produces: なし(ドキュメント/CI設定のみ。このタスクが本プランの最終タスク)

- [ ] **Step 1: `CLAUDE.md` の Build Commands セクションを更新**

`CLAUDE.md` の `## Build Commands` セクションを以下に置き換える:

```markdown
## Build Commands

```bash
# Build the Addin for a specific Revit year (must be built before the CLI, once per supported year)
dotnet build src/RevitHarness.Addin -c "Debug R24"    # Revit 2024 (net48)
dotnet build src/RevitHarness.Addin -c "Debug R25"    # Revit 2025 (net8.0-windows)
dotnet build src/RevitHarness.Addin -c "Debug R26"    # Revit 2026 (net8.0-windows)

# Build the solution (defaults Addin to the R24/Revit 2024 build for backward compatibility)
dotnet build revit-cli-harness.sln -c Debug

# Build single project
dotnet build src/RevitHarness.Contracts
dotnet build src/RevitHarness.Addin -c "Debug R24"
dotnet build src/RevitHarness.Cli

# Generate NuGet packages
dotnet pack src/RevitHarness.Contracts -c Release
dotnet pack src/RevitHarness.Cli -c Release
```

**Important**: Build the Addin project for all three configurations (`Debug R24`/`Debug R25`/`Debug R26`, or the `Release` equivalents) before the CLI project — the CLI embeds each year's Addin build output as resources (see `RevitHarness.Cli.csproj` `EmbeddedResource` items with `Condition="Exists(...)"`), and `RevitInstaller` selects the matching set at install time based on `--revit-version`. `lookupElement` is only available in the R24 (Revit 2024, net48) build; R25/R26 builds return a `NOT_SUPPORTED` error for that operation because the `RevitHarness.Lookup` dependency (`lib/LookupEngine/*.dll`) is net48-only.
```

- [ ] **Step 2: `.github/workflows/publish.yml` にR25/R26のAddinビルドを追加**

`.github/workflows/publish.yml` の `publish` ジョブ内、`Build solution` ステップと `Pack Contracts` ステップの間に2ステップ追加する:

```yaml
      - name: Build solution
        run: dotnet build revit-cli-harness.sln -c Release -p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Build Addin for Revit 2025
        run: dotnet build src/RevitHarness.Addin -c "Release R25" -p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Build Addin for Revit 2026
        run: dotnet build src/RevitHarness.Addin -c "Release R26" -p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Pack Contracts
        run: dotnet pack src/RevitHarness.Contracts -c Release -p:Version=${{ needs.resolve-version.outputs.version }} -p:Platform=x64 --no-build -o ./nupkgs

      - name: Pack CLI
        run: dotnet pack src/RevitHarness.Cli -c Release -p:Version=${{ needs.resolve-version.outputs.version }} -p:Platform=x64 --no-build -o ./nupkgs
```

(`Build solution` は既存どおり `-c Release` のまま。既存のsln構成マッピング(Task 4)により、これはAddinをR24として、他すべてのプロジェクトを通常のReleaseとしてビルドする。)

- [ ] **Step 3: 全構成・全プロジェクトの最終ビルド確認**

Run:
```bash
dotnet build src/RevitHarness.Contracts -c Release
dotnet build src/RevitHarness.Addin -c "Release R24"
dotnet build src/RevitHarness.Addin -c "Release R25"
dotnet build src/RevitHarness.Addin -c "Release R26"
dotnet build revit-cli-harness.sln -c Release
dotnet pack src/RevitHarness.Contracts -c Release -o /tmp/nupkg-final --no-build
dotnet pack src/RevitHarness.Cli -c Release -o /tmp/nupkg-final --no-build
```
Expected: すべてPASS。

- [ ] **Step 4: コミット**

```bash
git add CLAUDE.md .github/workflows/publish.yml
git commit -m "Document and CI-build the Revit 2024/2025/2026 multi-version workflow"
```

---

## Self-Review Notes

- **Spec coverage**: §2.1(Addin/Lookup Configuration方式)→Task2、§2.2(Contracts TargetFrameworks方式)→Task1、§2.3(CLI/インストーラー)→Task5、§2.4(Named Pipe維持)→変更なし(意図的に対象タスクなし)、§2.5(ソリューション/ビルドコマンド)→Task4・Task6。LookupEngineブロッカー(スペック外で発覚)→Task3で対応。
- **既知の未確定点**: Nice3point.Revit.Api の2025年度版バージョン`2025.4.0`はNuGet Gallery確認時点(2026-07-15)の値。実装時に存在しなければ `dotnet add package Nice3point.Revit.Api.RevitAPI --version 2025.4.0 --package-directory /tmp/nuget-check` 等で実在確認し、無ければ直近の2025.x.xに読み替える。
- **スコープ外(意図的)**: 複数Revitバージョン同時起動時のNamed Pipe分離、Revit 2025/2026実機での動作検証、README.md/README.ja.mdの更新。
