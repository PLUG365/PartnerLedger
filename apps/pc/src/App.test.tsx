import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import App, { ApprovalPolicySettingsForm, ContactRegistrationForm, HomeView, LiveArchiveView, LiveBusinessCardQueue, LiveContactDetail, LiveContactDirectory, LiveContractReadSection, LiveContractRegistrationForm, LivePartnerDetail, LivePartnerDirectory, LivePartnerShareSettingsPanel, NewRegistrationView, RegistrationForm } from './App'
import { ApprovalCenterView } from './ApprovalCenter'
import { partnerLinkFromQuery } from './appLinks'
import { approvalRequestTypeLabels, changeAttributeLabel, formatChangeValue, parseChangeSet, saveResubmissionDraft, sortApprovalItems, statusGroup } from './approvalPresentation'
import { LivePartnerEditForm } from './LivePartnerEditForm'
import { DataverseBusinessCardRegistration } from './DataverseBusinessCardRegistration'
import { LiveContractApprovalDraftForm } from './LiveContractApprovalDraftForm'
import type { ApprovalInboxItem } from './approvalReadModel'
import type { StandardApprovalWriteApi } from './standardApprovalTransitionApi'
import { ThemeSwitcher, UserMenu } from './HeaderTools'

// 利用者に見せない技術用語・試作時の文言。どの画面にも出てはいけない。
const forbiddenTexts = ['モック', 'Dataverse', '標準CRUD', '標準Read', '標準Create', '標準Update', '冪等', '要求キー', 'LIVE ', 'STANDARD ', 'Custom API', 'ACL版', 'PartnerId', 'ContactId', 'デモ', '架空']

function expectPlainLanguage(html: string) {
  for (const text of forbiddenTexts) expect(html, `画面に「${text}」が含まれている`).not.toContain(text)
}

const partner = {id: '33333333-3333-4333-8333-333333333333', name: '青空ソリューションズ株式会社', industry: 'IT', tradingStatus: '取引中' as const, mainOwnerName: '田中 健一', ownerName: '田中 健一', registeredBy: '佐伯 菜月', registeredAt: '2026-09-12T00:00:00.000Z', aclVersion: 3}
const contact = {id: '44444444-4444-4444-8444-444444444444', name: '井上 真帆', partnerId: partner.id, partnerName: partner.name, departmentRole: 'パートナー推進部 部長', email: 'm.inoue@example.com', phone: '03-1234-5678', status: '在籍' as const, registeredAt: '2026-09-12T00:00:00.000Z'}
const contract = {id: '55555555-5555-4555-8555-555555555555', name: '基本取引契約（2026年度）', partnerId: partner.id, contractTypeName: '基本取引契約', status: '締結済み' as const, endDate: '2027-03-31T00:00:00.000Z', noticeDate: '2027-01-31T00:00:00.000Z', decisionDate: '2027-01-15T00:00:00.000Z', autoRenew: true, link: '未登録'}
const writeApi: StandardApprovalWriteApi = {
  createRequest: async () => ({requestId: '66666666-6666-4666-8666-666666666666'}),
  createSubmissionDraft: async () => ({submissionVersionId: '77777777-7777-4777-8777-777777777777'}),
  updateRequestDraft: async () => ({requestId: '66666666-6666-4666-8666-666666666666'}),
  updateSubmissionDraft: async () => ({submissionVersionId: '77777777-7777-4777-8777-777777777777'}),
  submit: async () => ({requestId: '66666666-6666-4666-8666-666666666666'}),
  cancel: async () => ({requestId: '66666666-6666-4666-8666-666666666666'}),
}

function approvalItem(status: string, requestType: string, changeSetJson: string, requestedAt = '2026-09-20T00:00:00.000Z', submissionStatus = status === '下書き' ? '下書き' : '提出済み'): ApprovalInboxItem {
  const latestSubmission = {id: `sv-${status}`, requestId: `req-${status}-${requestType}`, name: `提出版 ${status}`, status: submissionStatus, versionNumber: submissionStatus === '下書き' ? undefined : 1, changeSetJson}
  return {
    request: {id: `req-${status}-${requestType}`, name: `${partner.name} ${status}`, requestType, status, requestedAt, partnerName: partner.name, requestingUserName: '佐伯 菜月'},
    latestSubmission,
    draftSubmission: submissionStatus === '下書き' ? latestSubmission : undefined,
  }
}

const nameChange = JSON.stringify({schemaVersion: 1, target: {entity: 'pl_partner', id: partner.id}, changes: [{attribute: 'pl_name', value: '青空ソリューションズ ホールディングス'}]})
const statusChange = JSON.stringify({schemaVersion: 1, target: {entity: 'pl_partner', id: partner.id}, changes: [{attribute: 'pl_tradingstatuscode', value: 100000002}]})
const contractChange = JSON.stringify({schemaVersion: 1, target: {entity: 'pl_contract', id: contract.id}, changes: [{attribute: 'pl_name', value: '基本取引契約'}, {attribute: 'pl_contractstatuscode', value: 100000001}, {attribute: 'pl_enddate', value: '2027-03-31T00:00:00.000Z'}, {attribute: 'pl_autorenew', value: false}, {attribute: 'pl_link', value: null}]})

