# RevitHarness

Revit 2024 用のハーネスアドイン。実行中の Revit インスタンスに対して、CLI やエージェントからテスト実行・コマンド実行・要素インスペクションを行える。DLL ホットスワップにより Revit 再起動なしでコードを更新可能。

[English README](README.md)

## 特徴

- **Named Pipe IPC** — CLI ⇄ Revit 間の高速な JSON ベース通信
- **Shadow Copy + Byte Load** — DLL をシャドウコピーしバイト配列でロード。ファイルロック回避とアセンブリキャッシュバイパスにより、同名 DLL の繰り返し更新が可能
- **Element Lookup** — RevitLookup ライクな要素インスペクションを CLI から実行
- **Addin Manager UI** — Revit リボンから DLL を選択し、コマンドを一覧表示・実行できる GUI
- **HarnessCommandBase** — 1 つのクラスで CLI（IHarnessCommand）とリボンボタン（IExternalCommand）の両方から実行可能

## インストール

### CLI ツール

```bash
dotnet tool install -g RevitHarness.Cli
```

### アドインの展開

```bash
# インストール
revit-harness install --revit-version 2024

# 再インストール（--force で上書き）
revit-harness install --revit-version 2024 --force

# 状態確認
revit-harness status

# アンインストール
revit-harness uninstall --revit-version 2024
```

> Revit を閉じた状態でインストール/アンインストールしてください。インストール後は Revit を再起動すると自動的にアドインがロードされます。

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

`IHarnessCommand` または `IExternalCommand` を実装した型を実行。

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
