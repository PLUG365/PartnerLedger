# PL-010 通知フローの導入先セットアップ

この手順はPartnerLedgerのマネージドソリューションを別環境へ導入するときだけ読む。DecisionFlowのソリューションや接続参照は対象外。実際の他テナント導入はまだ実施しておらず、本書は開発環境で確認できた構成とMicrosoft Learnのインポート仕様を基にした導入チェックリスト。

## 送信元と接続参照

`PartnerLedger_NotificationDaily_v1_Delivery_v2`はOffice 365 Outlookの`Send an email (V2)`を使い、送信アクションの`From`値は指定していない。したがって送信元は、PartnerLedgerの接続参照`pl_office365outlook`に割り当てたOffice 365 Outlook接続のメールボックスであり、Dataverse行所有者や受信対象者ではない。共有メールボックスは必須ではない。

接続参照は接続資格情報そのものではなく、Solution内でConnector接続を指す部品。移送先で接続を割り当てる。インポート実行者がサービスプリンシパルでも、その事実だけでサービスプリンシパルがメール送信元になるわけではない。割り当てる接続が意図した送信元であり、対象環境のポリシー下で利用・有効化できることを確認する。Office 365 Outlook接続はメールボックスへアクセスできる職場／学校アカウントが必要。サービスプリンシパルを送信接続として使う場合は、接続・メールボックス・コネクター対応をそのテナントで個別に実証できるまで前提にしない。

| 対象 | Solution上の論理名 | Connector | 送信への影響 |
| --- | --- | --- | --- |
| Dataverse | `pl_dataverse` | Microsoft Dataverse | 通知候補／通知状態を読み書きする |
| メール | `pl_office365outlook` | Office 365 Outlook | 接続されたメールボックスを送信元としてメールを送る |

注意：2026-09-26に、DecisionFlowと共有していた接続参照（`ds_shared_commondataserviceforapps`／`ds_shared_office365`）をやめ、PartnerLedger専用の`pl_dataverse`／`pl_office365outlook`へ付け替えた。導入全体の手順は[導入手順書](import-guide.md)を参照。

## 導入前・導入・受入

| 段階 | 許可する操作 | 不変条件／停止条件 | 確認 |
| --- | --- | --- | --- |
| 導入前 | 対象環境、パッケージ、接続、通知設定、候補宛先を読取確認。意図した送信元の既存Outlook接続を使う（必要なら環境管理者が接続を用意） | 実在の宛先へ確認メールを送らない。送信元を行所有者・候補担当者へフォールバックしない | Queueが07:00 JST、Deliveryが08:00 JSTであること、接続が利用可能であることを確認 |
| Solution import | `pl_dataverse`と`pl_office365outlook`へ対象環境の適切な接続をマップ | 接続参照・接続ID・Flow状態が正しく復元したと仮定しない。インポートによりFlowが一時停止／再開する可能性を考慮する | import履歴、接続参照の割当、2フローの有効状態を再読取 |
| 初回受入 | 非本番では合成通知と専用テスト宛先を使い、Queue→Deliveryの1件を確認する。実環境では対象・宛先・予定時刻を管理者が事前確認した初回スケジュール実行を監視する | 本番宛先へ送る確認メールは導入先管理者の承認後だけ。結果不明・失敗は自動／手動で盲目的に再送しない | Flow run、通知行状態、受信箱を照合し、制御した1件が重複なく到達したことを確認 |
| 異常時 | 接続参照、接続状態、Flow所有者の接続利用権、Run履歴を調査 | 接続エラーや結果不明のまま再送／再有効化しない。行所有者やユーザー個人へ送信元を切り替えない | 原因と対象行を照合し、重複送信の可能性を除いてから管理者が再開判断 |

### 1. 導入前

1. 対象テナントの管理者がExchange Onlineメールボックスを持つ、運用可能なアカウントを送信元として選ぶ。個人アカウントを使う場合は退職・ライセンス・認証ポリシー変更時の所有者交代手順も決める。新しい共有メールボックスの用意は必須条件にしない。
2. 対象環境にMicrosoft DataverseとOffice 365 Outlookの接続が存在し、状態が有効であることを確認する。受信した認証・アクセス許可プロンプトは導入先管理者が判断する。
3. 日次Flowの有効化で実データが処理され得るため、通知有効設定、稼働対象レコード、通知先、初回実行時刻を確認する。準備が整う前に本番Solutionをインポート／有効化しない。
4. **時刻の正本：** Queueは毎日07:00 JSTに候補をキューし、Delivery v2は毎日08:00 JSTに送信する。Queueの1時間先行で同日分を配信対象にする。両Recurrenceは`frequency=Day`、`interval=1`、`timeZone=Tokyo Standard Time`、明示した`startTime`、`schedule.hours`／`schedule.minutes`を含む。`startTime`は2026-09-22の予定時刻をアンカーとし、既に過去なら次の未来のスケジュール枠が初回となる。再公開後はクラウド定義・Flow状態・次回Run履歴を読み返し、時刻だけの変更であることを確認する。