describe('PCの画面（静的描画）', () => {
  it('初期画面はナビゲーションだけを出し、試作用の切替や説明を出さない', () => {
    const html = renderToStaticMarkup(<App />)
    for (const text of ['ホーム', '承認申請', '新規登録', '取引先リスト', '担当者リスト', 'アーカイブ', '設定', '取引先を読み込んでいます']) expect(html).toContain(text)
    for (const text of ['リスト中心', 'モバイル', 'v0.1', 'デモを初期化', 'デモ基準日']) expect(html).not.toContain(text)
    // メニューの開閉は三本線。詳細を開くパネル形のアイコンとは別の形にする。
    expect(html).toMatch(/aria-label="メニューを折り畳む"[^>]*><svg[^>]*lucide-menu/)
    expect(html).not.toContain('lucide-panel-left')
    expectPlainLanguage(html)
  })

  it('ヘッダーに検索・テーマ切替・サインイン中の利用者を出す', () => {
    const html = renderToStaticMarkup(<App />)
    for (const text of ['取引先・担当者を検索', 'aria-label="表示テーマ"', 'aria-label="ライト"', 'aria-label="ダーク"', 'aria-label="システムに合わせる"', 'サインイン中の利用者']) expect(html).toContain(text)

    const theme = renderToStaticMarkup(<ThemeSwitcher value="dark" onChange={() => {}} />)
    expect(theme).toMatch(/aria-pressed="true" aria-label="ダーク"/)
    expect(theme).toMatch(/aria-pressed="false" aria-label="ライト"/)

    const user = renderToStaticMarkup(<UserMenu user={{fullName: '山田 太郎', userPrincipalName: 'taro@example.com'}} />)
    expect(user).toContain('aria-label="山田 太郎（サインイン中）"')
    expect(user).toContain('>山</button>')
  })

  it('取引先リストは業務の列だけを表示し、読み込み失敗時は再読み込みできる', () => {
    const html = renderToStaticMarkup(<LivePartnerDirectory state={{status: 'ready', partners: [partner]}} query="" selectedId="" onQueryChange={() => {}} onShowDetail={() => {}} onRefresh={() => {}} />)
    for (const text of [partner.name, 'IT', '取引状態', '主担当', '田中 健一', '佐伯 菜月', '2026-09-12', '再読み込み']) expect(html).toContain(text)
    for (const text of ['title="取引先で並べ替え"', 'aria-label="取引状態で絞り込み"', 'aria-label="主担当で絞り込み"']) expect(html).toContain(text)
    expect(html).not.toContain(partner.id)
    // 「会社確認」はアプリで変えられず常に未確認だったため、表示しない（2026-09-26、ユーザー判断）。未承認かどうかは取引状態で分かる。
    expect(html).not.toContain('会社確認')
    // 詳細を開くボタンは横スクロールしなくても押せるよう、行の先頭に置く。
    expect(html).toMatch(/<tr[^>]*><td class="row-open-cell"><button class="row-open-button" type="button" aria-label="青空ソリューションズ株式会社の詳細を開く"/)
    expect(html).not.toContain('row-arrow')
    expect(html).toContain('lucide-panel-right-open')
    expectPlainLanguage(html)

    const error = renderToStaticMarkup(<LivePartnerDirectory state={{status: 'error', message: '取引先を読み込めませんでした。'}} query="" selectedId="" onQueryChange={() => {}} onShowDetail={() => {}} onRefresh={() => {}} />)
    expect(error).toContain('取引先を読み込めませんでした。')
    expect(error).toContain('再読み込み')
    expect(error).not.toContain(partner.name)
  })

  it('担当者リストは連絡先を出さず、詳細で連絡先と編集を表示する', () => {
    const html = renderToStaticMarkup(<LiveContactDirectory state={{status: 'ready', contacts: [contact]}} query="" selectedId="" onQueryChange={() => {}} onShowDetail={() => {}} onRefresh={() => {}} />)
    for (const text of [contact.name, partner.name, '部署・役職', '在籍状態']) expect(html).toContain(text)
    expect(html).not.toContain(contact.email)
    expectPlainLanguage(html)

    const detail = renderToStaticMarkup(<LiveContactDetail contact={contact} api={{update: async () => ({contactId: contact.id, retired: false})}} onClose={() => {}} />)
    for (const text of [contact.email, contact.phone, '担当者を編集', '基本情報', '掲示板']) expect(detail).toContain(text)
    expect(detail).not.toContain(contact.id)
    expectPlainLanguage(detail)
  })

  it('契約・期限は期限と操作だけを表示し、承認設定に応じて申請か登録を出す', () => {
    const readOnly = renderToStaticMarkup(<LiveContractReadSection state={{status: 'ready', contracts: [contract]}} approvalPolicyState={{status: 'ready', policy: {companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: false, settingsVersion: 1}}} contractTypeState={{status: 'ready', contractTypes: [{id: '1', name: '基本取引契約'}]}} onRegister={() => {}} onRequestUpdate={() => {}}/>)
    for (const text of ['契約・期限', contract.name, '契約終了日', '2027-03-31', '解約通知期限', '更新判断期限', '契約を登録', '更新・終了判断を申請']) expect(readOnly).toContain(text)
    expect(readOnly).not.toContain('更新・終了判断を登録')
    expect(readOnly).not.toContain(contract.id)
    expectPlainLanguage(readOnly)

    const linked = renderToStaticMarkup(<LiveContractReadSection state={{status: 'ready', contracts: [{...contract, link: 'https://contoso.example/contracts/a.pdf'}, {...contract, id: '66666666-6666-4666-8666-666666666666', link: 'javascript:alert(1)'}]}} approvalPolicyState={{status: 'ready', policy: {companyName: false, tradingStatus: false, address: false, phone: false, contractUpdate: false, mainOwner: false, settingsVersion: 1}}}/>)
    expect(linked).toContain('<a href="https://contoso.example/contracts/a.pdf" target="_blank" rel="noopener noreferrer">')
    expect(linked).not.toContain('href="javascript:')
    expect(linked).toContain('javascript:alert(1)</dd>')

    const direct = renderToStaticMarkup(<LiveContractReadSection state={{status: 'ready', contracts: [contract]}} approvalPolicyState={{status: 'ready', policy: {companyName: false, tradingStatus: false, address: false, phone: false, contractUpdate: false, mainOwner: false, settingsVersion: 1}}} onDirectUpdate={() => {}}/>)
    expect(direct).toContain('更新・終了判断を登録')
    expect(direct).not.toContain('更新・終了判断を申請')

    const empty = renderToStaticMarkup(<LiveContractReadSection state={{status: 'ready', contracts: []}} approvalPolicyState={{status: 'ready', policy: {companyName: false, tradingStatus: false, address: false, phone: false, contractUpdate: false, mainOwner: false, settingsVersion: 1}}}/>)
    expect(empty).toContain('この取引先の契約はまだありません。')

    const noTypes = renderToStaticMarkup(<LiveContractReadSection state={{status: 'ready', contracts: []}} contractTypeState={{status: 'ready', contractTypes: []}} approvalPolicyState={{status: 'ready', policy: {companyName: false, tradingStatus: false, address: false, phone: false, contractUpdate: false, mainOwner: false, settingsVersion: 1}}}/>)
    expect(noTypes).toContain('契約種類が登録されていないため、契約を登録できません。')
    expect(noTypes).not.toContain('>契約を登録<')
  })

  it('取引先の詳細に担当者タブがあり、この取引先に担当者を追加できる（2026-09-29、ユーザー要望）', () => {
    const html = renderToStaticMarkup(<LivePartnerDetail partner={partner} initialTab="担当者" onAddContact={() => {}} onClose={() => {}} />)
    expect(html).toContain('>担当者</button>')
    expect(html).toContain('担当者を追加')
    expectPlainLanguage(html)
  })

  it('取引先の画面から開いた担当者の登録は、その取引先を選んだ状態で始まる', () => {
    const parents = [{id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: '別の会社'}, {id: partner.id, name: partner.name}]
    const html = renderToStaticMarkup(<ContactRegistrationForm api={{registerContact: async () => ({contactId: 'x', isReplay: false})}} parents={parents} parentsState="ready" initialPartnerId={partner.id} onCancel={() => {}} onSaved={() => {}} />)
    expect(html).toMatch(new RegExp(`<option value="${partner.id}" selected="">`))
  })

  it('掲示板は入力欄を一覧より上に置く（新しい順の一覧の先頭に投稿が入る。2026-09-28、ユーザー要望）', () => {
    const html = renderToStaticMarkup(<LivePartnerDetail partner={partner} initialTab="掲示板" onClose={() => {}} />)
    const compose = html.indexOf('共有掲示板に投稿')
    const list = html.indexOf('読み込み中')
    expect(compose).toBeGreaterThan(-1)
    expect(list).toBeGreaterThan(-1)
    expect(compose).toBeLessThan(list)
  })

  it('取引先の詳細は基本情報・契約・掲示板・共有設定のタブを持つ', () => {
    const html = renderToStaticMarkup(<LivePartnerDetail partner={partner} onClose={() => {}} />)
    for (const text of ['基本情報', '契約・期限', '掲示板', '共有設定', '住所', '代表電話', '主担当']) expect(html).toContain(text)
    expect(html).not.toContain('アクセス管理')
    expect(html).not.toContain(partner.id)
    expectPlainLanguage(html)
  })

  it('通知メールのリンクから開いた取引先は、指定のタブ（契約・期限）で開く', () => {
    const html = renderToStaticMarkup(<LivePartnerDetail partner={partner} initialTab="契約・期限" onClose={() => {}} />)
    expect(html).toMatch(/<button aria-pressed="true">契約・期限<\/button>/)
    expect(html).toMatch(/<button aria-pressed="false">基本情報<\/button>/)
    expect(partnerLinkFromQuery({partnerId: partner.id, partnerTab: 'contracts'})).toEqual({id: partner.id, tab: '契約・期限'})
    expect(partnerLinkFromQuery({partnerId: partner.id})).toEqual({id: partner.id, tab: '基本情報'})
    expect(partnerLinkFromQuery({partnerId: 'not-a-guid', partnerTab: 'contracts'})).toBeUndefined()
    expect(partnerLinkFromQuery({})).toBeUndefined()
  })

  it('契約の登録と申請のフォームは入力項目と保存・申請ボタンだけを表示する', () => {
    const registration = renderToStaticMarkup(<LiveContractRegistrationForm partner={partner} contractTypes={[{id: '1', name: '基本取引契約'}]} api={{registerContract: async () => ({contractId: contract.id, isReplay: false})}} onSaved={() => {}} onCancel={() => {}} />)
    for (const text of ['契約を登録', '契約名', '契約種類', '契約終了日', '保存']) expect(registration).toContain(text)
    expectPlainLanguage(registration)

    const request = renderToStaticMarkup(<LiveContractApprovalDraftForm partner={partner} contract={contract} api={writeApi} onCancel={() => {}} />)
    for (const text of ['更新・終了判断を申請', '判断', '契約終了日', '申請する', '承認されると契約に反映されます']) expect(request).toContain(text)
    expect(request).not.toContain('<label>契約種類')
    expectPlainLanguage(request)
  })

  it('共有設定は人とグループの共有先を管理する欄だけを表示する', () => {
    const html = renderToStaticMarkup(<LivePartnerShareSettingsPanel partnerId={partner.id} api={{list: async () => []}} />)
    for (const text of ['共有設定', 'この取引先を閲覧・編集できる人とグループです。', '共有先を追加', '共有先を検索・選択']) expect(html).toContain(text)
    expect(html).toContain('role="combobox"')
    expectPlainLanguage(html)
  })

  it('ホームの処理待ちは状態と確認操作だけを表示し、未取得の列を出さない', () => {
    const items = [
      {id: '11111111-1111-4111-8111-111111111111', captureKey: 'FAIL-001', captureStatus: '失敗' as const, batchId: 'b1', batchKey: 'k1', batchStatus: '失敗' as const, registeredBy: 'Diego', registeredAt: '2026-09-18T00:00:00.000Z'},
      {id: '55555555-5555-4555-8555-555555555555', captureKey: 'REVIEW-001', captureStatus: '確認待ち' as const, batchId: 'b2', batchKey: 'k2', batchStatus: '完了' as const, registeredBy: 'Diego', registeredAt: '2026-09-18T00:00:00.000Z'},
      {id: '66666666-6666-4666-8666-666666666666', captureKey: 'WAIT-001', captureStatus: '未登録' as const, batchId: 'b3', batchKey: 'k3', batchStatus: '受付完了' as const, registeredBy: 'Mina', registeredAt: '2026-09-18T00:00:00.000Z'},
      {id: '77777777-7777-4777-8777-777777777777', captureKey: 'DONE-001', captureStatus: '登録済み' as const, batchId: 'b4', batchKey: 'k4', batchStatus: '完了' as const, registeredBy: 'Registered Person', registeredAt: '2026-09-18T00:00:00.000Z'},
    ]
    const html = renderToStaticMarkup(<LiveBusinessCardQueue state={{status: 'ready', items}} onRefresh={() => {}} onOpenReview={() => {}} />)
    for (const text of ['処理待ちの名刺', '登録者', '受付日', '状態', '読み取り失敗', '確認待ち', '読み取り待ち', 'Diego']) expect(html).toContain(text)
    // 本人が分からない、または外す操作が渡されていないときは、失敗行に操作も案内も出さない。
    for (const text of ['一覧から外す', 'もう一度登録']) expect(html).not.toContain(text)
    expect(html).toContain('>3</span>')
    expect((html.match(/確認する/g) ?? []).length).toBe(1)
    // 横スクロールしなくても押せるよう、確認するボタンは行の先頭に置く。
    expect(html).toMatch(/<tr><td class="queue-action-cell"><button class="secondary-button" type="button">確認する<\/button><\/td><td>Diego<\/td>/)

    const withReview = renderToStaticMarkup(<LiveBusinessCardQueue state={{status: 'ready', items}} reviewCaptureId={items[1].id} onRefresh={() => {}} onOpenReview={() => {}} onCloseReview={() => {}} />)
    expect(withReview).toContain('role="dialog" aria-modal="true" aria-label="名刺の内容を確認"')
    expect(withReview).toContain('aria-label="名刺の確認を閉じる"')
    for (const text of ['未取得', 'FAIL-001', 'REVIEW-001', '取込キー', 'Registered Person', '登録済み', '>未登録<']) expect(html).not.toContain(text)
    expectPlainLanguage(html)

    // 自分が受け付けた失敗・結果不明の行にだけ、もう一度登録と一覧から外すを出す。
    const me = '88888888-8888-4888-8888-888888888888'
    const mine = [
      {...items[0], registeredById: me},
      {...items[0], id: '99999999-9999-4999-8999-999999999999', captureKey: 'UNKNOWN-001', captureStatus: '結果不明' as const, registeredById: me},
      {...items[0], id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', captureKey: 'FAIL-OTHER', registeredById: '22222222-2222-4222-8222-222222222222'},
      {...items[2], registeredById: me},
    ]
    const withActions = renderToStaticMarkup(<LiveBusinessCardQueue state={{status: 'ready', items: mine}} currentUserId={me} dismissApi={{dismiss: async () => {}}} onRefresh={() => {}} onRegisterCard={() => {}} />)
    expect((withActions.match(/>一覧から外す</g) ?? []).length).toBe(2)
    expect((withActions.match(/>もう一度登録</g) ?? []).length).toBe(2)
    expect(withActions).toContain('「もう一度登録」から画像を選び直してください')
    expectPlainLanguage(withActions)

    const home = renderToStaticMarkup(<HomeView queueState={{status: 'ready', items: []}} onRefreshQueue={() => {}} onLedger={() => {}} onRegister={() => {}} onContacts={() => {}} />)
    for (const text of ['新規登録', '取引先リスト', '担当者リスト', '処理待ちの名刺はありません。']) expect(home).toContain(text)
    expect(home).not.toContain('現在の状態')
    expectPlainLanguage(home)
  })

  it('新規登録は取引先・担当者・名刺の3つの入口にする（名刺は1枚でも複数枚でも同じ入口）', () => {
    const html = renderToStaticMarkup(<NewRegistrationView onCompany={() => {}} onContact={() => {}} onBusinessCard={() => {}} />)
    for (const text of ['取引先を登録', '担当者を登録', '名刺で登録', 'まとめて複数枚でも']) expect(html).toContain(text)
    expect(html).not.toContain('名刺をまとめて登録')
    expectPlainLanguage(html)
  })

  it('名刺の受付画面は1つで、複数枚を選べる画像選択と受付ボタンを出す', () => {
    const html = renderToStaticMarkup(<DataverseBusinessCardRegistration api={{submit: async () => ({success: false, stage: 'validation', kind: 'invalid-input', message: 'x'})}} bulkApi={{submit: async () => ({success: false, batchKey: '', stage: 'validation', kind: 'invalid-input', message: 'x', items: []})}} onOpenQueue={() => {}} />)
    for (const text of ['名刺画像を選択', '画像を選択', '受け付ける', '0枚を選択', 'まとめて20枚まで']) expect(html).toContain(text)
    expect(html).toContain('multiple=""')
    expect(html).not.toContain('<h1>')
    expectPlainLanguage(html)
  })

  it('取引先・担当者の登録フォームはサーバーが決める項目を入力欄に出さない', () => {
    const company = renderToStaticMarkup(<RegistrationForm api={{registerPartner: async () => ({partnerId: partner.id, isReplay: false})}} owners={[{id: '1', name: '田中 健一'}]} ownersState="ready" onCancel={() => {}} onSaved={() => {}} />)
    for (const text of ['取引先情報', '会社名', '主担当', '登録']) expect(company).toContain(text)
    for (const text of ['登録者', '登録日', '初期状態', '登録ルール']) expect(company).not.toContain(text)
    expectPlainLanguage(company)

    const person = renderToStaticMarkup(<ContactRegistrationForm api={{registerContact: async () => ({contactId: contact.id, isReplay: false})}} parents={[{id: partner.id, name: partner.name}]} parentsState="ready" onCancel={() => {}} onSaved={() => {}} />)
    for (const text of ['担当者情報', '取引先', '氏名', '在籍状態', '登録']) expect(person).toContain(text)
    for (const text of ['登録者', '登録日', '登録ルール']) expect(person).not.toContain(text)
    expectPlainLanguage(person)
  })

  it('アーカイブは終了した取引先と退職した担当者を表示し、失敗時は再読み込みできる', () => {
    const html = renderToStaticMarkup(<LiveArchiveView state={{status: 'ready', partners: [{id: '11111111-1111-4111-8111-111111111111', name: '終了会社', tradingStatus: '終了', state: 'Active'}], contacts: [{id: '22222222-2222-4222-8222-222222222222', name: '退職担当者', partnerId: partner.id, partnerName: '終了会社', status: '退職', state: 'Inactive'}]}} onRefresh={() => {}} />)
    // 一覧と同じパネル見出し（タイトル・件数・検索・再読み込み）に揃え、取引先と担当者はタブで切り替える。
    for (const text of ['<h2>アーカイブ <span>1</span></h2>', 'アーカイブを検索', '再読み込み', '終了した取引先', '退職した担当者', '終了会社']) expect(html).toContain(text)
    expect(html).not.toContain('退職担当者')
    expect(html).not.toContain('<h1>')
    expect(html).toContain('aria-label="主担当で絞り込み"')
    expectPlainLanguage(html)

    const error = renderToStaticMarkup(<LiveArchiveView state={{status: 'error', message: 'アーカイブを読み込めませんでした。'}} onRefresh={() => {}} />)
    expect(error).toContain('アーカイブを読み込めませんでした。')
    expect(error).toContain('再読み込み')
  })
})

describe('承認申請の画面', () => {
  it('申請種別を日本語で表示し、変更内容を読める形で出す', () => {
    const html = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items: [approvalItem('提出中', 'partner.company-name', nameChange)]}} writeApi={writeApi} onRefresh={() => {}} />)
    for (const text of ['<h2>承認申請 <span>1</span></h2>', '承認申請を検索', '会社名の変更', partner.name, '変更内容', '会社名', '青空ソリューションズ ホールディングス', '佐伯 菜月']) expect(html).toContain(text)
    // 操作している本人が分からない間は、取消・取下げのボタンを出さない。
    for (const text of ['取り消す', '取り下げる']) expect(html).not.toContain(text)
    expect(html).not.toContain('<h1>')
    for (const text of ['partner.company-name', 'schemaVersion', 'pl_name', '変更内容（JSON）', '下書きを作成']) expect(html).not.toContain(text)
    expectPlainLanguage(html)
  })

  it('取下げは本人の下書き・差戻しだけ、取消は管理者の提出中だけに出す', () => {
    const me = '11111111-1111-4111-8111-111111111111'
    const other = '22222222-2222-4222-8222-222222222222'
    const own = (status: string) => {
      const item = approvalItem(status, 'partner.company-name', nameChange)
      return {...item, request: {...item.request, requestingUserId: me}}
    }
    const others = (status: string) => {
      const item = approvalItem(status, 'partner.phone', nameChange)
      return {...item, request: {...item.request, requestingUserId: other}}
    }
    const render = (items: ApprovalInboxItem[], isAdministrator: boolean) => renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items}} writeApi={writeApi} actor={{systemUserId: me, isAdministrator}} onRefresh={() => {}} />)

    const user = render([own('下書き'), own('差戻し'), own('提出中'), others('下書き'), others('提出中')], false)
    expect((user.match(/>取り下げる</g) ?? []).length).toBe(2)
    expect(user).not.toContain('>取り消す<')

    const admin = render([others('提出中'), others('下書き'), others('承認済み'), others('反映済み')], true)
    expect((admin.match(/>取り消す</g) ?? []).length).toBe(2)
    expect(admin).not.toContain('>取り下げる<')
    expectPlainLanguage(admin)
  })

  it('下書きは提出ボタン、差戻しは修正して再提出を出し、完了した申請には操作を出さない', () => {
    const items = [
      approvalItem('反映済み', 'partner.trading-status', statusChange, '2026-09-22T00:00:00.000Z'),
      approvalItem('下書き', 'partner.company-name', nameChange, '2026-09-10T00:00:00.000Z'),
      approvalItem('差戻し', 'contract.update', contractChange, '2026-09-11T00:00:00.000Z'),
    ]
    const html = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items}} writeApi={writeApi} onRefresh={() => {}} />)
    expect((html.match(/>提出する</g) ?? []).length).toBe(1)
    expect((html.match(/>修正して再提出</g) ?? []).length).toBe(1)
    for (const text of ['対応が必要', '承認待ち', '完了', 'すべて', '休止中', '契約の更新・終了判断', '判断', '終了', '2027-03-31', 'なし', '（空欄）']) expect(html).toContain(text)
    expectPlainLanguage(html)
  })

  it('反映失敗は理由を表示して完了に分け、本人にも管理者にも操作を出さない', () => {
    const me = '11111111-1111-4111-8111-111111111111'
    const failed = approvalItem('反映失敗', 'partner.company-name', nameChange)
    const cancelled = approvalItem('取消', 'partner.phone', nameChange)
    const items = [
      {...failed, request: {...failed.request, requestingUserId: me, cancellationReason: '承認待ちの間に、申請した項目が別の操作で変更されたため、反映しませんでした。'}},
      {...cancelled, request: {...cancelled.request, requestingUserId: me, cancellationReason: '承認者が休職中のため'}},
    ]
    const html = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items}} writeApi={writeApi} actor={{systemUserId: me, isAdministrator: true}} onRefresh={() => {}} />)
    expect(html).toContain('反映できなかった理由：承認待ちの間に、申請した項目が別の操作で変更されたため、反映しませんでした。')
    expect(html).toContain('取消の理由：承認者が休職中のため')
    for (const text of ['>取り消す<', '>取り下げる<', '>提出する<', '>修正して再提出<']) expect(html).not.toContain(text)
    expect(statusGroup('反映失敗')).toBe('done')
    expectPlainLanguage(html)
  })

  it('承認依頼のリンクから開いた申請を一覧の上に示し、見つからなければその旨を出す', () => {
    const items = [
      approvalItem('反映済み', 'partner.trading-status', statusChange, '2026-09-22T00:00:00.000Z'),
      approvalItem('提出中', 'partner.company-name', nameChange, '2026-09-21T00:00:00.000Z'),
    ]
    const focused = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items}} writeApi={writeApi} focusRequestId="req-提出中-partner.company-name" onRefresh={() => {}} />)
    expect(focused).toContain('承認依頼のリンクから開いた申請')
    // 指定の申請はリンクの案内の直後に出し、一覧では重ねて出さない。
    expect(focused.indexOf('承認依頼のリンクから開いた申請')).toBeLessThan(focused.indexOf('会社名の変更'))
    expect(focused.indexOf('会社名の変更')).toBeLessThan(focused.indexOf('取引状態の変更'))
    expect((focused.match(/会社名の変更/g) ?? []).length).toBe(1)
    expectPlainLanguage(focused)

    const missing = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items}} writeApi={writeApi} focusRequestId="req-存在しない" onRefresh={() => {}} />)
    expect(missing).toContain('リンクの申請が見つかりませんでした')
    expectPlainLanguage(missing)
  })

  it('対応が必要な申請を先頭に、同じ区分では新しい順に並べる', () => {
    const sorted = sortApprovalItems([
      approvalItem('反映済み', 'partner.company-name', nameChange, '2026-09-22T00:00:00.000Z'),
      approvalItem('提出中', 'partner.company-name', nameChange, '2026-09-21T00:00:00.000Z'),
      approvalItem('差戻し', 'partner.company-name', nameChange, '2026-09-10T00:00:00.000Z'),
      approvalItem('下書き', 'partner.company-name', nameChange, '2026-09-12T00:00:00.000Z'),
    ])
    expect(sorted.map(item => item.request.status)).toEqual(['下書き', '差戻し', '提出中', '反映済み'])
  })

  it('変更内容の解釈は不正なJSONを無視し、値を業務の表記へ変換する', () => {
    expect(parseChangeSet('{')).toBeUndefined()
    expect(parseChangeSet(JSON.stringify({schemaVersion: 1, changes: []}))).toBeUndefined()
    expect(parseChangeSet(nameChange)?.changes).toHaveLength(1)
    expect(changeAttributeLabel('pl_partner', 'pl_name')).toBe('会社名')
    expect(changeAttributeLabel('pl_contract', 'pl_name')).toBe('契約名')
    expect(formatChangeValue('pl_tradingstatuscode', 100000003)).toBe('終了')
    expect(formatChangeValue('pl_contractstatuscode', 100000000)).toBe('更新（締結済み）')
    expect(formatChangeValue('pl_enddate', '2027-03-31T00:00:00.000Z')).toBe('2027-03-31')
    expect(formatChangeValue('pl_autorenew', true)).toBe('あり')
    expect(formatChangeValue('pl_link', null)).toBe('（空欄）')
  })

  it('主担当の変更は、ユーザーのIDではなく名前で表示する（2026-09-29）', () => {
    const owner = '44444444-4444-4444-8444-444444444444'
    expect(approvalRequestTypeLabels['partner.main-owner']).toBe('主担当の変更')
    expect(changeAttributeLabel('pl_partner', 'pl_mainownerlookup')).toBe('主担当')
    expect(formatChangeValue('pl_mainownerlookup', owner, {[owner]: '田中 健一'})).toBe('田中 健一')
    expect(formatChangeValue('pl_mainownerlookup', owner)).toBe('（ユーザー）')
  })

  it('承認申請の画面は、主担当の変更を名前で表示する（2026-09-29）', () => {
    const owner = '44444444-4444-4444-8444-444444444444'
    const change = JSON.stringify({schemaVersion: 1, target: {entity: 'pl_partner', id: partner.id}, changes: [{attribute: 'pl_mainownerlookup', value: owner}]})
    const html = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items: [approvalItem('提出中', 'partner.main-owner', change)]}} users={[{id: owner, name: '田中 健一'}]} onRefresh={() => {}} />)
    expect(html).toContain('主担当の変更')
    expect(html).toContain('田中 健一')
    expect(html).not.toContain(owner)
    expectPlainLanguage(html)
  })

  it('主担当の変更フォームは、今の主担当を除いた利用者から選ぶ（2026-09-29）', () => {
    const owners = [{id: '44444444-4444-4444-8444-444444444444', name: '田中 健一'}, {id: '55555555-5555-4555-8555-555555555555', name: '佐伯 菜月'}]
    const policy = {companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: true, settingsVersion: 1}
    const html = renderToStaticMarkup(<LivePartnerEditForm partner={{...partner, mainOwnerId: owners[0].id}} target="mainOwner" owners={owners} policy={policy} updateApi={{updatePartner: async () => ({partnerId: partner.id})}} approvalApi={{} as never} onCancel={() => {}}/>)
    expect(html).toContain('主担当の変更を申請')
    expect(html).toContain('佐伯 菜月')
    expect(html).not.toContain('>田中 健一<')
    expectPlainLanguage(html)
  })

  it('差戻し後に保存した未提出の修正版は、提出するボタンと修正後の内容を出す', () => {
    const rejected = approvalItem('差戻し', 'partner.company-name', nameChange)
    const draftChange = JSON.stringify({schemaVersion: 1, target: {entity: 'pl_partner', id: partner.id}, changes: [{attribute: 'pl_name', value: '青空ソリューションズ（修正後）'}]})
    const item: ApprovalInboxItem = {...rejected, draftSubmission: {id: 'sv-draft', requestId: rejected.request.id, name: '修正版', status: '下書き', changeSetJson: draftChange}}
    const html = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items: [item]}} writeApi={writeApi} onRefresh={() => {}} />)
    for (const text of ['>提出する<', '>修正して再提出<', '変更内容（修正後・未提出）', '青空ソリューションズ（修正後）']) expect(html).toContain(text)
    expect(html).not.toContain('青空ソリューションズ ホールディングス')
  })

  it('再提出の保存は、未提出の修正版があれば更新し、無ければ作成する', async () => {
    const rejected = approvalItem('差戻し', 'partner.company-name', nameChange)
    const api = {...writeApi, createSubmissionDraft: vi.fn(writeApi.createSubmissionDraft), updateSubmissionDraft: vi.fn(writeApi.updateSubmissionDraft)}

    await saveResubmissionDraft(api, rejected, '{"a":1}')
    expect(api.createSubmissionDraft).toHaveBeenCalledWith({requestId: rejected.request.id, name: `${rejected.request.name} 修正`, changeSetJson: '{"a":1}'})
    expect(api.updateSubmissionDraft).not.toHaveBeenCalled()

    const withDraft: ApprovalInboxItem = {...rejected, draftSubmission: {id: 'sv-draft', requestId: rejected.request.id, name: '修正版', status: '下書き'}}
    await saveResubmissionDraft(api, withDraft, '{"a":2}')
    expect(api.updateSubmissionDraft).toHaveBeenCalledWith({requestId: rejected.request.id, submissionVersionId: 'sv-draft', name: '修正版', changeSetJson: '{"a":2}'})
    expect(api.createSubmissionDraft).toHaveBeenCalledTimes(1)
  })

  it('提出版を読めないときは、申請の一覧だけを表示していることを示す', () => {
    const item: ApprovalInboxItem = {request: approvalItem('提出中', 'partner.company-name', nameChange).request}
    const html = renderToStaticMarkup(<ApprovalCenterView state={{status: 'ready', items: [item], submissionSummaryUnavailable: true}} writeApi={writeApi} onRefresh={() => {}} />)
    expect(html).toContain('変更内容を読み込めなかったため、申請の一覧だけを表示しています。')
    expect(html).toContain(partner.name)
    expect(html).not.toContain('変更内容</b>')
  })

  it('読み込み中と失敗を表示する', () => {
    expect(renderToStaticMarkup(<ApprovalCenterView state={{status: 'loading'}} onRefresh={() => {}} />)).toContain('読み込み中')
    const error = renderToStaticMarkup(<ApprovalCenterView state={{status: 'error', message: '承認申請を読み込めませんでした。'}} onRefresh={() => {}} />)
    expect(error).toContain('承認申請を読み込めませんでした。')
    expect(error).toContain('再読み込み')
  })
})

