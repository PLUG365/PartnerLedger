# AI 開発環境の適用方法

この文書は PPDevStandard の初期化で個別プロジェクトへ配置されます。Canvas アプリを手作業で少し扱ったことがある人が、Codex、Claude Code、GitHub Copilot CLI を使って Power Platform 開発を始めるための実践ガイドです。

## 最初に: AI に任せる前の境界

AI は便利ですが、どれがプロジェクトの正本か、どの環境が開発用か、公開してよいかを自動では判断できません。AI に変更を頼む前に、プロジェクトの `AGENTS.md` に次を記録します。

- 正本: Canvas 成果物、Solution、YAML、フロー定義など、最終的にレビュー・デプロイするファイル
- 開発環境: 操作してよい環境の識別方法。テナント URL、アカウント、トークンは書かない
- 検証: 変更後に実行するコマンドと手動確認
- 承認: 公開、削除、Solution import、既存資産・本番環境の変更を誰が承認するか

読取りと検証は開発環境で行うことを既定にします。既存資産の変更、公開、削除、Solution import、本番操作は、対象と影響を示して明示承認を得てから行います。

## いつ何をするか

| 頻度 | 作業 | 完了の目安 |
| --- | --- | --- |
| **PC ごとに一度だけ** | AI クライアント、公式プラグイン、.NET / Node.js、開発者ローカルの Dataverse 接続を準備する | 新しい会話でプラグインと MCP を確認できる |
| **新しいリポジトリごと** | 共通設定をプレビューして適用し、`AGENTS.md` のプロジェクト情報を完成させる | 正本・開発環境・検証・承認条件が書かれている |
| **AI に作業を頼むたび** | 指示を読み、対象を調査し、変更後に検証する | 変更箇所と検証結果を説明できる |

## PC ごとに一度だけ行うこと

### 1. 使う AI クライアントを準備する

普段使うクライアントを一つ選んで始めれば十分です。後から別のクライアントを追加しても、プロジェクトの共通ルールは `AGENTS.md` で共有できます。

| クライアント | 一度だけ行うこと | 新しい会話での確認 |
| --- | --- | --- |
| Codex | 開発者プロファイルに必要な公式 skills / plugins と MCP を導入する | 導入したスキル、MCP、現在の指示を要約するよう頼む |
| Claude Code | 公式 marketplace を登録し、必要なプラグインを導入する | `/memory` で読み込んだ指示を確認する |
| GitHub Copilot CLI | 公式 marketplace を登録し、必要なプラグインを導入する | `/instructions` で読み込んだ指示を確認する |

Claude Code と GitHub Copilot CLI の marketplace 導入例です。実際に導入するプラグイン名と最新版は、各公式配布元で確認してください。

```text
/plugin marketplace add microsoft/power-platform-skills
/plugin install canvas-apps@power-platform-skills
/plugin install power-automate@power-platform-skills
```

Codex では開発者プロファイルの Plugins から、採用する公式パッケージを導入します。パッケージと対応クライアントは PPDevStandard の `profiles/capabilities.json` にあります。

Power Automate の skills が見えても、`list_environments` / `list_flows` などの FlowAgent MCP ツールが見えなければ導入完了ではありません。Codex の開発者ローカル設定にある `flowagent` が、公式 `power-automate` プラグインの現在の `server/mcp.mjs` を起動しているか確認してください。プラグイン更新後も旧キャッシュ版への絶対パスが残ることがあるため、可能なら起動時に現行キャッシュ版を解決し、版番号を固定しません。修正後は Codex を再起動し、新しいタスクで次を順に確認します。

1. ツール一覧に `list_environments` と `list_flows` がある。
2. `list_environments` の読み取りが成功する。
3. ユーザー固有パス、環境一覧、認証情報をリポジトリへ保存していない。

FlowAgent は Azure CLI の現在のアカウントで認証します。取得するトークンにテナントを指定しないため、**MCP の設定でテナントを分けることはできません**。サーバーを複数登録しても、認証されるテナントは Azure CLI が現在アクティブにしているアカウント一つに決まります。テナントを指定する環境変数を設定しても認証には反映されないため、設定が効いたと判断しないでください。

