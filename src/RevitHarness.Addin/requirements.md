# Revit 2024 常駐ハーネス要件（CLI からのテスト実行・DLL差し替え）

## 1. 目的 / ゴール
- Revit 2024 を**既に起動している状態**で、外部（CLI / コーディングエージェント）から
  - テスト（または任意処理）を実行できること
  - 起動中でも DLL をビルド・差し替えできること
  - 実行結果を機械可読（JSON）で回収できること
- テストを回す主体は **Claude Code 等のエージェント**を想定し、CLI 単発実行（exit code 付き）を最終インターフェースとする。

## 2. 前提 / 制約（Revit 2024 特有）
- Revit 2024 は一度ロードしたアセンブリのアンロードが困難（実質不可）であるため、
  - **ビルド成果物を Revit が直接ロードしない設計**が必須
  - シャドウコピー（別フォルダへコピーしてロード）で「上書き可能なビルド」を成立させる
- Revit API は Revit のメインスレッドでのみ安全に実行できる。
  - 外部プロセスからの要求は **ExternalEvent 経由**でメインスレッドに移送すること
- 「AddinManager を外部から叩く」などの公式ルートは使わない（対象外）。

---

## 3. システム構成（成果物）
### 3.1 Revit 側：常駐ハーネスアドイン（固定）
- `Harness.Addin`（.addin マニフェスト）
- `Harness.dll`（IExternalApplication 実装）
- 起動時に自動ロードし、以降は Revit 終了まで常駐して要求を待ち受ける。

### 3.2 外部側：CLI ランナー（固定）
- `revit-harness-cli.exe`（または `dotnet tool`）
- CLI が IPC で Revit 側ハーネスへ命令を送り、JSON を受け取り、exit code を返す。

### 3.3 テスト対象：差し替え対象 DLL（可変）
- ビルド対象（エージェントが編集する）側は以下のいずれかの形を取れること：
  - **方式A：EntryPoint 方式（推奨）**
    - `ITestEntryPoint` を実装し、1メソッドで実行し JSON を返す
  - **方式B：ExternalCommand 呼び出し方式（任意）**
    - 指定クラスの IExternalCommand を反射で生成して実行
- テスト対象 DLL は Revit から見て「直接ロードされる場所」に置かない。

---

## 4. IPC 要件（CLI ⇄ Revit）
### 4.1 方式
- Windows 前提で **Named Pipe** を採用する（優先）。
  - 例：`\\.\pipe\RevitHarness2024`
- 単一 Revit インスタンスを前提とする（複数起動は将来拡張）。

### 4.2 メッセージ形式
- 文字コード：UTF-8
- フレーミング：長さプレフィックス（int32 little endian）＋ JSON（推奨）
- JSON は request / response 共通で必須フィールドを持つこと。

### 4.3 リクエスト共通フィールド
- `id` : string（リクエスト相関ID）
- `op` : string（操作名）
- `timestamp` : string（ISO8601）
- `payload` : object（操作ごとの引数）

### 4.4 レスポンス共通フィールド
- `id` : string（リクエスト相関ID）
- `ok` : boolean
- `error` : object|null
  - `code` : string
  - `message` : string
  - `details` : object|null
- `result` : object|null
- `logs` : string[]（任意）
- `durationMs` : number

---

## 5. 提供する操作（op）
最低限、以下を実装する。

### 5.1 `ping`
- 目的：疎通確認
- request.payload：空
- response.result：
  - `revitVersion`
  - `processId`
  - `activeDocumentTitle`（あれば）

### 5.2 `runTests`（メイン機能）
- 目的：差し替え対象 DLL をシャドウコピーしてロードし、指定テストを実行
- request.payload：
  - `assemblyPath` : string（ビルド出力先の DLL パス。Revit が掴まない場所）
  - `entryType` : string（例：`Company.Tests.EntryPoint`）
  - `args` : object（テストフィルタ等）
    - 例：`{ "filter": "Smoke", "headless": false }`
  - `timeoutMs` : number（任意）
- response.result：
  - `passed` : boolean
  - `summary` : object（任意）
  - `artifactsDir` : string（任意：ログ/画像など出力先）