describe('承認設定の画面', () => {
  const current = {companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: true, settingsVersion: 0}
  const flagsOf = (policy: typeof current) => ({companyName: policy.companyName, tradingStatus: policy.tradingStatus, address: policy.address, phone: policy.phone, contractUpdate: policy.contractUpdate, mainOwner: policy.mainOwner})
  const noop = () => {}

  it('管理者以外は表示だけで、変更できない', () => {
    const html = renderToStaticMarkup(<ApprovalPolicySettingsForm policy={current} draft={flagsOf(current)} canEdit={false} saving={false} onToggle={noop} onSave={noop} onRevert={noop}/>)
    expect(html).toContain('この設定は管理者が変更します。')
    // 主担当の変更を足して6項目（2026-09-29）。
    expect(html.match(/<input[^>]*type="checkbox"[^>]*disabled=""/g)?.length).toBe(6)
    expect(html).toContain('主担当の変更')
    expect(html).not.toContain('保存する')
    expectPlainLanguage(html)
  })

  it('管理者は変更でき、行が無いときは既定値と表示する', () => {
    const html = renderToStaticMarkup(<ApprovalPolicySettingsForm policy={current} draft={flagsOf(current)} canEdit={true} saving={false} onToggle={noop} onSave={noop} onRevert={noop}/>)
    expect(html).not.toMatch(/<input[^>]*type="checkbox"[^>]*disabled=""/)
    expect(html).toContain('既定値')
    expect(html).not.toContain('保存する')
    expectPlainLanguage(html)
  })

  it('変更すると、承認待ちへの影響を示して保存と元に戻すを出す', () => {
    const html = renderToStaticMarkup(<ApprovalPolicySettingsForm policy={{...current, settingsVersion: 2}} draft={{...flagsOf(current), address: true}} canEdit={true} saving={false} onToggle={noop} onSave={noop} onRevert={noop}/>)
    expect(html).toContain('版2')
    expect(html).toContain('承認待ちの申請は、承認されても反映されず「反映失敗」になります。')
    expect(html).toContain('差戻し中の申請は再提出できなくなるため、取り下げて出し直してもらいます。')
    expect(html).toContain('保存する')
    expect(html).toContain('元に戻す')
    expectPlainLanguage(html)
  })

  it('保存の結果を表示する', () => {
    const html = renderToStaticMarkup(<ApprovalPolicySettingsForm policy={current} draft={flagsOf(current)} canEdit={true} saving={false} message={{kind: 'error', text: 'ほかの管理者が先に変更しました。再読み込みしてから、もう一度変更してください。'}} onToggle={noop} onSave={noop} onRevert={noop}/>)
    expect(html).toContain('ほかの管理者が先に変更しました。')
  })
})
