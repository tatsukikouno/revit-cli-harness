# RevitHarness

[![NuGet RevitHarness.Cli](https://img.shields.io/nuget/v/RevitHarness.Cli.svg?label=RevitHarness.Cli)](https://www.nuget.org/packages/RevitHarness.Cli/)
[![NuGet RevitHarness.Contracts](https://img.shields.io/nuget/v/RevitHarness.Contracts.svg?label=RevitHarness.Contracts)](https://www.nuget.org/packages/RevitHarness.Contracts/)

Revit 2024 / 2025 / 2026 / 2027 用のハーネスアドイン。実行中の Revit インスタンスに対して、CLI やエージェントからテスト実行・コマンド実行・要素インスペクションを行える。DLL ホットスワップにより Revit 再起動なしでコードを更新可能。

[English README](README.md)

## 特徴

- **Named Pipe IPC** — CLI ⇄ Revit 間の高速な JSON ベース通信
- **世代ロード** — DLL をシャドウコピーし、新しい世代でロード。net48 は byte load、net8/net10 は収集可能 ALC を使い、同名 DLL と自前の補助 DLL を繰り返し更新可能
- **Element Lookup** — RevitLookup ライクな要素インスペクションを CLI から実行
- **Addin Manager UI** — Revit リボンから DLL を選択し、コマンドを一覧表示・実行できる GUI
- **HarnessCommandBase** — 1 つのクラスで CLI（IHarnessCommand）とリボンボタン（IExternalCommand）の両方から実行可能

## インストール

### CLI ツール

.NET 10 SDK またはランタイムが必要。

```bash
dotnet tool install -g RevitHarness.Cli
```

### アドインの展開

```bash
# インストール: アドイン、ダイアログウォッチャー、SKILL を展開（再実行で上書き）
revit-harness install --revit-version 2024   # 2024 / 2025 / 2026 / 2027
revit-harness install --revit-version 2024 --skills-dir D:\my-skills   # SKILL の展開先を変更

# 状態確認
revit-harness status

# アンインストール: アドインを削除。最後のバージョンを消すとウォッチャーと SKILL も削除
revit-harness uninstall --revit-version 2024
```

> Revit を閉じた状態でインストール/アンインストールしてください。インストール後は Revit を再起動すると自動的にアドインがロードされます。アンインストールしても `%LOCALAPPDATA%\RevitHarness\dialogs`（allowlist・ログ・pending）は残ります。ウォッチャーは自動起動しません。Revit を起動する前に `revit-harness dialog-watcher start` で起動し、終わったら `stop` してください。

### インストール先

```
%APPDATA%\Autodesk\Revit\Addins\2024\
├── RevitHarness.addin              # マニフェスト
└── RevitHarness\
    ├── RevitHarness.Addin.dll
    ├── RevitHarness.Contracts.dll
    ├── RevitHarness.Lookup.dll
    ├── LookupEngine.dll
    ├── Newtonsoft.Json.dll
    └── ...
%LOCALAPPDATA%\RevitHarness\watcher\       # ダイアログウォッチャー
%USERPROFILE%\.claude\skills\<skill>\      # SKILL（--skills-dir で変更可）
```

## CLI コマンド

### ping — 接続確認

```bash
revit-harness ping
```

Revit のバージョン、プロセス ID、アクティブドキュメント名を返す。

### run-tests — テスト実行

```bash
revit-harness run-tests --dll "path/to/tests.dll" --type "Company.Tests.EntryPoint" [--filter "Smoke"] [--timeout 300000]
```

指定 DLL から `ITestEntryPoint` 実装を実行。結果 JSON の `passed` フィールドで終了コードが決まる。

### run-command — コマンド実行

```bash
revit-harness run-command --dll "path/to/command.dll" --type "Company.MyCommand" [--args '{"key":"value"}'] [--timeout 300000]
```

`IHarnessCommand` または `IExternalCommand` を実装した型を実行。`IExternalCommand` には実物の `ExternalCommandData` が渡される（`commandData.Application` は実行中の `UIApplication`、`View` はアクティブドキュメントのアクティブビュー。ドキュメントが無ければ `null`）。`samples/SampleCommands` を参照。

### post-command — 登録済み Revit コマンドの実行

```bash
revit-harness post-command --id "CustomCtrl_%CustomCtrl_%Add-Ins%Addin%Command"
```

Revit に登録されたリボンコマンドを ID 指定で実行。

### lookup — 要素インスペクション

```bash
revit-harness lookup --id 12345 [--depth 1] [--max-members 200] [--include-fields] [--include-private] [--include-static] [--filter "Name"]
```

RevitLookup ライクな要素検査を CLI から実行。プロパティ・メソッドの戻り値を JSON で出力。

### shutdown — IPC サーバー停止

```bash
revit-harness shutdown
```

IPC サーバーを停止。Revit 本体は継続動作。

### その他

```bash
revit-harness version   # バージョン表示
revit-harness help      # ヘルプ表示
```

## 終了コード

| コード | 意味 |
|--------|------|
| 0 | 成功 |
| 1 | 失敗（`ok=false` または `passed=false`） |
| 2 | 接続/インフラエラー |

## 対象アドインのホットリロード

常駐する `IExternalApplication` と自前の補助 DLL を Revit 再起動なしで更新できる。
Revit 2024 は net48、2025/2026 は net8.0-windows、2027 は net10.0-windows でビルドし、
補助 DLL は本体の隣に配置する。Revit API 参照は `Private=False` にする。
二重ロードを避けるため、対象自身の `.addin` は外し、ハーネス経由でロードする。

```bash
revit-harness reload --dll "C:\path\SampleTarget.dll" --type SampleTarget.App [--timeout <ms>]
revit-harness target-status
revit-harness unload [--timeout <ms>]
```

リロードは旧 `OnShutdown` → 旧世代の破棄 → シャドウコピー → 新世代の作成 → 新 `OnStartup`
の順。Shutdown 例外は警告として返して続行し、Startup 例外は失敗（`ok=false`）となり
`Unloaded` 状態に戻る。`target-status` は現在世代、DLL パス・型、ロード時刻、ランタイム、
ワーキングセット、作成済み世代数、生存中の旧 ALC 数（net48 は null）、リボンの割当を返す。
`status` は引き続きインストール状態の確認用。

対象の `OnStartup` では Contracts のヘルパーでボタンを登録する。

```csharp
TargetRibbon.AddPushButton(panel, "Hello", "Hello", typeof(HelloCommand).FullName!);
```

同じ `(panel.Name, name)` は既存ボタンを再利用し、表示文字と現在世代への割当を更新する。
Revit はリボン項目を削除できないため、ボタンは `RevitHarness.Addin.dll` の固定スタブ
`TargetSlot0`〜`TargetSlot7` を呼ぶ。異なるボタンは最大 8 個。unload 後もボタンは残るが、
対象コードは実行できなくなる。

ハーネスの `.addin` 自体が薄いローダーになる。成功した reload の DLL パスと型は
`%LOCALAPPDATA%\RevitHarness\target.json` に保存され、次回 Revit 起動時に復元されるので
リボンが起動時に作られる。復元失敗は Revit 起動を妨げない。`unload` は対象を停止して
このファイルを削除する。

IUpdater は `application.ActiveAddInId`（ハーネスが対象を動かしている間はハーネスのID）で登録してください。他のIDだとRevitが拒否します。net48 では .NET Framework のバインダが同じ単純名の読み込み済みアセンブリを再利用するため、ハーネスは対象の自前DLLを世代ごとに名前だけ変えて（Mono.Cecil）読み込み、再ビルドした補助DLLが使われるようにしています。コマンドがホストの Newtonsoft.Json に自分の型（匿名オブジェクトなど）を渡してシリアライズすると、その型がライブラリのキャッシュに残り、net8以降の世代が解放されなくなります。

対象の `OnShutdown` は updater 登録解除、イベント購読解除、`ExternalEvent` とタイマーの
Dispose、バックグラウンド処理の停止を行う必要がある。net8/net10 は世代ごとに新しい
収集可能 AssemblyLoadContext を使うが、参照が残ると解放できない（`previousReleased`、
`released`、warnings を確認）。net48 は毎回新しい世代を byte load し、旧世代は Revit 終了まで
メモリに残る。ネイティブ DLL は非対応。byte load したアセンブリの `Assembly.Location` は空に
なるため、これを使ったファイル探索に依存しないこと。
ビルド方法と世代・補助 DLL・イベント・updater・リボンの検証例は
[SampleTarget](samples/SampleTarget/README.md) を参照。

## Addin Manager UI

Revit 起動後、リボンの **RevitHarness** タブ → **Addin Manager** ボタンからアクセス。

1. **Browse** で DLL を選択
2. **Scan** で `IHarnessCommand` / `IExternalCommand` 実装型を一覧表示
3. 型を選択して **Execute** で実行
4. 結果が下部のテキストエリアに表示される

## テスト/コマンドの実装

### Contracts パッケージ

```bash
dotnet add package RevitHarness.Contracts
```

### ITestEntryPoint — テスト用

```csharp
using RevitHarness.Contracts;

public class MyTestEntryPoint : ITestEntryPoint
{
    public string Run(UIApplication uiapp, IDictionary<string, object> args)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        // テストロジック
        return JsonConvert.SerializeObject(new { passed = true, summary = "All tests passed" });
    }
}
```

### IHarnessCommand — コマンド用（CLI 専用）

```csharp
using RevitHarness.Contracts;

public class MyCommand : IHarnessCommand
{
    public string Execute(UIApplication uiapp, IDictionary<string, object> args)
    {
        // コマンドロジック
        return JsonConvert.SerializeObject(new { result = "done" });
    }
}
```

### HarnessCommandBase — CLI + リボン両対応

CLI（`revit-harness run-command`）からも Revit リボンボタン（`IExternalCommand`）からも実行できるコマンドを作成する場合に使用。

```csharp
using RevitHarness.Contracts;

[Transaction(TransactionMode.Manual)]
public class MyDualCommand : HarnessCommandBase
{
    public override string Execute(UIApplication uiapp, IDictionary<string, object> args)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        // CLI からもリボンからも動くロジック
        return JsonConvert.SerializeObject(new { result = "done" });
    }
}
```

## IPC プロトコル

Named Pipe `\\.\pipe\RevitHarness2024` 上で JSON メッセージを length-prefix framing（int32 LE）で送受信。

| オペレーション | 説明 |
|---------------|------|
| `ping` | ヘルスチェック |
| `runTests` | `ITestEntryPoint` 実行 |
| `runCommand` | `IHarnessCommand` / `IExternalCommand` 実行 |
| `postCommand` | 登録済み Revit コマンド実行 |
| `reload` | 対象 IExternalApplication の世代を置換 |
| `unload` | 対象世代を停止・破棄 |
| `targetStatus` | 対象の状態・メモリ・世代・リボン割当 |
| `lookupElement` | 要素インスペクション |
| `shutdown` | IPC サーバー停止 |

## 開発者向け

### ビルド

```bash
# ソリューション全体
dotnet build revit-cli-harness.sln -c Debug

# NuGet パッケージ生成
dotnet pack src/RevitHarness.Contracts -c Release
dotnet pack src/RevitHarness.Cli -c Release
```

### プロジェクト構成

```
revit-cli-harness/
├── src/
│   ├── RevitHarness.Contracts/     # 共有インターフェース (NuGet: RevitHarness.Contracts)
│   │   ├── ITestEntryPoint.cs
│   │   ├── IHarnessCommand.cs
│   │   └── HarnessCommandBase.cs
│   ├── RevitHarness.Addin/         # Revit アドイン
│   │   ├── App.cs                  # IExternalApplication + リボン作成
│   │   ├── Commands/
│   │   │   ├── CommandExecutor.cs          # ShadowCopy + リフレクション実行エンジン
│   │   │   ├── AssemblyScanner.cs          # DLL 内コマンド型検出
│   │   │   └── OpenAddinManagerCommand.cs  # リボンボタン用 IExternalCommand
│   │   ├── Ipc/
│   │   │   ├── IpcMessages.cs
│   │   │   ├── IpcServer.cs
│   │   │   ├── HarnessEventHandler.cs
│   │   │   └── ShadowCopier.cs
│   │   └── UI/
│   │       ├── AddinManagerWindow.xaml
│   │       └── AddinManagerWindow.xaml.cs
│   ├── RevitHarness.Lookup/        # 要素インスペクション
│   └── RevitHarness.Cli/           # CLI ツール (NuGet: RevitHarness.Cli)
├── lib/LookupEngine/               # LookupEngine ライブラリ
├── Directory.Build.props            # 共通ビルド設定・バージョン管理
└── Directory.Packages.props         # パッケージバージョン管理
```

### ログ

```
%LOCALAPPDATA%\RevitHarness\logs\harness_{date}.log
```

### Shadow Copy ディレクトリ

```
%LOCALAPPDATA%\RevitHarness\shadow\{timestamp}\
```

古いディレクトリは自動クリーンアップされる（最大 10 世代、1 日以上経過したものを削除）。

## ライセンス

MIT