#### 実行モデル（Revit 側）
- IPC 受信スレッド → リクエストをキューへ投入 → `ExternalEvent.Raise()`
- `IExternalEventHandler.Execute()`（Revit メインスレッド）で以下を行う：
  1. シャドウコピー先ディレクトリ作成  
     - 例：`%LOCALAPPDATA%\RevitHarness\shadow\{yyyyMMdd_HHmmss_fff}\`
  2. `assemblyPath` の DLL と依存 DLL をコピー
  3. 反射で `entryType` をロードし、EntryPoint を呼び出し
  4. 返ってきた結果を JSON 化して IPC 返信

### 5.3 `runCommand`（任意）
- 目的：IExternalCommand の `Execute` を反射で呼ぶ（テスト以外にも使える）
- request.payload：
  - `assemblyPath` : string
  - `commandType` : string（IExternalCommand 実装型）
  - `journalData` : object（任意）
- 注意：UI依存コマンドは不安定になりやすいので、基本は `runTests` を主に使う。

### 5.4 `shutdown`（任意）
- 目的：ハーネスの IPC サーバ停止（Revit終了はしない）
- Revit セッションは継続。

---

## 6. シャドウコピー要件（重要）
- Revit が直接ロードしている DLL ファイルをビルドで上書き可能にするため、以下を必須とする：
  - `assemblyPath` は **ビルド出力（通常フォルダ）**
  - ハーネスが **別フォルダへコピー**してからロードする
- Revit 2024 ではアンロードできないため、
  - シャドウコピー先は**毎回別ディレクトリ**（または別ファイル名）にする
  - 例：`shadow\20260203_153012_123\Target.dll`
- ディスク肥大化対策：
  - `shadow` 配下は「N 世代のみ残す」「古いものを削除」などの回収ポリシーを実装（任意だが推奨）
  - ただしロード中のものを削除しない（安全策：一定日数より古いもののみ削除）

---

## 7. スレッド / 実行保証
- IPC 受信はバックグラウンドスレッドで行う。
- Revit API を触る処理は **必ず ExternalEvent の Execute() 内**で行う。
- リクエストは FIFO キューで逐次実行（同時実行しない）。
- CLI 側はタイムアウトを持ち、超過時は `exit code != 0` とする（Revit側は処理継続の可能性あり）。

---

## 8. 例外設計（エラーコード）
- `HARNESS_NOT_READY`：Revit側ハーネス未起動 / pipe 接続不可
- `INVALID_REQUEST`：JSON 不正、必須フィールド不足
- `ASSEMBLY_NOT_FOUND`：`assemblyPath` 不存在
- `TYPE_NOT_FOUND`：`entryType` / `commandType` 見つからない
- `ENTRYPOINT_FAILED`：EntryPoint 実行中例外
- `REVIT_API_ERROR`：Revit API 例外
- `TIMEOUT`：CLI タイムアウト
- `INTERNAL_ERROR`：ハーネス内部例外

---

## 9. CLI 要件
- コマンド例：
  - `revit-harness-cli ping`
  - `revit-harness-cli run-tests --dll "<path>" --type "Company.Tests.EntryPoint" --filter "Smoke"`
- CLI 出力：
  - 標準出力：レスポンス JSON（そのまま）
  - 標準エラー：人間向けの補助ログ（任意）
- exit code：
  - 0：`ok=true` かつ（`runTests` の場合）`passed=true`
  - 1：`ok=false` または `passed=false`
  - 2：接続不可 / タイムアウト等のインフラ障害（任意）

---

## 10. テスト対象 DLL（EntryPoint 方式の契約）
### 10.1 インターフェース（概念）
- `Run(UIApplication uiapp, IDictionary<string,object> args) -> string(json)`
  - `UIApplication` を渡せるようにする（Revit API を使うテストのため）
  - 返り値は JSON 文字列（または結果オブジェクトをハーネス側で JSON 化）
- 例の結果フィールド：
  - `passed` : bool
  - `cases` : array（任意）
  - `logs` : array（任意）

### 10.2 推奨
- テストはできるだけ UI 非依存（ダイアログ抑止・操作不要）に寄せる
- Revit依存ロジックは最小にし、重いユニットテストは `dotnet test` に逃がす

---

## 11. 非機能要件
- ロギング
  - ハーネス側：IPC 受信、ExternalEvent 実行、例外スタックをログに残す
  - 返却 JSON に `logs` を含められるようにする
- セキュリティ（最低限）
  - pipe 名は固定でよいが、同一マシン内利用に限定
  - 必要なら簡易トークン（環境変数等）で認証（任意）
- 配布
  - ハーネスは一度インストールしておけば、テスト対象 DLL はビルドで差し替え可能

---

## 12. 受け入れ基準（Acceptance Criteria）
1. Revit 2024 起動済みの状態で `revit-harness-cli ping` が成功し、Revit の情報が返る
2. Revit を閉じずに、テスト対象 DLL を連続ビルドしても上書き失敗しない（ロックしない）
3. `runTests` を連続実行でき、結果 JSON と exit code が正しく反映される
4. Revit API を触る処理が ExternalEvent 経由で実行され、スレッド例外が発生しない
5. 異常系（dll無い/型無い/例外発生/タイムアウト）でエラーコードとメッセージが返る

---
