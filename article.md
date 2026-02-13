---
title: "AIのためにRevitアドインをCLIから実行できるツールを作った"
emoji: "🏗️"
type: "tech"
topics: ["revit", "csharp", "cli", "ai", "bim"]
published: false
---

## はじめに

建築業界で広く使われているBIMソフト **Autodesk Revit**。その自動化にはアドイン(C#プラグイン)の開発が不可欠ですが、Revit APIには大きな制約があります。

**すべてのAPI呼び出しはRevitのメインスレッドで実行しなければならない。**

つまり、外部プロセスからRevitを直接操作することはできません。AIエージェント（Claude Code、Cursor、GitHub Copilotなど）がRevitのモデルデータを取得したり、コマンドを実行したりしたくても、GUIを手動で操作するしかない――そんな状況でした。

この問題を解決するために、**RevitHarness** というOSSツールを作りました。

https://github.com/tatsukikouno/revit-cli-harness

## RevitHarnessとは

RevitHarnessは、**起動中のRevitインスタンスに対してCLIからコマンドやテストを実行できるハーネスツール**です。

```
┌─────────────┐    Named Pipe     ┌──────────────────┐
│  CLI / AI   │ ◄──────────────► │  Revit + Addin   │
│  エージェント │    JSON IPC      │  (メインスレッド)  │
└─────────────┘                   └──────────────────┘
```

主な特徴:

- **CLIから任意のC#コードをRevit内で実行**できる
- **DLLホットスワップ対応** — Revit再起動なしでコードを差し替え可能
- **AIエージェントとの統合**を想定した設計（JSON入出力、終了コード）
- `dotnet tool` としてインストールするだけで使える

## なぜ作ったのか

### Revit API の壁

Revit APIには「スレッド親和性」という厳しい制約があります。

```
❌ 外部プロセスから直接: RevitAPI.dll を参照しても動かない
❌ 別スレッドから: InvalidOperationException が発生
✅ ExternalEvent経由: Revitのアイドリング時にメインスレッドで実行
```

通常のアドイン開発ではこれで問題ありませんが、**AIエージェントが自律的にRevitを操作したい場合**、この制約が大きな壁になります。

### AI時代の開発フロー

現代のAIコーディングエージェントは、ターミナルでコマンドを実行し、その結果を読んで次のアクションを決定します。

```bash
# AIエージェントの典型的なワークフロー
$ dotnet build           # ビルド
$ dotnet test            # テスト実行
$ revit-harness ping     # Revitに接続確認  ← ここが必要だった
$ revit-harness run-command --dll ./bin/MyCommand.dll --type MyNamespace.MyCommand
```

このワークフローにRevit操作を組み込むために、CLIツールが必要でした。

## アーキテクチャ

### 全体構成

```
revit-cli-harness/
├── RevitHarness.Contracts   # 共有インターフェース (NuGetパッケージ)
├── RevitHarness.Addin       # Revitアドイン (IPC サーバー)
└── RevitHarness.Cli         # CLIツール (dotnet tool)
```

### 3つの技術的ポイント

#### 1. Named Pipe による IPC 通信

CLIとRevit間の通信には **Named Pipe** (`\\.\pipe\RevitHarness2024`) を使用しています。

```
CLI (クライアント)              Revit (サーバー)
     │                              │
     │  ── [4byte長さ][JSON] ──►    │  リクエスト受信
     │                              │  キューに格納
     │                              │  ExternalEvent.Raise()
     │                              │  メインスレッドで実行
     │  ◄── [4byte長さ][JSON] ──    │  レスポンス送信
     │                              │
```

プロトコルはシンプルな **長さプレフィックス + JSON** 方式です。

```csharp
// メッセージの書き込み
var json = JsonConvert.SerializeObject(request);
var messageBytes = Encoding.UTF8.GetBytes(json);
var lengthBytes = BitConverter.GetBytes(messageBytes.Length); // int32 LE

await stream.WriteAsync(lengthBytes, 0, 4, ct);
await stream.WriteAsync(messageBytes, 0, messageBytes.Length, ct);
```

Named Pipeを選んだ理由:
- 同一マシン内の通信に最適化されている
- TCPと異なりポート競合がない
- Windowsのセキュリティモデルと自然に統合できる

#### 2. ExternalEvent によるスレッド安全性

Revit APIのスレッド制約を解決するため、**ExternalEvent パターン**を使っています。

```csharp
public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        _server = new IpcServer(Log);
        _eventHandler = new HarnessEventHandler(_server, Log);
        _externalEvent = ExternalEvent.Create(_eventHandler);

        _server.Start();

        // 100msごとにキューをチェックし、リクエストがあればRaise
        _processingTimer = new Timer(
            _ => {
                if (_server.PendingRequestCount > 0)
                    _externalEvent.Raise();
            },
            null,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100));

        return Result.Succeeded;
    }
}
```

処理の流れ:

1. IPCサーバーがリクエストを受信し、`ConcurrentQueue`に格納
2. 100msポーリングタイマーがキューを監視
3. リクエストがあれば `ExternalEvent.Raise()` を呼び出し
4. Revitのアイドリング時に `HarnessEventHandler.Execute()` がメインスレッドで実行
5. 結果をNamed Pipe経由でCLIに返却

```csharp
public class HarnessEventHandler : IExternalEventHandler
{
    public void Execute(UIApplication app)
    {
        // ここはRevitのメインスレッド上 → API呼び出しが安全
        while (_server.TryDequeueRequest(out var item))
        {
            var (request, callback) = item;
            var response = ProcessRequest(request);
            callback(response);  // Named Pipe経由で返却
        }
    }
}
```

#### 3. Shadow Copy による DLL ホットスワップ

Revitがアドインを読み込むと、DLLファイルがロックされます。開発中に何度もビルド→実行を繰り返すためには、この問題を回避する必要があります。

RevitHarnessでは **Shadow Copy + バイト配列ロード** の2段階で解決しています。

```csharp
public class ShadowCopier
{
    // Step 1: タイムスタンプ付きディレクトリにコピー
    public string CopyToShadow(string assemblyPath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var shadowDir = Path.Combine(_shadowRoot, timestamp);
        Directory.CreateDirectory(shadowDir);

        // 本体DLL + 依存DLL + PDB + config をすべてコピー
        File.Copy(assemblyPath, Path.Combine(shadowDir, fileName));
        // ...
        return shadowAssemblyPath;
    }

    // Step 2: バイト配列からロード（.NETのアセンブリキャッシュを回避）
    public (Assembly, ResolveEventHandler) LoadAssemblyFromBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var assembly = Assembly.Load(bytes);  // ファイルロックなし！
        // ...
    }
}
```

ポイント:
- `Assembly.LoadFrom()` ではなく `Assembly.Load(byte[])` を使用
- 同名・同バージョンのDLLでも毎回新しくロードできる
- **Revitを再起動せずにコードを修正→再実行が可能**

## 使い方

### インストール

```bash
# CLIツールをインストール
dotnet tool install -g RevitHarness.Cli

# Revitにアドインを配置
revit-harness install --revit-version 2024
```

これだけで、Revit起動時に自動でIPCサーバーが立ち上がります。

### 基本操作

```bash
# Revitとの接続確認
$ revit-harness ping
{
  "id": "...",
  "ok": true,
  "result": {
    "revitVersion": "2024",
    "processId": 12345,
    "activeDocumentTitle": "MyProject.rvt"
  }
}

# コマンド実行
$ revit-harness run-command \
    --dll "./bin/MyCommand.dll" \
    --type "MyNamespace.MyCommand"

# テスト実行
$ revit-harness run-tests \
    --dll "./bin/MyTests.dll" \
    --type "MyNamespace.TestEntryPoint" \
    --filter "Smoke"
```

### 自分のコマンドを書く

NuGetパッケージ `RevitHarness.Contracts` を参照して、インターフェースを実装するだけです。

```bash
dotnet add package RevitHarness.Contracts
```

```csharp
using RevitHarness.Contracts;
using Autodesk.Revit.UI;

public class WallCounter : IHarnessCommand
{
    public string Execute(UIApplication uiapp, IDictionary<string, object> args)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        var walls = new FilteredElementCollector(doc)
            .OfClass(typeof(Wall))
            .ToElements();

        return JsonConvert.SerializeObject(new
        {
            count = walls.Count,
            message = $"プロジェクト内の壁: {walls.Count}個"
        });
    }
}
```

```bash
$ dotnet build
$ revit-harness run-command --dll "./bin/WallCounter.dll" --type "WallCounter"
{
  "ok": true,
  "result": {
    "count": 142,
    "message": "プロジェクト内の壁: 142個"
  }
}
```

## AIエージェントとの連携例

### Claude Code + Revit MCP

RevitHarnessは、MCP (Model Context Protocol) サーバーと組み合わせることで、AIエージェントがRevitモデルを自然言語で操作できる基盤になります。

```
ユーザー: 「1階の壁をすべて赤色にして」

Claude Code:
  1. revit-harness run-command → 壁の一覧を取得
  2. フィルタリング（1階のみ）
  3. revit-harness run-command → 色を変更するコマンドを実行
  4. 「1階の壁42枚を赤色に変更しました」
```

### CI/CD パイプラインでの活用

Revitが起動しているマシンに対して、CIから自動テストを投入できます。

```yaml
# GitHub Actions (self-hosted runner + Revit)
steps:
  - run: dotnet build
  - run: revit-harness ping
  - run: revit-harness run-tests --dll ./bin/Tests.dll --type Tests.EntryPoint
```

## IPCプロトコル仕様

他のツールからRevitHarnessと通信したい場合のプロトコル仕様です。

| 項目 | 値 |
|---|---|
| トランスポート | Named Pipe (`\\.\pipe\RevitHarness2024`) |
| フレーミング | 長さプレフィックス (int32 LE) + JSON (UTF-8) |
| 最大メッセージサイズ | 10MB |

### オペレーション一覧

| Op | 説明 | ペイロード |
|---|---|---|
| `ping` | 疎通確認 | なし |
| `runTests` | テスト実行 | `assemblyPath`, `entryType`, `args` |
| `runCommand` | コマンド実行 | `assemblyPath`, `commandType`, `args` |
| `postCommand` | 登録済みRevitコマンド実行 | `commandId` |
| `shutdown` | IPCサーバー停止 | なし |

### 終了コード

| コード | 意味 |
|---|---|
| `0` | 成功 |
| `1` | 失敗（テスト不合格含む） |
| `2` | 接続エラー / タイムアウト |

## 技術的な課題と解決策

### アセンブリの再読み込み問題

.NET Frameworkでは、一度AppDomainに読み込んだアセンブリをアンロードする方法がありません。同名・同バージョンのアセンブリを再読み込みしようとすると、キャッシュから古いものが返されます。

**解決策**: `Assembly.Load(byte[])` を使うことで、アセンブリIDキャッシュを完全にバイパスしています。ファイルパスに紐づかないため、毎回新しいアセンブリとして読み込まれます。

### 依存関係の解決

バイト配列から読み込んだアセンブリは、依存DLLの自動解決が効きません。

**解決策**: `AppDomain.CurrentDomain.AssemblyResolve` イベントにカスタムリゾルバーを登録し、Shadow Copyディレクトリから依存DLLを動的に解決しています。リクエスト処理後はメモリリーク防止のためリゾルバーを解除します。

### Revitのスレッドモデル

Revit APIはCOMベースで、メインスレッド以外からの呼び出しを一切許容しません。

**解決策**: Named Pipeのリクエスト受信は別スレッドで行い、実際のAPI呼び出しは`ExternalEvent`経由でメインスレッドにマーシャリングしています。100msのポーリング間隔で応答性とCPU使用率のバランスを取っています。

## まとめ

RevitHarnessは「**AIエージェントがRevitを操作するためのブリッジ**」として設計しました。

- Named Pipe IPC でプロセス間通信
- ExternalEvent でスレッド安全性を確保
- Shadow Copy + バイト配列ロードでDLLホットスワップ

これらの仕組みにより、**CLIから自由にRevit上でコードを実行できる環境**が手に入ります。

AIコーディングエージェントの進化に伴い、GUIアプリケーションとCLIの橋渡しはますます重要になります。BIM業界でも同様のアプローチが広まることを期待しています。

リポジトリ: https://github.com/tatsukikouno/revit-cli-harness
ライセンス: MIT