複数テナントの環境を扱う場合は、次の順で切り替えます。

1. `list_environments` が期待するテナントの環境を返すか確認します。サーバーが起動したこと、ツールが一覧に出たことは判定になりません。
2. 切り替えるときは Azure CLI のログイン先アカウントを切り替えます。サブスクリプションを持たないテナントでは、サブスクリプション無しのログインを許可する指定が必要です。
3. FlowAgent は取得済みトークンをディスクにキャッシュします。有効期限内のトークンが残っていると切り替えが反映されないため、キャッシュを削除します。
4. AI クライアントを再起動します。稼働中の MCP プロセスがメモリ上にもトークンを保持しています。
5. 再度 `list_environments` で、期待するテナントの環境が返ることを確認します。

切り替えている間、**元のテナントの環境は FlowAgent から見えなくなります**。同時に両方を扱うことはできないため、テナントをまたぐ作業は切り替え単位でまとめてください。Dataverse MCP と PAC CLI はそれぞれ独自の認証を持ち、この切り替えの影響を受けません。フローの定義だけであれば Dataverse の `workflow` テーブルからも取得できるため、FlowAgent が必要になるのは主に実行履歴を確認するときです。

`mcs-assistant@copilot-studio-plugin` は実験的なため、標準構成には含めません。評価する場合も開発環境に限定し、別途明示承認を得てください。

### 1.1 Microsoft Learn MCP を公式知識源として確認する

PPDevStandard が生成する設定には、`https://learn.microsoft.com/api/mcp` を使う Microsoft Learn MCP が含まれます。これは認証、テナント URL、業務データを必要としない公開・読み取り専用の知識源です。Power Platform の現在の仕様、制限、非推奨化、公式コード例を確認する質問では、まず Microsoft Learn MCP を使います。

Microsoft Learn MCP はプロジェクトや環境の実態を知るものではありません。テーブル、レコード、フロー、Canvas アプリなどの実際の状態は、許可された開発環境の Dataverse MCP、FlowAgent、Canvas Authoring MCP、PAC MCP で確認します。Microsoft Learn MCP が接続できない場合は公式 Learn サイトを参照し、根拠が取得できなかったことを明示してください。

### 2. ローカルの前提を確認する

PPDevStandard のリポジトリ ルートで、使うクライアントと機能を指定して実行します。`$client` と `$capability` は自分の環境に合わせて変更します。

```powershell
$client = 'codex' # codex / claude-code / github-copilot-cli
$capability = 'canvas-apps' # all / canvas-apps / power-automate-flowagent / dataverse / copilot-studio / power-cat

pwsh -NoProfile -File scripts/check-prerequisites.ps1 -Client $client -Capability $capability
```

Canvas Apps と PAC MCP には **.NET 10** 以上が必要です。FlowAgent、Copilot Studio YAML skills、Power CAT には **Node.js 18** 以上、GitHub Copilot CLI には **Node.js 22** 以上が必要です。このコマンドは確認だけを行い、プラグインの導入、MCP 接続、認証、ネットワーク、クラウド環境を変更しません。

### 3. Dataverse を開発者ローカルで接続する

Dataverse MCP の接続先は開発者と環境ごとに異なります。公式 Dataverse Skills の `dv-connect` を使い、許可された**開発環境**へユーザー設定として接続します。

接続先や認証情報を `.mcp.json`、`.codex/config.toml`、Git、共有ドキュメントへ保存しないでください。最初は `dv-query`、Dataverse MCP のメタデータ取得、PAC MCP の一覧・検証だけで接続先と要求権限を確認します。

## 新しいリポジトリごとに行うこと

### 1. 初期化をプレビューする

この文書を作成するために使った PPDevStandard の初期化コマンドを、まずは `-Apply` を付けずに実行します。表示される `would create` と `manual merge required` を確認し、既存の設定を意図せず上書きしないことを確かめます。

