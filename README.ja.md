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
revit-harness install --revit-version 2024 --vendor-id <ベンダーID>      # 下の「VendorId」を参照

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

### ダイアログ自動処理

アドインは `DialogBoxShowing` と `FailuresProcessing` を購読し、allowlist（`%LOCALAPPDATA%\RevitHarness\dialogs\allowlist.json`）に一致するダイアログに答えます。記録は同じフォルダの `dialogs_{date}.jsonl` です。ハーネス自身は AI を呼びません。未知のダイアログは pending として登録され、呼び出し側（AI エージェントまたは人）が決めます。

```bash
revit-harness dialog-log [--count 50]     # 直近の JSONL 記録
revit-harness reload-allowlist            # allowlist.json を再読込して検証
revit-harness dialog-pending [--all]      # 未決のダイアログ（id、title、buttons、bodySummary、screenshotPath など）
revit-harness dialog-decide --id <pendingId> --button "<ボタン名>" --by "<決めた者>" [--save-rule] [--timeout 10000]
revit-harness dialog-watcher start|stop|status
```

コマンドが固まる・タイムアウトするときは `dialog-pending` を確認し、`bodySummary` とスクリーンショットを読んでから一覧のボタンを押します。`dialog-decide` はウォッチャーが UI Automation でボタンを押して完了するのを待ちます（終了コード 0 解決、1 却下/stale/失敗、4 ウォッチャーが拾わなかった）。`--save-rule` は戻せるボタン（OK、Cancel、Close）に限り allowlist ルールも保存します。学習ルールはタイトル、AutomationId、ボタン集合、本文ハッシュで一致します。タイトルが「外部ツール」(External Tool(s)) のダイアログ（「見つからないアドイン アセンブリ」など）は、本文で「アドイン」の直後にある引用符で囲まれたアドイン名（ファイルパスとその他の引用文は除く）を `<addin>` に置き換えてからハッシュするため、1 回の判断で、パスと文言が同じ全アドインのダイアログが閉じます。それ以外の本文の違いは一致しません。以前の版で学習したルールは全文をハッシュしているため一致しなくなります。`allowlist.json` から削除して、もう一度判断してください。

ウォッチャー（`RevitHarness.DialogWatcher.exe`、.NET 10 Desktop Runtime が必要）は常駐せず、自動起動もしません。Revit 起動前に `dialog-watcher start`、終了後に `stop` します。Revit.exe と WerFault のトップレベル窓を UI Automation で監視し、既知のダイアログは allowlist のボタンを Invoke で押します（実マウスは使いません）。押してもダイアログが閉じなければ、戻せるボタンに限り次の走査で再度押します（1 つの窓につき最大 3 回）。それでも閉じなければ pending にします。詳細は英語版 README を参照してください。

### その他

```bash
revit-harness version   # バージョン表示（--version、-v も可）
revit-harness help      # ヘルプ表示（--help、-h も可）
```

## 終了コード

| コード | 意味 |
|--------|------|
| 0 | 成功 |
| 1 | 失敗（`ok=false` または `passed=false`） |
| 2 | `CONNECTION_FAILED`: パイプがない、または切れた（Revit 未起動、ハーネス未ロード） |
| 3 | `HARNESS_NOT_READY`: 接続できたが Revit の起動が終わっていない（アドインの OnStartup、保存済み対象の復元、`ApplicationInitialized`）。数秒後に再試行 |
| 4 | `TIMEOUT`: `--timeout` 内に応答がなく、保留ダイアログもない |
| 5 | `DIALOG_BLOCKED`: 応答がなく、保留ダイアログがある（stderr に ID）。`dialog-pending` → `dialog-decide` |

終了コード 2〜5 では stderr の 1 行目が `revit-harness: <TOKEN>: <message>` になり、stdout の JSON も `error.code` に同じトークンを持ちます。Revit がまだ開始していないリクエストは破棄され、遅れて実行されることはありません。すでに開始したもの（コマンド自身がダイアログを出した場合など）は最後まで実行されるので、再実行の前に結果を確認してください。

## 対象アドインのホットリロード

常駐する `IExternalApplication` と自前の補助 DLL を、Revit 再起動なし・対象アドインのソース変更なしで
更新できる。設計と、退けた代替案は [docs/hot-reload-redesign.md](docs/hot-reload-redesign.md)（英語）に書いてある。

Revit 2024 は net48、2025/2026 は net8.0-windows、2027 は net10.0-windows で、Revit の Addins フォルダ
**以外**の出力先にビルドし、ハーネス経由でだけロードする。

```bash
revit-harness reload --dll "C:\path\SampleTarget.dll" --type SampleTarget.App [--timeout <ms>]
revit-harness target-status
revit-harness unload [--timeout <ms>]
```

リロードは旧 `OnShutdown` → 旧世代の破棄 → 対象フォルダのコピー → 新世代の作成 → 新 `OnStartup`
の順。Shutdown 例外は警告として返して続行し、Startup 例外は失敗（`ok=false`）となり
`Unloaded` 状態に戻る。`target-status` は現在世代、DLL パス・型、ロード時刻、ランタイム、
ワーキングセット、作成済み世代数、生存中の旧 ALC 数（net48 は null）、リボンの割当を返す。
`status` は引き続きインストール状態の確認用。

