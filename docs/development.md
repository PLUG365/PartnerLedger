# 改造・開発の手順

PartnerLedger を自分の環境で改造するための手順です。導入して使うだけなら [インポート手順](import-guide.md) を見てください。

## 1. 必要なもの

| もの | 使いみち |
| --- | --- |
| Node.js（22.12 以降の 22 系、24 系、または 26 以上） | 画面（Code App）のビルド・テスト |
| .NET SDK 9 以降 | プラグインのビルド（.NET Framework 4.6.2 向け）とテスト（.NET 9） |
| Power Platform CLI（`pac`） | ソリューションのまとめ・インポート、プラグインの差し替え |
| Python 3 | ソリューションの取り込み・書き出しの台本（`scripts/*.py`） |
| PowerShell 7（`pwsh`） | ソリューションの構造検査（`scripts/test-solution-environment-variables.ps1`） |
| Dataverse のある開発用の Power Platform 環境 | 実際に動かす場所。本番とは別の環境を使ってください |

Windows では、ソリューションのファイル名が長いため、深いフォルダに複製すると「Filename too long」で失敗します。長いパスを有効にして複製するか、短いフォルダ（例：`C:\src`）に複製してください。

```sh
git clone -c core.longpaths=true <このリポジトリのURL>
```

## 2. まず手元で動くか確かめる

```sh
npm ci
npm run check
```

`npm run check` は、公開前の検査・型・lint・テスト・ビルドを順に行います。環境につながなくても通ります（Dataverse の生成コード `apps/pc/src/generated` と表の定義 `apps/pc/.power/schemas` はリポジトリに入っています）。

プラグインは次のとおりです。

```sh
cd plugins
dotnet test
```

## 3. 開発環境に PartnerLedger を入れる

リポジトリの `solutions/PartnerLedger` は、ソリューションを展開した形（アンマネージド）です。開発環境にはアンマネージドで入れると、画面・フロー・プラグインを環境の上で直せます。

> 未確認：この節の、展開した形から zip にまとめて新しい環境へアンマネージドで入れる流れは、まだ通しで試していません（zip にまとめるところまでは確認済み）。つまずいたら Issue で知らせてください。

1. 開発環境に `pac` でサインインします。

   ```sh
   pac auth create --environment <開発環境のURL>
   ```

2. zip にまとめ、接続参照と環境変数の設定ファイルを作ります。

   ```sh
   pac solution pack --zipfile PartnerLedger.zip --folder solutions/PartnerLedger --packagetype Unmanaged
   pac solution create-settings --solution-zip PartnerLedger.zip --settings-file settings.json
   ```

3. `settings.json` に、4つの接続参照の接続 ID と、2つの環境変数の値を入れます。何を入れるかは [インポート手順](import-guide.md) の 2 章（承認グループ・グループチーム）と 3-1（接続）を見てください。`settings.json` は環境固有なので、リポジトリに入れないでください。

4. インポートします。

   ```sh
   pac solution import --path PartnerLedger.zip --settings-file settings.json --activate-plugins --publish-changes
   ```

5. [インポート手順](import-guide.md) の 4 章以降（ロールの割り当て、承認設定、契約種類、フローの確認）を行います。

## 4. 画面（Code App）を直す

画面は `apps/pc` にあります。構成は [apps/pc の README](../apps/pc/README.md) を見てください。

- **開発環境につなぐ**：`apps/pc` で `power.config.json` を作ります（環境固有なので Git 管理外）。手順は apps/pc の README の「開発環境のデータソースをつなぐ」にあります。生成コードはリポジトリに入っているので、表を変えないかぎり作り直す必要はありません。
- **表に列を足したとき**：`pa app add data-source`（apps/pc の README）で生成コードを作り直し、その差分もコミットします。生成コードは手で編集しないでください。
- **ローカルで動かす**：`npm run dev` で Local Play の URL が出ます。
- **環境へ反映する**：`npm run build` のあと `npx pa app push --solution-id <開発環境のPartnerLedgerソリューションのID>`。ソリューションの ID は `pac solution list` で分かります。

## 5. プラグインを直す

プラグインは `plugins/PartnerLedger.Plugins`（C#、.NET Framework 4.6.2）です。署名キー（`PartnerLedger.Plugins.snk`）もリポジトリに入っています。

1. 直したら、テストを足して通します。

   ```sh
   cd plugins
   dotnet test
   dotnet build PartnerLedger.Plugins -c Release
   ```

2. 開発環境のアセンブリを差し替えます。アセンブリの ID はソリューションの一部なので、どの環境でも同じです。

   ```sh
   pac plugin push --pluginId 088d1313-74ae-f111-aaab-e4fade057b20 --pluginFile plugins/PartnerLedger.Plugins/bin/Release/net462/PartnerLedger.Plugins.dll --type Assembly
   ```

3. 新しいプラグインの型やステップを足すときは、Plugin Registration Tool（`pac tool prt`）で登録します。サーバー処理はユーザーの権限と SYSTEM の権限を使い分けています。設計は [データ設計](data-model-design.md) と [セキュリティロール](security-role-matrix.md) を見てください。

## 6. フロー・表・ロールを直す

フロー（承認・日次通知・名刺の読み取り・担当者の共有）、表、フォーム、ロールは、開発環境の上（Power Automate・Power Apps の画面）で直します。直したら、リポジトリへ取り込みます。

```sh
python scripts/sync-solution-from-cloud.py
```

この台本は、`pac` で選んでいる環境から PartnerLedger をアンマネージドで書き出し、`solutions/PartnerLedger` へ展開します。環境固有の値（環境変数の値、プラグインの実行ユーザー名）は取り込みません。人向けの4つのロールの XML はリポジトリ側を正とし、上書きしません。

取り込んだら、構造検査を通します。フローの形（承認の宛先、失敗時の扱いなど）や、フローにメールアドレスを直接書いていないことも確かめます。

```sh
pwsh -File scripts/test-solution-environment-variables.ps1
python -m unittest discover -s scripts -p "test_*.py"
```

## 7. 配布用の zip を作る

配布用のマネージドソリューションは、開発環境から書き出します。版を上げ、環境固有の値（実行ユーザー名、環境の URL、PC のパス、DecisionFlow への参照など）が入っていないかを検査します。問題があれば zip を消して止まります。

```sh
python scripts/export-managed-solution.py --out <出力フォルダ> --version 0.1.0.6
```

## 8. 公開前の検査

`npm run check` の最初の `npm run scan`（`scripts/scan-public-tree.mjs`）は、リポジトリに次のものが入っていないかを調べます。

- Dataverse の環境 URL
- `onmicrosoft.com` のアドレス
- PC のユーザーフォルダのパス
- zip

`example`・`contoso` などの架空の例は除きます。自分の環境の ID や名前も調べたいときは、リポジトリの外に「種類<TAB>値」の一覧を置き、環境変数 `PL_PRIVATE_SCAN_LIST` でその場所を指定します（既定は `~/.partnerledger/private-scan-list.txt`）。

## 9. AI と一緒に開発する

AI（Codex・Claude Code・GitHub Copilot）への共通の指示は [AGENTS.md](../AGENTS.md) にあります。環境固有の指示は、Git 管理外の `AGENTS.local.md` に書くと、AI がそれも読みます。使う AI ツールと MCP は [AI 開発ツール](AI_DEVELOPMENT_TOOLING.md) にあります。