`TargetPath`、`Client`、`Capability` は自分のプロジェクト用に置き換える値です。`Apply` は確認後にだけ付ける書込み用スイッチです。

### 2. 適用後のファイルを完成させる

初期化は既存のファイルを上書きしません。次のファイルが新規作成された場合は、内容を確認し、`<...>` をプロジェクト固有の値に置き換えます。既存ファイルがある場合は、必要な節だけを手動マージします。

| ファイル | 役割 | 最初にすること |
| --- | --- | --- |
| `AGENTS.md` | AI が読む共通のプロジェクトルール | 正本、開発環境、検証、承認条件を記入する |
| `docs/AI_DEVELOPMENT_TOOLING.md` | この導入・運用ガイド | チームへ共有し、使うクライアントの節を確認する |
| `.mcp.json` | Claude Code / Copilot CLI が使うプロジェクト MCP 設定 | 接続先や資格情報が含まれていないことを確認する |
| `.codex/config.toml` | Codex のプロジェクト設定 | Codex を使う場合だけ、MCP が想定どおりであることを確認する |

`.mcp.json` には `pac-cli` と標準の公式知識源 `microsoft-learn` が入ります。Claude Code と GitHub Copilot CLI では Canvas Authoring MCP が公式プラグイン側から提供されるためです。Codex はプラグインが同じ役割を果たさないので、Canvas Apps を使う場合は `.codex/config.toml` に `canvas-authoring` が含まれます。いずれの場合も、新しい会話で Microsoft Learn MCP と必要な操作系 MCP が実際に使えることを確認してください。

## 指示ファイルをどう分けるか

同じルールを三つの AI クライアント用ファイルにコピーすると、いずれ内容がずれます。**プロジェクトの正本・環境・検証・承認条件は、リポジトリの `AGENTS.md` に一度だけ書く**ことを基本にします。クライアント専用のファイルは、そのクライアントにしかない追加設定だけに使います。

| クライアント | 個人: 全リポジトリに効く指示 | リポジトリ: チームで共有する指示 | 使いどころ |
| --- | --- | --- | --- |
| Codex | `~/.codex/AGENTS.md` | `AGENTS.md` | 個人の承認の好みは前者、プロジェクトの正本と検証は後者 |
| Claude Code | `~/.claude/CLAUDE.md` | `CLAUDE.md` または共通 `AGENTS.md`。領域限定は `.claude/rules/` | Claude 固有の手順だけを追加する |
| GitHub Copilot CLI | `~/.copilot/copilot-instructions.md` | `.github/copilot-instructions.md` と共通 `AGENTS.md` | Copilot 固有の補足だけを追加する |

複数の指示が同時に読み込まれることがあります。矛盾する指示を書かないでください。特に「どの環境を操作してよいか」「公開の条件」「正本の場所」は `AGENTS.md` だけを更新するようにすると、三つのクライアントで同じ判断ができます。

### グローバル指示の最小サンプル

グローバル指示は、個人のどのリポジトリにも当てはまる原則だけを書きます。プロジェクト名、環境名、URL、顧客情報はここに書きません。

```md
# 個人の AI 開発ルール

- 秘密情報をチャット、Git、共有文書に書かない。
- 外部サービスへの変更、依存関係の追加、公開・削除の前に確認を求める。
- 変更後はリポジトリに定義された検証を実行し、結果を短く報告する。
```

Codex は `~/.codex/AGENTS.md` に置きます。Claude Code は `~/.claude/CLAUDE.md` に置きます。GitHub Copilot CLI は `~/.copilot/copilot-instructions.md` に置きます。作成後は、それぞれ新しい会話で現在の指示を要約させる、`/memory`、`/instructions` のいずれかで読み込みを確認します。

### リポジトリの `AGENTS.md` 最小サンプル

初期化で作成されるテンプレートを使うのが基本です。自分で作る場合も、次の `<...>` は必ず実際のプロジェクト情報へ置き換えます。

