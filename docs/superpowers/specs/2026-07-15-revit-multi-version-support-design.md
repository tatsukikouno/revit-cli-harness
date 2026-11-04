# Revit 2024/2025/2026 マルチバージョン対応 設計

## 1. 背景 / 目的

現状、本リポジトリはRevit 2024専用に構築されている:

- `Directory.Packages.props` で `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` が `2024.3.40` に固定
- `RevitHarness.Addin`/`RevitHarness.Lookup`/`RevitHarness.Contracts` は `net48` 固定
- `RevitHarness.Addin.csproj` のDebug起動設定 (`StartProgram`) が `C:\Program Files\Autodesk\Revit 2024\Revit.exe` 固定

Revit 2025以降は `net8.0-windows` が必須となり、Nice3pointパッケージも年度ごとに別バージョンが公開されている。本設計は、Revit 2024/2025/2026の3バージョンすべてに対応させるための変更範囲を定義する。

**確認済みの前提条件・スコープ:**
- 2024/2025/2026すべてを維持する(2024の廃止は行わない)
- Named Pipe名は現状の単一名を維持する(複数Revitバージョンの同時起動・同時接続はサポートしない)
- 検証は「`dotnet build`/`dotnet pack` が3バージョン分すべてで通ること」までとする。実機でのRevit 2025/2026起動・install/ping等の動作確認はスコープ外(実行環境に2025/2026がインストールされているか未確認のため)

## 2. アーキテクチャ

### 2.1 Addin / Lookup: Configuration方式のマルチターゲット

`RevitHarness.Addin`・`RevitHarness.Lookup` は、Nice3pointエコシステムの標準パターンに倣い、**Configuration名にRevit年度を埋め込む方式**を採用する。単純な `<TargetFrameworks>` 多重ターゲットでは2025と2026を区別できない(どちらも `net8.0-windows` だが参照するAPIパッケージバージョンが異なる)ため、この方式が必要になる。

- `Configurations` を `Debug R24;Release R24;Debug R25;Release R25;Debug R26;Release R26` に拡張
- `TargetFramework` をConfiguration名で条件分岐: `R24` → `net48`、`R25`/`R26` → `net8.0-windows`
- `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` のバージョンをConfiguration名で条件分岐(2024→`2024.3.40`、2025→`2025.4.0`、2026→`2026.4.0`。NuGet Gallery上での確認時点(2026-07-15)の値であり、実装時に最新パッチが出ていれば追随してよい)
- `RevitHarness.Addin.csproj` の `StartProgram`(Debug起動パス)もConfiguration名で条件分岐し、`Revit 2024\Revit.exe`/`Revit 2025\Revit.exe`/`Revit 2026\Revit.exe` を切り替える

### 2.2 Contracts: TargetFrameworks方式のマルチターゲット

`RevitHarness.Contracts` はNuGet.orgに公開される外部消費用パッケージであるため、Addin/Lookupとは異なる方式を取る。Configuration切り替えは「このソリューション内でのビルド選択」であり、外部の消費者(コマンド実装者)が自分のプロジェクトTFMに応じて自動解決できる形にはならないため、**実際の `<TargetFrameworks>net48;net8.0-windows</TargetFrameworks>` 多重ターゲット**を使う。

- `net48` ビルド: `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` を2024年度版に固定
- `net8.0-windows` ビルド: 2025/2026共用の単一ビルドとする(`IExternalCommand`・`UIApplication`など`Contracts`が使用する基本インターフェースは年度間で破壊的変更がない前提)。参照パッケージバージョンは新しい方(`2026.4.0`)に固定する — Nice3pointパッケージは年度が進んでも既存メンバーの削除は基本的に行われないため、新しい方を基準にする方が2027以降への追随もしやすい

この結果、`dotnet pack` で生成されるNuGetパッケージは `lib/net48/` と `lib/net8.0-windows/` の2系統を持ち、消費者側プロジェクトのTFMに応じてNuGetが自動選択する。

### 2.3 CLI / インストーラー

`RevitHarness.Cli.csproj` は現在、単一Configuration分のAddinビルド成果物のみを `EmbeddedResource` として埋め込んでいる。これを3構成(R24/R25/R26)分すべて埋め込むように拡張する。

- 各バージョンのビルド出力(`bin\x64\{Configuration} R2x\*.dll`)を、バージョンを識別できる `LogicalName` プレフィックス付きで埋め込む
- `RevitInstaller.Install(revitVersion)` は、指定された `revitVersion` に対応するリソース群を選んでインストール先(`%APPDATA%\Autodesk\Revit\Addins\{revitVersion}\`)に展開する
- 2024/2025/2026以外の未対応バージョンが指定された場合、CLIは「サポート対象は2024/2025/2026」である旨を明示するエラーを返す(現状の `GetInstalledRevitVersions()` が2019-2030の任意年度を許容している挙動と整合を取る形で、インストール時のみ対応バージョンを制限する)

### 2.4 Named Pipe

現状の `IpcServer.cs`/`IpcClient.cs` にハードコードされた `"RevitHarness2024"` 定数は、値・挙動とも変更しない(複数バージョン同時起動はサポート対象外のため)。定数名のリネームなど表記上の整理は実装時の任意判断とする。

### 2.5 ソリューション / ビルドコマンド

- `revit-cli-harness.sln` に `Debug R24`/`Release R24`/`Debug R25`/`Release R25`/`Debug R26`/`Release R26` の6構成を追加する(CLIプロジェクト自体は引き続き `Debug`/`Release` の2構成のみを持ち、ソリューション構成マッピングで吸収する)
- `CLAUDE.md` の Build Commands セクションを更新し、Addinを3構成分明示的にビルドしてからCLIをビルド/packする手順を記載する(例: `dotnet build src/RevitHarness.Addin -c "Release R24"` を3回実行後、CLIをビルド)

## 3. 影響範囲まとめ

| ファイル | 変更内容 |
|---|---|
| `Directory.Packages.props` | Nice3point APIパッケージのバージョンをConfiguration/TFM条件で分岐 |
| `src/RevitHarness.Addin/RevitHarness.Addin.csproj` | Configurations拡張、TargetFramework条件分岐、StartProgram条件分岐 |
| `src/RevitHarness.Lookup/RevitHarness.Lookup.csproj` | Configurations拡張、TargetFramework条件分岐 |
| `src/RevitHarness.Contracts/RevitHarness.Contracts.csproj` | `TargetFrameworks` 多重ターゲット化 |
| `src/RevitHarness.Cli/RevitHarness.Cli.csproj` | 3構成分のEmbeddedResource追加 |
| `src/RevitHarness.Cli/Installation/RevitInstaller.cs` | バージョン別リソース選択ロジック、未対応バージョンのエラーハンドリング |
| `revit-cli-harness.sln` | 6構成追加 |
| `CLAUDE.md` | Build Commandsセクション更新 |

## 4. 検証方法

- `dotnet build`(または `dotnet pack`)が以下の組み合わせすべてで成功することを確認する:
  - Addin/Lookup: `Release R24`(net48) / `Release R25`(net8.0-windows) / `Release R26`(net8.0-windows)
  - Contracts: `net48` / `net8.0-windows` の両TFM
  - CLI: 3構成分のAddin成果物を埋め込んだ状態でのビルド・pack
- 実機でのRevit起動・install/ping/run-tests等の動作確認はスコープ外(2025/2026がローカル環境にインストールされているか未確認のため、ユーザー側で後日実施)

## 5. スコープ外

- Named Pipeのバージョン別分離(複数Revitバージョン同時起動時の同時接続)
- Revit 2025/2026実機での動作検証
- 2024サポートの廃止