### 2. Solution import

1. Power Appsの対象環境で、信頼できるPartnerLedger Solutionパッケージをインポートする。
2. 接続参照の割当画面で、Dataverse参照には対象環境のDataverse接続を、`pl_office365outlook`には手順1で選んだ送信元アカウントのOffice 365 Outlook接続を明示的に割り当てる。存在しない接続を新規作成する場合は、導入先管理者が選んだアカウントで作成する。
3. インポートを完了し、Solution履歴とエラー／警告を確認する。既にFlowがある環境への更新インポートは既存の有効／無効状態を保持することがある。新規Flowでも、エクスポート時の状態と接続参照のマッピングに基づいて有効化が試みられる。よって「Import成功」だけで起動可能と判断しない。
4. import後、Solution内の`PartnerLedger_NotificationDaily_v1_QueueStopped`と`PartnerLedger_NotificationDaily_v1_Delivery_v2`が意図した状態であり、各Connection Referenceが意図した接続に結び付いていることを読み返す。Flowを有効化する操作者が参照先接続を所有するか、利用権を持つことも確認する。権限がない場合は接続所有者または管理者が解決する。

### 3. 初回受入と運用

1. 開発環境では日次Queue 07:00 JST／Delivery 08:00 JSTがStartedで、RecurrenceとevaluatedRecurrenceのReadbackを確認済み。合成通知1件についてDeliveryのメール送信・通知状態更新が成功し、Adminメールボックスへの到達を目視確認済み。次回の通常Runで時刻・順序を確認する。他テナントでの接続割当・ライセンス・DLP／Conditional Access・初回配送は未検証であり、この結果を他テナントの成功証拠にしない。
2. 他テナントの非本番受入では、専用テスト宛先への合成通知を1件だけ使用する。実際に送信されることを事前に明示し、導入先管理者が許可した後に実行する。
3. 本番では、両Flowの有効状態・次の実行予定・通知設定と候補宛先を確認し、最初の通常スケジュール実行を監視する。実際の受信先へ手動Test送信は行わない。
4. Run履歴、通知行の`送信済み`／`結果不明`、制御対象の受信箱を照合する。`Send an email (V2)`の成功応答だけでは受信箱到達を意味しない。`結果不明`は重複送信の有無を確認してから管理者が扱い、自動再送しない。
5. 接続切れ、Flow停止、予定外の宛先、初回の意図しない時刻実行があればその場で停止し、接続所有者／導入先管理者が解決する。Dataverse行所有者からの送信や担当者へのフォールバックは追加しない。

## 公式手順

- [Microsoft Learn: Import solutions](https://learn.microsoft.com/power-apps/maker/data-platform/import-update-export-solutions) — インポート時の接続参照マップとSolution履歴
- [Microsoft Learn: Use a connection reference in a solution](https://learn.microsoft.com/power-apps/maker/data-platform/create-connection-reference) — Connection Referenceと接続所有者／利用権
- [Microsoft Learn: Import a solution (Power Automate)](https://learn.microsoft.com/power-automate/import-flow-solution) — 新規／既存Flowのインポート後の状態
- [Microsoft Learn: Office 365 Outlook connector](https://learn.microsoft.com/connectors/office365) — メールボックス接続とコネクター要件
- [Microsoft Learn: Recurrence trigger schedules](https://learn.microsoft.com/azure/logic-apps/concepts-schedule-automated-recurring-tasks-workflows#patterns-for-start-date-and-time) — `startTime`、時間帯、schedule、過去／未来の開始時刻の挙動
- [Microsoft Learn: Troubleshoot Power Automate triggers](https://learn.microsoft.com/troubleshoot/power-platform/power-automate/flow-run-issues/triggers-troubleshoot#recurrence-trigger-runs-ahead-of-schedule) — Recurrenceの開始時刻と予定実行
