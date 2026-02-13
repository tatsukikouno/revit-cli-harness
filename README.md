# RevitHarness

Revit 2024用のハーネスアドインで、実行中のRevitインスタンス内でCLI/エージェントからテストや任意のコードを実行できます。DLLホットスワップをサポートし、Revit実行中でもコードの更新が可能です。

## 特徴

- **Named Pipe IPC**: CLI ⇄ Revit間の高速通信
- **Shadow Copy**: DLLをシャドウコピーしてファイルロックを回避
- **ホットスワップ**: Revit再起動なしでDLLを更新可能

## インストール

### CLIツールのインストール

```bash
dotnet tool install -g RevitHarness.Cli
```

### Revitへのアドイン展開

```bash
# Revit 2024にアドインをインストール
revit-harness install --revit-version 2024

# インストール状態を確認
revit-harness status

# アンインストール
revit-harness uninstall --revit-version 2024
```

## CLI使用方法

```bash
# Revitとの接続確認
revit-harness ping

# テスト実行
revit-harness run-tests --dll "path/to/tests.dll" --type "Company.Tests.EntryPoint"

# コマンド実行
revit-harness run-command --dll "path/to/command.dll" --type "Company.MyCommand"

# 登録済みRevitコマンドの実行
revit-harness post-command --id "CustomCtrl_%CustomCtrl_%Add-Ins%Addin%Command"

# IPCサーバー停止（Revitは継続）
revit-harness shutdown
```

## テスト/コマンドの実装

### Contractsパッケージのインストール

```bash
dotnet add package RevitHarness.Contracts
```

### ITestEntryPointの実装

```csharp
using RevitHarness.Contracts;
using Autodesk.Revit.UI;
using Newtonsoft.Json;

public class MyTestEntryPoint : ITestEntryPoint
{
    public string Run(UIApplication uiapp, IDictionary<string, object> args)
    {
        // テストロジック
        var doc = uiapp.ActiveUIDocument.Document;

        return JsonConvert.SerializeObject(new {
            passed = true,
            summary = "All tests passed"
        });
    }
}
```

### IHarnessCommandの実装

```csharp
using RevitHarness.Contracts;
using Autodesk.Revit.UI;
using Newtonsoft.Json;

public class MyCommand : IHarnessCommand
{
    public string Execute(UIApplication uiapp)
    {
        // コマンドロジック
        return JsonConvert.SerializeObject(new {
            result = "Command completed"
        });
    }
}
```

## 終了コード

| コード | 意味 |
|--------|------|
| 0 | 成功 |
| 1 | 失敗（ok=false または passed=false） |
| 2 | 接続/インフラエラー |

## 開発者向け

### ビルド

```bash
# ソリューション全体をビルド
dotnet build -c Release

# NuGetパッケージ生成
dotnet pack -c Release
```

### プロジェクト構成

```
revit-cli-harness/
├── src/
│   ├── RevitHarness.Contracts/   # 共有インターフェース (NuGet: RevitHarness.Contracts)
│   ├── RevitHarness.Addin/       # Revitアドイン
│   └── RevitHarness.Cli/         # CLIツール (NuGet: RevitHarness.Cli)
├── Directory.Build.props         # 共通ビルド設定
└── Directory.Packages.props      # パッケージバージョン管理
```

### ログ

ログは以下に出力されます: `%LOCALAPPDATA%\RevitHarness\logs\harness_{date}.log`

## ライセンス

MIT
