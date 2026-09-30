# PartnerLedger データモデル設計

2026-09-27：旧アクセス付与`pl_AccessGrant`テーブルと`pl_OperationLog.pl_TargetGrantLookup`列を削除した（旧Custom API経路の名残で、どこからも使われていなかった。経緯は実装計画（開発記録、非公開））。取引先の共有は共有設定（`pl_PartnerShareSetting`）と承認者Teamへの初期Read共有だけで扱う。以下の`pl_AccessGrant`に関する節は経緯として残している。

2026-09-26：どこからも参照されずデータも無かった7列（`pl_AccessGrant.pl_DelegationCode`、`pl_ApprovalLink.pl_RetryCount`、`pl_BusinessCardReview.pl_ResolutionCode`／`pl_ResolutionNote`、`pl_Notification.pl_LastAttemptAt`／`pl_NotificationKindCode`、`pl_Settings.pl_EffectiveAt`）と、合成データ1行のダミー値だけだった`pl_CardInputVersion.pl_ImageHash`を削除した。以下の表に残るこれらの列の記述は削除前の設計記録。

2026-09-14の方式見直し：Custom APIゼロの標準機能優先レビュー（開発記録、非公開）に従い、必要要件を満たしつつ独自処理・維持管理を最小化する。PL-030は[初期共有＝登録時にサーバーが承認者グループ（保護された閲覧）と主担当（編集）へ共有、登録後＝会社アクセス保持者](requirements.md#company-sharing-policy)（2026-09-28改訂。それ以前は初期共有＝承認者）、PL-006/032は初回リリース対象外。PL-031の全申請・完了後Readも承認済み。旧API追加案・日付付き記録を実行指示にしない。会社ACLの旧管理者限定・登録者初期管理・申請の一時共有と失効・重複保留は移行対象であり、**R2スキーマ、標準CRUDの最小同期Plugin、公開PCの共有設定Create、承認者M365グループTeamのInitialSetup許可／拒否まで開発環境へ適用・公開・実機確認済みである。** 既存テーブル・API・データは移行受入まで削除・停止していない。既存／外部共有行は`pl_ismanagedprojection=false`（または旧行の欠落をfalse扱い）で未照合として保持する。現在地はHANDOFFを参照する。

現況（2026-09-14、WP-002-Z）：承認基盤Custom APIは既存5件に反映実行1件を加えた6件。6件すべてを開発環境へ登録・公開し、反映実行APIの入出力と専用同期ステップをPartnerLedger Solutionへ収録済みである。合成データによる反映実行の初回成功・冪等再送・後始末まで実機確認済み。承認基盤の標準CRUD中心への再設計・移行、恒久`pl_Settings`行、DecisionFlow実接続、Power Appsホスト受入、TV-14は未実施。

R12-B（2026-09-18）：OCR前処理の相関を入力版単位で固定するため、`pl_OcrJob`へ`pl_CardInputVersionLookup`（`pl_CardInputVersion`参照、ApplicationRequired）と`pl_OcrResultJson`（Memo、最大1MB）、`pl_BusinessCardReview`へ同じ入力版Lookupと`pl_CorrectedFieldsJson`（Memo、最大1MB）を追加し、PartnerLedger Solutionへ収録・対象テーブル限定公開した。Relationshipは`pl_CardInputVersion_OcrJob`／`pl_CardInputVersion_BusinessCardReview`で、削除／アーカイブはRemoveLink、それ以外の連鎖はNoCascadeとする。適用前に既存の処理時刻3列をDateAndTime／UserLocalへ補正した。対象3テーブルは0行で、OCRフロー・AI Builder接続・正式登録・Role変更はまだ行わない。物理名の詳細と検証結果はHANDOFFとtechnical-validationを正本とする。

版：1.4 / 2026-09-13。Astra設計レビュー反映案、権限付き重複照会、状態列棚卸し、内部処理状態の遷移契約、所有形態の適用差異、承認基盤のサーバー側契約と次バッチ境界を追記。論理設計に基づく専用publisher、非管理Solution、21テーブルの骨格を開発環境へ適用済み。取引先・担当者・契約の利用者向け状態4列はChoiceとして適用済み。`pl_AccessGrant`の会社・主体・理由・ACL版・共有投影列、`pl_Partner.pl_AclVersion`、4ロール、Custom API＋同期PostOperationプラグイン、実行用Application User、初期アクセスのブートストラップを開発環境へ登録済みで、取引先行の明示共有と実効境界を限定確認済み。会社単位アクセス一覧用の`pl_ListPartnerAccess`と同期PostOperationプラグインも登録・Solution収録し、対象限定公開の後にユーザー承認済みの`PublishAllXml`を実行、公式CLIの生成サービスを追加してPCの読み取り境界へ結線した。承認基盤5テーブルのDateTime補正、追加列、15本のLookupは開発環境へ適用・発行し、`ApprovalSubmissionService`、`ApprovalResultIngressService`、`ApprovalDecisionService`、`ApprovalViewingGrantService`、`ApprovalReflectionService`のローカル契約とC#テストを追加した。さらに承認基盤5 Custom API、入力18件、応答34件、同期PostOperationステップ5件を登録・Solution収録し、ユーザー承認後に環境全体公開まで完了したが、公開後も新5 APIが`$metadata`と公式CLI検索に現れないため、PCサービス生成は未到達である。DecisionFlow接続、承認者解決、実データ受入、内部処理状態のChoice化、期限切れバッチ、子テーブルへの共有波及、Canvas／PC UIの通常ログイン、フローは未確定・未実装であり、所有形態は全21テーブルがUserOwnedであることを読み取り確認済み。`pl_Contact`、`pl_CardCapture`、`pl_CardInputVersion`の`pl_RegisteredAt`と`pl_CardCapture.pl_ReceivedAt`を`DateOnly`／`DateTimeBehavior=DateOnly`へ補正・限定公開し、5本の主要Lookupを作成した。対象4テーブルのデータ行は0件で、DecisionFlow・既存フローへの書込みは行っていない。

承認基盤6 APIは公開済みだが、公開後も`$metadata`と公式CLI検索に現れず、6 APIの公式サービス生成は未到達である。`pl_Request`／`pl_SubmissionVersion`のテーブルサービスは公式CLIで生成済みで、`dataverseApprovalReadApi.ts`と`ApprovalCenterView`へ読み取り専用で結線した。6 APIの送信境界は`dataverseApprovalApi.ts`の一時定義を使い、実IDを伴う提出・判定・反映UI、実データ受入、Power Appsホスト認証はまだ未確認である。

2026-09-13のR2保存前スキーマゲートで、既存`pl_partner`へ`pl_Industry`（String／MaxLength 200）、`pl_Address`（String／MaxLength 500）、`pl_Phone`（String／MaxLength 200）、`pl_MainOwnerLookup`（表示名「主担当」、`systemuser`参照）、`pl_RegisteredByLookup`（表示名「登録者」、`systemuser`参照）をPartnerLedger Solution内へ追加し、型・上限・Lookup対象の再取得確認と`pl_partner`限定公開を完了した。Relationshipはそれぞれ`pl_Partner_MainOwner`／`pl_Partner_RegisteredBy`で、Assign／Merge／Reparent／Share／Unshareは`NoCascade`、Delete／Archiveは`RemoveLink`である。公式CLI生成モデルにも反映し、版管理Solutionへ3ファイルだけを同期、再pack→unpack全386ファイル差分0件を確認した。これは登録画面の保存列とPCライブ読取契約を揃えるための準備であり、保存API、Create権限、重複確認、初期アクセス付与を確定・実装したものではない。

### PC承認申請読み取り境界（2026-09-13、WP-002-N）

承認一覧は、サーバー側の現在の行権限で取得できる`pl_Request`と`pl_SubmissionVersion`だけを表示する読み取り専用境界とする。公式CLI生成のテーブルサービスをアダプターから`getAll`で呼び、`statecode=0`、申請・提出版・申請者・承認Team・申請日時・状態・版番号・提出日時などの最小列に限定する。申請ごとに最新の提出版だけを選び、親申請のない提出版は表示しない。GUID／状態／応答形が不正、通信が失敗、重複申請がある場合はエラーとして扱い、部分的な一覧や架空モックへ自動フォールバックしない。

（2026-09-25改訂）最新提出版の`pl_ChangeSetJson`だけは、申請者が申請内容を確認し差戻し後に修正できるよう、項目名と値を業務表記へ変換して表示する。この列は列セキュリティ無しで、読めるかは提出版行の読取権限だけで決まるため、表示しても権限は広がらない。生のJSONは表示しない。以下はこの改訂前のR3読取期の範囲限定である。承認者・判定値・結果確知性のクライアント指定、提出・承認・差戻し・却下・反映操作、5 Custom APIの自動呼出し、直接CRUDはこの画面へ持ち込まない。R3の提出・判定・反映は、登録済みCustom APIを前提にせず、標準の申請／提出版／連携行の状態遷移と同期検証へ再設計する。画面のライブ読取りは明示操作でのみ開始し、失敗時はモックへ戻さない。Power Apps Local Playの認証セッション、実ユーザーの行権限、実データ表示、DecisionFlow接続は未受入であり、このローカル境界だけをクラウド総合合格とは扱わない。

<a id="custom-api-zero-data-model"></a>

## R2 Custom APIゼロの論理データ境界（2026-09-14、標準CRUD経路を適用・公開済み）

この節の物理スキーマ部分、標準Create／共有設定同期Pluginの登録、PCの新CRUD切替、対象限定公開、公開PC UIの標準共有設定Create、承認者M365グループTeamのInitialSetup許可／拒否は開発環境で確認済みである。旧書込み経路の停止、子テーブル共有波及は未実施で、既存API・Role・データ・DecisionFlowは維持している。

R2では、`pl_Partner`を通常のUserOwned台帳行のまま本人接続の標準Createで受け付ける。保存時にサーバーが設定する「初期共有設定中／設定完了」の最小状態列、登録要求キー、同一要求判定用ハッシュを使い、クライアントが状態・正規化値・ハッシュを権威値として指定できない契約を先行固定した。要求キーだけをクライアント入力として受け、ハッシュは会社・主担当・登録者・入力項目からサーバーが算出する。別要求の同名会社は許容し、要求キーだけを再送保護に使う。

会社共有は、論理的な**共有設定行**を利用者の直接共有意図の正本とし、同期プラグインがDataverseの標準共有へ投影する。開発環境の読み取りでは、既存`pl_AccessGrant`はUserOwned・監査オフ・親共有`NoCascade`で、旧プロファイル合算／期限／ACL版／投影状態の列を持ち、要求キー／内容ハッシュ／保護経路の由来を持たない。`pl_Partner`にも再送用Alternate Keyはない。よってR2では旧表を再定義せず、**`pl_PartnerShareSetting`（仮スキーマ名）の最小UserOwnedテーブルを新設する**ことを推奨する。旧`pl_AccessGrant`のプロファイル合算、期限、ACL版、OperationLogをR2の必須列としない。一方で、既存行・列・APIをこの設計変更だけで削除・変更しない。

共有設定行には、少なくとも親取引先、対象principal（Userまたは既存Dataverseグループチームの排他指定）、付与する最小アクセス種別、現在状態、保護された承認者経路かどうか、要求キー、サーバー算出内容ハッシュ、サーバー導出の`pl_ismanagedprojection`を持たせる。取消は物理Deleteではなく状態遷移で扱う。グループの候補は、既存のMicrosoft Entra Security group TeamまたはMicrosoft Entra Office group Teamに限る。アプリは共有設定を保持するだけで、Entra／Microsoft 365グループそのものやメンバーシップの作成・変更はしない。`pl_Partner`へ追加候補の初期共有状態・登録要求キー／サーバー算出内容ハッシュ、共有設定行の要求キーにはAlternate Keyを作り、インデックスが有効になるまで受入を開始しない。共有設定行の物理候補は、`pl_partnerlookup`、`pl_principalkindcode`、`pl_userprincipallookup`／`pl_teamprincipallookup`、`pl_accesslevelcode`、`pl_settingstatuscode`、`pl_isprotectedmanagementpath`、`pl_ismanagedprojection`、`pl_requestkey`、`pl_contenthash`とする。`pl_ismanagedprojection`は直接共有の由来をサーバー側で管理できる新規行だけtrueとし、追加前の既存行・外部共有はfalse／未照合として権利縮小を保留する。共有設定行そのものを会社アクセス保持者が安全に読めるか、親子RelationshipのShare／Unshare連鎖で足りるかは実機で確認する。足りなければ、同じ同期プラグインが最小Readを投影するが、利用者が親の共有と設定行を別々に直接操作できる経路は作らない。

Auto-created Access Teamは採用しない。行ごとのTemplateと個人メンバー管理を増やす方式であり、Userと既存Entra Security／Office group Teamを同じ画面で選んで直接共有するPL-030には不要である。グループのメンバーシップはEntraの正本に委ね、DataverseではそのGroup Teamを共有先principalとして使う。

2026-09-14の追加確定：ユーザー指定のM365グループ`DecisionFlow-Deciders`に対応するDataverse Office group Teamを、`PL 承認`Roleの割当先かつ初期共有の保護対象として採用した。`pl_isprotectedmanagementpath`は初期設定中の全行へ一律に設定せず、サーバー解決済みの承認者Teamに一致するTeam共有行だけへ設定する。登録後は作成者に限定せず、対象会社への実効アクセスを持つメンバーが通常の共有設定を変更・取消できるが、本人の実効権を超える付与、保護経路の変更、未照合旧ACLの縮小は拒否または保留する。現在のスキーマに会社ごとの承認者Team lookupは追加しない。M365 group Teamのメンバー同期後、InitialSetupの許可／拒否を実機確認済みで、旧Owner Teamは比較用に残置する。

現在の開発環境は組織監査と`pl_Partner`／`pl_AccessGrant`／`pl_Contact`のテーブル監査がすべてオフである。標準Dataverse監査を採用するなら、Share／Modify Share／Unshareと設定行のCreate／Updateを要件どおり追跡できるかを、組織設定・対象テーブル・保持期間・閲覧者を明示した別承認で確認する。それまでR2の共有履歴に標準監査があると仮定しない。環境全体の監査設定を変更しない判断なら、既存`pl_OperationLog`を限定業務ログとして比較する。

## 1. 設計判定

専用Dataverseテーブルを正本とする方針は妥当。Astra判定は、段階2の設計検証へ進む条件付きGO。本実装用スキーマの確定前に、状態の分離、サーバー側の原子性、DecisionFlowとの版対応、Canvas同期、OCR結果不明時の扱いを実測する。

取引先・担当者はCRM標準の`account`／`contact`に置かない。将来のCRM連携は、PartnerLedger専用テーブルを正本にした一方向連携として別計画で設計する。DecisionFlow本体と既存ソリューションは変更しない。

日付付きの旧設計では共通業務操作をDataverse Custom API＋同期プラグインの第一候補としていたが、R2以降の新規実装には使わない。通常CRUDや状態変更からの迂回を閉じられること、複数行の確定単位、トランザクション境界をCustom APIゼロの受入（開発記録、非公開）で確認してから、標準行操作に結び付く最小の同期プラグインだけを採用する。Custom APIを作るだけでは承認・監査・不変項目の保護を満たさない。

## 2. 論理テーブル

物理論理名は`pl` prefixで作成済み。21テーブルの主要な文字列・数値・日時列を骨格として適用した。取引先・担当者・契約の利用者向け状態4列はChoiceとして検証用に適用した。内部処理状態のChoice化、UserOwnedを前提とした権限適用、Lookup、Relationshipの連鎖動作は段階2で確定する。現時点では5本のLookupと4列の業務状態Choiceを検証用に適用したが、設計上の最終確定扱いにはしない。

| 論理テーブル | 責務 | 主なLookup／関連 | 状態・不変条件 |
| --- | --- | --- | --- |
| 取引先 | 会社情報、正規化名、取引状態、確認状態、主担当、登録元 | 主管Team、主担当User | 取引状態と会社確認状態を分離。終了はアーカイブ表示で、物理削除しない |
| 担当者 | 氏名、部署、役職、連絡先、担当者固有の登録者・登録日 | 取引先（必須。登録後不変） | 在籍／休職／退職。退職はアーカイブ表示 |
| 契約 | 契約名、種類、終了日、解約通知期限、更新判断期限、自動更新、外部リンク | 取引先、契約種類、主管Team | 1社に複数契約。同じ契約種類も複数可。期限超過は状態と別の算出表示 |
| 名刺取込バッチ | 単票／一括の受付単位、処理順、件数 | 登録User | 受付中／受付完了。画像明細から進捗を集計 |
| 名刺取込 | 不変の取込ID、現行入力版、登録先、登録完了記録 | バッチ、取引先、担当者 | 未登録／登録済み。取込ID＋入力版でOCRジョブを一意化 |
| 名刺入力版 | 画像と端末入力。送信後の入力を固定 | 名刺取込 | 下書き／送信要求／固定済み／旧版 |
| OCR処理・結果 | 入力版ごとのジョブ、OCR原文、試行、エラー | 名刺入力版 | 画像待ち／待機／処理中／確認待ち／失敗／結果不明 |
| 名刺確認版 | 人の修正値、選択会社、確認者・時刻、登録内容の固定 | 入力版、OCR結果、取引先 | 確認編集中／確認確定。確定版から会社・担当者登録を一体で行う |
| 申請 | 取引先変更・契約判断の共通管理 | 取引先または契約、申請User、主管Team | 下書き／提出中／差し戻し／承認／却下／取消 |
| 提出版 | 変更前後、対象row version、ポリシー版、提出者・時刻 | 申請、導入設定版 | 作成後不変。判断状態と反映状態を分離 |
| 申請閲覧付与（旧方式・移行対象） | 旧方式の申請単位付与履歴。改訂PL-031の新規発行は不要 | 申請、提出版、承認者User／Team | 既存テーブル・行は保持。新方式の閲覧判定に用いない切替を別途検証 |
| 重複確認依頼（初回リリース対象外） | 旧登録処理の保留記録。新リリースでは発行しない | 既存の登録要求・候補取引先参照は保持 | テーブル・行は削除せず、確認者再開処理も今回追加しない |
| 承認連携記録 | 提出版とDecisionFlow申請の対応、結果照合 | 提出版、`ds_application` | 送信待ち／連携済み／結果確認済み／連携不明・失敗 |
| 操作・反映記録 | 冪等キー、要求識別値、結果ID、反映状態 | 提出版または名刺確認版、対象レコード | 受付／完了／競合／失敗。業務操作者と実行主体を区別 |
| 掲示板投稿 | 本文、投稿者、投稿時刻、再送識別子 | 取引先または担当者のどちらか一方 | 追記済み。編集・削除なし |
| 取引先アクセス付与 | 取引先ごとのUser／Team、アクセスプロファイル、委譲可能範囲、期間、付与理由、ACL版 | 取引先、対象UserまたはTeam | 有効／取消。Dataverse共有への投影記録と監査を保持 |
| 契約種類 | 表示名、表示順、選択可否 | なし | 使用可能／廃止。使用済みの物理削除は避ける |
| 導入設定・設定版 | 承認対象、通知条件、運用設定 | 更新User | 準備中／有効／旧版。過去版を上書きしない |
| 処理リース | OCRの実行枠、ワーカー、期限、世代 | OCR処理 | 空き／取得中／失効・照合待ち |
| 通知・外部送信記録 | 送信予定、宛先、対象期限、試行と結果 | 契約、名刺、提出版等 | 待機／送信済み／失敗／結果不明 |

### オフライン機能の廃止（2026-09-22）

モバイルはオンライン専用とし、端末保存・選択同期・圏外閲覧を採用しない。旧`pl_OfflineSelection`は開発環境とSolution正本から削除済みであり、DecisionFlow本体は変更していない。

### 状態の分離

現在の要件にある「新規取引先は取引状態『未承認』」「名刺由来の未知会社は状態『未確認』」を同じ列へ詰め込まない。次の2軸で表現し、画面では必要に応じて複合ラベルを表示する案を採用候補とする。

- `tradingStatus`：未承認／取引中／休止中／終了
- `identityConfirmationStatus`：未確認／確認済み

新規取引先の初期値は`tradingStatus=未承認`。名刺確認後に会社の同一性が未確定なら、`identityConfirmationStatus=未確認`のまま保存API経由で登録する。`未承認`を取引中集計へ含めず、`終了`はアーカイブへ表示する。この二軸は要件上の表示と集計を両立するための設計案であり、段階2で利用者レビューを行う。

### 状態列の棚卸しと選択肢化方針

初期骨格では、ユーザー向け状態と内部処理状態を`nvarchar`の暫定列として作成していた。保存済み列のデータ型は後から変更できないため、既存行がないこととフォーム／ビューの依存を確認したうえで、4列を削除・Choice再作成した。内部処理状態は、画面表示とサーバー状態機械の契約を先に固定してから個別にChoice化する。

| 区分 | 対象列 | 初期値・候補 | 方針 |
| --- | --- | --- | --- |
| 業務状態 | `pl_Partner.pl_TradingStatusCode` | 未承認／取引中／休止中／終了 | Choice適用済み。取引先画面・集計で使う |
| 同一性確認 | （廃止）`pl_Partner.pl_IdentityStatusCode` | — | 2026-09-26に列ごと削除（ユーザー判断）。確認済みにする機能が初回リリースに無く、常に未確認だったため。未承認かどうかは取引状態で分かる |
| 担当者状態 | `pl_Contact.pl_StatusCode` | 在籍／休職／退職 | Choice適用済み。退職はアーカイブ表示 |
| 契約状態 | `pl_Contract.pl_ContractStatusCode` | 締結済み／終了 | Choice適用済み。期限3種と分離する |
| 内部処理状態 | `pl_CaptureBatch.pl_BatchStatusCode`、`pl_CardCapture.pl_CaptureStatusCode`、`pl_CardInputVersion.pl_ImageStateCode`、`pl_CardInputVersion.pl_InputStateCode`、`pl_OcrJob.pl_JobStatusCode`、`pl_BusinessCardReview.pl_ReviewStatusCode`、`pl_Request.pl_RequestStatusCode`ほか | 受付・同期・OCR・確認・申請の有限状態 | 画面表示とサーバー状態機械の契約を先に固定し、Choice化対象を個別に決定 |

Choiceの値は`100000000`以降のPartnerLedgerローカル値とし、2026-09-11に日本語ラベルを含めて検証環境へ適用した。`pl_ContractType`の名称は契約種類マスタの表示名として文字列を維持し、契約行からLookupする。`状態`という表示だけで業務状態と内部処理状態を混在させない。内部処理状態のChoice化、API／フローの参照列、状態遷移ガードは、クラウド変更前に設計承認と自動テストを行う。

### 内部処理状態の遷移契約（Choice化前の設計固定）

内部状態はUIの表示ラベルではなく、サーバー側の処理主体が更新する有限状態とする。以下を段階2の実装契約とし、現時点ではクラウドの内部状態列を`nvarchar`から変更しない。Choice化は、値・遷移・API／フローの参照を実装テストで固定した後に、テーブル単位で行う。

| テーブル／列 | 状態値 | 状態を更新できる主体 |
| --- | --- | --- |
| `pl_CaptureBatch.pl_BatchStatusCode` | 受付中／受付完了／処理中／完了／一部失敗／失敗 | 受付API・OCRワーカー・再集計処理 |
| `pl_CardCapture.pl_CaptureStatusCode` | 未登録／処理中／確認待ち／登録済み／失敗／結果不明 | 受付API・OCRワーカー・人の登録確定処理 |
| `pl_CardInputVersion.pl_ImageStateCode` | 端末保存済み／同期待ち／画像到着確認済み／画像失敗 | Canvas同期・サーバー画像確認処理 |
| `pl_CardInputVersion.pl_InputStateCode` | 下書き／送信要求／固定済み／旧版 | 入力受付API・版固定処理 |
| `pl_OcrJob.pl_JobStatusCode` | 待機／処理中／成功／失敗／結果不明／取消 | OCRキュー・作業リース処理 |
| `pl_BusinessCardReview.pl_ReviewStatusCode` | 確認編集中／確認確定 | 確認画面の共通業務操作 |
| `pl_Request.pl_RequestStatusCode` | 下書き／提出中／承認済み／差戻し／却下／取消／反映待ち／反映済み／反映失敗 | 申請API・DecisionFlow連携・反映処理 |
| `pl_SubmissionVersion.pl_SubmissionStatusCode` | 下書き／提出済み／承認済み／差戻し／却下／取消／反映待ち／反映済み／反映失敗 | 版固定処理・承認照合・反映処理 |
| `pl_ApprovalGrant.pl_GrantStatusCode` | 有効／終了／取消／期限切れ | 申請受付・承認完了・失効処理 |
| `pl_ApprovalLink.pl_LinkStatusCode` | 送信待ち／連携済み／結果確認済み／連携不明／連携失敗 | DecisionFlow連携・照合処理 |
| `pl_DuplicateReview.pl_ReviewStatusCode` | 確認待ち／同一法人／別法人／却下 | 重複確認担当者の共通業務操作 |
| `pl_AccessGrant.pl_GrantStatusCode` | 有効／取消／期限切れ | アクセス管理の共通業務操作 |
| `pl_OfflineSelection.pl_SelectionStatusCode` | 選択／解除 | 利用者の選択操作 |
| `pl_WorkLease.pl_LeaseStatusCode` | 空き／取得中／失効／照合待ち | ワーカーのリース処理 |
| `pl_Notification.pl_NotificationStatusCode` | 待機／送信済み／失敗／結果不明 | 通知ワーカー・結果照合処理 |
| `pl_OperationLog.pl_OperationStatusCode` | 受付／完了／競合／失敗 | 共通業務操作・反映処理 |

遷移の共通ルールは次のとおりとする。

- 画像は`端末保存済み → 同期待ち → 画像到着確認済み`の順で進める。到着確認前にOCRジョブを作らず、画像失敗は再送判断へ送る。
- 入力版は`下書き → 送信要求 → 固定済み`で、固定後は画像・入力・OCR対象を変更しない。修正して再処理する場合は新しい入力版を作り、旧版と過去結果を保持する。
- OCRは`待機 → 処理中 → 成功／失敗／結果不明`とする。処理中の件数は作業リースを持つ1件に限定し、結果不明を成功・失敗・未実行へ自動変換しない。失敗の再試行は同じ取込IDを維持し、新しいジョブ試行として記録する。
- OCR成功は名刺を`確認待ち`にするだけで、取引先・担当者を登録しない。人が確認確定した版だけを登録確定処理へ渡し、登録完了時に`登録済み`へ進める。
- バッチは明細の集計で`処理中／完了／一部失敗／失敗`を算出する。1件の失敗で後続を停止せず、全明細が終端状態になった時だけバッチを終端化する。
- 申請は`提出中 → 承認済み／差戻し／却下／取消`を経て、反映前に`反映待ち`へ進める。差し戻し後の再提出は`差戻し → 提出中`を許可し、新しい提出版として履歴を追加する。反映時は申請版、ポリシー版、対象row versionを再照合し、成功時だけ`反映済み`とする。反映失敗の永続化と再試行ワーカーは別の復旧経路が必要であり、現行の標準Power Automateグループ経路では同期トランザクション全体をロールバックする。フロー実行中は同じLink・同じ判断値を1回だけ再試行し、2回とも失敗した場合は部分成功にせず運用介入へ送る。
- 承認連携のタイムアウトは`連携不明`とし、同じ申請を盲目的に再送しない。照合結果を確認してから再送または運用介入を選ぶ。
- クライアントは内部状態を直接確定できない。状態遷移と副作用は共通業務操作で検査し、同一要求の再送は同じ結果を返す。異なる内容を同じ冪等キーで送る要求は拒否する。

#### 状態契約に対応する先行テスト

| テストID | 守る条件 | 機械ゲート |
| --- | --- | --- |
| ST-01 | 定義した遷移だけを許可し、逆戻り・終端後の更新を拒否する | 状態機械の単体テスト |
| ST-02 | 画像到着確認前のOCR、OCR成功だけの登録、クライアント指定状態を拒否する | API境界テスト |
| ST-03 | 同じ取込ID・入力版・冪等キーの再送がジョブ・申請・反映を増やさない | 冪等性テスト |
| ST-04 | ワーカー同時実行、リース期限切れ、結果到着順逆転でも1件だけを処理中にする | 並行実行テスト |
| ST-05 | 結果不明を成功／失敗へ自動変換せず、照合・再試行の記録を残す | 障害復旧テスト |
| ST-06 | バッチの一部失敗後も後続明細を処理し、集計状態を正しく終端化する | バッチ統合テスト |
| ST-07 | 提出版・ポリシー版・row versionが一致しない反映を拒否し、古い承認で上書きしない | 反映拒否テスト |
| ST-08 | 固定済み入力版・提出版・監査記録を上書きせず、修正は新しい版になる | 不変性テスト |

### 冪等キーとAlternate Keyの候補

開発環境で候補列の存在、文字列型、既存Alternate Keyが空であることを読み取り確認した。候補列は現在すべて任意入力（`RequiredLevel=None`）であり、キーを作ってもNULL値の行には一意性が効かない。したがって、必須値をサーバーで確定する業務操作と、空値・スコープ・再試行単位を自動テストで固定するまで、Alternate Keyはクラウドへ追加しない。[Alternate Keyの制約](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-reference-records)

| 候補列 | 想定する一意性 | 判断 |
| --- | --- | --- |
| `pl_CaptureBatch.pl_BatchKey` | バッチ1受付単位 | 追加候補。受付APIが必ず生成し、同じ値を再送で再利用する |
| `pl_CardCapture.pl_CaptureKey` | 撮影・画像1件 | 追加候補。単票・一括で共通生成し、再送で変更しない |
| `pl_OcrJob.pl_JobKey` | OCRジョブ1試行 | 追加候補。再試行は新しいジョブキーで、取込IDとの対応を別に保持する |
| `pl_Request.pl_RequestKey` | 申請1件 | 追加候補。提出版の再送と申請の新規作成を分離する |
| `pl_OperationLog.pl_IdempotencyKey` | 共通業務操作1要求 | 追加候補。ただしキーのグローバル一意性を採用するか、操作種別を含む複合キーにするかを先に確定する |
| `pl_WorkLease.pl_LeaseKey` | リース1取得記録 | 保留。失効・再取得の履歴を同じ値で表すのか、新しいリース行ごとに値を変えるのかを確定する |
| `pl_ApprovalLink.pl_ExternalRequestKey` | 外部DecisionFlow連携1件 | 保留。外部側の再送・複数リンクの仕様を確認するまで一意と断定しない |
| `pl_ApprovalLink.pl_RequestKey`、`pl_DuplicateReview.pl_RequestKey` | 申請との参照・確認依頼の対応 | 追加しない。1申請に複数の連携記録／確認履歴が生じ得るため、単独キーにはしない |

Alternate Keyを追加する段階では、対象列をサーバー必須化し、空値の既存行がないことを確認してから、テーブルごとに非破壊追加・インデックス作成完了・Solution export／unpackを確認する。作成後も、UIの重複検知だけを根拠にせず、APIのupsert／再送テストで一意性を検証する。

### LookupとRelationshipの読み取り結果

2026-09-11に開発環境の5本のPartnerLedger間Relationshipを読み取り確認した。いずれも親の共有・共有解除は子へ連鎖せず（`Share=NoCascade`、`Unshare=NoCascade`）、削除・アーカイブ時は子を削除せずLookupを外す（`Delete=RemoveLink`、`Archive=RemoveLink`）設定だった。

| 親 → 子 | Lookup | 読み取り結果 | 設計上の扱い |
| --- | --- | --- | --- |
| 取引先 → 担当者 | `pl_PartnerLookup` | Delete／Archive: RemoveLink、Share／Unshare: NoCascade | 取引先終了は状態変更。物理削除は設けず、担当者の所属を自動変更しない |
| 取引先 → 契約 | `pl_PartnerLookup` | Delete／Archive: RemoveLink、Share／Unshare: NoCascade | 契約履歴を会社共有から暗黙公開しない。会社単位のアクセス付与だけで契約権限を与えない |
| 取引先 → 名刺取込バッチ | なし | 今回の5本には含まれない | バッチと会社の紐付けは登録確定後の設計で追加する |
| 契約 → 契約種類 | `pl_ContractTypeLookup` | Delete／Archive: RemoveLink、Share／Unshare: NoCascade | 契約種類マスタの廃止はLookupを壊さず、既存契約を保持する |
| 取込バッチ → 名刺取込 | `pl_CaptureBatchLookup` | Delete／Archive: RemoveLink、Share／Unshare: NoCascade | バッチ削除を業務操作にせず、明細の監査・再送履歴を保持する |
| 名刺取込 → 名刺入力版 | `pl_CardCaptureLookup` | Delete／Archive: RemoveLink、Share／Unshare: NoCascade | 入力版を親の共有から自動公開せず、名刺画像・版の権限を個別に評価する |

上表の5つの実Relationship行は、取引先→担当者、取引先→契約、契約→契約種類、取込バッチ→名刺取込、名刺取込→名刺入力版を表す。`RemoveLink`は物理削除を安全にする設定ではなく、誤削除時に親子の結び付きを外す挙動である。削除・アーカイブはUI/APIから拒否する方針を前提に、TV-06／TV-13で共有・取消・終了時の実効権限と、失敗時の孤児化防止を確認する。今回確認した5本以外の名刺確認・OCR・申請関連の関連、実データを使った連鎖挙動は未確定である。

## 3. 状態・操作契約

| 操作 | 許可・評価時点 | 拒否・不変条件 |
| --- | --- | --- |
| 新規取引先登録 | 登録権限を確認し、初期`tradingStatus`をサーバーで未承認に固定 | クライアント指定の取引中・休止中・終了を拒否。同じ要求の再送は同じ結果を返す |
| 会社内容が一致する登録 | PL-006/032は初回対象外。候補照会を行わず、同名・同内容の別要求も登録する | 既存候補の詳細・ID・件数を返さず、自動統合もしない。同一要求の二重保存防止は新規登録契約で維持 |
| 取引先変更 | 変更項目ごとに承認設定を評価。直接更新と申請差分を分ける | 承認対象項目の直接更新、別項目への波及、監査なし更新を拒否 |
| 承認済み反映 | 提出版、設定版、対象row version、実行権限を反映直前に照合 | 古い承認、別申請、二重反映、競合時の自動上書きを拒否 |
| 担当者編集 | 所属取引先を維持して更新。退職は同じレコードをアーカイブ表示 | 所属、登録者、登録日を変更不可。所属変更は旧担当者を退職、新規担当者を登録 |
| 取引先終了 | 取引状態変更ポリシーに従い、承認または直接処理 | 関連担当者・契約を自動で終了させない。終了前にアーカイブしない |
| 契約登録・判断 | 初回登録は直接。更新・終了判断は契約ID単位で設定を評価 | 会社単位の一括判断、別契約への反映、自動更新フラグだけの期間延長を拒否 |
| 名刺受付 | 画像到着と入力版をサーバー確認後に固定してキュー化 | 画像なし投入、行だけ先行したOCR、固定後の画像・入力差替えを拒否 |
| OCR実行 | 取込ID＋入力版のジョブと作業リースを取得。成功は確認待ち、明確な失敗は失敗 | 古いワーカーの結果、リース世代不一致、同じ入力版の二重ジョブを拒否 |
| OCR結果不明 | 照会または運用上の再試行判断へ送る | タイムアウトを未実行と断定しない。外部呼出しの絶対一回実行は未保証として扱う |
| 名刺確認・登録 | 人が確定した版から、必要な会社・担当者・登録完了記録を一体で確定 | OCR成功だけの自動登録、確認応答消失後の二重登録を拒否 |
| 掲示板投稿 | 対象台帳の閲覧・投稿権限を検査し、投稿者・時刻をサーバー確定 | 投稿者偽装、対象差替え、既存投稿編集、同一要求の重複投稿を拒否 |

### 権限付き重複照会（旧案・初回リリース対象外）

この小節は後回しにしたPL-006/032の旧案であり、初回リリースに実装しない。旧サーバーの候補照会・拒否・Review作成／保留をUIとともに止めることが切替条件。正規化の既存列・検索用途・他の入力検証まで削除するものではない。

新規登録画面から会社名・住所・代表電話などを受け取ったら、クライアントの一覧検索ではなくサーバー側の正規化照会を行う。照会結果は「登録者が閲覧できる候補」と「存在を開示できない候補」を分ける。

- 閲覧権限のある候補は、会社名・所在地の要約・状態など、登録判断に必要な最小情報だけを表示する。選択して既存取引先へ紐付けるか、重複確認依頼を提出できる。
- 権限外の候補は、会社名・住所・電話・正確な件数・一致した項目を返さない。存在を推測できるエラーや「権限外に1件」といった表示も避け、画面では中立的な状態だけを示す。
- 権限外候補が疑われる登録要求は、重複確認依頼として保留し、新しい取引先を先に作らない。権限を持つ確認者が同一法人か別法人かを判断し、別法人と判断された場合だけ新規取引先を`未承認`で作成し、登録者へ初期アクセスを付与する。
- 同名企業を名前だけで統合しない。正規化キーは候補抽出用であり、一意制約や自動統合の根拠にはしない。並行登録・再送は要求IDと照会結果を冪等化し、同じ候補に複数の取引先行や確認依頼を作らない。
- 高権限のサーバー処理で全候補を取得して画面側で隠す方式は採用しない。候補要約、保留状態、確認者向けの詳細表示をAPIの返却契約として分離し、PC・Canvas・APIで同じ開示境界を適用する。

同じ冪等キーで内容が同じ再送は既存結果を返し、内容が異なる再送は拒否する。通信再送と、入力修正による新しい版・新しい提出は別物として扱う。

## 4. 取引先単位のアクセス制御

2026-09-28改訂：初期共有は登録と同時にサーバーが完了する（承認者Teamの保護行と、主担当が登録者と別なら主担当の編集行を作り、共有し、「設定完了」にする。設計は実装計画（開発記録、非公開））。以下の「承認者がアプリで初期共有を設定する」は、開発環境に残る「初期設定中」の旧行にだけ当てはまる。

現在の要件は[改訂PL-030](requirements.md#company-sharing-policy)。一般利用者も登録し、初期共有を承認者グループがアプリで設定する。登録後は会社アクセス保持者も、個人・セキュリティグループ・M365グループへの共有を設定できる。専用管理者プロファイルは必須にせず、共有管理と基本編集の権限を区別する。共有先グループはDataverseのEntraグループチームとして扱い、グループ本体・所属をアプリで管理しない。初期共有の認可、承認者の管理経路、権限上限、取消後の実効権は実装前の試験表（開発記録、非公開）に従う。

Dataverseの標準セキュリティロールは権限の土台、取引先ごとのアクセス付与は初版ではサーバー側の明示共有を基本方式とし、システム管理Access Team Templateは代替候補とする。一般利用者にPartner／担当者の組織全体Readを付与しない。権限付与は累積するため、組織全体Readがあると会社単位の拒否を実現できない。[Dataverseのセキュリティ概念](https://learn.microsoft.com/power-platform/admin/wp-security-cds#table-record-ownership)

### 旧実装モデルと監査履歴（移行比較用）

以下の§4内のプロファイル・API・監査契約は旧実装の記録であり、新要件の採用方式を固定しない。「閲覧者・編集者は共有不可」「登録者だけ初期管理」は改訂PL-030では引き継がない。一方、会社分離・権限超過拒否・監査・再送と途中失敗の保護は必要。標準共有への切替で必要な部分を再利用し、独自台帳と標準共有を二重の書込み正本にしない。既存付与・期限・データは黙って失効・削除しない。

- `取引先アクセス付与`テーブルに、取引先、対象UserまたはTeam、アクセスプロファイル、開始・終了、付与理由、状態、登録者・日時を保持する。User／Teamのどちらか一方だけを指定し、変更は管理者または専用の共通業務操作だけに許可する。
- 新規取引先の作成では、登録者にだけ直接の「基本編集＋アクセス管理」プロファイルを付与する。全利用者や既定グループへの自動共有は行わず、登録者が必要なUser／TeamをUIから追加する。名刺確認から登録する場合も、登録完了を実行した利用者を初期のアクセス管理者とする。
- 初版の取引先ごとの共有は、既存のUser／Teamを対象にしたサーバー側の明示共有を基本方式として固定する。`GrantAccess`／`ModifyAccess`／`RevokeAccess`で会社行への最小権限を投影し、`pl_AccessGrant`を意図・期限・プロファイル・監査の正本として扱う。システム管理Access Team Templateは、対象テーブルの有効化、テンプレート、行ごとのチーム生成を伴うため、初版では採用せず、必要性が確認できた場合の代替方式とする。[Dataverseの共有操作](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/security-sharing-assigning)／[アクセスチームと所有チーム](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-access-teams-owner-teams-collaborate-share-information)
- 取引先・担当者は基本台帳アクセス、契約・申請・名刺画像は追加の制限付きアクセスとして扱う。取引先の共有だけで子テーブルが公開されるとは想定せず、各テーブルの共有とRelationshipの連鎖動作を検証する。
- 通常ユーザーにはShare／Assign等の迂回権限を与えず、アクセス付与・取消・アーカイブ時の扱いを共通業務操作で監査する。新規取引先は作成直後に登録者と主管チームへ必要なアクセスを与える。

### 方式確定（2026-09-11読み取り後）

初版は「User／Teamへのサーバー側明示共有＋`pl_AccessGrant`の業務記録」を基本方式とする。開発環境ではAccess Team Templateが0件で、PartnerLedgerのテーブルもシステム管理Access Teamを有効化していない。公式仕様上、Access Team Templateは対象テーブルの有効化とテンプレートによる行専用チーム生成が必要で、既存チームを複数行へ使う要件とは別の仕組みである。

- アプリは既存のDataverse User／Teamだけを候補にし、Entra／Microsoft 365グループ本体の作成・メンバー変更は行わない。グループはDataverse Teamを共有先の主体として扱う。
- サーバー側の共通業務操作が、対象会社・対象User／Team・プロファイル・期間・委譲可能範囲・現行ACL版を検査して、共有反映と`pl_AccessGrant`の状態を一体の業務結果として確定する。再送は付与IDと対象row versionで冪等にする。
- `GrantAccess`／`ModifyAccess`／`RevokeAccess`はUser／Teamを共有主体にできるが、付与できる権利は対象テーブルのSecurity Roleで許可された範囲を超えない。一般利用者へShare／Assignを直接付与せず、専用サーバー処理主体へだけ共有操作を許可する。[PrincipalAccess](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/principalaccess)／[ModifyAccess](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/modifyaccess)
- `pl_AccessGrant`は共有権そのものの代替ではなく、付与意図・プロファイル・期間・取消理由・結果・監査を保持する。記録だけあって共有が無い、共有だけあって記録が無い、という途中失敗を再送・照合で検出する。
- Access Team Templateは、会社ごとに行専用チームを自動生成する必要があり、今回の「既存User／Teamを会社単位で追加・変更・取消」「プロファイルと期限を業務記録で管理」という初版要件には過剰なため保留する。性能・運用・実効権限の検証で明示共有が成立しない場合だけ、対象テーブルの有効化とテンプレート作成を別計画にする。

### `pl_AccessGrant`実体照合と再計画（2026-09-12）

開発環境と展開済みSolutionを読み取り照合した結果、現行`pl_AccessGrant`には`pl_AccessProfileCode`、`pl_DelegationCode`、`pl_StartAt`、`pl_EndAt`、`pl_GrantStatusCode`、`pl_Name`と標準列しかなく、設計で必要な会社Lookup、対象User／Team、付与理由、ACL版、共有投影結果を保持できない。このため、アクセス管理の実装は開始せず、次の最小スキーマを追加候補として固定した。

**2026-09-12に以下を開発環境へ適用済み。** 適用時、`pl_AccessGrant.pl_StartAt`／`pl_EndAt`が`Format=DateOnly`のまま`DateTimeBehavior`未設定という既存の不備（他テーブルで先に発見・修正したものと同種、今回のみ直し漏れ）を検出し、`DateTimeBehavior=DateOnly`に補正してから列・関係を追加した。両テーブルとも適用前0行のためデータ影響なし。共通業務操作の実装、フォーム／ビューへの反映、Security Role・Access Team・DecisionFlow・ユーザー割当・公開・Solution importは未実施。

| 対象 | 追加候補 | 制約 |
| --- | --- | --- |
| `pl_AccessGrant` | `pl_PartnerLookup` → `pl_Partner` | 必須。付与対象会社を固定し、別会社への付替えを拒否 |
| 同上 | `pl_UserLookup` → `systemuser`、`pl_TeamLookup` → `team` | User／Teamのどちらか一方だけを指定 |
| 同上 | `pl_PrincipalKey` | サーバー生成の主体種別＋ID。同一会社・主体・プロファイルの重複を防止 |
| 同上 | `pl_Reason` | 付与・変更・取消の理由。過去の理由は`pl_OperationLog`にも保存 |
| 同上 | `pl_AclVersion`、`pl_AppliedAclVersion` | 会社の確定ACL版と、共有へ反映できた版を分離 |
| 同上 | `pl_ProjectionStatusCode` | 未反映／一致／不一致／要照合。業務上の有効／取消とは分離 |
| `pl_Partner` | `pl_AclVersion` | 会社単位の共通競合点。アクセス変更ごとに更新 |

`createdby`／`createdon`／`modifiedby`／`modifiedon`、主キー、標準row version／ETag、`ownerid`は標準列を利用する。ただし、画面を操作した利用者とサーバー実行主体が異なる場合の業務操作者、冪等キー、要求ハッシュ、変更前後、付与ポリシー版は`pl_OperationLog`または専用列で保持する。`ownerid`は共有先のUser／Teamや業務上の登録者の代用にしない。

付与の業務単位は「会社×主体×プロファイル」とする。同一主体に「基本編集」と「アクセス管理者」を併存でき、実共有権は有効な付与の合算から計算する。取消後の再付与は新しい履歴として扱う。開始・終了は`開始日時 ≤ 現在時刻 < 終了日時`の半開区間で判定し、会社ACL版が変わった場合は共有投影せず再取得する。

#### 会社アクセス操作の状態契約

| 操作 | 許可・評価 | 拒否・失敗時の不変条件 |
| --- | --- | --- |
| User／Team追加 | 対象会社のアクセス管理権、主体の有効性、プロファイル・委譲・期間上限、現行ACL版をサーバーで再評価 | User／Team両指定・両未指定、閲覧者の操作、自己昇格、権限上限超過、別会社への付替えを拒否 |
| プロファイル変更 | 対象付与と会社ACL版を照合し、変更後の共有権を再計算 | 古いACL版、権限外プロファイル、クライアント指定の実行者を信用しない |
| 取消 | 残る有効付与から実共有権を再計算し、必要権利がゼロのときだけ共有解除 | 最後のアクセス管理者、他の付与が持つ権利、Team経由の実効権を誤って消さない |
| 期限失効 | `pl_EndAt`到達時に最新ACLと実共有を照合 | 業務列だけ更新して失効済みと扱わない。定期処理停止時の直接取得可否は別検証 |
| 再送・競合 | 同一冪等キーかつ同一内容なら確定済み結果を返し、会社ACL版一致時だけ投影 | 同じキーの内容違い、古いACL版、同時更新を無条件確定しない |
| 途中失敗 | 共有、付与記録、ACL版、成功監査の確定範囲を一体で扱う | 一部だけ共有された状態を成功扱いしない。結果不明は照合待ちにする |

この契約を`apps/pc/src/accessGrantContract.ts`とテストで先行固定した。これはDataverseの実効認可・共有API・トランザクションの合格を意味しない。

この方式の実効性は、実際の`pl_partner`行、非管理者User、既存Team、最小Security Roleを使ったTV-06／TV-08で確認する。2026-09-11に合成行2件・一時Security Role・既存User／Teamへの共有を使ったTV-13限定試験を行い、共有APIの付与・変更・取消と共有一覧を確認した。一時資産は削除済みで、非管理者の実効データ読取り、共通業務操作による境界、Canvas／API parityは未検証である。

### アクセスプロファイル案

| プロファイル | 取引先・担当者 | 契約・期限 | 名刺・申請 | 掲示板 | 付与主体 |
| --- | --- | --- | --- | --- | --- |
| 閲覧 | 読取り | 許可された契約の概要のみ | 不可 | 読取り | 管理者／主管チーム |
| 基本編集 | 読取り・更新・担当者／投稿の作成 | 許可された契約の概要のみ | 自分の申請のみ | 追記 | 管理者／主管チーム |
| 契約担当 | 読取り | 読取り・更新・判断申請 | 必要範囲 | 読取り | 管理者 |
| 名刺処理担当 | 必要な基本台帳 | 不可 | 対象名刺の確認・登録 | 不可 | 管理者／主管チーム |
| アクセス管理者 | アクセス一覧の確認、許可範囲内のUser／Team追加・変更・取消 | 自動では付与しない | 自動では付与しない | 自動では付与しない | 管理者／既存アクセス管理者 |

プロファイル名と権限の組合せは案であり、最終的なSecurity Role、Access Team、共通業務操作の分担はTV-06／08で確定する。アクセス付与テーブルを読めることだけで対象データを読める状態にしない。

### 状態・操作表

| 操作 | 許可・評価時点 | 拒否・不変条件 |
| --- | --- | --- |
| 会社アクセス付与 | 管理者または当該会社のアクセス管理者が、対象会社、User／Team、プロファイル、期間を指定し、委譲範囲を検査してDataverse共有と記録を一体で確定 | 閲覧者・編集者による変更、対象なし、User／Team両指定、権限上限を超える付与、一般ユーザーによる直接共有、監査なしを拒否 |
| 新規取引先の初期アクセス | 登録者にだけ基本編集＋アクセス管理を付与し、登録者が最初のメンバーをUIから追加する | 全社・既定グループへの暗黙共有、クライアント指定の別登録者、登録者不在のままの確定を拒否 |
| 会社アクセス取消 | 管理者が取消を確定し、新規のPC／Canvas／API要求から即時除外 | 取消後の新規取得・更新を許可しない。無関係な会社のアクセスを変更しない |
| 取引先・担当者読取り | Dataverse権限と取引先アクセスをサーバー側で評価 | アプリの表示フィルターだけで制限しない。組織全体Readで迂回できない |
| 契約・申請・名刺読取り | 取引先アクセスに加えてデータ種別の追加権限を評価 | 期限・件数・存在を権限外の集計やエラーから推測できない |
| 会社アクセス変更中 | 付与・取消の版と対象row versionを検査 | 途中失敗で記録だけ、または共有だけが残らない。再送は冪等にする |
| アクセス管理者の追加・変更・取消 | 当該会社のアクセス管理権と委譲可能範囲を検査し、変更後のACL版を確定 | 閲覧者・編集者による変更、権限上限を超える付与、監査なし変更を拒否 |
| 最後のアクセス管理者 | 変更後も有効な管理者を最低1主体残す案を第一候補とする | 自己削除、相互削除、降格、有効期限切れで管理者不在になる変更を拒否 |
| グループ所属変更 | PartnerLedgerでは既存User／Teamへの会社アクセス付与だけを管理 | Entra／Microsoft 365グループの作成・メンバー変更・ライセンス・環境アクセス変更を受け付けない |
| 会社終了・アーカイブ | 既存アクセスを保持するか読取り専用にする運用を段階2で確定 | 関連担当者・契約・掲示板へのアクセスを暗黙に連鎖変更しない |
| 圏外端末 | 最終同期時点で許可された基本情報だけを表示 | 取消後も残るキャッシュを即時失効できるとは扱わず、再接続時に除外・削除する |

取引先単位の制御は、付与・取消を「共有設定」と「業務データ」の別々の直接CRUDにしない。認可判定、共有反映、監査記録の原子性と、応答消失・再送時の整合性をTV-06／07で検証する。

### `pl_AccessGrant`設計固定表（2026-09-12、実装前ゲート）

高リスク変更（権限ルール）のため、実装着手前に次を固定する。既存の状態契約（本節の表）に対する追加確定事項のみを記す。

- `pl_GrantStatusCode`は**有効／取消／期限切れの3値**で確定する（`apps/pc/src/accessGrantContract.ts`の型定義と一致）。2章の要点表にある「有効／取消」の2値表記は省略であり、3章の内部処理状態遷移契約表が正。
- 期限失効の検知は**定期バッチとアクセス時点評価の両方**を行う。バッチだけでは失効から実共有解除までの間に不整合が残り得るため、アクセス発生時点でも`pl_EndAt`と実共有を照合し、失効を検出したら即時に無効扱いへ倒す。バッチ処理の実装（頻度・実行主体）は段階3の別作業とする。
- 会社終了・アーカイブ時の既存アクセスは**保持**する（読取り専用への降格はしない）。関連担当者・契約・掲示板への暗黙連鎖は行わない（既存方針どおり）。
- 自動テスト（`accessGrantContract.test.ts`）がカバーする範囲：入力必須項目、半開区間の失効判定、同一会社・主体・プロファイルの重複拒否、最後のアクセス管理者保護、古いACL版での投影拒否。**カバーしない範囲**：再送・冪等キーでの重複検出とスキップ、`pl_ProjectionStatusCode`の遷移（途中失敗検出）、プロファイル変更時の共有権再計算、自己昇格・権限上限超過の判定、別会社への付替え拒否。これらはDataverse実装時に追加のテストで固定する。TV-06／07／08／13／14は未実施（TV-13は限定試験のみ）のため、PC／Canvas／通常ログインの同一性、他テーブル境界、UIからの付与／変更／取消、オフライン同期時の除外は自動テスト・実機確認とも未検証。
- 変更する範囲：`pl_AccessGrant`への`pl_PartnerLookup`／`pl_UserLookup`／`pl_TeamLookup`／`pl_PrincipalKey`／`pl_Reason`／`pl_AclVersion`／`pl_AppliedAclVersion`／`pl_ProjectionStatusCode`の追加、`pl_Partner`への`pl_AclVersion`の追加（200〜208行の候補と同一）。変更しない範囲：既存21テーブルの他の列、4 Security Roleの状態（残り3ロールのSharePoint系権限削除検証は保留）、Access Team Template、DecisionFlow、ユーザー／Team割当、公開、Solution import、共通業務操作の実装そのもの。

### 独立監査（2026-09-12）を受けた設計差し戻しと再確定

Custom API + プラグイン実装の1回目の独立監査で、コード修正だけでは閉じない3つの設計未決事項が見つかり、「設計への差し戻し」と判定された。以下のとおり再確定する。

1. **期限の粒度**：`pl_AccessGrant.pl_StartAt`／`pl_EndAt`は2026-09-12の直しレポで`Format=DateOnly`／`DateTimeBehavior=DateOnly`に補正していたが、これは誤りだった（他テーブルの日付列の直し漏れと同じ手順を機械的に適用してしまい、本来「日時」であるべき列に適用した）。**`Format=DateAndTime`／`DateTimeBehavior=UserLocal`へ再修正する**（0行のため列の削除・再作成を含めて安全）。半開区間`開始日時 ≤ 現在時刻 < 終了日時`は時刻単位で評価する。
2. **監査記録の保存先**：既存の`pl_OperationLog`（`pl_OperationCode`／`pl_ResultCode`／`pl_ErrorCode`／`pl_IdempotencyKey`／`pl_RecordedAt`／`pl_Name`を保持）を採用する。ただし現状「誰が」「どの付与に対して」の列が無いため、`pl_InitiatingUserLookup`（→`systemuser`、必須）と`pl_TargetGrantLookup`（→`pl_AccessGrant`、任意）を追加する。GrantAccess／ModifyAccess／RevokeAccessの成功・拒否を問わず、呼び出し元（`InitiatingUserId`）・操作種別・結果・エラーコードを毎回1行追記する。`pl_AccessGrant.pl_Reason`は「現在の状態の理由」を保持し、変更履歴の正本は`pl_OperationLog`とする。
3. **自己昇格・権限上限**：v1では「呼び出し元は自分自身（Userとして紐づく付与）を対象にしたGrantAccess／ModifyAccessを呼べない」というシンプルな規則で固定する（自分の権限は必ず別の管理者に変更してもらう）。委譲可能範囲の細分化（`pl_DelegationCode`）は引き続き未使用・未確定のまま据え置く。

これらに合わせてプラグイン実装（`plugins/PartnerLedger.Plugins/`）を全面的に修正した。認可判定は`AccessGrantContract.IsAccessGrantActiveAt`に一本化し、`now`を必須引数にして呼び忘れをコンパイルエラーにした。GrantAccess／ModifyAccess／RevokeAccessはすべて、同一主体が対象会社に持つ他の有効な付与から実共有権を合算してから実共有APIを呼ぶ。取消・期限切れの付与へのModifyAccessは拒否する。詳細は開発記録（非公開）にある。

**2026-09-12に開発環境へ適用済み。** 上記1・2をDataverseへ実適用した（3の自己昇格ルールはコード側の判定のみでスキーマ変更を伴わない）。`pl_AccessGrant.pl_StartAt`／`pl_EndAt`は削除後`Format=DateAndTime`／`DateTimeBehavior=UserLocal`で再作成、`pl_OperationLog`に`pl_InitiatingUserLookup`（→`systemuser`、必須）・`pl_TargetGrantLookup`（→`pl_AccessGrant`、任意）を追加した。適用作業中、`pl_OperationLog.pl_RecordedAt`が`Format=DateOnly`のまま`DateTimeBehavior`が未設定（null）という別の既存不備を検出し（フィルタービュー生成を壊しLookup追加をブロックしていた）、ユーザー承認を得て同様に削除→`Format=DateAndTime`／`DateTimeBehavior=UserLocal`で再作成した。DateTimeBehaviorが一度`DateOnly`／`TimeZoneIndependent`に設定されると`UserLocal`へ変更する経路がAPI上存在しない（Microsoft Learn公式ドキュメントで確認、逆方向のみ可）ため、いずれも削除・再作成方式を採用した。対象2テーブルとも適用前後で0行、フォーム・ビュー参照も0件を確認済み。`dotnet test`（16件）・ルート`npm run check`（型・lint・テスト81件・ビルド）を再実行し全通過。

### 独立監査（2回目、2026-09-12）を受けた状態契約表の追加確定

2回目の独立監査（詳細は`docs/archive/audit-findings-access-grant.md`）はP0 3件・P1 7件を検出し、「設計への差し戻し」と判定した。1回目の3決定（期限の粒度・監査記録の保存先・自己昇格）は実装へ正しく反映されていると確認され、今回のP0は別の軸（未来日時の付与の扱い、fail-closed範囲の不足）だった。以下5行を状態契約表へ追加確定する。

| 論点 | 決定 |
| --- | --- |
| 将来開始の付与（`StartAt` > `now`） | `requirements.md`に予約・将来開始の要件記載が無いため、**入力段階で拒否**する（v1既定）。`AccessGrantContract.ValidateAccessGrantInput`に`now`を必須引数化し、`StartAtInFuture`を追加した。 |
| 期限切れへの遷移（アクセス時点評価の実装） | `AccessGrantExpiry.ReconcileExpired`を新設。Grant/Modify/RevokeAccessが対象会社の付与を読むたびに、`pl_GrantStatusCode=有効`のまま`pl_EndAt`を過ぎた行を検出し、`期限切れ`へ書き換えたうえで、影響を受けた主体の実共有権を再計算・適用（縮小またはRevokeAccessRequest）する。 |
| 最後の恒常的管理者のEndAt失効防止 | `AccessGrantContract.HasOtherPermanentManager`を追加。GrantAccessで、唯一の恒常的（`EndAt`なし）アクセス管理者付与に終了日時を設定する変更を`last-permanent-manager`で拒否する。 |
| 管理者バイパス・新規取引先ブートストラップ | `PartnerAccessBootstrapPlugin`を新設（`pl_partner`のCreate・PostOperationに登録予定、未登録）。付与が0件の取引先にだけ、登録者へ基本編集＋アクセス管理者を恒常的に自動付与する。Dataverseシステム管理者ロールによる汎用バイパスは実装せず、この専用ブートストラップ経路だけを唯一の例外とする（意図的なスコープ縮小）。 |
| 冪等キー入力 | `pl_OperationLog.pl_IdempotencyKey`（既存列、追加スキーマ変更なし）を使い、GrantAccess／ModifyAccess／RevokeAccessすべてに`IdempotencyKey`必須パラメータを追加した。**2026-09-12の3回目独立監査でP0指摘（下記参照）を受けて訂正**：同一操作種別・同一キーで過去に`Success`の記録があっても、内容が一致する場合だけ確定済み結果を返す。Modify／Revokeは要求された`GrantId`と過去の成功対象の`GrantId`が一致するかを比較し、不一致なら`idempotency-key-conflict`で拒否する。Grantは会社・主体種別・主体ID・プロファイルの一致を`AccessGrantContract.MatchesReplayedGrant`で比較する。これは`:96`／`:170`／`:224`が既に要求していた「内容が異なる再送は拒否する」を実装したものであり、以前の本行の記述（「内容の一致検証はせず」）は誤りだった。 |

TS版（`apps/pc/src/accessPolicy.ts`／`accessGrantContract.ts`）は表示ヒント専用へ格下げし、C#（`plugins/PartnerLedger.Plugins/`）を唯一の認可正本と明記した（両ファイルの先頭コメントに追記）。

対応するテストを`plugins/PartnerLedger.Plugins.Tests/`へ先に追加してから実装した。`IOrganizationService`の最小限のテストダブル（`FakeOrganizationService`、FakeXrmEasy等の外部依存は追加せず）を新設し、`ComputeEffectiveRights`・`ToRecord`のfail-closed（`pl_StartAt`欠損・User/Team主体欠損）・`AccessGrantExpiry.ReconcileExpired`・`OperationLogRepository.FindSuccessGrantId`・`PartnerAccessBootstrapPlugin.CreateInitialGrants`を検証。テストは16件から33件へ拡充した。`dotnet build`・`dotnet test`（33件）・ルート`npm run check`（型・lint・テスト81件・ビルド）全通過。

**今回のバッチで意図的に対応しなかった項目**（`docs/archive/audit-findings-access-grant.md`のP1-6・P2群）：主体（User／Team）の有効性・期間上限の検査、`ProjectionStatus.不一致`遷移の実装、`pl_PrincipalKey`の会社・プロファイル込みキー化、`PartnerShareRights`の`AppendAccess`→`AppendToAccess`要検証、`ModifyAccessPlugin`の拒否順序統一、Team経由の自己昇格（設計決定3で許容範囲外と明記済み）。これらは次回以降の別バッチ、またはTV-06確定後の対応とする。

### 独立監査（3回目、2026-09-12）を受けた修正

2回目監査への対応バッチに対する3回目の独立監査は「差し戻し」（P0 1件・P1 9件）と判定した。P0は今回のバッチが持ち込んだ回帰で、以下のとおり是正した。

- **P0-A（冪等再送が要求と異なる付与の結果を返す）**：`ModifyAccessPlugin`/`RevokeAccessPlugin`の冪等キー再送短絡が、要求された`GrantId`と過去に成功した対象の`GrantId`を比較していなかった。取消要求Bが、以前成功した無関係な取消Aの結果を「成功」として返し、**Bの取消が実際には実行されないまま監査ログだけ「成功」を記録する**という致命的な欠陥だった。要求と過去成功対象の一致検証（`GrantId`比較、Grantは`AccessGrantContract.MatchesReplayedGrant`による内容比較）を追加し、不一致は`idempotency-key-conflict`で拒否するよう修正（本節末尾の表`:293`も訂正済み）。
- **P1-7（認可前の再送短絡）**：冪等キーによる再送短絡を、3プラグインとも認可判定の後へ移動した。未認可の呼び出し元へ成功を返し、その主体名でpl_OperationLogに虚偽のSuccess行を作らせない。
- **P1-1（最後の恒常的管理者、多段階のロックアウト経路）**：`CanRemoveAccessManager`が「有効な管理者が1人でも残るか」だけを見ていたため、「恒常的管理者Aを取消→期限付き管理者Bだけが残る→Bの期限切れで管理者0人」という経路が開いていた。`CanRemoveAccessManager`を`HasOtherPermanentManager`（恒常的管理者が残るか）へ統一し、取消・降格・EndAt付き新規付与のすべてで同じ不変条件を守るようにした。
- **P1-3（ReconcileExpiredが投影状態を戻さない）**：`AccessGrantExpiry.ReconcileExpired`が実共有適用後に`ProjectionStatus`を「未反映」のまま残し、`RevokeAccessPlugin`のM-8冪等判定（投影状態が一致の場合だけ短絡）を期限切れ後は永久に無効化していた。適用後に`一致`へ戻すよう修正。
- **P1-4（ReconcileExpiredがACL版を進めない）**：実共有を変更しているのに`pl_Partner.pl_AclVersion`を進めておらず、この変更をまたぐクライアントの楽観的並行性制御が効かなかった。`BumpAclVersion`を呼ぶよう修正。
- **P1-9（クロックスキュー）**：`StartAtInFuture`の判定にクライアント時計の数分のずれを許容する窓（5分）を追加した。

**設計判断のみでコードは変更しない2件（監査の指摘どおり）**：
- **P1-2（期限切れへの実共有解除は「管理操作が呼ばれたとき」にしか起きない）**：この設計はDataverseの実共有APIそのものが認可境界であり、本コードベースに「読取り時にpl_AccessGrantを都度参照する」経路が無いため、読取り時点での評価はバッチ（段階3・未実装）なしには実装できない、という制約を追認した。`:266`の「アクセス発生時点」は「Grant/Modify/RevokeAccessのいずれかが呼ばれたとき」の意味に限定されると明記する。v1のUIは`EndAt`を「その日時に自動的にアクセスが切れる」と説明してはならない（段階3のバッチが入るまで）。
- **P1-6（`ToRecord`の全件fail-closedが復旧経路自体を塞ぐ）**：行単位でスキップする代替案は、`HasActiveDuplicateGrant`等に対してfail-openになる（壊れた行を無視すると重複や最後の管理者判定を誤る側に倒れる）ため、現状の「一覧全体を停止する」設計を維持する。**対になる予防策として、`pl_AccessGrant.pl_StartAt`の`RequiredLevel`を`None`から`ApplicationRequired`へユーザー承認を得て開発環境へ適用済み**（対象0行、PUT→Publish→再読取りで確認済み。`RequiredLevel`はDateTimeBehaviorと異なり変更可能と事前に確認していた）。これにより欠損行はそもそも作成されなくなる。

**登録手順への注記（P1-10）**：`PartnerAccessBootstrapPlugin`は同期・トランザクション内（PostOperation、非同期不可）で登録すること。非同期登録だと、`Create`成功後に実共有APIが失敗した場合、再試行時に「付与行はあるが実共有が無い」状態が固定化し、登録者が管理者として扱われるのに実データを読めない状態になる。

機械的ゲート再実行：`dotnet build`・`dotnet test`（36件）・ルート`npm run check`（型・lint・テスト81件・ビルド）全通過。テストはP0-A/P1-1/P1-3/P1-4/P1-9を固定する3件を追加（36件、うちP0-Aの`GrantId`不一致比較そのものはコードリーディングで確認、単体テストでは未固定——比較が1行の`!=`であるため）。

### PC向けアクセス一覧読み取りAPI（2026-09-12、実装前固定）

PCのアクセス管理画面は、`pl_AccessGrant`を直接Readしない。SEC-06で一般利用者の直接`Create`／`RetrieveMultiple`が403になることを確認済みであり、クライアントへ直接Readを足すと、会社単位の共通業務境界を迂回する経路になり得る。Power Apps Code Appからは、取引先単位で呼び出し元を認可する専用Custom API（仮名：`pl_ListPartnerAccess`）だけを呼ぶ。Custom APIの応答は複合型を安定して生成できるよう、`AclVersion`（Integer）と、最小項目の`GrantsJson`（String）を返す。Custom APIの応答プロパティに複数の単純型を定義すると複合型になる仕様は[Microsoft Learn](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api-tables)に従う。EntityCollectionをクライアントへ直接公開する方式は、生成サービスの型と空集合時のACL版表現を確認してから別案として比較する。

| 論点 | 設計固定 | 自動テスト／実機確認 |
| --- | --- | --- |
| 入力と本人性 | `PartnerId`（GUID）のみ必須。`CallerObjectId`、主体種別、権限結果、付与IDの一覧をクライアントから受け取らず、`IPluginExecutionContext.InitiatingUserId`と所属Teamを使う | GUID不正・任意のCallerObjectId無視、実呼び出し元と監査主体の一致 |
| 認可 | 実行用Application Userの`PluginUserService`で付与一覧を読み、呼び出し元のUser／Teamが対象会社の有効な「アクセス管理者」である場合だけ返す。管理者の汎用バイパスは追加しない | 会社A管理者→A成功、B拒否、閲覧／基本編集だけ→拒否、権限外会社の存在を同じ拒否応答にする |
| 返却項目 | `GrantId`、`PrincipalKind`、`PrincipalId`、`Profile`、`GrantStatus`、`StartAt`、`EndAt`、`AppliedAclVersion`、`ProjectionStatus`だけ。理由、登録者、監査詳細、他社の件数・名称は返さない。ACL版は別の`AclVersion`で返す | 最小項目のスキーマ固定、理由・監査・他社行の混入なし、空集合でもACL版を返す |
| 期限 | 一覧取得時にも`EndAt`を現在時刻で評価する。期限切れ行の状態変更と影響主体の実共有再計算が必要な場合は同じ同期処理内で行い、失敗時は古い一覧を成功として返さない | 期限直前／直後、期限切れの実共有解除・投影一致・ACL版更新、再取得の整合 |
| 破損・上限 | `pl_AccessGrant`の必須列欠損・未知の列挙値・User／Team主体欠損・5000件超過はfail-closed。同じ汎用エラーとして扱い、部分一覧を返さない | 破損1行、上限超過、途中失敗時の空成功・部分返却がないこと |
| 冪等性・評価時点 | 読み取りAPI自体に冪等キーを設けない。呼出しごとに現在時刻と最新ACL版を評価し、画面は返却ACL版を次のGrant／Modify／Revokeの`ExpectedAclVersion`へ渡す | 古いACL版での書込み拒否、再取得後の再試行、応答消失後の再取得 |
| 影響範囲 | 新規Custom API、同APIの同期PostOperation実行ステップ、生成サービス、PCアクセス管理画面だけ。DecisionFlow、承認基盤、子テーブル共有、ユーザー／Team本体変更、既存直接Read権限は変更しない | ローカル契約テスト後に、対象・副作用・戻し方を提示してから開発環境へ限定登録。登録前後のSolution差分を照合 |

このAPIのローカル実装・テストに続き、承認済みの外部変更としてCustom API／プラグインステップの開発環境登録とPartnerLedger Solution収録まで完了した。対象限定`PublishXml`の後、ユーザー承認を得て`PublishAllXml`を実行し、公式CLIでメタデータ検索と生成サービス追加に成功した。`dataverseAccessSnapshotApi.ts`へ結線し、Dataverse GUIDを持つ経路だけ読み取り専用のライブ一覧へ切り替えるUI境界を追加した。架空IDのモック、付与・変更・取消のライブ操作、実ユーザーの認証・認可・A/B境界は別途受入対象として残る。

### PC向け取引先一覧読み取り境界（2026-09-12）

PCの初期画面は架空データのまま維持し、利用者がヘッダーで明示的に切り替えた場合だけ、生成済み`Pl_partnersService`で`pl_partner`のアクティブ行を取得する。クエリは`statecode eq 0`、名前順、画面に必要な最小列（会社名、2つの状態、所有者、登録者、登録日、ACL版、ID）に限定し、画面フィルターで全社行を取得して隠す方式にはしない。応答は`partnerDirectory.ts`でGUID、Choice、日付、ACL版、重複IDを検証し、通信失敗・不正行・非アクティブ行の混入は全体拒否する。ライブ詳細はこの一覧契約と会社単位アクセス一覧の読み取りだけを表示し、担当者・契約・履歴は未接続のため架空値で補完しない。これはローカル接続境界の実装であり、通常ログイン、実ユーザーのRead権限、他テーブルの共有、ライブ書込みの受入完了を意味しない。

## 5. 承認申請の閲覧権

2026-09-14の[PL-031改訂](requirements.md#approval-read-policy)を正本とする。承認者グループには全申請・不変提出版・変更差分・判断に必要な最小コンテキストへのReadを与え、完了後も閲覧可能とする。申請の提出者や対象会社ごとの一時共有は行わない。会社・担当者・契約・掲示板・名刺画像の本体行へReadを追加せず、必要な情報は申請・提出版の承認コンテキストへ限定する。

`PL 承認`の`pl_Request`／`pl_SubmissionVersion`組織Readを採用方針とする。両テーブルにある下書きも承認者から閲覧可能とすることは追加承認済みで、下書きを隠すガードや別保存先は作らない。実際の保存列・権限差分を確認してから適用し、ApprovalLink等の連携内部行も一括して公開しない。取引先詳細の会社アクセス管理は別境界であり、承認画面で申請ごとの閲覧付与を必須にしない。

承認・却下・差し戻し・取消・期限切れは閲覧権の失効契機にしない。再提出でも新しい閲覧付与は不要だが、新しい不変提出版は必要。承認者ロール／グループ所属の管理と監査は残す。全件Readは全件の判断・更新・反映の許可ではなく、判断者・申請・版・結果は引き続きサーバー照合する。

| 操作 | 許可・評価時点 | 拒否・不変条件 |
| --- | --- | --- |
| 申請提出 | 対象・差分・提出版を固定し、設定済み承認ルートを解決する。個別閲覧付与は不要 | 会社の通常ACLへ承認者を追加、クライアント指定の承認者、提出版の後編集を拒否 |
| 承認用閲覧 | 承認者という役割で全申請・提出版を読める。申請の完了後も継続 | 基本編集、会社ACL変更、Share／Assign、申請差分の変更、権限外の本体情報・期限集計の取得を拒否 |
| 承認結果 | DecisionFlowの申請、提出版、判断者、判断結果をサーバーで照合し、状態を更新・監査する | クライアント指定の承認済み値、別申請・古い版の結果、結果不明の成功確定を拒否 |
| 承認者役割の解除 | Role／グループ所属の変更を反映し、他ロール・所有・共有に由来する残存権限を確認する | 申請の完了だけで閲覧を失効させない。役割解除だけであらゆる別経路の権限も消えるとみなさない |
| オフライン承認 | オフラインでは申請詳細・承認操作・承認用権限を扱わない | 申請閲覧権や提出版をCanvas端末へ同期しない |

### 承認基盤のローカル契約（2026-09-12、クラウド実装前ゲート）

ここから§5末尾までの日付付き契約・API実装記録は**改訂前の実装履歴**。申請単位の閲覧判定・付与・失効のコードとテストは現在も旧方式のままで、新PL-031の受入にはならない。移行対象と維持する保護（開発記録、非公開）に従い、提出版・結果照合・反映の保護まで削らない。

> **2026-09-25 更新（DecisionFlow依存の撤去）**：承認結果の源はPower Automateグループ承認（`PowerAutomateGroup`）だけになった。以下の記述のうち、`apps/pc/src/approvalContract.ts`、`ApprovalResultIngressService`、`ApplyQueued`、旧Custom API（`pl_ApplyApprovalDecision`／`pl_QueueApprovalReflection`など）、`pl_ApplicationLookup`／`pl_ApprovalLink_Application`との照合は、コードから削除済み（列と関連は開発環境から削除予定）。源が`DecisionFlow`のLinkは結果取込・判定・反映のすべてで拒否する。現行の入口は標準`pl_ApprovalLink` Update＋`StandardApprovalResultPlugin`／`StandardApprovalDecisionPlugin`、反映は`ApprovalReflectionApplyService.ApplyStandardApproved`。

承認基盤は、`pl_Request`、`pl_SubmissionVersion`、`pl_ApprovalGrant`、DecisionFlow連携をDataverseへ作成する前に、`apps/pc/src/approvalContract.ts`で状態と照合条件を固定する。ここでの実装は永続化・認可・DecisionFlow呼出しを行わず、サーバー側実装へ渡す拒否条件をテストするための契約である。

| 操作 | 許可する遷移・評価時点 | 拒否・不変条件 | 自動ゲート |
| --- | --- | --- | --- |
| 申請提出 | `下書き`→`提出中`、新しい提出版を`下書き`→`提出済み`。差戻し後も提出版IDを再利用しない | 提出後編集、旧提出版の再送、承認者・承認結果のクライアント指定、設定版・対象row version欠損 | `validateApprovalSubmission`とテスト |
| DecisionFlow判断 | 同一の申請ID・提出版IDをDecisionFlow側で照合し、結果が確知できる場合だけ`承認`／`差戻し`／`却下`へ遷移 | クライアント由来の結果、別申請・古い版、結果不明、提出中でない状態 | `validateApprovalDecision`とテスト |
| 承認結果反映 | `承認済み`→`反映待ち`→`反映済み`。反映直前に申請・提出版・設定版・対象row versionを再照合 | 古い設定・対象行、提出版不一致、`承認済み`からの直接反映、結果不明 | `validateApprovalReflection`とテスト |
| 申請閲覧権 | `有効`→`終了`／`取消`／`期限切れ`。通常の会社ACLとは別に管理する | 承認者を会社通常ACLへ昇格、完了後の継続閲覧、オフライン同期 | `canEndApprovalGrant`とテスト |

このローカル契約を通過しても、DecisionFlowの実際の照合、申請単位の行権限、承認結果の送信応答消失、反映トランザクション、PC／Canvasの通常ログインは合格とは扱わない。次のクラウド作業では、対象テーブル・DecisionFlowとの参照方向・承認者グループの決定元・失効時点・戻し方を別途固定し、Solution収録前後の差分を確認する。

### 承認基盤の次バッチ設計固定（2026-09-13、WP-002-A）

承認基盤の最初の実装単位は、永続化や外部呼出しではなく、サーバー側で共通利用する状態・版照合契約とする。`plugins/PartnerLedger.Plugins/ApprovalContract.cs`を認可・反映処理の正本候補、`ApprovalContractTests.cs`を機械ゲートとし、既存のTypeScript契約と同じ拒否境界を固定する。DecisionFlow本体・既存クラウド資産・ユーザー／Team割当は変更しない。

| 操作 | 許可・評価時点 | 拒否・不変条件 | 機械ゲート |
| --- | --- | --- | --- |
| 申請提出 | 下書きの申請と新しい提出版を提出中／提出済みへ進め、設定版・対象row version・理由をサーバーで記録 | 提出済み版の再利用、提出後編集、承認者・承認結果のクライアント指定、版情報欠損 | `ValidateSubmission` |
| DecisionFlow判断 | 同じ申請ID・提出版IDを照合でき、結果が確知できる場合だけ承認／差戻し／却下を記録 | クライアント由来、別申請・旧版、結果不明、提出中でない状態 | `ValidateDecision` |
| 承認結果反映 | 反映直前に申請・提出版・設定版・対象row versionを再取得して一致した場合だけ反映済みへ進める | 古い設定・対象行、提出版不一致、承認済みからの直接反映、結果不明 | `ValidateReflection` |
| 申請閲覧権 | 有効な一時権を終了／取消／期限切れへ進め、通常会社ACLとは別に扱う | 承認者の会社通常ACLへの昇格、完了後の継続閲覧、オフライン同期 | `CanEndApprovalGrant` |

開発環境の読み取り監査では、`pl_Request`、`pl_SubmissionVersion`、`pl_ApprovalGrant`、`pl_ApprovalLink`、`pl_OperationLog`はいずれも0行・UserOwnedだった。一方、現在の骨格には対象・提出版・承認者・DecisionFlowとの対応Lookupと、不変の変更スナップショット、実際に解決した承認者、結果確知性を保持する列が不足している。クラウド追加の推奨候補は次のとおりである。

| テーブル | 推奨する追加境界（名称は設計候補） | サーバー側不変条件 |
| --- | --- | --- |
| `pl_Request` | `pl_PartnerLookup`、条件付き`pl_ContractLookup`、`pl_RequestingUserLookup`、解決済み`pl_ApproverTeamLookup` | すべての申請は取引先に属し、契約申請は契約の所属先と一致。承認者Teamは設定から解決し、クライアント指定を受けない |
| `pl_SubmissionVersion` | `pl_RequestLookup`、`pl_SubmittedByLookup`、`pl_SubmittedAt`、`pl_ChangeSetJson` | 申請に属する版を一度だけ固定。提出後のスナップショットは更新せず、差戻し後は新しい版を作る |
| `pl_ApprovalGrant` | `pl_RequestLookup`、`pl_SubmissionVersionLookup`、User／Team主体Lookup、`pl_Reason`、`pl_PartnerAclVersion` | 主体はUser／Teamのどちらか一方。申請単位の一時読取りだけを表し、通常会社ACLへ権限を広げない |
| `pl_ApprovalLink` | `pl_RequestLookup`、`pl_SubmissionVersionLookup`、`pl_ApplicationLookup`（→`ds_application`）、`pl_DecisionCode`、`pl_ResultKnown`、`pl_DecidedAt` | PartnerLedgerからDecisionFlowの`ds_application`へ一方向に対応付け、応答不明を成功へ変換しない |
| `pl_OperationLog` | `pl_TargetRequestLookup`、`pl_TargetSubmissionVersionLookup`、要求内容ハッシュ | `pl_InitiatingUserLookup`と標準`createdby`を分け、提出・付与・判断・反映・失敗を同じ監査軸で追跡する |

この候補を開発環境へ適用する前に、対象テーブル・列・Lookup・Solution収録・公開、DecisionFlowへの副作用が無いこと、0行前提の戻し方（追加した空列／関係の削除またはスナップショット復元）を別途明示承認する。これはWP-002-A時点の設計ゲートであり、次のWP-002-Bでスキーマ部分を適用した。

### 承認基盤スキーマ適用（2026-09-13、WP-002-B）

ユーザー承認後、開発環境へスキーマの空枠を適用した。承認処理・DecisionFlow呼出し・申請単位の行権限はまだ実装しておらず、対象5テーブルはいずれも適用前後で0行である。既存列の補正は、フォーム／ビュー参照が無いことを確認してから行った。

補正した既存DateTime列（実際の論理名）は次の7列で、すべて`DateAndTime`／`UserLocal`になっている。

| テーブル | 論理名 |
| --- | --- |
| `pl_request` | `pl_requestedat` |
| `pl_submissionversion` | `pl_fixedat` |
| `pl_approvalgrant` | `pl_expiresat`、`pl_grantedat`、`pl_revokedat` |
| `pl_approvallink` | `pl_responseat`、`pl_sentat` |

追加した非Lookup列（実際の論理名・型）は次の7列である。

| テーブル | 論理名 | 型・設定 |
| --- | --- | --- |
| `pl_submissionversion` | `pl_submittedat` | DateTime、`DateAndTime`／`UserLocal` |
| `pl_submissionversion` | `pl_changesetjson` | Memo、最大1,048,576文字 |
| `pl_approvalgrant` | `pl_reason` | Memo、最大2,000文字 |
| `pl_approvalgrant` | `pl_partneraclversion` | Integer、0以上 |
| `pl_approvallink` | `pl_decisioncode` | String、最大50文字 |
| `pl_approvallink` | `pl_resultknown` | Boolean、既定値`false` |
| `pl_approvallink` | `pl_decidedat` | DateTime、`DateAndTime`／`UserLocal` |

追加した15本のLookup Relationshipは、すべて`Delete=RemoveLink`、`Assign`／`Merge`／`Reparent`／`Share`／`Unshare=NoCascade`とした。

| Relationship | Lookup（実際の論理名） | 参照先 |
| --- | --- | --- |
| `pl_Request_Partner` | `pl_request.pl_partnerlookup` | `pl_partner` |
| `pl_Request_Contract` | `pl_request.pl_contractlookup` | `pl_contract` |
| `pl_Request_RequestingUser` | `pl_request.pl_requestinguserlookup` | `systemuser` |
| `pl_Request_ApproverTeam` | `pl_request.pl_approverteamlookup` | `team` |
| `pl_SubmissionVersion_Request` | `pl_submissionversion.pl_requestlookup` | `pl_request` |
| `pl_SubmissionVersion_SubmittedBy` | `pl_submissionversion.pl_submittedbylookup` | `systemuser` |
| `pl_ApprovalGrant_Request` | `pl_approvalgrant.pl_requestlookup` | `pl_request` |
| `pl_ApprovalGrant_SubmissionVersion` | `pl_approvalgrant.pl_submissionversionlookup` | `pl_submissionversion` |
| `pl_ApprovalGrant_UserPrincipal` | `pl_approvalgrant.pl_userprincipallookup` | `systemuser` |
| `pl_ApprovalGrant_TeamPrincipal` | `pl_approvalgrant.pl_teamprincipallookup` | `team` |
| `pl_ApprovalLink_Request` | `pl_approvallink.pl_requestlookup` | `pl_request` |
| `pl_ApprovalLink_SubmissionVersion` | `pl_approvallink.pl_submissionversionlookup` | `pl_submissionversion` |
| `pl_ApprovalLink_Application` | `pl_approvallink.pl_applicationlookup` | `ds_application` |
| `pl_OperationLog_TargetRequest` | `pl_operationlog.pl_targetrequestlookup` | `pl_request` |
| `pl_OperationLog_TargetSubmissionVersion` | `pl_operationlog.pl_targetsubmissionversionlookup` | `pl_submissionversion` |

非管理Solutionのexport→unpackで上記の列とRelationshipが収録されることを確認し、`solutions/PartnerLedger`へ同期した。ユーザーの明示承認後、対象5テーブルだけの`PublishXml`を実行し、発行後の再読取りでDateTime 9列、Relationship 15本、対象5テーブル0行を確認した。発行後Solutionもexport→unpackし、今回の22項目の収録と版管理ソースとのファイルSHA-256差分0件を確認した。DecisionFlow本体・データ・フロー、ユーザー／Team割当、Solution import、本番操作は行っていない。要求内容ハッシュ列は今回追加せず、既存の`pl_IdempotencyKey`と監査設計で扱うかを承認処理実装時に再評価する。

### 承認提出のサーバー実装（2026-09-13、WP-002-C）

適用済みスキーマを使う最初の書込み単位として、申請提出のローカル実装と機械ゲートを追加した。`ApprovalSubmissionService.Submit`は、クライアントからポリシー版・対象row version・変更スナップショット・提出者・承認者・判定結果を受け取らず、`pl_Request`／`pl_SubmissionVersion`の保存値と`IPluginExecutionContext.InitiatingUserId`だけで判定する。

| 確認点 | 実装・テスト固定 |
| --- | --- |
| 対象 | 申請IDと提出版IDを再取得し、提出版の`pl_RequestLookup`が申請IDと一致する場合だけ続行 |
| 本人性 | `pl_Request.pl_RequestingUserLookup`が存在し、`InitiatingUserId`と一致する場合だけ続行。提出者Lookupはサーバーが設定 |
| 版固定 | 申請／提出版のポリシー版が一致し、提出版の対象row versionと変更スナップショットが空でない場合だけ続行。提出済み時刻・提出者が既にある版は再提出しない |
| 状態 | 申請は`下書き`または`差戻し`、提出版は`下書き`の組合せだけを`提出中`／`提出済み`へ進める |
| 競合 | 申請・提出版の取得時row versionを`UpdateRequest.ConcurrencyBehavior=IfRowVersionMatches`へ渡す。同期プラグインの例外はDataverseトランザクションでロールバックさせる前提 |
| 冪等性 | 成功監査の同一操作種別・同一キーを対象申請・提出版・理由まで照合。同一なら書込みなしで成功再送、対象または理由違いは`idempotency-key-conflict` |
| 監査 | 成功／業務拒否を`pl_OperationLog`へ申請・提出版Lookup、`InitiatingUser`、操作種別、結果、エラー、冪等キー付きで記録 |

提出理由専用列は現行スキーマにないため、このバッチでは既存監査列`pl_OperationLog.pl_Name`へ操作理由として保存する暫定境界を採用し、同列の最大850文字を超える理由は更新前に拒否する。`pl_Name`への情報詰め込みを最終的な申請理由の正本とするか、専用列を追加するかは、承認者閲覧・DecisionFlow連携の設計レビューで再決定する。`SubmitApprovalPlugin`はこの入力境界をCustom APIへ接続するための未登録クラスであり、開発環境のCustom API／ステップ・DecisionFlow・承認者Team・申請単位共有は変更していない。

ローカル機械ゲートはC#テスト69件全通過。これは状態・永続化契約の確認であり、実環境のCustom API呼出し、承認者への最小読取り、DecisionFlowの結果照合、反映原子性、PC／Canvasの通常ログインをTV-14の合格とは扱わない。

### 保存済み承認結果の反映実装（2026-09-13、WP-002-D）

提出後のDecisionFlow結果を扱う次のローカル実装として、`ApprovalDecisionService.ApplyPersistedDecision`を追加した。これは外部サービスを直接呼ばず、サーバー側で作成された`pl_ApprovalLink`の保存値だけを読む。クライアント入力にDecisionや結果確知性を持たせないため、現時点ではこの処理を呼ぶCustom API／プラグインステップを登録しない。

| 連携記録の状態 | 結果 | サーバー処理 |
| --- | --- | --- |
| `連携済み` | `pl_ResultKnown=true`、Decisionが承認／差戻し／却下、申請／提出版Lookupが整合 | 申請・提出版を対応する判断状態へ、連携記録を`結果確認済み`へ、3行をrow version一致で更新し監査 |
| `連携済み` | 結果不明、Decision不正、外部キー欠損 | 状態変更なし、拒否監査 |
| `送信待ち`／`連携不明`／`連携失敗` | 任意 | 状態変更なし、拒否監査 |
| `結果確認済み` | 保存済みDecisionと申請・提出版の現在状態が一致 | 更新なしで確定結果を再送。状態不一致・結果欠損は破損として拒否 |

成功時の遷移は`提出中／提出済み → 承認済み／差戻し／却下`であり、`承認済み`からの反映済み遷移は対象レコード・ポリシー版・row versionの反映契約を別実装とする。DecisionFlowのコールバック真正性、承認者グループの解決、申請単位の行共有はこのローカル処理の前提であって、実装・実機合格とは扱わない。

ローカルC#テストは74件全通過。クラウド登録前に、`pl_ApprovalLink`へ結果を書き込む信頼境界（DecisionFlow側の外部キー・応答不明・再送・実行主体）を別途固定する。

### 申請単位の閲覧権実装（2026-09-13、WP-002-E）

承認者へ会社全体の権限を広げず、申請と不変提出版だけを読ませる共通処理として、`ApprovalViewingGrantService`を追加した。承認Teamはクライアント引数ではなく、申請行へサーバーが解決済みとして保存した`pl_ApproverTeamLookup`から取得する。閲覧権付与APIは申請者起点で呼べるが、登録時の実行権限は専用Application Userとし、サービスが申請行の申請者Lookupを再照合する。期限は必須で、サービス側で最大24時間に制限し、期限なしの恒久閲覧権を作らない。

| 操作 | サーバー側の固定 |
| --- | --- |
| 付与 | `提出中`／`提出済み`の同一申請・提出版、申請者、取引先、承認Teamを再取得。申請者起点を再検証し、期限を現在時刻から24時間以内に制限する。`pl_Request`と`pl_SubmissionVersion`へTeamの`ReadAccess`だけを共有し、`pl_ApprovalGrant`へ`有効`・開始／終了・理由・Partner ACL版を記録 |
| 重複・再送 | 同じ申請・提出版・Teamの有効付与は拒否。同一操作種別・冪等キー・対象・理由の成功は既存付与を返し、対象／理由違いは拒否 |
| 終了 | `有効`から`終了`／`取消`／`期限切れ`だけを許可。同じ申請・提出版・主体の別の有効付与が無い場合だけ、申請と提出版への共有を`RevokeAccess`し、付与行を終端化 |
| 非対象 | `pl_Partner`の共有、通常の取引先ACL、承認者User／Team本体、Entra／Microsoft 365グループのメンバーは変更しない |

共有・付与行・成功監査は同じ同期トランザクションで確定する前提で、途中例外は成功扱いにしない。C#テストは80件全通過。これは未登録のローカル共通処理の検証であり、実環境の承認Team解決、実ユーザーの申請単位Read、完了時失効、DecisionFlowコールバック、PC／Canvas通常ログインをTV-14の合格とは扱わない。

### 承認縦断のローカル接続（2026-09-13、WP-002-F）

判定確定時に有効な申請閲覧付与を終了し、共有解除に失敗した場合は同期処理全体を失敗させるよう`ApprovalDecisionService`へ接続した。これにより承認・差戻し・却下後に承認者が申請を見続ける部分成功を許さない。`ApprovalReflectionService.Queue`は、結果確認済み・承認リンク、申請／提出版の承認済み状態、row versionを確認してから`反映待ち`へ進めるだけで、対象テーブルの値は変更しない。

| 境界 | 実装 |
| --- | --- |
| 判定後の閲覧権 | 有効な`pl_ApprovalGrant`ごとに申請／提出版の共有を必要時に解除し、付与を`終了`へ進める |
| 反映待ち | `pl_ApprovalLink`の保存済み承認、申請／提出版の対応、現在状態を確認し、2行をrow version一致で`反映待ち`へ更新 |
| 部分失敗 | 閲覧権解除・付与更新・連携記録更新のいずれかが失敗したら同期トランザクションを例外にする |

C#テストは85件全通過。これはローカル状態・共有要求の機械確認であり、対象行の変更、DecisionFlowコールバックの真正性、Custom API登録、実環境の申請単位Read、PC／Canvas通常ログインをTV-14の合格とは扱わない。

### DecisionFlow結果取込みのローカル境界（2026-09-13、WP-002-G）

`ApprovalResultIngressService`を追加し、DecisionFlowから戻る結果を直接業務状態へ反映せず、まず保存済み`pl_ApprovalLink`へ取り込む境界を固定した。サーバーが保持するApplication Lookupと外部要求キーを照合し、結果確知時は有効な判定値・判定時刻を保持した`連携済み`、結果不明時は判定値・判定時刻を空にした`連携不明`へrow version一致で更新する。後続の`ApprovalDecisionService`だけが、Application・判定時刻・結果確知性を再確認して申請／提出版を進める。

| 境界 | 実装 |
| --- | --- |
| 信頼できる対応付け | `pl_ApplicationLookup`と外部要求キーを保存値と照合し、不一致は保存しない |
| 応答不明 | `pl_ResultKnown=false`、判定値なし、`連携不明`、監査結果`Uncertain`。承認・反映は行わない |
| 再送・上書き | 同じ配信キーの同一内容だけ更新なしで成功。内容違い、`結果確認済み`、`連携失敗`、既知結果の別配信上書きは拒否 |

C#テストは109件全通過。これはコールバック値の保存契約、ローカル監査、サーバー専用入口の入力境界の確認であり、DecisionFlowからの真正な呼出し、実行主体、コールバック登録経路、Custom API／同期ステップ、公開、実環境の結果反映を合格とは扱わない。

### 標準`pl_ApprovalLink`結果取込みのクラウド境界（2026-09-16）

DecisionFlow結果の保存入口は、独自Custom APIではなく`pl_ApprovalLink`の標準Updateとする。`StandardApprovalResultPlugin`をPreOperation／Stage 20／同期で登録し、クライアント入力を`pl_ResultKnown`／`pl_DecisionCode`だけに限定する。Application Lookup、外部要求キー、Link状態、応答日時、判断日時、申請／提出版Lookup、監査列は保存済み値とサーバー時刻から再構成する。

| 状態 | 固定した契約 |
| --- | --- |
| 結果確知 | 保存済みApplication・外部要求キー・申請・提出版が整合し、許可された判断コードがある場合だけ`連携済み`へ進め、判断値・応答／判断日時・成功監査を確定する |
| 結果不明 | `pl_ResultKnown=false`かつ判断値空の場合だけ`連携不明`へ進め、判断日時を空にしたまま応答日時と`Uncertain`監査を確定する。承認・反映は進めない |
| 再送／拒否 | 同じ結果の成功／不明再送は更新なしで冪等成功。内容違い、既確定結果の上書き、未送信／失敗Link、Application／外部キー欠損、一般利用者／承認者の直接更新は拒否する |

開発環境ではPlugin Type `70ad882f-33b1-f111-aaac-e4fb1eff79c7`、Step `54f72039-33b1-f111-aaac-e4fb1eff79c7`、専用実行ユーザー `5f44427c-14af-f111-aaac-e4fb1eff79c7`を登録した。Diegoの直接更新はHTTP 403で、対象Linkの状態・結果・時刻は不変だった。C# Releaseテスト406件、登録DLLとローカルDLLのSHA-256 `05CF82E699A02114C3326825F8978B3779F1A2CFC7D77D3D0891692E0CBC5DEF`一致、Solution 425ファイルのCloud／localおよびrepack→unpack差分0を確認した。

ただし標準Submitが作成したLinkにはApplication Lookupが未設定で、PartnerLedger側のDecisionFlow dispatchフローも未作成である。読み取り確認したDecisionFlow管理ソリューションの`Application_OnSubmitted`は`ds_stage=100000001`の通知処理で、Application作成の送信口ではない。`ds_application.ds_deciderid`は`systemuser` Lookupで、`issue_decision_card`も単一の`ds_deciderid`と実行ユーザーの一致を検査するため、承認者M365グループをそのまま判断者として扱えるとは未確認である。既存`ds_application`を推測で割り当てず、DecisionFlow本体も変更しない。正しいApplication・判断者マッピング・外部要求キーをLinkへ確定する標準送信／バインディング経路を先に設計し、その後にこの結果取込の成功系を実機確認する。旧結果API／Stepは移行完了まで保持する。

### 承認変更セットの反映前契約（2026-09-13、WP-002-Q）

`pl_ChangeSetJson`を単なる任意JSONとして対象行へ渡さないため、`ApprovalChangeSetContract`で反映前の構文・対象・型・許可項目を検証する。入力はサーバー側で解決した対象Entity名・対象GUID・申請種別／ポリシー由来の許可項目集合を受け取り、Custom APIのクライアント入力からこれらを受け取らない。

| 境界 | 固定した契約 |
| --- | --- |
| JSON形状 | `schemaVersion=1`、`target.entity`／`target.id`、`changes[].attribute`／`changes[].value`だけを許可。未知・重複プロパティ、空配列、深すぎるJSON、64KiB超を拒否 |
| 対象 | `pl_partner`または`pl_contract`だけ。保存済みJSONのEntity名・GUIDがサーバー再取得対象と一致しない場合は拒否 |
| 許可項目 | 取引先の`pl_name`／`pl_tradingstatuscode`、契約の`pl_name`／`pl_contractstatuscode`／`pl_autorenew`／`pl_decisiondate`／`pl_enddate`／`pl_noticedate`／`pl_link`。Lookup、所有者、`pl_aclversion`、状態管理列、`pl_normalizedname`は対象外 |
| 型・値 | 文字列長、Choice値、Boolean、日時の範囲を検証。日時は明示オフセットを必須にし、UTCへ正規化。任意項目のnull以外の型違いは拒否 |
| 冪等性 | 属性順を正規化したSHA-256指紋を返し、同一キーの内容比較に使える形にする。値そのものを監査ログへ出す契約にはしない |

この契約は変更セットを更新・反映せず、反映サービスが使える正規化結果だけを返す。実行コードは署名済み個別プラグイン登録の依存関係を増やさないためBCLのみでJSONを解析し、System.Text.Jsonのサンドボックス版差異を前提にしない。申請種別から許可項目を解決する設定版、現在対象行と保存済みrow versionの再取得、取引先名変更に伴う`pl_normalizedname`のサーバー算出、契約Lookupの所属確認、監査・原子性、`pl_ApplyApprovalDecision`との結線は次の設計・実装単位で固定する。今回のローカルC#テストは117件全通過したが、Custom API／DLL／Solution／Dataverse、DecisionFlow、実データ反映、TV-14は未変更・未実施である。

### 承認反映対象の再解決契約（2026-09-13、WP-002-S）

変更セットを適用する前段として、`ApprovalTargetRepository`が申請の取引先／契約Lookupと現在行を再取得する境界を追加した。申請種別と承認対象ポリシーは次節のローカルv1契約で解決し、対象の所属と競合条件も同じ反映経路で再確認する。

| 境界 | 固定した契約 |
| --- | --- |
| 申請Lookup | `pl_request`から`pl_partnerlookup`と`pl_contractlookup`を再取得する。Lookupの論理名はそれぞれ`pl_partner`／`pl_contract`に固定する。契約Lookupが無い申請は取引先行を対象とし、契約Lookupがある申請は契約行を対象とする。申請種別文字列は未確定のため、文字列値だけで対象を決めない |
| 対象行 | `pl_partner`／`pl_contract`を明示列で再取得し、Entity名・GUID・`statecode=0`・row versionを確認する。対象欠損、非アクティブ、状態／row version欠損は拒否 |
| 契約所属 | 契約側`pl_partnerlookup`の論理名が`pl_partner`で、申請の取引先Lookupと同一GUIDの場合だけ許可。欠損・不正論理名・不一致は拒否 |
| 更新計画 | 検証済み変更セットを型付きAttribute値へ変換し、対象row versionを設定した`UpdateRequest`を`IfRowVersionMatches`で組み立てる。ここでは`Execute`しない |
| 副作用 | この段階は現在行とrow versionを返すだけで、変更セット・状態列・ACL・監査行を更新しない |

C#テストは127件全通過、Releaseビルド0エラー、Release NuGet内DLLとビルドDLLのSHA-256一致を確認した。これは対象再解決と更新計画のローカル契約であり、変更セット適用、原子性、`pl_ApplyApprovalDecision`結線、Custom API／DLL／Solution／Dataverse、DecisionFlow、TV-14の実機合格とは扱わない。

（2026-09-13の列追加前時点の記録）当時の`pl_partner`実スキーマには会社名・正規化名・取引状態等はあるが、PL-001／PL-002が要求する住所・代表電話の保存列は確認できなかった。そのため、住所・電話を承認対象へ有効化すること、既存資産へ列を追加すること、列名を推測して実装することを保留していた。後続のR2保存前スキーマゲートで、列名・型・上限を確定して適用済みである。

### 会社名正規化・承認ポリシーv1（2026-09-13、WP-002-T）

`PartnerNameNormalizer`の正規化バージョン1は、入力をUnicode FormKCへ変換し、Unicode空白をすべて除去し、Invariant大文字化した後、先頭／末尾にある限定した法人格（株式会社、有限会社、合同会社、合資会社、合名会社、主要な社団／財団・法人種別、FormKC後の`(株)`／`(有)`）だけを除去する。内部の法人格・句読点・記号・その他の文字は保持する。空文字化・入力長超過・不正な入力は拒否し、表示用`pl_name`は変更せず、会社名変更時の同一`UpdateRequest`にサーバー導出の`pl_normalizedname`を含める。名前だけの自動統合や一意制約の根拠にはしない。

`ApprovalPolicyContract`の`pl_Settings.pl_policyjson` v1は、既存の最大長200に収まる次の固定フラットJSONとする。未知／重複／欠落プロパティ、型違い、未対応schemaVersion、JSON構文違反は拒否する。

```json
{"schemaVersion":1,"companyName":true,"tradingStatus":true,"address":false,"phone":false,"contractUpdate":true}
```

（2026-09-13の列追加前時点の記録）申請種別コードは`partner.company-name`（`pl_partner.pl_name`）、`partner.trading-status`（`pl_partner.pl_tradingstatuscode`）、`partner.address`、`partner.phone`、`contract.update`（契約変更の許可属性集合）を使用する。解決時は申請種別だけでなく、現在再取得した対象Entityも一致確認する。住所／電話はポリシーがfalseなら`TargetDisabled`、trueでも当時のスキーマに保存列が無かったため`TargetSchemaUnavailable`としてfail-closedしていた。これはローカル契約であり、後続のR2保存前スキーマゲートで住所／電話列は適用済み、`pl_ApplyApprovalDecision`と対象行更新は未実装である。

C#テストは138件全通過した。Releaseビルド／NuGetの再生成とDLLハッシュ一致確認後、クラウドDLL更新やSolution変更を行う前に、変更セット実行の原子性と`pl_ApplyApprovalDecision`結線を別の高リスク実装単位として検証する。

### 承認済み版の反映実行契約（2026-09-13、WP-002-U）

`pl_ApplyApprovalDecision`は保存済みDecisionFlow結果を承認済みへ確定し、`pl_QueueApprovalReflection`は反映待ちへ移す。対象行の実反映はこの2操作へ混ぜず、未登録のローカル`ApplyQueued`サービスとして先に実装・テストする。ローカルではさらに、`ApprovalLinkId`と冪等キーだけを受けるサーバー専用`ApplyApprovalReflectionPlugin`を追加した。将来の実行口は、承認済み版を反映待ちから反映済みへ進める同期サーバー処理とする。

| 操作／状態 | 許可する条件 | 拒否・不変条件 | 機械ゲート |
| --- | --- | --- | --- |
| 反映前検査 | `ApprovalLink`が結果確認済み・承認、申請／提出版が同一ペアで反映待ち、現在の設定版・申請種別・対象Entity・変更セット・対象row versionが一致 | 未承認、結果不明、別版、設定版変更、対象欠損／非アクティブ、所属不一致、許可属性外、JSON不正、競合は対象行・状態を成功更新しない | ポリシー解決、変更セット再検証、対象再解決、row version競合テスト |
| 成功反映 | 対象行へ`IfRowVersionMatches`付き更新を行い、取引先名変更時は`pl_normalizedname`を同じ要求へ含める | `pl_normalizedname`をクライアント値で受けない。住所／電話はスキーマ不足のまま有効化しない | 型付きUpdateRequest、正規化値、許可項目、対象IDのテスト |
| 完了記録 | 同一同期処理内で提出版を`反映済み`＋`pl_fixedat`、申請を`反映済み`、成功監査を記録 | 対象だけ／状態だけ／監査だけの部分成功を許容しない。同期プラグイン例外時はトランザクション失敗として扱う | 実行順、例外時、row version競合、監査軸のテスト。実DataverseはTV-07で確認 |
| 再送 | 同じ操作種別・冪等キー・申請／提出版の成功記録があり、現在も反映済みなら更新なしで成功再送 | 同じキーの別対象・別内容、成功記録と状態の不整合は競合／データ整合性エラー | 同一再送・キー競合・状態不整合テスト |

影響範囲は対象`pl_partner`または`pl_contract`、対応する`pl_Request`／`pl_SubmissionVersion`、承認反映監査だけとし、ACL、承認Team、DecisionFlow本体、設定行、他の子テーブルは変更しない。反映実行サービスと入口クラスはまだCustom API／同期ステップへ登録しておらず、開発環境の行・Solution・公開状態も変更しない。既存PluginAssemblyだけはユーザー承認済みのDLL更新を実施し、クラウド内容とRelease DLLのSHA-256一致を確認した。C#テストは152件全通過し、対象更新後の状態更新失敗とrow version競合は、テスト用トランザクション境界で全体失敗・状態復元を確認した。人間レビューは、機械ゲート後に新規反映口・実行順・失敗時の戻りを確認してから行う。

### 反映実行APIのクラウド登録・Solution同期（2026-09-13、WP-002-X）

ユーザー承認後、`ApplyApprovalReflectionPlugin`のPlugin Type、`pl_ApplyApprovalReflection`（Global束縛・`isprivate=false`・`allowedcustomprocessingsteptype=2`）、入力2件、応答9件、専用Application User実行の同期PostOperationステップを開発環境へ登録した。API本体IDは`fa4a9e06-4baf-f111-aaac-e4fb1eff79c7`、ステップIDは`83a5f5dc-4baf-f111-aaac-e4fb1eff79c7`である。既存5承認APIへ反映を混在させず、既存アセンブリ・既存ステップ・DecisionFlow・業務データは変更していない。

API本体・入力・応答・ステップの13コンポーネントを`PartnerLedger` Solutionへ追加し、Solution component数は106件から119件へ増加した。新APIだけを対象に`PublishXml`（HTTP 204）を実行した。ゼロGUIDの無効入力スモークはHTTP 200で`Success=false`／`ErrorCode=server-only`を返し、サーバー専用入口へ到達したが、該当冪等キーの監査ログは0件で、反映対象データは作成・更新していない。

公開後の開発環境 export→unpack成果物と`solutions/PartnerLedger`を全381ファイルでSHA-256突合し、差分0件。再pack→unpackでも新API、新ステップ、新Plugin Type定義、Release DLL（SHA-256 `7D3E86704878C11283F79E60D607D542461B21B383ED6CA7E62443738A4D374A`）を確認した。`$metadata`／公式CLI検索の非表示は既存承認APIと同じ既知制限として残り、`pl_Settings`実設定行、成功系の実データ反映、DecisionFlow実接続、TV-14は未実施である。

### 設定列の物理長補正と反映実行の実機成功系（2026-09-13、WP-002-Z）

承認反映の成功系検証で、`pl_Settings.pl_PolicyJson`はメタデータ上の`MaxLength=200`に対して`DatabaseLength=200`となっており、111文字の固定v1ポリシーJSONを保存できなかった。先に同じテーブルの`pl_EffectiveAt`が`DateOnly`／`DateTimeBehavior=null`でFiltered View生成を阻害していることを検出し、ユーザー承認後に`DateAndTime`／`UserLocal`へ補正した。その後、PolicyJsonを`MaxLength=200`の契約を維持したまま物理`DatabaseLength=800`（Unicode 200文字相当）へ是正し、`pl_Settings`だけを公開した。既存データは0行である。

補正後、固定v1ポリシーJSON、合成`pl_partner`、`pl_request`、`pl_submissionversion`、結果確認済み`pl_approvallink`を使い、専用Application Userの実行主体で`pl_ApplyApprovalReflection`を実機呼出しした。初回成功では対象`pl_partner`の表示名とサーバー導出`pl_normalizedname`を同一反映で更新し、申請／提出版を`反映済み`へ進め、反映監査を1件記録した。同じ冪等キーの再送は更新なしで成功し、監査は1件のままだった。合成行とBootstrap付与・監査はテーブルの依存順に削除し、既存の孤立`pl_AccessGrant`18件は変更していない。これは反映サービスと同期ステップの実機成功系であり、DecisionFlow実接続、恒久設定、Power Appsホスト受入、TV-14の合格根拠ではない。

## 6. 権限とオフライン境界

### 所有形態の適用差異と再計画

最新のSolution展開物（2026-09-11）を確認したところ、21テーブルすべてが`UserOwned`で、`OrganizationOwned`は0件だった。設計案の「契約種類・導入設定を組織所有」はクラウドへ適用されていない。Dataverseの所有形態はテーブル作成時に決まり、作成後に変更できないため、既存テーブルを無理に変更せず、初版は全テーブル`UserOwned`を前提にSecurity Roleと共通業務操作で境界を作る。[Dataverseの所有形態](https://learn.microsoft.com/en-us/power-platform/admin/wp-security-cds)

| 区分 | 現在の所有形態 | 初版の権限設計 | 扱い |
| --- | --- | --- | --- |
| 取引先・担当者・契約 | UserOwned | 改訂PL-030の会社共有は標準Role＋User／Entraグループチーム共有を優先。契約は別の追加制限を維持 | 一般利用者への全会社Read／ShareやAssignは与えない。承認者の管理経路・所有者配置は別途確認し、標準Shareも許可された会社・権限内に限定 |
| 申請・提出版 | UserOwned | 承認者グループは下書きを含む全申請・完了後Read、申請者の編集は自分の下書きだけ | Readと判断・変更権限を分離。組織Readの実適用は別承認 |
| 名刺・OCR・確認・処理 | UserOwned | 主管チーム／処理担当の必要範囲だけ。サーバー実行主体を所有者候補にする | PL-031改訂により承認者へのReadを追加しない |
| 掲示板・アクセス付与・監査・通知 | UserOwned | 対象台帳の権限またはサーバー処理の必要範囲に限定 | 行共有の暗黙連鎖に依存しない |
| 契約種類 | UserOwned | `PL 利用者`は組織レベルRead、`PL システム管理`だけCreate／Update。行単位Shareは使わない | 既存テーブルを維持。廃止は状態で管理 |
| 導入設定・設定版 | UserOwned | `PL システム管理`と`PL システム保守`だけRead／Write。一般利用者は直接取得しない | 既存テーブルを維持。設定版を共通業務操作で解決 |

この再計画では、`pl_ContractType`と`pl_Settings`をOrganizationOwnedへ再作成しない。再作成はRelationship、Solution、既存参照、将来データ移行を伴う破壊的変更であり、別途対象・移行・戻し方の承認が必要である。UserOwnedのままでも組織レベルReadと管理者限定WriteをSecurity Roleで設定できることをTV-06／TV-13で確認する。確認できない場合だけ、OrganizationOwnedの新テーブル移行を再計画する。

Lookupがあることだけで契約・申請・名刺へ権限が連鎖すると扱わず、Relationshipの連鎖動作と行権限を実測する。共有・取消・アーカイブの共通操作は所有者直接操作の迂回を許さない。

| データ | 推奨アクセス |
| --- | --- |
| 取引先・担当者 | 改訂PL-030に従う会社別の閲覧・編集・共有管理。所有Teamは設計候補で未確定。標準CRUD／Shareを優先し、承認対象列等の不足条件だけ同期処理で補う |
| 契約 | 主管チーム所有。主管チームと指定判断者に限定 |
| 申請・提出版 | 申請者・主管チームの既存境界に加え、承認者グループは下書きを含む全申請を完了後も読取り専用で閲覧。判断権限とは別 |
| 名刺・入力版・OCR結果 | 登録者と必要な処理担当者に限定 |
| 掲示板 | 対象の基本台帳と同じ閲覧範囲。投稿は共通操作のみ |
| 設定・契約種類 | 読取りは必要範囲、変更は`PL システム管理`のみ |
| リース・操作・送信記録 | サーバー処理主体に限定。利用者には必要な状態のみ |

ホームの期限集計や取引先リストの期限列も、利用者が閲覧できる契約だけから算出する。共有取引先へ制限付き契約の期限や存在を無条件に集約保存しない。

Canvasのオフライン同期は、選択済み取引先・担当者の基本情報と自分の未完了名刺入力・必要な画像だけを対象とする。契約、期限集計、申請・承認状態、掲示板、監査詳細、OCR内部情報、他人の名刺はオンライン時のみ取得する。全件自動同期へ黙って変更しない。

## 7. 段階2の検証と完了条件

| 検証 | 自動テスト | 実機・人間レビュー |
| --- | --- | --- |
| 状態・設定 | 初期未承認の強制、二軸集計、承認5項目全組合せ、全OFF | 状態名称、アーカイブ、利用者の理解 |
| 認可・迂回 | 直接CRUD、状態変更、所有者変更、掲示板、画像、履歴の拒否 | 非管理者・他チームのPC／Canvas確認 |
| 版・競合・原子性 | row version、設定版、古い承認、並列反映、途中例外、応答消失 | 競合時の再確認・再提出運用 |
| DecisionFlow | 提出版との対応、古い・重複・別申請結果の拒否 | 導入版、差戻し、最終判断、送信応答消失 |
| Canvas同期 | 許可対象、検索API、契約・申請の混入検査 | iOS／Androidの圏外、再起動、権限取消、容量不足 |
| OCRキュー | 取込ID＋入力版の一意性、リース世代、失敗継続、再試行 | 日本語名刺、結果不明、容量・429・タイムアウト |
| 契約 | 同一種類複数、種類廃止、期限3種分離、契約ID別申請 | 契約情報の閲覧範囲と期限表示 |
| 性能 | 50人、取引先1万、担当者3万、契約1万でp95 | 固定ネットワーク・同時利用条件 |

設計確定の受入条件は次のとおり。

1. 状態の二軸、主要Lookup、所有形態、Relationshipの連鎖動作、権限境界を正本上で確定する。
2. TV-01／02、TV-05〜08、TV-12／13で、Canvas同期、承認・認可、原子性、共通操作、順次OCR、会社単位のアクセス委任の成立を実測する。
3. 直接CRUDやクライアント指定の承認済み値でガードを迂回できないことを自動試験で確認する。
4. 個人情報、端末保存、保持期間、権限の人間レビューを完了する。
5. 検証用資産の作成承認と、本実装方式の採用判定を分ける。現時点ではどちらも未実施。

### 未解決事項

- `未承認`と`未確認`を二軸で画面表示する最終名称と集計ルール。
- 未承認の取引先に対する契約の初回事実記録と、更新・終了判断の可否。
- 取引先終了時に、未終了契約・担当者・掲示板投稿を許可する範囲。
- 設定変更中の既存申請を再評価するか、旧設定版で処理するか。
- DecisionFlowの照合キー、最終判断の確定性、送信応答消失時の回復。
- OCR外部呼出しの結果不明時に、重複課金防止まで保証できるか。
- 名刺・OCR原文・掲示板・監査・提出版の保持期間と例外的削除手続。
- 登録者が退職・無効化・異動した場合の管理者不在検知と復旧アクセス。通常の既定全社アクセスで代替しない。
- 承認者グループの決定元・正確な割当先、実際の保存列の確認、旧申請閲覧付与から標準ロールへの移行。下書きReadは承認済み。申請単位の失効時点はPL-031改訂により検討対象から外す。

## 8. 適用境界

この文書は設計案であり、今回適用したDataverseのpublisher・非管理Solution・21テーブルの骨格以外について、権限変更、Canvasデータソース追加、フロー作成・有効化、公開を承認するものではない。今後の変更時は対象資産、試験データ、副作用、戻し方を提示し、既存DecisionFlowを変更しない。
