# PartnerLedger 権限ロール・行アクセス対応表

2026-09-26：移送するRoleは人向け4つ（`PL システム保守`／`PL システム管理`／`PL 利用者`／`PL 承認`）だけに確定。`PL システム管理`は申請の全件Read／Write（取消用）を持ち、提出版Readは持たない。どの人向けRoleにも環境変数の権限は付けない（承認系PluginのRun-asはシステム管理者）。技術Roleと`PartnerLedger 環境設定読取`は廃止。詳細は実装計画（開発記録、非公開）。以下の技術Roleに関する記述は廃止前の記録。

2026-09-14の再設計：PL-030の[初期共有＝承認者・登録後＝会社アクセス保持者](requirements.md#company-sharing-policy)、PL-031の全申請・完了後Read、重複確認の初回除外は要件確定済み。PartnerLedger独自Custom APIをゼロにする標準CRUD中心の受入（開発記録、非公開）を優先し、既存API・権限カタログ・過去テストを存続条件にしない。Role／共有投影Plugin／対象限定公開は承認を受けて開発環境へ適用済み。公開PC既定画面の標準`pl_partner`／共有設定CRUD切替も実機確認済み。承認者はユーザー指定のM365グループ`DecisionFlow-Deciders`に対応するDataverse Office group Teamを採用し、`PL 承認`割当とInitialSetup許可／拒否を実機確認済み。R4では`PL 利用者`へ提出版の標準Create／Write／Append=Basicを追加し、下書きCRUDガードStepとDiegoの実機確認まで完了した。R5では契約標準Createの`prvAppendTopl_ContractType`をGlobal追加し、管理者／DiegoのCreate境界を確認済み。旧Owner Teamは比較用に保持し、旧11 Custom API経路は削除済みである。以下の設計表と日付付き旧実測を区別する。最新の実績はHANDOFFを参照する。

正本 / 2026-09-14。対象はPartnerLedger専用テーブルだけ。DecisionFlowのロール、Entra／Microsoft 365グループ本体のメンバー、環境管理者権限は対象外とする。以下はUserOwnedを前提にした設計であり、4つの業務Security Roleは開発環境へ作成・権限適用済み、SharePoint系既定権限4件を4業務Roleから削除し0件を再確認済み、PartnerLedger Solutionへ収録済みである。`PL 利用者`には共有設定CRUD 4件=Basic、User／Team AppendTo 2件=Local、親Partner AppendTo=Localを適用し、R5で契約種類Lookup用`prvAppendTopl_ContractType`をGlobal追加した。`PL 承認`にも共有設定CRUD 4件、User／Team AppendTo 2件、親Partner AppendTo、必要Readを適用した。`PL 承認`はユーザー指定のM365グループ`DecisionFlow-Deciders`に対応するDataverse Office group Teamへ割当済みで、`SyncGroupMembersToTeam`後のメンバー3名とInitialSetup実機結果を確認した。実行Roleには共有設定CRUD 4件と必要なPluginメタデータReadを残し、SharePoint系4件を削除した。Diegoへ`PL 利用者`を割り当て、今後の検証用に保持している。Access Team Templateは未作成。旧11 Custom API経路は削除済みである。

## 2026-09-23の変更方針：Application Userなし・標準機能優先

2026-09-24の開発環境読取監査では、4業務Roleと4技術Roleの権限を深度込みで照合し、`PL システム保守`＋`PL システム管理`は技術Roleの権限をまだ包含していない。差分件数はアクセス実行10／承認実行12／OCR Job実行8／環境設定読取1。Role統合済みとは扱わず、必要権限を業務操作別に精査する。Partner／Contact／ShareSettingのCascade設定と、親Share実行者に必要な子テーブルShare権限は別条件である。実値・未検証範囲はFlow移行設計（開発記録、非公開）を参照。Role／割当／Stepは今回変更していない。

**2026-09-23再評価（共有投影の現行設計）**：この文書内の「移送者接続Flowが標準Shareを投影」「技術PluginはFlow移行までの暫定境界」というShareSetting記述は旧候補であり、現行の確定構成ではない。同期Pluginによる認可・ACL投影を維持する案を第一候補とし、業務要求者はInitiating User、限定Share操作は移送実行者のPlugin Step実行コンテキストで評価する構成を検証する。停止中のShareSetting Flow 2本は有効化せず、Application User／Run-as／Roleのクラウド変更も受入前に行わない。これは会社共有だけの候補であり、Partner初期共有・承認閲覧権・名刺子行Owner同期・Contact作成時に親PartnerへShareを再GrantするFlowは別の境界として棚卸し中。条件、登録Stepと未登録旧APIの区別、操作別の許可／拒否表はFlow移行設計（開発記録、非公開）を参照。

上記の旧記録にある「実行Role」「専用Application User」「同期Pluginが標準Shareを投影する」構成は現行開発環境の暫定状態であり、完成形として新規テナントへ要求しない。完成形は次の4業務Roleと移送実行者の接続で構成する。

| Role | 人に割り当てる責務 | 標準機能の範囲 | 明示的な禁止 |
| --- | --- | --- | --- |
| `PL 利用者` | 通常の台帳・名刺・契約・自分の申請 | 所有者／標準共有された行のCRUD、標準Team／User共有の対象行操作 | 全件Read、内部Job直接操作、他社への自己付与、承認・設定変更 |
| `PL 承認` | 承認者Teamの業務 | 全申請・提出版のRead、DecisionFlow結果の承認、要件で許可した会社共有 | 申請／提出版の直接変更、DecisionFlow本体変更、Role割当、Team所属変更 |
| `PL システム保守` | 移送・運用・障害対応担当者 | 移送者接続で動くFlowのDataverse操作、OCR／通知／監査／再処理 | 一般利用者への付与、クライアント指定の結果・実行者を信用する処理 |
| `PL システム管理` | PartnerLedgerの管理者 | 設定、契約種類、承認対象、理由付き管理者取消、監査 | DecisionFlow本体変更、監査・承認中データの削除・上書き |

定期／OCR／通知Flowの接続参照は、移送先でインポート実行者の接続へ割り当てる。Flowが強い接続で動く場合も、入力された行ID・要求者・対象会社・状態を必ず再照合し、画面利用者の認可を迂回する汎用更新口にしない。固定Run-asと技術Role 3個は、OCR内部Job／会社共有のFlow移行後に削除する候補であり、受入前にクラウド状態を変更しない。旧節にある「人へ付与しない非対話主体」は現行クラウドの暫定記録として扱う。

SEC-02の権限セット是正は2026-09-12に適用済みである。`PL システム保守`には契約種類のLookup解決に必要なRead（Global）だけを残し、Create／Write／Append／AppendToを個別に削除した。Diegoへの検証用`PL 利用者`割当は承認・Dataverse再読取りまで完了しているが、Diego本人の非管理者実効操作確認、一般ユーザー／Teamの割当先を含む人間レビューは未完了である。

R5の標準Create実機相当確認で、`PL 利用者`の`pl_Contract` Createには親Partnerだけでなく、Lookup先の管理者所有`pl_ContractType`へAppendToできる権限が必要と判明した。開発環境では`prvAppendTopl_ContractType`をGlobalで追加し、Diegoの契約標準Createが成功することを確認した。`pl_ContractType`のReadは組織レベル、Write／Createは`PL システム管理`に限定し、`PL 利用者`へWriteは付与していない。これはR5の実効RoleとローカルRole XMLを同期した補正である。

R8の標準契約Updateでは、`PL 利用者`に`pl_Settings`のGlobal Read（ポリシー分岐UIの現行設定取得のみ）と`pl_Contract`のBasic Writeを個別付与した。旧開発環境では専用実行Roleにも`pl_Settings`／`pl_Contract`のGlobal Readを追加していたが、これは固定Run-asを使う暫定状態であり、標準機能優先の移送先へは要求しない。AddPrivilegesRoleの既知のSharePoint系再注入は両Roleで個別除去し、再取得で0件を確認した。Write権限だけでは直接更新を許可せず、R8 Pluginがポリシー・状態・allow-list・監査を再検査する。

## 1. 権限の二層構造

PartnerLedgerでは、テーブルに対する操作可能性と、取引先行へのアクセスを分ける。新設計は本人接続の標準共有と既存User／Entraグループチームを起点とし、独自`pl_AccessGrant`台帳・投影処理を前提にしない。以下の実装記録は再利用・移行の参考であり、新方式の受入ではない。

`pl_AccessGrant`には、取引先Lookup、対象User／Team Lookup、付与理由、会社ACL版、共有投影結果の列を追加済みである。旧共通業務操作と承認基盤APIは移行受入まで保持する。完成形では標準`pl_partner`／`pl_PartnerShareSetting` CRUD、標準所有者／Team／Share、移送者接続Flowを入口とし、技術PluginはFlow移行までの暫定境界に限定する。実行Application Userは新規テナントの前提にしない。標準CreateのPostOperation共有投影とDiegoの親・子行Readは開発環境で確認済みだが、Flow移行後の再受入を残す。公開PC UIの標準共有設定Createと標準`pl_partner` Createも確認済みである。M365グループTeamの解決・メンバー同期・InitialSetup許可／拒否、申請／提出版のGlobal ReadとVanceの実効Read=HTTP 200まで確認済みで、R4の`PL 利用者`提出版Create／Write／Append=Basic、標準下書きCRUDガードStep、Diegoの下書きCreate／編集、直接提出拒否も確認済みである。公開PCの承認一覧目視、完了後を含む全状態のRead、契約・名刺等の子テーブルへの共有波及、期限切れバッチ、提出トランザクションは未実装・未検証である。

2026-09-14のR2実機確認で、`pl_RegisterPartner`のDiego成功、同一キー再送、直接`pl_partner` Create拒否、訂正版Bootstrapの登録者・監査主体を確認した。新方式では公開Power Apps画面から標準`pl_partner` Createを実行し、デモPartnerの保存→Dataverse一覧表示まで確認した。これはSecurity Role単体の完全な受入ではなく、保存APIの入口権限とサーバー側Create境界の確認である。重複候補の開示境界、一般ユーザー／Teamの恒久割当後の実効操作は未実施である。

1. **Security Role**：その利用者が、対象テーブルで作成・読取り・更新・付加・付加先などの操作を実行できるかを定める。
2. **取引先行アクセス**：対象取引先のアクセスチームまたはサーバー側共有により、会社Aの行を読めるかを定める。会社Bの行へ暗黙に広がらない。

画面の非表示だけを認可としない。改訂PL-030では通常利用者にも許可された会社行の共有設定を認めるが、一般の`PL 利用者`／`PL 承認`へ`pl_Partner`のネイティブShareを渡さない。共有設定行の標準CRUDを入口にして移送者接続Flowが標準Shareを投影する候補とする。全会社ShareやAssignを渡す変更ではない。Flow移行前は同じ境界を暫定Pluginで維持する。PL-031の全申請Readと、PL-030の承認者による会社共有管理は目的・対象表を分ける。契約・名刺等の追加制限は維持する。

2026-09-14のユーザー承認済み方針では、全社共通の専用Dataverse Teamに`PL 承認`を割り当てる。このTeamを初期共有の保護経路とし、初期設定中のUser共有・その他Team共有は保護しない。登録後は会社への実効アクセスを持つメンバーも共有設定行を管理でき、本人の実効権を超える付与だけを拒否する。ユーザー指定のM365グループ`DecisionFlow-Deciders`に対応するDataverse Office group Teamを採用し、作成・Entra関連付け・`PL 承認`割当・メンバー同期まで完了している。会社共有の投影は移送者接続Flowへ移行するまで暫定Plugin経路を保持する。

### Custom APIゼロR2のRole原則（一部適用）

| 主体 | 標準CRUDの候補 | 明示的に渡さないもの | サーバー側の補完 |
| --- | --- | --- | --- |
| `PL 利用者` | 自分が作成・共有された`pl_Partner`／`pl_Contact`と、共有設定行の必要最小Create／Read／Update | `pl_Partner`のShare／Assign、全会社Read、保護された承認者ルートの変更、実行用Application User権限 | 共有設定行Create／Update時に、初期設定状態・会社アクセス・付与上限・対象主体を同期検査 |
| `PL 承認` | PL-031の申請／提出版Global Read、保護共有された取引先のRead、初期共有用と登録後の共有設定行Create／Read／Update | 取引先の基本編集、`pl_Partner`のネイティブShare／Assign、申請／提出版Write、保護された承認者Teamの最後の管理経路の取消 | 初期設定中は承認者Teamだけを許可。設定完了後は会社アクセス保持者としても通常共有を許可し、保護経路だけをサーバーで保護 |
| 移送者接続Flow | 画面利用者へ付与しない。移送先で接続参照へ割り当てる | 任意の行ID・要求者・実行者を受け入れない。DecisionFlow本体操作もしない | 標準`GrantAccess`／`ModifyAccess`／`RevokeAccess`、OCR内部Job作成、通知を、入力行の再照合後に行う |

この表はRoleの最終Privilege名・深度を設計する正本である。Dataverseの実際のPrivilege、UserOwnedの所有・共有の加算、Pluginの実行コンテキストを確認したうえで、2026-09-14に承認済みの対象Roleへ個別反映した。専用承認Teamの割当と、一般ユーザー／Teamへの恒久割当は別途扱う。

2026-09-14、`PL 利用者`へ共有設定行の4権限をBasic、`prvAppendToUser`／`prvAppendToTeam`をLocal、親`prvAppendTopl_Partner`をLocalで個別追加した。`PL 承認`へも共有設定行の4権限、User／Team AppendTo 2件、親AppendTo、必要なPartner Readを適用した。2026-09-15にPL-031の`pl_Request`／`pl_SubmissionVersion` ReadをGlobalへ個別追加した。実行Roleへ共有設定行4権限をGlobal追加した。`prvAppendToUser`／`prvAppendToTeam`はBasic不可・Local可で、Basic指定は部分適用なしで拒否された。Role再取得でSharePoint系4件は`PL 利用者`／`PL 承認`／実行Roleの全て0件、実行RoleのPluginメタデータReadは維持された。[Privilege確認](https://learn.microsoft.com/en-us/power-platform/admin/how-record-access-determined)

旧Plugin未登録時のDiego標準Createは親`pl_Partner`行へのAppendTo不足でHTTP 403となったが、承認後に`PL 利用者`の親AppendToをLocalへ適用した。新Plugin登録後、管理者所有Partnerへの標準ShareSetting CreateとDiego共有を実施し、Diego偽装で親GET=200・子行GET=200を確認した。標準CreateでShare／Assignを直接許可したわけではなく、共有設定行のCreateを同期Pluginが検査し、実行主体が標準Shareを投影する。[Data operations and access rights](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/security-access-rights)

## 2. 4ロール構成

| ロール | できること | できないこと | 行アクセスの前提 |
| --- | --- | --- | --- |
| PL 利用者 | 一般利用者の新規登録、許可会社・担当者の閲覧／編集、登録後の共有設定行による会社共有設定。契約・名刺等は各要件の範囲 | 初期共有の先回り、他社への自己付与、保有範囲を超す権限付与、ネイティブShare／任意Assign、保護された承認者経路の変更、他人の名刺、提出版改変、DecisionFlow本体変更 | 標準CRUDを起点に、テーブル権限＋対象行権限＋同期ガードで制御。直接Createの一律禁止を新設計の前提にしない |
| PL 承認 | PL-031の全申請・提出版の継続Read。承認者グループはPL-030の初期共有・登録後管理も行う | 全申請Readを理由とする申請／提出版変更・ネイティブShare／Assign、無権限の判断、契約・名刺への一律アクセス拡大 | 申請Readと会社共有管理を別目的で設計。会社の基本編集は共有管理から自動付与しない。Role／所有／割当は適用前に確定 |
| PL システム保守 | OCR、状態遷移、冪等処理、通知、監査、移送後の接続・設定運用を担当する | 画面利用者としての任意編集、クライアント指定の承認結果・実行者の受入、DecisionFlow本体の改修 | Flow接続の引継ぎ・再接続と障害対応を担当。Application Userは要求しない |
| PL システム管理 | 契約種類、承認対象、通知、運用設定、権限モデルの管理と監査 | 通常利用者としての会社データ閲覧範囲の拡大、既存DecisionFlowの改修、監査履歴・承認中版の上書き | `pl_ContractType`／`pl_Settings`と技術設定を管理。環境のSystem Administratorとは別のPartnerLedger管理ロール |

### Role定義の再監査（2026-09-23、開発環境 Solution export Readback）

PACのActive profileが開発環境であることを確認し、`PartnerLedger` Solutionを変更せずExportしてRole componentをReadbackした。Solution package内には7つのRole定義があり、`PL 利用者`58件、`PL 承認`17件、`PL システム保守`93件、`PL システム管理`13件、`PartnerLedger アクセス実行`26件、`PL OCR Job実行`18件、`PartnerLedger 環境設定読取`1件だった。`PartnerLedger 承認実行`は環境全体のRole一覧には以前存在を確認しているが、今回のSolution exportには含まれなかった。逆に`PartnerLedger 環境設定読取`はExportに含まれるが、現ローカルSolution Role XMLにはない。これはパッケージ間の構成差分である。ExportにはRole割当先の一覧は含まれないため、人／Team／Application Userへの実割当はこのReadbackから断定しない。

ローカルXMLと今回のExportの権限名・深度を突合すると、4人向けRoleと`PartnerLedger アクセス実行`／`PL OCR Job実行`の6定義は完全一致し、相違は上記2 Role componentだけだった。ローカルの`PartnerLedger 承認実行`は31件、Export側の`PartnerLedger 環境設定読取`は1件。Solution再importやRole削除でこの差分を自動是正せず、完成形に不要なApplication User用Roleをどう移送物から外すか、残存する承認処理の実行方式と合わせて決める。

続けて開発環境の全PartnerLedger／PL Roleと割当関連を直接GETしたところ、環境にはRoleが8件あった。`PartnerLedger アクセス実行`／`PartnerLedger 承認実行`／`PL OCR Job実行`にApplication Userが各1名、`PartnerLedger 環境設定読取`にApplication Userが3名割当済みで全員有効。`PL 利用者`は非Application User 1名とTeam 1組、`PL 承認`はTeam 1組。`PL システム保守`／`PL システム管理`はユーザー／Teamへの割当なし。したがって、開発環境は現時点でApplication User依存が残っており、4 Role＋移送者接続の完成形に切替済みではない。既存App User割当はOCR／共有／承認結果の移行E2Eと人間レビューまで解除しない。

`PL システム保守`はExport上の93件全てがGlobal深度で、`pl_Partner`・`pl_Contact`・`pl_Contract`へのGlobal Shareを含む。人向けの完成目標として広すぎるため、そのまま移送者／保守担当へ割り当てない。ローカル正本での旧技術Role 3つ（Access／Approval／OCR）のPrivilege和集合は60件だが、保守Roleはそのうち22件を含まず、技術Role和集合にないPrivilege名を55件持ち、共通名2件は深度が異なる。従って「保守Roleが技術Roleを包含している」とは言えず、権限を丸ごとコピー・Role統合する根拠にもならない。

このReadbackは**現開発環境 Solution package内のRole定義**の証拠であり、Environment全体の最新Role割当や他Solution由来権限、ユーザーの実効権限までは示さない。別途確認済みの過去クラウドPrivilege値と混同せず、次はFlow／Plugin操作ごとに必要権限・実行主体を結び、Packageの欠落／余分なRoleを整合する候補を作る。**Solution再import、Role変更・割当解除、技術Role削除は、差分・許可／拒否テスト・戻し方を示して人間レビューを得るまで行わない。**

上の4ロールは既存の業務責務を説明するための枠であり、再設計の権限ペイロードではない。旧`取引先アクセス管理者`プロファイルを追加付与しなくても、登録後の会社アクセス保持者は共有設定を行える。Shareは会社行の境界と組み合わせ、Assignや全会社アクセスを一括で与えない。

一人の利用者が複数ロールを持つ場合、権限は累積する。`PL システム保守`は人へ付与せず、PL-031の申請ReadとPL-030の会社共有を分けて評価する。会社Aの共有メンバーが会社Bへ自己付与できる状態や、承認者の所有Teamと利用者Roleの合算で意図しない全件編集が生じる配置を作らない。

## 3. テーブル別の公開境界

| テーブル群 | PL 利用者 | PL 承認 | PL システム保守 | PL システム管理 | 方針 |
| --- | --- | --- | --- | --- | --- |
| `pl_Partner`、`pl_Contact` | 新規登録、許可行の閲覧・編集。会社共有は共有設定行の標準CRUDから同期投影 | PL-030の初期共有・継続管理に必要な会社アクセス。担当者へ無条件に広げない | 標準だけで不足する初期化・状態検証・監査等に限定 | 管理目的の設定・監査範囲だけ | 一般利用者の全会社Read／ネイティブShareは不可。承認者のRole／所有配置、共有連鎖は別途検証。終了・退職は状態で管理 |
| `pl_Contract` | 対象会社・主管チーム・指定判断者の行だけ。初回記録と更新・終了判断申請 | 申請対象の最小コンテキストだけ | 期限算出、初回登録、契約単位反映 | 障害対応・監査の必要範囲だけ | 取引先共有だけで契約を公開しない |
| `pl_CaptureBatch`、`pl_CardCapture`、`pl_CardInputVersion`、`pl_OcrJob`、`pl_BusinessCardReview` | 自分の下書きと許可された確認対象だけ | 不可 | 画像到着確認、OCR、状態遷移、監査 | 障害・再処理の管理範囲だけ | 画像・OCR原文は会社基本台帳とは別境界。Canvas同期は選択した自分の対象だけ |
| `pl_Request`、`pl_SubmissionVersion` | 自分の申請状況と提出前の自分の版 | 下書きを含む全申請・提出版のRead、完了後も継続 | 提出版固定、DecisionFlow照合、反映 | 監査・障害対応の必要範囲だけ | Readだけの拡大。申請・提出版の変更権限と判断権限は拡大しない |
| `pl_ApprovalGrant`、`pl_ApprovalLink` | 必要な申請状況だけを既存境界で返す | 全申請Readの対象外。これらの内部行を読む必要性は別途評価 | 既存付与の移行、DecisionFlow照合 | 監査・障害対応の必要範囲だけ | ApprovalGrantは新方式の必須データから外すが既存行は保持。ApprovalLinkは結果照合に引き続き必要 |
| `pl_AccessGrant` | 旧実装は直接Read／Write不可。新設計はこの台帳を共有設定の前提にしない | 新方式の必須Read対象にしない | 旧付与の照合・移行 | 設計・監査の必要範囲だけ | 存廃は新設計後に判断。旧投影と標準共有の二重正本を残さず、既存行は移行承認まで保持 |
| `pl_BulletinPost` | 共有済み基本台帳の投稿・閲覧 | 判断に必要な最小範囲だけ | 投稿者・時刻・対象をサーバー確定 | 監査の必要範囲 | 編集・削除・添付・通知は初版対象外 |
| `pl_ContractType` | 組織レベルRead | 不可 | 契約登録時のLookup解決 | Create／Update／廃止 | 行共有や自由入力を使わず、廃止は状態で保持 |
| `pl_Settings` | 組織レベルRead（ポリシー分岐UIの現行設定取得のみ） | 不可 | 有効設定版の解決 | 設定版の作成・管理 | 過去版を上書きせず、反映操作が参照する版を固定。利用者のWriteは不可 |
| `pl_OperationLog`、`pl_WorkLease`、`pl_Notification` | 不可 | 不可 | 必要範囲の作成・更新・照合 | 監査閲覧 | 監査・処理状態を利用者の入力対象にしない |

## 4. 会社単位の追加・取消契約

取引先詳細の共有設定では、既存の個人／セキュリティグループ／M365グループと保有範囲内の権限を指定する。グループはDataverseのEntraグループチームを用いる。対象は会社への共有であり、グループ本体の作成・メンバー変更ではない。旧プロファイル・期限入力を新UIの必須項目にしない。

- 一般利用者も登録でき、初期共有は承認者グループが設定する。登録者だけに初期管理を与える旧Bootstrapは移行対象。
- 登録後は会社アクセス保持者も追加・変更・取消できる。別の管理者プロファイルを要求せず、実効権、対象会社、主体、保有範囲、承認者の管理経路をサーバーで評価する。標準配置で満たせる部分に独自判定を重ねない。
- 取引先アクセスを取り消しても、User／Team本体や別会社の付与は変更しない。取消後のPC／Canvas／APIの新規取得を拒否する。
- PL-031の全申請ReadとPL-030の会社共有管理は別の権限。共有管理を契約・名刺本体や申請／提出版の更新権へ拡大しない。既存共有の取消・期限データは移行時に照合する。
- 端末へ同期済みのデータは権限取消と同時に失効できない場合があるため、同期対象、保持期間、サインアウト、再接続時の除外を運用条件として定める。

## 5. Security Roleの作成・適用ガード

新規Security Roleへ`AddPrivilegesRole`でPartnerLedger権限を追加しただけでは、Dataverseの初期権限が残る。開発環境の一時検証では、意図した`prvReadpl_Partner`／`prvReadpl_Contact`の2件に加えて、SharePoint・SDKメッセージ・プラグイン関連の標準権限9件が含まれ、合計11件になった。この一時ロールは削除済みである。

恒久ロールは次の順で適用する。

1. 役割ごとの許可権限名・深度を、環境固有IDではなくプロジェクト正本の許可リストで固定する。
2. 新規ロール作成後、`ReplacePrivilegesRole`で許可リスト全体を置き換える。`AddPrivilegesRole`だけで完成扱いにしない。
3. 適用前に取得した権限セットを、欠落・想定外・重複・深度違いで監査する。1件でも不一致ならユーザー／Teamへ割り当てない。
4. 割当後に対象ユーザー／Teamのロールと、会社A／Bの実効読取り・書込みを確認する。失敗時は割当解除とロール削除を行う。

この監査契約は`apps/pc/src/rolePrivilegePolicy.ts`とテストで固定している。2026-09-12の開発環境実適用では、初回の`ReplacePrivilegesRole`後にDataverseの新規Roleへ既定付与される次の4件がGlobalで残った。

- `prvReadSharePointDocument`
- `prvReadSharePointData`
- `prvWriteSharePointData`
- `prvCreateSharePointData`

公式サンプルは新規Roleの既定権限としてSharePoint 4件とSDK／Plugin系5件を示すが、4件が必須・削除不能とは示していない。契約書は単なるリンクとして設計しているため、この4件を基盤例外として許容しない。追加承認を受けて4ロールすべてから`RemovePrivilegeRole`で削除し、SharePoint系4件が0件であることを再取得で確認した。`ReplacePrivilegesRole`を再実行すると4件が再付与されるため、今後の権限セット変更は個別の`AddPrivilegesRole`／`RemovePrivilegeRole`で行う。4ロールは未割当のまま、割当先と実効試験の承認待ちである。

2026-09-12に残り3ロール（`PL 利用者`／`PL システム保守`／`PL システム管理`）でも`RemovePrivilegeRole`を実行し、`PL 承認`（再置換で復活していた分）も含めて4ロールすべてでSharePoint系4件が0件であることを確認した。以後、権限セットを変更する際は`ReplacePrivilegesRole`（4件を再付与してしまう）ではなく、`AddPrivilegesRole`／`RemovePrivilegesRole`で個別に行う。4件なしでのSolution移送後の保持、ユーザー／Team割当後の実効動作は未検証のまま。なお、2026-09-14のR2試験では別管理の`PartnerLedger アクセス実行`に不足していた`prvReadPluginType`／`prvCreatepl_Partner`だけを個別追加したところ、同RoleへSharePoint系4件も存在する状態が再取得された。これは4業務ロールの0件確認とは別のRoleであり、差分是正を別カードに分離する。

## 6. 受入条件

SEC-03の新期待値は初期共有＝承認者、登録後＝会社アクセス保持者による個人／両グループ種別の設定。下表SEC-03は旧試験の証跡として保持する。新方式は共有・取消・権限上限の受入表（開発記録、非公開）で確認し、旧テストの継承は要求しない。SEC-06も直接CRUD／Shareの全面禁止ではなく、承認・不変条件・行権限の迂回拒否を検証する。

| ID | 条件 | 確認方法 |
| --- | --- | --- |
| SEC-01 | 会社Aだけを共有した利用者は、会社Bの取引先・担当者・契約・申請・掲示板・名刺画像を取得できない | PC／Canvas／APIの同一利用者で照合。**2026-09-12に実機確認済み**：`PL 利用者`ロール（SharePoint権限削除後のクリーンな状態）を割当てた既存非管理者ユーザーへ`pl_GrantAccess`で会社Aだけ基本編集を付与し、`CallerObjectId`で代理呼出し。会社AはGET 200・PATCH 204、会社BはGET 403、PATCHは412（`0x80040237`、通常のAccessDenied 403とは異なるコード。同一PATCHを管理者として実行すると204で成功するため、権限起因の拒否であってデータ不整合ではないことを確認済み。理由はDataverseの内部実装差と推定・未確定）で書込み不可を確認。試験用データ・ロール割当は削除・解除済み |
| SEC-02 | 組織レベルReadを持つのは契約種類など必要なマスタだけで、契約種類のWriteは`PL システム管理`に限定される | Security Roleと非管理者操作。**2026-09-12に`PL システム保守`からCreate／Write／Append／AppendToのGlobal権限を削除し、Read(Global)だけを再確認。権限セット部分は是正済み、非管理者の実効操作確認は未実施** |
| SEC-03 | アクセス管理者がUI/APIから会社AにUser／Teamを追加・変更・取消できるが、会社B、自分の権限上限、最後の管理者を変更できない | 共通業務操作、直接API、同時実行。**2026-09-12に実機確認済み**：会社C（テスト）でDiegoを恒久アクセス管理者、Alexを期限付きアクセス管理者として付与。①Diego自身によるC上での正当な新規付与（他ユーザーへの閲覧権付与）→成功。②Diegoが会社D（無関係）への付与を試行→`not-authorized`で拒否。③Diegoが自分自身の付与をModifyAccessで格下げ試行→`self-escalation`で拒否。④Alex（期限付き管理者、恒久管理者ではない）がDiego（唯一の恒久管理者）の付与をRevokeAccessで取消試行→`not-authorized-or-last-access-manager`で拒否（最後の恒久管理者保護が、呼び出し元自身への操作でなくても機能することを確認）。試験データは全て削除・ロール解除済み |
| SEC-04 | 承認者は下書きを含む全申請・提出版を完了後も読める。これだけで提出版変更、申請のShare／Assign、無権限の判断、契約・名刺取得を許可しない。会社共有管理はPL-030として別に試験 | `PL 承認`へ申請／提出版Read=Globalを適用し、TeamメンバーVanceでデモ申請・提出版Read=HTTP 200を確認済み。残りは公開PCの一覧目視、完了後を含む全状態のRead、提出版直接Update拒否、追加制限付き本体取得拒否、役割解除後の実効権限であり、旧API到達・申請単位Readの合格を流用しない |
| SEC-05 | 会社ACL等の取消・期限切れ・アーカイブ後の新規取得と再接続同期を所定の境界で制御する。申請の完了・期限切れは承認者のRead取消理由にしない | PC／Canvas再接続、端末保持の実機確認。会社ACLの取消試験と承認者ロール解除試験を分ける。過去のSEC-01／SEC-03試験は旧実装の限定証跡。新しい承認者の役割解除・他経路の残存権限・オフライン除外は未検証 |
| SEC-06 | 直接CRUD、クライアント指定状態、画面フィルター除外を迂回手段として受け入れない | 非管理者API、状態遷移テスト、監査。**2026-09-12に実機確認済み**：`PL 利用者`ロールを持つ非管理者ユーザーで`pl_accessgrant`テーブルへ直接Web API Create／RetrieveMultipleを試行し、いずれも`prvCreatepl_AccessGrant`／`prvReadpl_AccessGrant`が不足しているとして403で拒否されることを確認（4業務ロールのいずれもpl_AccessGrantへの直接権限を持たない設計どおり）。クライアント指定状態・画面フィルター除外の迂回耐性は未着手 |

この表をもとにTV-06／TV-13／TV-14を実施する。初版は`GrantAccess`／`ModifyAccess`／`RevokeAccess`相当のサーバー側共通業務操作でUser／Teamへの会社単位共有を投影する方式を実装・登録済みである。R2の`pl_RegisterPartner` Custom API、直接Createガード、同期Bootstrapも登録・Solution収録・公開し、Diegoの成功／同一キー再送／直Create拒否を実機確認した。Access Team Templateは対象テーブルの有効化と行専用チーム生成を伴うため、明示共有が成立しない場合の代替候補として保留する。4つのPartnerLedger Security Roleの作成・権限適用・SharePoint系4件の削除・Solution収録は完了している。承認基盤5 API・同期ステップは登録・Solution収録・公開済みだが、新5 APIのmetadata／公式CLI discovery問題が残るためPCの5 API公式生成は未到達である。申請／提出版の読み取りは公式生成テーブルサービスで接続し、`PL 承認`のGlobal ReadとVanceのHTTP 200まで確認した。ユーザー／Team割当、DecisionFlow接続、子テーブル境界、期限切れバッチ、接続参照の作成・変更、提出・承認・反映の実効Writeは未実施である。[Dataverseの共有操作](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/security-sharing-assigning)

ローカルの`apps/pc/src/accessPolicy.ts`と`accessPolicy.test.ts`には改訂前のSEC-01〜SEC-06の先行モデルがある。会社A／B、契約・名刺処理の分離、申請ID単位の承認閲覧、期限切れ・取消、最後のアクセス管理者保護等を検査する。PL-031の申請ID単位Read／完了後失効の期待値はGlobal Readへ改訂済みであり、旧テスト合格は新仕様の証拠にならない。無関係な会社ACL・反映の保護は残して新しいSEC-04を固定し、公開PCの目視、全状態のRead、提出版の直接更新拒否、役割解除後の実効権限を追加確認する。メモリ内テストだけではDataverseの実効権限を証明しない。
