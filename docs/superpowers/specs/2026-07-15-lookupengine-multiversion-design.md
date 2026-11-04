# LookupEngine Revit 2025/2026対応 設計

## 1. 背景 / 目的

`docs/superpowers/specs/2026-07-15-revit-multi-version-support-design.md`(先行PR #8でマージ済み)により、`RevitHarness.Addin`/`RevitHarness.Contracts`/`RevitHarness.Cli` はRevit 2024/2025/2026すべてでビルド・インストール可能になった。ただし `RevitHarness.Lookup`(`lookupElement` IPC操作の実体)は明示的にスコープ外とされ、以下の状態で凍結されている:

- `lib/LookupEngine/LookupEngine.dll`・`LookupEngine.Abstractions.dll` は `.NETFramework,Version=v4.8` 専用バイナリ
- `RevitHarness.Lookup.csproj` は `net48` 固定、Configuration分岐なし
- `RevitHarness.Addin` 側は `LOOKUP_SUPPORTED` コンパイルシンボル(R24構成でのみ定義)で `HandleLookupElement` を分岐しており、R25/R26ビルドは `{ "ok": false, "error": { "code": "NOT_SUPPORTED", ... } }` を返す

本設計は、この凍結を解除し `lookupElement` をRevit 2024/2025/2026すべてで動作させるための変更範囲を定義する。

**由来の確認事項(調査で判明):** `lib/LookupEngine/*.dll` は LookupEngine単体のNuGetパッケージやGitHubリリースの成果物ではなく、**実際にインストールされたRevitLookup(公式アドイン)が同梱するDLLをそのまま抜き出したもの**(コミット `e87733c`: "Switch LookupEngine from submodule ProjectReference to direct Reference against RevitLookup's installed DLLs... This eliminates version conflicts between RevitHarness and RevitLookup at runtime")。`RevitHarness.Addin` は `Assembly.Load(byte[])` でRevitのAppDomainに直接ロードするため、同時にRevitLookup本体がインストールされている環境ではLookupEngineのバージョンを合わせておく必要がある、という設計判断に基づく。LookupEngine単体は現在 `lookup-foundation/LookupEngine` としてOSS化されているが、NuGet.orgには未公開でGitHub Releaseにもバイナリ成果物がないため、引き続き「対応するRevitLookupの実インストールからDLLを抜き出してvendorする」方式を踏襲する。

**確認済みの前提条件・スコープ:**
- 本機には Revit 2024・2026 が実インストール済みで、両方に公式RevitLookupアドインも導入済み。Revit 2025は本機に未インストール。
- 2026にインストール済みのRevitLookup(`LookupEngine.dll` アセンブリバージョン `2026.1.1.0`、net8.0)から抜き出したバイナリを、R25/R26で共用する(LookupEngineはRevit APIに一切依存しない純粋な.NETリフレクションエンジンのため、`RevitHarness.Contracts` のnet8.0-windows単一共用ビルドと同じ理屈が成立する)。
- 検証として、抜き出した2026版バイナリ(`2026.1.1.0`)を参照する使い捨てプロジェクトで `RevitHarness.Lookup` が使用するAPI表面(`Descriptor`/`ObjectDescriptor`/`DecomposeOptions`/`LookupComposer.Decompose`/`DecomposedMember`/`MemberAttributes`)がコンパイル可能なことを確認済み。既存net48版(`2024.3.1.0`)とアセンブリバージョンは異なるが、コンシューマー側コード(`ElementInspector.cs`・`Descriptors/*.cs`)の変更は不要。

## 2. アーキテクチャ

### 2.1 `lib/LookupEngine/` のTFM別サブフォルダ化

現状ルート直下にある `LookupEngine.dll`・`LookupEngine.Abstractions.dll`(net48)を `lib/LookupEngine/net48/` に移動し、新たに `lib/LookupEngine/net8.0-windows/` を追加して `C:\ProgramData\Autodesk\Revit\Addins\2026\RevitLookup\` から同名2ファイルを抜き出してvendorする。`LICENSE`(MIT)はルートに残し両TFMで共用する。

### 2.2 `RevitHarness.Lookup`: Addinと対称なConfiguration方式マルチターゲット

`RevitHarness.Lookup.csproj` を `RevitHarness.Addin.csproj` と同じ「Configuration名にRevit年度を埋め込む」方式に変更する:

- `Configurations` を `Debug R24;Release R24;Debug R25;Release R25;Debug R26;Release R26` に拡張
- `TargetFramework` をConfiguration名で条件分岐: `R24` → `net48`、`R25`/`R26` → `net8.0-windows`
- LookupEngineへの `<Reference>`(`HintPath`)をConfiguration条件で `lib/LookupEngine/net48/` または `lib/LookupEngine/net8.0-windows/` に切替
- `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` の `VersionOverride` 明示ピンを削除し、`Directory.Packages.props` の既存Configuration条件付き中央バージョン(R24→2024.3.40等)に委ねる(Addinと同じ扱いになり、今後のバージョン更新は一箇所で完結する)

### 2.3 `RevitHarness.Addin`: ゲート解除

- `LOOKUP_SUPPORTED` の `DefineConstants` PropertyGroup(R24構成限定で定義していたもの)を削除
- `RevitHarness.Lookup` への `ProjectReference` の `Condition="$(Configuration.EndsWith('R24'))"` を削除し、全構成で無条件参照にする
- `HarnessEventHandler.cs` の `HandleLookupElement` から `#if LOOKUP_SUPPORTED`/`#else`/`#endif` を除去し、常に `ElementInspector` を使う実装(現行R24分岐)に統一。`using RevitHarness.Lookup;` も無条件化
- `IpcMessages.cs` の `ErrorCodes.NotSupported`(このゲート専用に追加され他に参照箇所なし)を削除

### 2.4 ソリューションファイル

`revit-cli-harness.sln` の `RevitHarness.Lookup` プロジェクト(GUID `{D3E4F5A6-7B8C-9D0E-1F2A-3B4C5D6E7F8A}`)のビルド構成マッピングを、`RevitHarness.Addin` と同じパターンに変更する: 現状は `Debug R25|x64`/`Release R25|x64` 等がすべて plain `Debug|x64`/`Release|x64` にフォールバックしているが、これを `Debug R25|x64 → Debug R25|x64` のように対応するConfigurationへ直接マッピングする。

### 2.5 CI

`.github/workflows/publish.yml` は変更不要。既存の `dotnet build src/RevitHarness.Addin -c "Release R2x"` の各ステップが `ProjectReference` 経由で `RevitHarness.Lookup` を同名Configurationでビルドするため(MSBuildの標準的なConfiguration伝播)、Lookup用の追加ビルドステップは不要。

## 3. 影響範囲まとめ

| ファイル | 変更内容 |
|---|---|
| `lib/LookupEngine/net48/*.dll` | 既存ファイルを移動(git mv) |
| `lib/LookupEngine/net8.0-windows/*.dll` | 新規vendor(RevitLookup 2026インストールから抽出) |
| `src/RevitHarness.Lookup/RevitHarness.Lookup.csproj` | Configuration方式マルチターゲット化、Reference切替、VersionOverride削除 |
| `src/RevitHarness.Addin/RevitHarness.Addin.csproj` | `LOOKUP_SUPPORTED` 削除、ProjectReference無条件化 |
| `src/RevitHarness.Addin/Ipc/HarnessEventHandler.cs` | `#if` ゲート除去 |
| `src/RevitHarness.Addin/Ipc/IpcMessages.cs` | `ErrorCodes.NotSupported` 削除 |
| `revit-cli-harness.sln` | Lookupプロジェクトの構成マッピング修正 |
| `CLAUDE.md` | Lookupの対応範囲・vendoring由来とリフレッシュ手順の記述更新 |

## 4. 検証方法

- `dotnet build` が以下すべてで成功すること: `RevitHarness.Lookup`(R24/R25/R26個別)、`RevitHarness.Addin`(R24/R25/R26個別)、`revit-cli-harness.sln`(Debug/Release、および `Release R25`/`Release R26`)
- **実機検証**: 本機にはRevit 2024・2026とそれぞれ対応するRevitHarnessが実インストール済みのため、変更後に再インストールして両バージョンで `revit-harness lookup --id <elementId>` を実行し、`lookupElement` の応答を確認する
- Revit 2025は本機に未インストールのため実機検証はスコープ外(R25はR26と同一バイナリを使うため、R26での実機確認とビルド成功をもって間接的に担保する)

## 5. スコープ外

- LookupEngineをソースから自前ビルドすること(RevitLookup公式インストール済みバイナリの抽出で完結させる)
- Revit 2025実機での動作検証
- `ElementInspector`/`Descriptors` 以外の機能追加・LookupEngine APIバージョンアップに伴う挙動変更
