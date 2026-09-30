# PartnerLedger

取引先・担当者・契約・名刺を1か所で管理し、大事な変更は承認を通してから反映する Power Platform アプリです。

[![PartnerLedger の紹介動画（YouTube）](https://img.youtube.com/vi/rEq5W_ptPPQ/maxresdefault.jpg)](https://www.youtube.com/watch?v=rEq5W_ptPPQ)

▶ 紹介動画：[PartnerLedger ― 取引先・名刺・契約・承認を、ひとつに](https://www.youtube.com/watch?v=rEq5W_ptPPQ)（YouTube・PLUG Channel）

*English summary is at the [end of this page](#english-summary).*

## できること

- 取引先（会社）・担当者（人）・契約を登録し、一覧で探す
- 名刺の画像から、AI が読み取った内容を確かめて取引先・担当者として登録する
- 会社名・取引状態・主担当・契約の更新などの大事な変更を「変更申請」にし、承認されたら自動で反映する（どの変更に承認が要るかは管理者が設定で切り替えられます）
- 承認依頼は承認者一人ひとりへ届き、Outlook のメールの中や Teams の承認アプリで承認・却下できる
- 取引先ごとに、誰が見られるか・編集できるかを「共有設定」で決める（登録と同時に、承認者グループと主担当への共有が自動で付きます）
- 契約の期限が近いときや名刺が処理されずに残っているときに、毎朝メールで知らせる

PC でもスマホでも、同じアプリをブラウザ（または Power Apps モバイルアプリ）で使います。

## しくみ

| 部分 | 中身 | 場所 |
| --- | --- | --- |
| 画面 | Power Apps Code App（React・TypeScript・Vite） | [`apps/pc`](apps/pc) |
| データ | Dataverse の表（`pl_` で始まる表） | [`solutions/PartnerLedger`](solutions/PartnerLedger) |
| サーバー処理 | Dataverse プラグイン（C#、.NET Framework 4.6.2）。入力の検査、承認の反映、共有の付け外しなど | [`plugins`](plugins) |
| 承認・通知・名刺 | Power Automate のフロー5つ（Power Automate の承認、Office 365 Outlook、AI Builder の名刺リーダー） | `solutions/PartnerLedger/Workflows` |
| 権限 | 人向けのセキュリティロール4つ（PL 利用者・PL 承認・PL システム管理・PL システム保守）と、取引先ごとの共有 | `solutions/PartnerLedger/Roles` |

専用のアプリケーションユーザーやサービスプリンシパルは要りません。フローは、導入する人が割り当てる接続（Dataverse・承認・Office 365 Outlook・Office 365 Groups）で動きます。

## 導入

Dataverse のある Power Platform 環境に、PartnerLedger のマネージドソリューション（zip）をインポートして使います。zip は GitHub の [Releases](../../releases) からダウンロードします。手順は [インポート手順](docs/import-guide.md) にあります（承認グループの用意、接続、インポート、ロールの割り当て、受入の確認まで）。

## 資料

- [インポート手順](docs/import-guide.md)：導入と運用の注意
- [改造・開発の手順](docs/development.md)：自分の環境で改造するとき
- [説明書](docs/user-manual.md)：利用者・承認者・管理者向けの使い方
- [要件定義](docs/requirements.md)：製品の要件と受入条件
- [データ設計](docs/data-model-design.md)・[セキュリティロール](docs/security-role-matrix.md)：表・権限の設計
- [日次通知の導入](docs/notification-deployment.md)
- [デザインガイド](design-guide.md)・[AI 開発ツール](docs/AI_DEVELOPMENT_TOOLING.md)
- [AI 共通指示](AGENTS.md)：AI（Codex・Claude Code・GitHub Copilot）と一緒に開発するときの指示

資料には、設計を決めたときの日付付きの経緯が残っています。

## 開発

Node.js 22.12 以降の 22 系、24 系、または 26 以上を使います。リポジトリのルートで実行します。

```sh
npm ci
npm run dev
npm run check
```

`npm run check` は、公開前の検査（環境の URL やテナント固有のアドレスなどが入っていないか）、型、lint、テスト、ビルドを順に行います。環境につながなくても通ります。プラグインは `plugins` で `dotnet test` を実行します。

Windows では、ファイル名の長さの上限で複製に失敗することがあります。`git clone -c core.longpaths=true` で複製するか、短いフォルダに複製してください。

自分の環境で改造する手順（開発環境への導入、画面・プラグイン・フローの直し方、配布用の zip の作り方）は [改造・開発の手順](docs/development.md) にあります。

## ライセンス

[MIT License](LICENSE)

## English summary

PartnerLedger is a Power Platform app for managing business partners, their contacts, contracts and business cards in one place. Important changes (company name, trading status, main owner, contract renewal decisions, and so on) go through an approval before they are applied.

- **UI**: a Power Apps Code App (React, TypeScript, Vite) in `apps/pc`, for PC and mobile browsers and the Power Apps mobile app.
- **Data**: Dataverse tables shipped in the `solutions/PartnerLedger` solution.
- **Server logic**: Dataverse plug-ins (C#, .NET Framework 4.6.2) in `plugins`, which validate input, apply approved changes and manage record sharing.
- **Approvals, notifications, business cards**: five Power Automate flows. Approvals are assigned to each member of an approver group, so they can be approved inside Outlook and Teams. Business cards are read with the AI Builder business card reader.
- **Security**: four security roles plus per-partner sharing. No dedicated application user is required.

To install, download the managed solution from [Releases](../../releases), import it into an environment with Dataverse and follow the [import guide](docs/import-guide.md) (in Japanese). For development, run `npm ci` and `npm run check` at the repository root; see the [development guide](docs/development.md) (in Japanese) to customize the app in your own environment. On Windows, clone with `git clone -c core.longpaths=true` because some solution file names are long.

Demo video (in Japanese): https://www.youtube.com/watch?v=rEq5W_ptPPQ

The documentation is written in Japanese. Licensed under the [MIT License](LICENSE).