```md
# このリポジトリの AI 開発ルール

## 正本

- Canvas アプリ: `<版管理されている成果物の場所>`
- Solution / フロー: `<管理対象の場所>`
- 検証: `<変更後に実行するコマンド>`

## Power Platform の操作境界

- 使用するのは `<開発環境の識別方法>` のみ。URL、アカウント、トークンは記録しない。
- 既存資産の変更、公開、削除、Solution import、本番操作は、対象と影響を示して明示承認を得てから行う。
```

### クライアント固有の補足例

Claude Code の `CLAUDE.md` と Copilot の `.github/copilot-instructions.md` は、共通ルールのコピーではなく補足にします。

```md
# このクライアント向けの補足

- プロジェクトの正本、操作境界、検証、承認条件は `AGENTS.md` に従う。
- このクライアント固有のコマンドや確認方法だけをここに追加する。
```

Copilot CLI では `/instructions` で読み込まれた指示を確認できます。Claude Code では `/memory` で `CLAUDE.md` を含む記憶を確認できます。Codex では新しいセッションで指示を要約するよう頼み、グローバルとリポジトリの `AGENTS.md` が反映されていることを確認します。

## AI に作業を頼むたびに行うこと

1. **調査する**: `AGENTS.md` とプロジェクトの正本を読み、対象の環境と変更範囲を確認する。
2. **提案を確認する**: 既存資産、データ、権限、接続、公開に影響する操作は、対象・影響・戻し方を先に確認する。
3. **開発環境で変更する**: 試作は新規かつ一時的な資産から始める。新規フローは停止状態で作る。
4. **検証する**: リポジトリの検証コマンド、`validate_flow`、`preflight_flow`、YAML 検証など、該当する確認を実行する。
5. **正本へ反映してレビューする**: AI の試作結果をそのまま公開せず、Git 管理された正本、接続参照、依存関係、検証結果をレビューする。

## 機能別ルーティング

| 作業 | 第一候補 | 境界 |
| --- | --- | --- |
| フローの探索・新規試作・接続調査・失敗診断 | FlowAgent | 新規フローは開発環境で停止状態にする。採用前に定義と接続参照を正本へ反映する。 |
| 既存フローの変更 | プロジェクト固有のフロー定義・Solution・スクリプト | FlowAgent の結果を確認材料にし、正本を迂回して変更しない。 |
| Dataverse のメタデータ・レコード調査 | Dataverse Skills / MCP | 読取りを既定にする。既存資産、削除、ロール、環境変更は明示承認後。 |
| Canvas Apps の試作 | Canvas Authoring MCP | 新規の開発環境 coauthoring セッションだけで行う。採用時は版管理成果物をレビューする。 |
| 現在の公開仕様・制限・公式コード例の確認 | Microsoft Learn MCP | 標準の公式知識源。公開情報のみを扱い、テナントやプロジェクトの実態は示さない。 |
| Code Apps の実装・ローカル実行・データソース追加 | 公式 Code Apps skills と PAC CLI | デプロイ先は開発環境に限る。共有・本番環境への公開は明示承認後。 |
| Copilot Studio の作成・検証・評価 | 公式 YAML skills | 既存エージェントの push / publish / チャネル変更は明示承認後。 |
| Solution 内フロー・Code Apps の品質確認 | Power CAT | 指摘はレビュー材料とし、修正はプロジェクトの正本へ行う。 |

FlowAgent の新規フローは、開発環境で停止状態に作成し、`validate_flow`、`preflight_flow`、接続参照のレビューを通します。既存 Solution の接続参照を優先し、接続を自動作成しません。有効化、公開、Solution import は、対象と影響を示した明示承認後だけにします。

機能ごとに実際に踏んだ落とし穴は `profiles/capabilities.json` の `knownPitfalls` に記録しています。作業前に該当機能の項目を確認してください。特に Code Apps は、CLI パッケージの宣言、`pac code push` と `npx power-apps push` の使い分け、認証ラッパー経由での失敗、生成物が gitignore 対象であることの4点が繰り返し問題になります。