**ファイルと `Assembly.Location`。** リロードのたびに、対象 DLL があるフォルダ全体（サブフォルダを含む全ファイル）を
`%LOCALAPPDATA%\RevitHarness\shadow\<timestamp>\` にコピーし、そのコピーからパス指定でロードする。
`Assembly.Location` はシャドウのパスになり、DLL の隣から読む設定・テンプレート・データもそこにあるので、
`Assembly.Location` を使う既存コードは無改造で動く。アドイン側が知っておくこと:
DLL の隣へ**書いた**ファイルはシャドウに入り、次のリロードで消える（永続する状態は `%APPDATA%` か
`%LOCALAPPDATA%` に置く）。サブフォルダのサテライトリソースアセンブリはコピーされるが束縛されない
（DLL に埋め込んだニュートラルリソースは動く）。

**参照。** 対象フォルダにある DLL のうち、Revit のインストールフォルダに同名のファイルがあるもの
（`RevitAPI`、`RevitAPIUI`、`RevitAPIIFC`、`AdWindows`、`UIFramework`、`Newtonsoft.Json` など）は Revit のものとして扱い、
対象側のコピーはロードしない（対象側のアセンブリバージョンが Revit のものより高い場合だけ、対象の持ち物になる）。CopyLocal ビルドで 2 つ目の `RevitAPI` ができることはない。それ以外の補助 DLL は
世代の持ち物。net8/net10 は世代ごとに収集可能な `AssemblyLoadContext`。net48 はアンロードできる
コンテキストがないので、補助 DLL を世代ごとに名前だけ変えて（Mono.Cecil）`Assembly.LoadFile` でパス指定ロードする。

**ビルドが出力する `.addin`。** ビルドが対象自身のマニフェストを Addins フォルダに書くと、Revit が起動時に
対象を自力でロードし、ハーネスが 2 つ目をロードしてしまう。退避する（リネームのみで何も削除しない。ビルドが
ファイルを再生成したら再実行する。効くのは次の Revit 起動から）:

```bash
revit-harness prepare-target --addin "C:\path\Target.addin"            # Target.addin -> Target.addin.parked
revit-harness prepare-target --addin "C:\path\Target.addin" --restore
```

**VendorId。** ベンダー ID で縛った Extensible Storage スキーマは、実行中アドインの `<VendorId>` と一致する必要が
あり、ホットロードしたコードはハーネスのアドインとして動く。インストール時に指定する:
`revit-harness install --revit-version 2024 --vendor-id <id>`。フラグなしの再インストールはインストール済みの
ID を保つ。`status` で確認できる。Revit はマニフェストを起動時に読むので、**VendorId を変えたら Revit を
再起動する**（install は元々 Revit を閉じて行う）。ID はコミットしない。対象自身の `.addin` も編集しない。

対象の `OnStartup` では Contracts のヘルパーでボタンを登録する。

```csharp
TargetRibbon.AddPushButton(panel, "Hello", "Hello", typeof(HelloCommand).FullName!);
```

同じパネル上の同名ボタンは既存ボタンを再利用し、表示文字と現在世代への割当を更新する。
Revit はリボン項目を削除できないため、ボタンは `RevitHarness.Addin.dll` の固定スタブ
`TargetSlot0`〜`TargetSlot7` を呼ぶ。異なるボタンは最大 8 個。unload 後もボタンは残るが、
対象コードは実行できなくなる。

ハーネスの `.addin` 自体が薄いローダーになる。成功した reload の DLL パスと型は
`%LOCALAPPDATA%\RevitHarness\target.json` に保存され、次回 Revit 起動時に復元されるので
リボンが起動時に作られる。復元失敗は Revit 起動を妨げない（復元が終わるまでリクエストは
`HARNESS_NOT_READY` になる）。`unload` は対象を停止してこのファイルを削除する。

IUpdater は `application.ActiveAddInId`（ハーネスが対象を動かしている間はハーネスのID）で登録してください。他のIDだとRevitが拒否します。コマンドがホストの Newtonsoft.Json に自分の型（匿名オブジェクトなど）を渡してシリアライズすると、その型がライブラリのキャッシュに残り、net8以降の世代が解放されなくなります。

対象の `OnShutdown` は updater 登録解除、イベント購読解除、`ExternalEvent` とタイマーの
Dispose、バックグラウンド処理の停止を行う必要がある。net8/net10 は参照が残ると世代を解放できない
（`previousReleased`、`released`、warnings を確認）。net48 は旧世代が Revit 終了までメモリに残り、
WPF の pack URI はアセンブリ名で解決されるため、対象の XAML リソースは最初の世代に束縛される。
ネイティブ DLL は非対応。
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
| `reload` | 対象 IExternalApplication の世代を置換 |
| `unload` | 対象世代を停止・破棄 |
| `targetStatus` | 対象の状態・メモリ・世代・リボン割当 |
| `lookupElement` | 要素インスペクション |
| `shutdown` | IPC サーバー停止 |
| `dialog-log` | 直近のダイアログ記録（パイプスレッドで応答。モーダル中も動作） |
| `reload-allowlist` | ダイアログ allowlist の検証（同上） |

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

Revit プロセスごとに `shadow\<pid>` フォルダを使い、次の起動時に終了済みプロセスのフォルダを削除する。各ディレクトリは対象の出力フォルダの完全なコピー。reload / unload のたびに、もう生存していない世代のディレクトリを削除する（新しい 3 つは残す）。net48 は全世代が Revit 終了までロードされたままなので、コピーも残る。

## ライセンス

MIT
