# PartnerLedger Code App（apps/pc）

PartnerLedger の画面です。Power Apps の Code App（React＋TypeScript＋Vite）で、PC とスマホのブラウザ・Power Apps モバイルアプリの両方で使います。起動と検査はリポジトリのルートの [README](../../README.md) を見てください。

## 構成

- `src/App.tsx`：画面全体（取引先・担当者・契約・承認申請・共有設定・掲示板・名刺の処理待ち・アーカイブ・設定）。
- `src/*Api.ts`・`src/dataverse*Api.ts`：Dataverse への読み書き。書き込みは標準の Create／Update だけで、認可と検査はサーバー側のプラグイン（`plugins/`）とロールで行います。
- `src/*.test.ts(x)`：Vitest のテスト。画面の描画の確認は静的な描画だけで、実際の操作・クラウドの動きは対象外です。
- `src/generated/`・`.power/schemas/`：Power Apps CLI が生成したデータソースのコードと表の定義。環境固有の値を含まないので、リポジトリに入れています（表を変えたら下の手順で作り直します）。

初期画面の見本データは架空のものです。実際の名刺や個人情報を見本に入れないでください。

## 開発環境のデータソースをつなぐ

`power.config.json`（環境とアプリの ID）は環境固有のため Git 管理外です。PartnerLedger のソリューションを入れた開発用の Power Platform 環境で、`apps/pc` から次のように作ります（`<環境ID>` は自分の環境の ID）。開発環境への導入は [改造・開発の手順](../../docs/development.md) を見てください。

```text
npx --yes @microsoft/power-apps-cli app init --non-interactive --environment-id <環境ID> --app-type CodeApp --display-name PartnerLedger --description "PartnerLedger PC Code App" --build-path ./dist --file-entry-point index.html
```

表を変えたときは、アプリが使う表を1つずつデータソースとして追加し直し、生成コード（`src/generated`・`.power/schemas`）を作り直します。

```text
npx --yes @microsoft/power-apps-cli app add data-source --non-interactive --connector dataverse --table <表の論理名>
```

使う表：`pl_partner`、`pl_contact`、`pl_contract`、`pl_contracttype`、`pl_partnersharesetting`、`pl_request`、`pl_submissionversion`、`pl_settings`、`pl_bulletinpost`、`pl_capturebatch`、`pl_cardcapture`、`pl_cardinputversion`、`pl_ocrjob`、`pl_businesscardreview`、`systemuser`、`team`。

生成されたコードは手で編集しないでください。表に列を足したときは、同じコマンドをもう一度実行して作り直します。

## ローカルで動かす

`npm run dev` を実行すると、Power Apps の Vite プラグインが Local Play の URL を表示します。Power Platform にサインインしている同じブラウザのプロファイルで開いてください。

Local Play が Power Apps のホストからローカルの接続を取得できないときは、公式 CLI の接続ホストを一緒に動かします（接続設定を `http://localhost:8080` で渡すだけで、アプリや Dataverse には書き込みません）。

```text
dnx Microsoft.PowerApps.CLI.Tool --yes -- code run --port 8080 --appUrl http://127.0.0.1:5173
```

Chrome や Edge では、公開元から localhost へつなぐため、ローカルネットワークへのアクセスを許可する必要がある場合があります（[公式クイックスタート](https://learn.microsoft.com/power-apps/developer/code-apps/how-to/create-an-app-from-scratch)）。

## 環境へ反映する

ビルドしてから、ソリューションを指定して反映します（`<ソリューションID>` は開発環境の PartnerLedger ソリューションの ID）。

```text
npm run build
npx pa app push --solution-id <ソリューションID>
```
