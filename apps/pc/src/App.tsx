import { useCallback, useEffect, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react'
import { getContext } from '@microsoft/power-apps/app'
import './App.css'
import { type BusinessCardQueueItem, type BusinessCardQueueLoadState } from './businessCardQueue'
import { dataverseBusinessCardQueueApi } from './dataverseBusinessCardQueueApi'
import { dataverseBusinessCardCaptureApi } from './dataverseBusinessCardCaptureApi'
import { DataverseBusinessCardRegistration } from './DataverseBusinessCardRegistration'
import { dataverseBusinessCardOcrJobApi } from './dataverseBusinessCardOcrJobApi'
import { dataverseBusinessCardOcrJobContextApi } from './dataverseBusinessCardOcrJobContextApi'
import type { BusinessCardCaptureResult } from './businessCardCapture'
import { DataverseBusinessCardReview } from './DataverseBusinessCardReview'
import { dataverseBusinessCardFormalRegistrationApi } from './dataverseBusinessCardFormalRegistrationApi'
import { dataverseBusinessCardBulkCaptureApi } from './dataverseBusinessCardBulkCaptureApi'
import type { BusinessCardRegistrationOwner, BusinessCardRegistrationOwnerState } from './DataverseBusinessCardReview'
import type { BusinessCardFormalRegistrationApi } from './businessCardFormalRegistration'
import { dataversePartnerDirectoryApi } from './dataversePartnerDirectoryApi'
import { filterPartnerDirectory, type PartnerDirectoryItem, type PartnerDirectoryLoadState } from './partnerDirectory'
import { dataverseContactDirectoryApi } from './dataverseContactDirectoryApi'
import { filterContactDirectory, type ContactDirectoryApi, type ContactDirectoryItem, type ContactDirectoryLoadState } from './contactDirectory'
import { dataverseArchiveDirectoryApi } from './dataverseArchiveDirectoryApi'
import { type ArchiveContactItem, type ArchiveDirectoryLoadState, type ArchivePartnerItem } from './archiveDirectory'
import { dataverseContractReadApi } from './dataverseContractReadApi'
import { type ContractReadApi, type ContractReadItem, type ContractReadLoadState } from './contractReadModel'
import { dataverseContractTypeReadApi } from './dataverseContractTypeReadApi'
import { type ContractTypeReadApi, type ContractTypeReadItem } from './contractTypeReadModel'
import { ContractTypeManagementError, type ContractTypeAdminItem, type ContractTypeManagementApi } from './contractTypeManagement'
import { dataverseContractTypeManagementApi } from './standardContractTypeManagementApi'
import { LiveContractApprovalDraftForm } from './LiveContractApprovalDraftForm'
import { LiveContractDirectUpdateForm } from './LiveContractDirectUpdateForm'
import type { ContractDirectUpdateApi } from './contractDirectUpdate'
import { dataverseStandardContractUpdateApi } from './standardContractUpdateApi'
import { LivePartnerEditForm } from './LivePartnerEditForm'
import { dataverseStandardPartnerUpdateApi } from './standardPartnerUpdateApi'
import type { PartnerDirectUpdateApi } from './partnerDirectUpdate'
import { type ContractApprovalPolicy, type ContractApprovalPolicyLoadState, type ContractApprovalPolicyReadApi } from './contractApprovalPolicy'
import { ApprovalSettingsWriteError, type ApprovalPolicyFlags, type ApprovalSettingsWriteApi } from './approvalSettingsWrite'
import { dataverseApprovalSettingsWriteApi } from './standardApprovalSettingsWriteApi'
import { dataverseContractApprovalPolicyReadApi } from './standardContractApprovalPolicyReadApi'
import { dataverseContactParentDirectoryApi } from './dataverseContactParentDirectoryApi'
import { type ContactRegistrationParent } from './contactParentDirectory'
import { dataverseApprovalReadApi } from './dataverseApprovalReadApi'
import type { ApprovalReadLoadState } from './approvalReadModel'
import { ApprovalCenterView } from './ApprovalCenter'
import { isDataverseGuid } from './dataverseIds'
import { partnerLinkFromQuery, type PartnerDetailTab, type PartnerLink } from './appLinks'
import { loadApprovalActor, type ApprovalActor } from './approvalActor'
import { BusinessCardDismissError, canDismissBusinessCard, type BusinessCardDismissApi } from './businessCardDismissal'
import { dataverseBusinessCardDismissApi } from './dataverseBusinessCardDismissApi'
import { dataverseApprovalActorQuery } from './dataverseApprovalActorApi'
import { dataverseStandardApprovalWriteApi, type StandardApprovalWriteApi } from './standardApprovalTransitionApi'
import { listActivePartnerRegistrationOwners, type PartnerRegistrationOwner, type PartnerRegistrationOwnerLoadState } from './dataverseRegistrationOwnersApi'
import { dataverseStandardPartnerRegistrationApi } from './standardPartnerRegistrationApi'
import { createPartnerRegistrationIdempotencyKey, PartnerRegistrationApiError, type PartnerLedgerRegistrationApi } from './partnerRegistrationApi'
import { ContactRegistrationApiError, createContactRegistrationIdempotencyKey, type ContactLedgerRegistrationApi, type ContactStatusCode } from './contactRegistrationApi'
import { dataverseStandardContactRegistrationApi } from './standardContactRegistrationApi'
import { ContractRegistrationApiError, createContractRegistrationIdempotencyKey, type ContractLedgerRegistrationApi } from './contractRegistrationApi'
import { dataverseStandardContractRegistrationApi } from './standardContractRegistrationApi'
import { ContactUpdateApiError, type ContactUpdateApi, type ContactUpdateRequest, type ContactUpdateResult } from './contactUpdateApi'
import { dataverseContactUpdateApi } from './dataverseContactUpdateApi'
import { dataversePartnerSharePrincipalDirectoryApi } from './dataversePartnerSharePrincipalDirectoryApi'
import { dataversePartnerShareSettingReadApi } from './dataversePartnerShareSettingReadApi'
import { dataversePartnerShareSettingApi } from './dataversePartnerShareSettingApi'
import { createPartnerShareSettingRequestKey, PartnerShareSettingApiError, type PartnerShareAccessLevel, type PartnerShareSettingApi } from './partnerShareSettingApi'
import type { PartnerSharePrincipal, PartnerSharePrincipalDirectoryApi, PartnerSharePrincipalKind } from './partnerSharePrincipalDirectory'
import type { PartnerShareSettingReadApi, PartnerShareSettingView } from './partnerShareSettingReadModel'
import { BulletinBoardApiError, createBulletinPostRequestKey, type BulletinBoardScope, type BulletinPost } from './bulletinBoard'
import { dataverseBulletinBoardApi, type BulletinBoardApi } from './dataverseBulletinBoardApi'
import { AsyncOperationOverlay } from './AsyncOperationOverlay'
import { nameInitial } from './displayText'
import { HeaderSearch, ThemeSwitcher, UserMenu, type HeaderUser } from './HeaderTools'
import { searchDirectory } from './headerSearch'
import { ColumnHeader, FilterSummary, RefreshButton, SearchField } from './TableControls'
import { activeFilterCount, applyTableView, useTableView, type TableColumn } from './tableView'
import { applyThemePreference, readThemePreference, saveThemePreference, type ThemePreference } from './theme'
import { Archive, ArrowRight, Building2, ClipboardCheck, FilePlus2, Home, Image as ImageIcon, Menu, PanelRightOpen, Settings2, Users, X, type LucideIcon } from 'lucide-react'

type DesktopView = 'home' | 'ledger' | 'approvals' | 'new' | 'company-register' | 'contact-register' | 'card-register' | 'contacts' | 'trash' | 'settings'
type PartnerEditTarget = 'companyName' | 'tradingStatus' | 'address' | 'phone' | 'mainOwner'
type ApprovalTarget = PartnerEditTarget | 'contractUpdate'
type NavIconName = 'home' | 'add' | 'building' | 'approvals' | 'users' | 'archive' | 'image' | 'settings' | 'panel-close' | 'panel-open'
type ContactRegistrationParentLoadState = 'idle' | 'loading' | 'ready' | 'error'

const approvalTargetKeys: ApprovalTarget[] = ['companyName', 'tradingStatus', 'address', 'phone', 'mainOwner', 'contractUpdate']
const partnerApprovalTargetKeys: PartnerEditTarget[] = ['companyName', 'tradingStatus', 'address', 'phone', 'mainOwner']
const approvalTargetLabels: Record<ApprovalTarget, string> = { companyName: '会社名の変更', tradingStatus: '取引状態の変更', address: '住所の変更', phone: '代表電話の変更', mainOwner: '主担当の変更', contractUpdate: '契約の更新・終了判断' }

function contactDirectoryStateClass(status: ContactDirectoryItem['status']) {
  return status === '在籍' ? 'active' : status === '休職' ? 'paused' : status === '退職' ? 'ended' : 'unconfirmed'
}

function partnerTargetLabel(target: PartnerEditTarget) {
  return target === 'companyName' ? '会社名' : target === 'tradingStatus' ? '取引状態' : target === 'address' ? '住所' : target === 'mainOwner' ? '主担当' : '代表電話'
}

function NavIcon({name}: {name: NavIconName}) {
  const icons: Record<NavIconName, LucideIcon> = {
    home: Home,
    add: FilePlus2,
    building: Building2,
    approvals: ClipboardCheck,
    users: Users,
    archive: Archive,
    image: ImageIcon,
    settings: Settings2,
    // メニューの開閉は、詳細パネルを開くボタン（パネル形）と見分けられるよう三本線にする。
    'panel-close': Menu,
    'panel-open': Menu,
  }
  const Icon = icons[name]
  return <Icon className="nav-icon" aria-hidden="true" strokeWidth={1.8} />
}

function initialCaptureIdParam(): string | null {
  return typeof window === 'undefined' ? null : new URLSearchParams(window.location.search).get('captureId')
}

/** 承認依頼のリンク（requestId付き）から開いたときの申請ID。GUIDの形でなければ使わない。 */
function validRequestId(value: string | null | undefined): string | undefined {
  return value && isDataverseGuid(value) ? value : undefined
}

function initialRequestIdParam(): string | undefined {
  return typeof window === 'undefined' ? undefined : validRequestId(new URLSearchParams(window.location.search).get('requestId'))
}

function initialPartnerLink(): PartnerLink | undefined {
  if (typeof window === 'undefined') return undefined
  const search = new URLSearchParams(window.location.search)
  return partnerLinkFromQuery({partnerId: search.get('partnerId') ?? undefined, partnerTab: search.get('partnerTab') ?? undefined})
}

function loadErrorMessage(subject: string) {
  return `${subject}を読み込めませんでした。時間をおいて再読み込みしてください。`
}

const breadcrumbLabels: Record<DesktopView, string[]> = {
  home: ['ホーム'],
  ledger: ['取引先リスト'],
  approvals: ['承認申請'],
  new: ['新規登録'],
  'company-register': ['新規登録', '取引先を登録'],
  'contact-register': ['新規登録', '担当者を登録'],
  'card-register': ['新規登録', '名刺で登録'],
  contacts: ['担当者リスト'],
  trash: ['アーカイブ'],
  settings: ['設定'],
}

export default function App() {
  const [view, setView] = useState<DesktopView>(() => initialCaptureIdParam() ? 'home' : initialRequestIdParam() ? 'approvals' : 'ledger')
  const [partnerLink, setPartnerLink] = useState<PartnerLink | undefined>(() => initialPartnerLink())
  const [navCollapsed, setNavCollapsed] = useState(false)
  const [selectedId, setSelectedId] = useState(() => initialPartnerLink()?.id ?? '')
  const [detailOpen, setDetailOpen] = useState(() => Boolean(initialPartnerLink()))
  const [query, setQuery] = useState('')
  const [notice, setNotice] = useState('')
  const [directoryLoadSequence, setDirectoryLoadSequence] = useState(0)
  const [liveDirectoryState, setLiveDirectoryState] = useState<PartnerDirectoryLoadState>({status: 'loading'})
  const [contactDirectoryLoadSequence, setContactDirectoryLoadSequence] = useState(0)
  const [liveContactDirectoryState, setLiveContactDirectoryState] = useState<ContactDirectoryLoadState>({status: 'loading'})
  const [liveContactQuery, setLiveContactQuery] = useState('')
  const [selectedContactId, setSelectedContactId] = useState('')
  const [liveContactDetailOpen, setLiveContactDetailOpen] = useState(false)
  const [archiveLoadSequence, setArchiveLoadSequence] = useState(0)
  const [archiveState, setArchiveState] = useState<ArchiveDirectoryLoadState>({status: 'loading'})
  const [approvalLoadSequence, setApprovalLoadSequence] = useState(0)
  const [approvalReadState, setApprovalReadState] = useState<ApprovalReadLoadState>({status: 'loading'})
  const [approvalFocusRequestId, setApprovalFocusRequestId] = useState<string | undefined>(() => initialRequestIdParam())
  const [businessCardQueueLoadSequence, setBusinessCardQueueLoadSequence] = useState(0)
  const [businessCardQueueState, setBusinessCardQueueState] = useState<BusinessCardQueueLoadState>({status: 'loading'})
  const [businessCardReviewCaptureId, setBusinessCardReviewCaptureId] = useState<string | undefined>(() => initialCaptureIdParam() ?? undefined)
  const [contactRegistrationParents, setContactRegistrationParents] = useState<ContactRegistrationParent[]>([])
  const [contactRegistrationParentState, setContactRegistrationParentState] = useState<ContactRegistrationParentLoadState>('idle')
  const [contactRegistrationPartnerId, setContactRegistrationPartnerId] = useState<string>()
  const [registrationOwners, setRegistrationOwners] = useState<PartnerRegistrationOwner[]>([])
  const [registrationOwnerState, setRegistrationOwnerState] = useState<PartnerRegistrationOwnerLoadState>('idle')
  const [user, setUser] = useState<HeaderUser>()
  const [approvalActor, setApprovalActor] = useState<ApprovalActor>()
  const [theme, setTheme] = useState<ThemePreference>(() => readThemePreference())

  useEffect(() => {
    let cancelled = false
    void getContext()
      .then(context => {
        if (cancelled) return
        setUser({fullName: context.user?.fullName, userPrincipalName: context.user?.userPrincipalName})
        void loadApprovalActor(context.user?.objectId, dataverseApprovalActorQuery)
          .then(actor => { if (!cancelled) setApprovalActor(actor) })
        const link = partnerLinkFromQuery({partnerId: context.app.queryParams?.partnerId, partnerTab: context.app.queryParams?.partnerTab})
        if (link) {
          setPartnerLink(current => current ?? link)
          setSelectedId(current => current || link.id)
          setDetailOpen(true)
        }
        const requestId = validRequestId(context.app.queryParams?.requestId)
        if (requestId) {
          setApprovalFocusRequestId(current => current ?? requestId)
          setApprovalReadState({status: 'loading'})
          setApprovalLoadSequence(sequence => sequence + 1)
          setView(current => current === 'ledger' ? 'approvals' : current)
        }
        const captureId = context.app.queryParams?.captureId
        if (!captureId) return
        setBusinessCardReviewCaptureId(current => current ?? captureId)
        setBusinessCardQueueState({status: 'loading'})
        setBusinessCardQueueLoadSequence(sequence => sequence + 1)
        setView(current => current === 'ledger' ? 'home' : current)
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  const reloadPartners = () => { setLiveDirectoryState({status: 'loading'}); setDirectoryLoadSequence(sequence => sequence + 1) }
  const reloadContacts = () => { setLiveContactDirectoryState({status: 'loading'}); setContactDirectoryLoadSequence(sequence => sequence + 1) }
  const reloadArchive = () => { setArchiveState({status: 'loading'}); setArchiveLoadSequence(sequence => sequence + 1) }
  const reloadApprovals = () => { setApprovalReadState({status: 'loading'}); setApprovalLoadSequence(sequence => sequence + 1) }
  const reloadBusinessCardQueue = () => { setBusinessCardQueueState({status: 'loading'}); setBusinessCardQueueLoadSequence(sequence => sequence + 1) }

  const navigate = (next: DesktopView) => {
    setNotice('')
    setApprovalFocusRequestId(undefined)
    setPartnerLink(undefined)
    if (next === 'ledger') { setSelectedId(''); setQuery(''); setDetailOpen(false); reloadPartners() }
    if (next === 'contacts') { setSelectedContactId(''); setLiveContactQuery(''); setLiveContactDetailOpen(false); reloadContacts() }
    if (next === 'trash') reloadArchive()
    if (next === 'approvals') reloadApprovals()
    if (next === 'home') { setBusinessCardReviewCaptureId(undefined); reloadBusinessCardQueue() }
    if (next === 'company-register') { setRegistrationOwners([]); setRegistrationOwnerState('loading') }
    if (next === 'contact-register') { setContactRegistrationParents([]); setContactRegistrationParentState('loading') }
    setView(next)
  }

  const showPartner = (partnerId: string, message = '') => { setSelectedId(partnerId); setQuery(''); setDetailOpen(Boolean(partnerId)); reloadPartners(); setNotice(message); setView('ledger') }
  const showContact = (contactId: string, message = '') => { setSelectedContactId(contactId); setLiveContactQuery(''); setLiveContactDetailOpen(Boolean(contactId)); reloadContacts(); setNotice(message); setView('contacts') }
  const handleLivePartnerUpdated = (partnerId: string) => showPartner(partnerId, '取引先を更新しました。')
  const handleLiveContactUpdated = (result: ContactUpdateResult) => {
    if (result.retired) showContact('', '担当者を退職にしました。アーカイブで確認できます。')
    else showContact(result.contactId, '担当者を更新しました。')
  }
  const openBusinessCardReview = (captureId: string) => { setRegistrationOwners([]); setRegistrationOwnerState('loading'); setBusinessCardReviewCaptureId(captureId); setNotice(''); setView('home') }
  const openBusinessCardQueue = () => navigate('home')
  const createDataverseBusinessCardOcrJob = useCallback(async (capture: Extract<BusinessCardCaptureResult, {success: true}>) => {
    const context = await dataverseBusinessCardOcrJobContextApi.getCreateContext(capture.captureId)
    return dataverseBusinessCardOcrJobApi.create({context, now: new Date()})
  }, [])

  useEffect(() => {
    let cancelled = false
    void dataversePartnerDirectoryApi.listPartners()
      .then(partners => { if (!cancelled) setLiveDirectoryState({status: 'ready', partners}) })
      .catch(() => { if (!cancelled) setLiveDirectoryState({status: 'error', message: loadErrorMessage('取引先')}) })
    return () => { cancelled = true }
  }, [directoryLoadSequence])
  useEffect(() => {
    let cancelled = false
    void dataverseContactDirectoryApi.listContacts()
      .then(contacts => { if (!cancelled) setLiveContactDirectoryState({status: 'ready', contacts}) })
      .catch(() => { if (!cancelled) setLiveContactDirectoryState({status: 'error', message: loadErrorMessage('担当者')}) })
    return () => { cancelled = true }
  }, [contactDirectoryLoadSequence])
  useEffect(() => {
    if (view !== 'trash') return
    let cancelled = false
    void Promise.all([
      dataverseArchiveDirectoryApi.listArchivedPartners(),
      dataverseArchiveDirectoryApi.listArchivedContacts(),
    ])
      .then(([partners, contacts]) => { if (!cancelled) setArchiveState({status: 'ready', partners, contacts}) })
      .catch(() => { if (!cancelled) setArchiveState({status: 'error', message: loadErrorMessage('アーカイブ')}) })
    return () => { cancelled = true }
  }, [archiveLoadSequence, view])
  useEffect(() => {
    // 主担当の候補は、取引先の登録・名刺の確認に加え、主担当の変更（取引先リスト）と申請の表示（承認申請）でも使う。
    if (view !== 'company-register' && view !== 'ledger' && view !== 'approvals' && !businessCardReviewCaptureId) return
    let cancelled = false
    void listActivePartnerRegistrationOwners()
      .then(owners => { if (!cancelled) { setRegistrationOwners(owners); setRegistrationOwnerState('ready') } })
      .catch(() => { if (!cancelled) setRegistrationOwnerState('error') })
    return () => { cancelled = true }
  }, [view, businessCardReviewCaptureId])
  useEffect(() => {
    if (view !== 'contact-register') return
    let cancelled = false
    void dataverseContactParentDirectoryApi.listParents()
      .then(parents => { if (!cancelled) { setContactRegistrationParents(parents); setContactRegistrationParentState('ready') } })
      .catch(() => { if (!cancelled) setContactRegistrationParentState('error') })
    return () => { cancelled = true }
  }, [view])
  useEffect(() => {
    if (view !== 'approvals') return
    let cancelled = false
    void dataverseApprovalReadApi.listAccessibleApprovalRequests()
      .then(result => { if (!cancelled) setApprovalReadState({status: 'ready', ...result}) })
      .catch(() => { if (!cancelled) setApprovalReadState({status: 'error', message: loadErrorMessage('承認申請')}) })
    return () => { cancelled = true }
  }, [approvalLoadSequence, view])
  useEffect(() => {
    if (view !== 'home') return
    let cancelled = false
    void dataverseBusinessCardQueueApi.listQueue()
      .then(items => { if (!cancelled) setBusinessCardQueueState({status: 'ready', items}) })
      .catch(() => { if (!cancelled) setBusinessCardQueueState({status: 'error', message: loadErrorMessage('処理待ちの名刺')}) })
    return () => { cancelled = true }
  }, [businessCardQueueLoadSequence, view])

  const liveSelected = liveDirectoryState.status === 'ready' ? liveDirectoryState.partners.find(partner => partner.id.toLowerCase() === selectedId.toLowerCase()) : undefined
  // 通知メールのリンクの取引先が一覧に無い（閲覧できない・無くなった）ときは、その旨を出す。
  const linkedPartnerMissing = Boolean(partnerLink && liveDirectoryState.status === 'ready'
    && !liveDirectoryState.partners.some(partner => partner.id.toLowerCase() === partnerLink.id.toLowerCase()))
  const liveSelectedContact = liveContactDirectoryState.status === 'ready' ? liveContactDirectoryState.contacts.find(contact => contact.id === selectedContactId) : undefined
  const closeDetail = () => setDetailOpen(false)
  const closeLiveContactDetail = () => setLiveContactDetailOpen(false)
  useEffect(() => {
    if (!detailOpen && !liveContactDetailOpen) return
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') { closeDetail(); closeLiveContactDetail() } }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [detailOpen, liveContactDetailOpen])
  const changeTheme = (next: ThemePreference) => { setTheme(next); saveThemePreference(next); applyThemePreference(next) }
  const searchHeader = (text: string) => searchDirectory(liveDirectoryState.status === 'ready' ? liveDirectoryState.partners : [], liveContactDirectoryState.status === 'ready' ? liveContactDirectoryState.contacts : [], text)
  const breadcrumbItems = breadcrumbLabels[view]
  const navButton = (target: DesktopView, label: string, icon: NavIconName, activeViews: DesktopView[] = [target]) =>
    <button title={label} aria-label={label} aria-current={activeViews.includes(view) ? 'page' : undefined} className={activeViews.includes(view) ? 'nav-active' : ''} onClick={() => navigate(target)}><NavIcon name={icon} /><span className="nav-label">{label}</span></button>

  return <div className="app-shell">
    <header className="prototype-toolbar">
      <a className="brand" href="#main" aria-label="PartnerLedger メインへ"><span className="brand-mark">pl<span>.</span></span><span>PartnerLedger</span></a>
      <div className="header-tools">
        <HeaderSearch search={searchHeader} loading={liveDirectoryState.status === 'loading' || liveContactDirectoryState.status === 'loading'} onOpen={result => result.kind === 'partner' ? showPartner(result.id) : showContact(result.id)}/>
        <ThemeSwitcher value={theme} onChange={changeTheme}/>
        <UserMenu user={user}/>
      </div>
    </header>
    <div className={'desktop-shell' + (navCollapsed ? ' nav-collapsed' : '')}>
      <aside className={'side-nav' + (navCollapsed ? ' collapsed' : '')}>
        <button className="nav-collapse" type="button" aria-expanded={!navCollapsed} aria-label={navCollapsed ? 'メニューを展開' : 'メニューを折り畳む'} title={navCollapsed ? 'メニューを展開' : 'メニューを折り畳む'} onClick={() => setNavCollapsed(current => !current)}><NavIcon name={navCollapsed ? 'panel-open' : 'panel-close'} /></button>
        <nav aria-label="PCナビゲーション">
          {navButton('home', 'ホーム', 'home')}
          {navButton('approvals', '承認申請', 'approvals')}
          {navButton('new', '新規登録', 'add', ['new', 'company-register', 'contact-register', 'card-register'])}
          {navButton('ledger', '取引先リスト', 'building')}
          {navButton('contacts', '担当者リスト', 'users')}
          {navButton('trash', 'アーカイブ', 'archive')}
          {navButton('settings', '設定', 'settings')}
        </nav>
      </aside>
      <main id="main" className="desktop-content">
        <div className="breadcrumb">PartnerLedger <span>/</span> {breadcrumbItems[0]} {breadcrumbItems[1] && <><span>/</span> {breadcrumbItems[1]}</>}</div>
        {notice && <div className="notice" role="status">{notice}<button aria-label="通知を閉じる" onClick={() => setNotice('')}><X className="nav-icon" aria-hidden="true" strokeWidth={1.8}/></button></div>}
        {view === 'ledger' && linkedPartnerMissing && <div className="notice" role="status">リンクの取引先が見つかりませんでした。閲覧できる権限がないか、取引が終了してアーカイブに移った可能性があります。<button aria-label="通知を閉じる" onClick={() => setPartnerLink(undefined)}><X className="nav-icon" aria-hidden="true" strokeWidth={1.8}/></button></div>}
        {view === 'home' && <HomeView queueState={businessCardQueueState} reviewCaptureId={businessCardReviewCaptureId} registrationOwners={registrationOwners} registrationOwnerState={registrationOwnerState} formalRegistrationApi={dataverseBusinessCardFormalRegistrationApi} currentUserId={approvalActor?.systemUserId} dismissApi={dataverseBusinessCardDismissApi} onRefreshQueue={reloadBusinessCardQueue} onOpenReview={openBusinessCardReview} onRegisterCard={() => navigate('card-register')} onRegistered={openBusinessCardQueue} onCloseReview={() => setBusinessCardReviewCaptureId(undefined)} onLedger={() => navigate('ledger')} onRegister={() => navigate('new')} onContacts={() => navigate('contacts')}/>}
        {view === 'approvals' && <ApprovalCenterView state={approvalReadState} writeApi={dataverseStandardApprovalWriteApi} actor={approvalActor} focusRequestId={approvalFocusRequestId} users={registrationOwners} onRefresh={reloadApprovals}/>}
        {view === 'ledger' && <>
          <LivePartnerDirectory state={liveDirectoryState} query={query} selectedId={selectedId} onQueryChange={setQuery} onShowDetail={id => { setSelectedId(id); setDetailOpen(true) }} onRefresh={reloadPartners}/>
          {detailOpen && liveSelected && <><button className="detail-drawer-backdrop" aria-label="詳細を閉じる" onClick={closeDetail}/><LivePartnerDetail key={liveSelected.id} partner={liveSelected} initialTab={partnerLink?.id.toLowerCase() === liveSelected.id.toLowerCase() ? partnerLink.tab : undefined} contractReadApi={dataverseContractReadApi} partnerUpdateApi={dataverseStandardPartnerUpdateApi} approvalWriteApi={dataverseStandardApprovalWriteApi} bulletinBoardApi={dataverseBulletinBoardApi} onPartnerUpdated={handleLivePartnerUpdated} owners={registrationOwners} onAddContact={partnerId => { setContactRegistrationPartnerId(partnerId); navigate('contact-register') }} onShowContact={contactId => showContact(contactId)} onClose={closeDetail}/></>}
        </>}
        {view === 'new' && <NewRegistrationView onCompany={() => navigate('company-register')} onContact={() => { setContactRegistrationPartnerId(undefined); navigate('contact-register') }} onBusinessCard={() => navigate('card-register')}/>}
        {view === 'company-register' && <RegistrationForm api={dataverseStandardPartnerRegistrationApi} owners={registrationOwners} ownersState={registrationOwnerState} onCancel={() => navigate('new')} onSaved={partnerId => showPartner(partnerId)}/>}
        {view === 'contact-register' && <ContactRegistrationForm key={contactRegistrationPartnerId ?? 'none'} api={dataverseStandardContactRegistrationApi} parents={contactRegistrationParents} parentsState={contactRegistrationParentState} initialPartnerId={contactRegistrationPartnerId} onCancel={() => { if (contactRegistrationPartnerId) { setPartnerLink({id: contactRegistrationPartnerId, tab: '担当者'}); showPartner(contactRegistrationPartnerId) } else navigate('new') }} onSaved={contactId => showContact(contactId)}/>}
        {view === 'card-register' && <DataverseBusinessCardRegistration api={dataverseBusinessCardCaptureApi} bulkApi={dataverseBusinessCardBulkCaptureApi} onCreateOcrJob={createDataverseBusinessCardOcrJob} onOpenQueue={openBusinessCardQueue}/>}
        {view === 'contacts' && <>
          <LiveContactDirectory state={liveContactDirectoryState} query={liveContactQuery} selectedId={selectedContactId} onQueryChange={setLiveContactQuery} onShowDetail={id => { setSelectedContactId(id); setLiveContactDetailOpen(true) }} onRefresh={reloadContacts}/>
          {liveContactDetailOpen && liveSelectedContact && <><button className="detail-drawer-backdrop" aria-label="詳細を閉じる" onClick={closeLiveContactDetail}/><LiveContactDetail contact={liveSelectedContact} api={dataverseContactUpdateApi} bulletinBoardApi={dataverseBulletinBoardApi} onUpdated={handleLiveContactUpdated} onClose={closeLiveContactDetail}/></>}
        </>}
        {view === 'trash' && <LiveArchiveView state={archiveState} onRefresh={reloadArchive}/>}
        {view === 'settings' && <LiveSettingsView approvalPolicyReadApi={dataverseContractApprovalPolicyReadApi} approvalSettingsWriteApi={dataverseApprovalSettingsWriteApi} canEditApprovalPolicy={approvalActor?.isAdministrator ?? false} contractTypeManagementApi={dataverseContractTypeManagementApi}/>}
      </main>
    </div>
  </div>
}

function Info({label, value}: {label: string; value: string}) {return <div><dt>{label}</dt><dd>{value}</dd></div>}

function LoadError({message, onRetry}: {message: string; onRetry?: () => void}) {
  return <section className="panel directory-live-state" role="alert"><b>{message}</b>{onRetry && <RefreshButton onClick={onRetry}/>}</section>
}

function Loading({label = '読み込み中…'}: {label?: string}) {
  return <section className="panel directory-live-state" role="status"><b>{label}</b></section>
}

type LiveBulletinBoardLoadState =
  | {status: 'loading'}
  | {status: 'ready'; posts: BulletinPost[]}
  | {status: 'error'; message: string}

function formatBulletinPostDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '日時未取得'
    : new Intl.DateTimeFormat('ja-JP', {dateStyle: 'short', timeStyle: 'short'}).format(date)
}

export function LiveMessageBoard({scope, api = dataverseBulletinBoardApi}: {scope: BulletinBoardScope; api?: BulletinBoardApi}) {
  const [state, setState] = useState<LiveBulletinBoardLoadState>({status: 'loading'})
  const [body, setBody] = useState('')
  const [requestKey, setRequestKey] = useState(createBulletinPostRequestKey)
  const [refreshToken, setRefreshToken] = useState(0)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState('')
  const partnerId = scope.partnerId
  const contactId = scope.contactId

  useEffect(() => {
    let cancelled = false
    void Promise.resolve().then(async () => {
      if (cancelled) return
      setState({status: 'loading'})
      setMessage('')
      try {
        const posts = await api.list({partnerId, contactId})
        if (!cancelled) setState({status: 'ready', posts})
      } catch {
        if (!cancelled) setState({status: 'error', message: '掲示板を読み込めませんでした。'})
      }
      })
    return () => { cancelled = true }
  }, [api, contactId, partnerId, refreshToken])

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (saving || state.status !== 'ready' || !body.trim()) return
    setSaving(true)
    setMessage('')
    try {
      await api.create({...scope, body, requestKey})
      setBody('')
      setRequestKey(createBulletinPostRequestKey())
      setMessage('投稿しました。')
      setRefreshToken(current => current + 1)
    } catch (error) {
      setMessage(error instanceof BulletinBoardApiError ? error.message : '投稿できませんでした。入力内容はそのままなので、もう一度お試しください。')
    } finally {
      setSaving(false)
    }
  }

  const posts = state.status === 'ready' ? state.posts : []
  const title = scope.contactId ? '担当者の共有掲示板' : '取引先の共有掲示板'
  // 入力欄を一番上に置き、その下に新しい投稿から順に並べる（2026-09-28、ユーザー要望）。
  return <section className="message-board" aria-label="共有掲示板"><div className="message-board-heading"><div><h3>{title}</h3></div>{state.status === 'ready' && <span className="status-chip">{posts.length}件</span>}</div><form className="message-compose" onSubmit={event => void submit(event)}><fieldset disabled={saving || state.status !== 'ready'}><label>新しい投稿<textarea aria-label="共有掲示板に投稿" value={body} onChange={event => setBody(event.target.value)} rows={3} maxLength={200} placeholder="連絡事項を入力"/></label><div><span/><button className="primary-button" type="submit" disabled={!body.trim() || saving}>{saving ? '投稿中…' : '投稿'}</button></div></fieldset></form>{message && <p className="settings-message" role="status">{message}</p>}{state.status === 'loading' && <p className="empty-state" role="status">読み込み中…</p>}{state.status === 'error' && <div className="directory-live-state" role="alert"><b>{state.message}</b><button className="secondary-button" type="button" onClick={() => setRefreshToken(current => current + 1)}>再読み込み</button></div>}{state.status === 'ready' && <div className="message-list">{posts.length ? posts.map(post => <article key={post.id}><div><b>{post.author}</b><time>{formatBulletinPostDate(post.createdAt)}</time></div><p>{post.body}</p></article>) : <p className="empty-state">投稿はありません。</p>}</div>}</section>
}

type PartnerShareSettingLoadState =
  | {status: 'loading'}
  | {status: 'ready'; settings: PartnerShareSettingView[]}
  | {status: 'error'; message: string}

function shareSettingPrincipalLabel(setting: PartnerShareSettingView) {
  const kind = setting.principalKind === 'User' ? '個人' : 'グループ'
  return `${setting.principalName ?? setting.principalId}（${kind}）`
}

type PartnerSharePrincipalLoadState =
  | {status: 'loading'}
  | {status: 'ready'; principals: PartnerSharePrincipal[]}
  | {status: 'error'; message: string}

function shareSettingErrorMessage(error: unknown) {
  if (error instanceof PartnerShareSettingApiError) return error.message
  return '共有設定を保存できませんでした。再読み込みしてからもう一度お試しください。'
}

function principalOptionLabel(principal: PartnerSharePrincipal) {
  const kind = principal.kind === 'User' ? '個人' : 'グループ'
  return `${principal.name}（${kind}）`
}

function PartnerPrincipalCombobox({state, selectedPrincipal, query, onQueryChange, onSelect, disabled}: {state: PartnerSharePrincipalLoadState; selectedPrincipal?: PartnerSharePrincipal; query: string; onQueryChange: (query: string) => void; onSelect: (principal: PartnerSharePrincipal) => void; disabled?: boolean}) {
  const [open, setOpen] = useState(false)
  const [highlightedId, setHighlightedId] = useState('')
  const normalizedQuery = query.trim().toLocaleLowerCase('ja-JP')
  const principals = state.status === 'ready' ? state.principals : []
  const visiblePrincipals = principals.filter(principal => !normalizedQuery || [principal.name, principal.detail, principal.kind === 'User' ? '個人' : 'グループ'].filter(Boolean).join(' ').toLocaleLowerCase('ja-JP').includes(normalizedQuery))
  const inputValue = selectedPrincipal ? principalOptionLabel(selectedPrincipal) : query
  const listboxId = 'partner-share-principal-options'
  const highlightedIndex = visiblePrincipals.findIndex(principal => principal.id === highlightedId)

  const selectPrincipal = (principal: PartnerSharePrincipal) => {
    onSelect(principal)
    setHighlightedId(principal.id)
    setOpen(false)
  }

  const handleKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      setOpen(false)
      return
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      setOpen(true)
      if (!visiblePrincipals.length) return
      const nextIndex = highlightedIndex < 0
        ? event.key === 'ArrowDown' ? 0 : visiblePrincipals.length - 1
        : (highlightedIndex + (event.key === 'ArrowDown' ? 1 : -1) + visiblePrincipals.length) % visiblePrincipals.length
      setHighlightedId(visiblePrincipals[nextIndex].id)
      return
    }
    if (event.key === 'Enter' && open && highlightedIndex >= 0) {
      event.preventDefault()
      selectPrincipal(visiblePrincipals[highlightedIndex])
    }
  }

  const renderOptions = (kind: PartnerSharePrincipalKind, label: string) => {
    const options = visiblePrincipals.filter(principal => principal.kind === kind)
    if (!options.length) return null
    return <div className="principal-combobox-group"><span className="principal-combobox-group-label">{label}</span>{options.map(principal => <button id={`${listboxId}-${principal.id}`} className={'principal-combobox-option' + (principal.id === highlightedId ? ' is-highlighted' : '')} key={principal.id} type="button" role="option" aria-selected={principal.id === selectedPrincipal?.id} onMouseDown={event => event.preventDefault()} onClick={() => selectPrincipal(principal)}><span>{principalOptionLabel(principal)}</span>{principal.detail && <small>{principal.detail}</small>}</button>)}</div>
  }

  return <label className="principal-combobox-field"><span>共有先</span><div className="principal-combobox"><input className="principal-combobox-input" aria-label="共有先を検索・選択" role="combobox" aria-controls={listboxId} aria-expanded={open} aria-autocomplete="list" aria-activedescendant={highlightedId ? `${listboxId}-${highlightedId}` : undefined} placeholder="氏名・メール・グループ名を検索" value={inputValue} onFocus={() => { setOpen(true); if (selectedPrincipal) onQueryChange('') }} onBlur={() => setOpen(false)} onChange={event => { setOpen(true); setHighlightedId(''); onQueryChange(event.target.value) }} onKeyDown={handleKeyDown} disabled={disabled}/>{open && <div id={listboxId} className="principal-combobox-options" role="listbox">{state.status === 'loading' && <div className="principal-combobox-status" role="status">候補を検索しています…</div>}{state.status === 'error' && <div className="principal-combobox-status" role="alert">{state.message}</div>}{state.status === 'ready' && !visiblePrincipals.length && <div className="principal-combobox-status" role="status">候補がありません。</div>}{state.status === 'ready' && renderOptions('User', '個人')}{state.status === 'ready' && renderOptions('Team', 'グループ')}</div>}</div><small className="principal-search-count" role="status">{query.trim() ? '検索結果（上位20件）' : '候補（上位20件）'}</small></label>
}

export function LivePartnerShareSettingsPanel({partnerId, api, writeApi = dataversePartnerShareSettingApi, principalDirectoryApi = dataversePartnerSharePrincipalDirectoryApi}: {partnerId: string; api: PartnerShareSettingReadApi; writeApi?: PartnerShareSettingApi; principalDirectoryApi?: PartnerSharePrincipalDirectoryApi}) {
  const [state, setState] = useState<PartnerShareSettingLoadState>({status: 'loading'})
  const [principalState, setPrincipalState] = useState<PartnerSharePrincipalLoadState>({status: 'loading'})
  const [refreshToken, setRefreshToken] = useState(0)
  const [principalQuery, setPrincipalQuery] = useState('')
  const [selectedPrincipal, setSelectedPrincipal] = useState<PartnerSharePrincipal>()
  const [accessLevel, setAccessLevel] = useState<PartnerShareAccessLevel>('Read')
  const [busyKey, setBusyKey] = useState<string | undefined>()
  const [message, setMessage] = useState('')

  useEffect(() => {
    let cancelled = false
    void api.list(partnerId)
      .then(settings => { if (!cancelled) setState({status: 'ready', settings}) })
      .catch(() => { if (!cancelled) setState({status: 'error', message: '共有設定を読み込めませんでした。'}) })
    return () => { cancelled = true }
  }, [api, partnerId, refreshToken])

  useEffect(() => {
    let cancelled = false
    const query = principalQuery.trim()
    const timer = setTimeout(() => {
      void principalDirectoryApi.list(query || undefined)
        .then(principals => { if (!cancelled) setPrincipalState({status: 'ready', principals}) })
        .catch(() => { if (!cancelled) setPrincipalState({status: 'error', message: '共有先候補を取得できませんでした。'}) })
    }, query ? 300 : 0)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [principalDirectoryApi, principalQuery])

  // 取消済み行は監査・再照合のためDataverseに残すが、現在の共有先一覧には表示しない。
  const settings = state.status === 'ready' ? state.settings.filter(setting => setting.status === 'Active') : []
  const activeCount = settings.length
  const canCreate = state.status === 'ready' && principalState.status === 'ready' && Boolean(selectedPrincipal) && !busyKey

  const handleCreate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!selectedPrincipal || busyKey) return
    setBusyKey('create')
    setMessage('')
    try {
      await writeApi.create({partnerId, principalKind: selectedPrincipal.kind, principalId: selectedPrincipal.id, accessLevel, requestKey: createPartnerShareSettingRequestKey('Create')})
      setSelectedPrincipal(undefined)
      setPrincipalQuery('')
      setMessage('共有設定を保存しました。')
      setRefreshToken(current => current + 1)
    } catch (error) {
      setMessage(shareSettingErrorMessage(error))
    } finally {
      setBusyKey(undefined)
    }
  }

  const handleUpdate = async (setting: PartnerShareSettingView, nextAccessLevel: PartnerShareAccessLevel) => {
    if (setting.status !== 'Active' || setting.isProtectedManagementPath || !setting.isManagedProjection || setting.accessLevel === nextAccessLevel || busyKey) return
    setBusyKey(setting.settingId)
    setMessage('')
    try {
      await writeApi.update({settingId: setting.settingId, partnerId: setting.partnerId, principalKind: setting.principalKind, principalId: setting.principalId, accessLevel: nextAccessLevel, requestKey: createPartnerShareSettingRequestKey('Update')})
      setMessage('共有設定のアクセスレベルを更新しました。')
      setRefreshToken(current => current + 1)
    } catch (error) {
      setMessage(shareSettingErrorMessage(error))
    } finally {
      setBusyKey(undefined)
    }
  }

  const handleRevoke = async (setting: PartnerShareSettingView) => {
    if (setting.status !== 'Active' || setting.isProtectedManagementPath || !setting.isManagedProjection || busyKey) return
    setBusyKey(setting.settingId)
    setMessage('')
    try {
      await writeApi.revoke(setting.settingId)
      setMessage('共有設定を取消しました。')
      setRefreshToken(current => current + 1)
    } catch (error) {
      setMessage(shareSettingErrorMessage(error))
    } finally {
      setBusyKey(undefined)
    }
  }

  const handlePrincipalQueryChange = (query: string) => {
    setSelectedPrincipal(undefined)
    setPrincipalState({status: 'loading'})
    setPrincipalQuery(query)
  }

  const handlePrincipalSelect = (principal: PartnerSharePrincipal) => {
    setSelectedPrincipal(principal)
    setPrincipalQuery('')
  }
  return <>
    <AsyncOperationOverlay open={Boolean(busyKey)} title={busyKey === 'create' ? '共有設定を追加しています' : busyKey ? '共有設定を更新しています' : ''} description="完了するまでこの画面を閉じないでください。" />
    <section className="access-management" aria-label="共有設定">
      <div className="access-heading">
        <div>
          <h3>共有設定</h3>
          <p>この取引先を閲覧・編集できる人とグループです。</p>
        </div>
        <span className="status-chip">{state.status === 'ready' ? `${activeCount}件` : '読み込み中'}</span>
      </div>
      {state.status === 'loading' && <p className="empty-state" role="status">読み込み中…</p>}
      {state.status === 'error' && <p className="empty-state" role="alert">{state.message}</p>}
      {principalState.status === 'error' && <p className="empty-state" role="alert">{principalState.message}</p>}
      {state.status === 'ready' && (
        <div className="access-list">
          {settings.length ? settings.map(setting => {
            const active = setting.status === 'Active'
            const protectedPath = setting.isProtectedManagementPath
            const unmanagedPath = !setting.isManagedProjection
            const disabled = !active || protectedPath || unmanagedPath || Boolean(busyKey)
            return (
              <article className={'access-row' + (!active ? ' access-row-revoked' : '')} key={setting.settingId}>
                <div className="access-principal">
                  <span className="access-principal-icon">{setting.principalKind === 'User' ? '個' : 'G'}</span>
                  <div><b>{shareSettingPrincipalLabel(setting)}</b></div>
                </div>
                <label className="access-profile-label">
                  <span>アクセスレベル</span>
                  <select aria-label={`${shareSettingPrincipalLabel(setting)}のアクセスレベル`} value={setting.accessLevel} disabled={disabled} onChange={event => void handleUpdate(setting, event.target.value as PartnerShareAccessLevel)}>
                    <option value="Read">閲覧</option>
                    <option value="Write">編集</option>
                  </select>
                </label>
                <span className={'state ' + (active ? 'active' : 'paused')}><i />{active ? '有効' : '取消済み'}</span>
                <span className="access-expiry">{protectedPath ? '初期設定' : unmanagedPath ? '外部で設定' : active ? '' : '取消済み'}</span>
                <button className="secondary-button" type="button" disabled={disabled} onClick={() => void handleRevoke(setting)}>
                  {protectedPath || unmanagedPath ? '変更不可' : active ? '取消' : '取消済み'}
                </button>
              </article>
            )
          }) : <p className="empty-state">共有先はありません。</p>}
        </div>
      )}
      <form className="access-add-form" onSubmit={event => void handleCreate(event)}>
        <div>
          <h4>共有先を追加</h4>
        </div>
        <PartnerPrincipalCombobox
          state={principalState}
          selectedPrincipal={selectedPrincipal}
          query={principalQuery}
          onQueryChange={handlePrincipalQueryChange}
          onSelect={handlePrincipalSelect}
          disabled={Boolean(busyKey)}
        />
        <label>
          アクセスレベル
          <select aria-label="新しい共有設定のアクセスレベル" value={accessLevel} onChange={event => setAccessLevel(event.target.value as PartnerShareAccessLevel)} disabled={!canCreate}>
            <option value="Read">閲覧</option>
            <option value="Write">編集</option>
          </select>
        </label>
        <button className="primary-button" type="submit" disabled={!canCreate}>{busyKey === 'create' ? '保存中…' : '共有設定を追加'}</button>
      </form>
      {message && <p className="settings-message" role="status">{message}</p>}
    </section>
  </>
}

function directoryTradingStateClass(status: PartnerDirectoryItem['tradingStatus']) {
  return status === '取引中' ? 'active' : status === '休止中' ? 'paused' : status === '終了' ? 'ended' : 'unconfirmed'
}

function formatDirectoryDate(value?: string) {
  return value ? value.slice(0, 10) : '—'
}

const tradingOrder: Record<string, number> = {未承認: 0, 取引中: 1, 休止中: 2, 終了: 3}
const contactStatusOrder: Record<string, number> = {在籍: 0, 休職: 1, 退職: 2, 未設定: 3}

const partnerColumns: TableColumn<PartnerDirectoryItem>[] = [
  {key: 'name', label: '取引先', sortValue: partner => partner.name},
  {key: 'trading', label: '取引状態', sortValue: partner => tradingOrder[partner.tradingStatus], filterValue: partner => partner.tradingStatus},
  {key: 'owner', label: '主担当', sortValue: partner => partner.mainOwnerName, filterValue: partner => partner.mainOwnerName},
  {key: 'registered', label: '登録', sortValue: partner => partner.registeredAt, filterValue: partner => partner.registeredBy},
]

const contactColumns: TableColumn<ContactDirectoryItem>[] = [
  {key: 'name', label: '担当者', sortValue: contact => contact.name},
  {key: 'partner', label: '取引先', sortValue: contact => contact.partnerName, filterValue: contact => contact.partnerName},
  {key: 'role', label: '部署・役職', sortValue: contact => contact.departmentRole},
  {key: 'status', label: '在籍状態', sortValue: contact => contactStatusOrder[contact.status], filterValue: contact => contact.status},
  {key: 'registered', label: '登録日', sortValue: contact => contact.registeredAt},
]

const archivePartnerColumns: TableColumn<ArchivePartnerItem>[] = [
  {key: 'name', label: '取引先', sortValue: partner => partner.name},
  {key: 'owner', label: '主担当', sortValue: partner => partner.mainOwnerName, filterValue: partner => partner.mainOwnerName},
  {key: 'registered', label: '登録', sortValue: partner => partner.registeredAt, filterValue: partner => partner.registeredBy},
]

const archiveContactColumns: TableColumn<ArchiveContactItem>[] = [
  {key: 'name', label: '担当者', sortValue: contact => contact.name},
  {key: 'partner', label: '取引先', sortValue: contact => contact.partnerName, filterValue: contact => contact.partnerName},
  {key: 'role', label: '部署・役職', sortValue: contact => contact.departmentRole},
  {key: 'registered', label: '登録日', sortValue: contact => contact.registeredAt},
]

function RowOpenButton({label, onClick}: {label: string; onClick: () => void}) {
  return <button className="row-open-button" type="button" aria-label={label} title="詳細を開く" onClick={onClick}><PanelRightOpen className="row-action-icon" aria-hidden="true" strokeWidth={1.8}/></button>
}


export function LivePartnerDirectory({state, query, selectedId, onQueryChange, onShowDetail, onRefresh}: {
  state: PartnerDirectoryLoadState
  query: string
  selectedId: string
  onQueryChange: (query: string) => void
  onShowDetail: (id: string) => void
  onRefresh: () => void
}) {
  const table = useTableView()
  if (state.status === 'idle' || state.status === 'loading') return <Loading label="取引先を読み込んでいます…"/>
  if (state.status === 'error') return <LoadError message={state.message} onRetry={onRefresh}/>
  const searched = filterPartnerDirectory(state.partners, query)
  const listed = applyTableView(searched, partnerColumns, table.state)
  const filtering = activeFilterCount(table.state)
  return <section className="panel table-panel master-list-pane ledger-list-panel live-directory-list" aria-label="取引先一覧">
    <div className="table-tools"><div><h2>取引先 <span>{listed.length}</span></h2><FilterSummary count={listed.length} total={searched.length} activeFilters={filtering} onClear={table.clearFilters}/></div><div className="table-filters"><SearchField label="取引先を検索" placeholder="会社名・業種・住所・電話番号で検索" value={query} onChange={onQueryChange}/><RefreshButton onClick={onRefresh}/></div></div>
    <div className="table-scroll"><table><thead><tr><th className="row-open-cell"><span className="sr-only">詳細</span></th>{partnerColumns.map(column => <ColumnHeader key={column.key} column={column} rows={searched} state={table.state} onSort={table.toggleSort} onFilter={table.setFilter}/>)}</tr></thead><tbody>{listed.map(partner => <tr key={partner.id} className={selectedId === partner.id ? 'row-selected' : ''}><td className="row-open-cell"><RowOpenButton label={partner.name + 'の詳細を開く'} onClick={() => onShowDetail(partner.id)}/></td><td><button className="company-link" type="button" onClick={() => onShowDetail(partner.id)}><span className="company-symbol">{nameInitial(partner.name)}</span><span><b>{partner.name}</b>{partner.industry && <small>{partner.industry}</small>}</span></button></td><td><span className={'state ' + directoryTradingStateClass(partner.tradingStatus)}><i/>{partner.tradingStatus}</span></td><td>{partner.mainOwnerName ?? '—'}</td><td>{partner.registeredBy ?? '—'}<small className="live-directory-date">{formatDirectoryDate(partner.registeredAt)}</small></td></tr>)}</tbody></table></div>
    {!listed.length && <p className="empty-state">{query || filtering ? '該当する取引先がありません。' : '取引先はまだありません。'}</p>}
  </section>
}

type ContractTypeReadLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; contractTypes: ContractTypeReadItem[]}
  | {status: 'error'; message: string}

type PartnerContactsLoadState =
  | {status: 'loading'}
  | {status: 'ready'; contacts: ContactDirectoryItem[]}
  | {status: 'error'}

/** 取引先の詳細の「担当者」タブ。この取引先の在籍・休職の担当者と、担当者を追加する入口を出す（2026-09-29）。 */
function PartnerContactsTab({partnerId, api, onAddContact, onShowContact}: {partnerId: string; api: ContactDirectoryApi; onAddContact?: (partnerId: string) => void; onShowContact?: (contactId: string) => void}) {
  const [state, setState] = useState<PartnerContactsLoadState>({status: 'loading'})
  useEffect(() => {
    let cancelled = false
    void api.listContacts()
      .then(contacts => { if (!cancelled) setState({status: 'ready', contacts: contacts.filter(contact => contact.partnerId.toLowerCase() === partnerId.toLowerCase() && contact.status !== '退職')}) })
      .catch(() => { if (!cancelled) setState({status: 'error'}) })
    return () => { cancelled = true }
  }, [api, partnerId])

  return <section className="partner-contacts" aria-label="この取引先の担当者">
    <div className="message-board-heading"><div><h3>担当者</h3></div>{onAddContact && <button className="secondary-button" type="button" onClick={() => onAddContact(partnerId)}>担当者を追加</button>}</div>
    {state.status === 'loading' && <p className="empty-state" role="status">読み込み中…</p>}
    {state.status === 'error' && <p className="empty-state" role="alert">担当者を読み込めませんでした。</p>}
    {state.status === 'ready' && (state.contacts.length
      ? <ul className="partner-contact-list">{state.contacts.map(contact => <li key={contact.id}>{onShowContact ? <button type="button" className="text-button" onClick={() => onShowContact(contact.id)}>{contact.name}</button> : <b>{contact.name}</b>}<span>{contact.departmentRole ?? ''}</span><span className="status-chip">{contact.status}</span></li>)}</ul>
      : <p className="empty-state">担当者はまだいません。</p>)}
    <p className="form-note">退職した担当者は「アーカイブ」にあります。</p>
  </section>
}

export function LivePartnerDetail({partner, initialTab, contractReadApi = dataverseContractReadApi, contractTypeReadApi = dataverseContractTypeReadApi, contractRegistrationApi = dataverseStandardContractRegistrationApi, approvalWriteApi = dataverseStandardApprovalWriteApi, partnerUpdateApi = dataverseStandardPartnerUpdateApi, contractDirectUpdateApi = dataverseStandardContractUpdateApi, contractApprovalPolicyReadApi = dataverseContractApprovalPolicyReadApi, shareSettingReadApi = dataversePartnerShareSettingReadApi, shareSettingWriteApi = dataversePartnerShareSettingApi, sharePrincipalDirectoryApi = dataversePartnerSharePrincipalDirectoryApi, bulletinBoardApi = dataverseBulletinBoardApi, contactDirectoryApi = dataverseContactDirectoryApi, owners = [], onPartnerUpdated, onAddContact, onShowContact, onClose}: {partner: PartnerDirectoryItem; initialTab?: PartnerDetailTab; owners?: PartnerRegistrationOwner[]; contractReadApi?: ContractReadApi; contractTypeReadApi?: ContractTypeReadApi; contractRegistrationApi?: ContractLedgerRegistrationApi; approvalWriteApi?: StandardApprovalWriteApi; partnerUpdateApi?: PartnerDirectUpdateApi; contractDirectUpdateApi?: ContractDirectUpdateApi; contractApprovalPolicyReadApi?: ContractApprovalPolicyReadApi; shareSettingReadApi?: PartnerShareSettingReadApi; shareSettingWriteApi?: PartnerShareSettingApi; sharePrincipalDirectoryApi?: PartnerSharePrincipalDirectoryApi; bulletinBoardApi?: BulletinBoardApi; contactDirectoryApi?: ContactDirectoryApi; onPartnerUpdated?: (partnerId: string) => void; onAddContact?: (partnerId: string) => void; onShowContact?: (contactId: string) => void; onClose: () => void}) {
  const [tab, setTab] = useState<PartnerDetailTab>(initialTab ?? '基本情報')
  const [contractState, setContractState] = useState<ContractReadLoadState>({status: 'idle'})
  const [contractTypeState, setContractTypeState] = useState<ContractTypeReadLoadState>({status: 'idle'})
  const [contractRegistrationOpen, setContractRegistrationOpen] = useState(false)
  const [contractApprovalTarget, setContractApprovalTarget] = useState<ContractReadItem>()
  const [contractDirectUpdateTarget, setContractDirectUpdateTarget] = useState<ContractReadItem>()
  const [partnerEditTarget, setPartnerEditTarget] = useState<PartnerEditTarget>()
  const [contractApprovalPolicyState, setContractApprovalPolicyState] = useState<ContractApprovalPolicyLoadState>({status: 'idle'})
  const [contractRefreshToken, setContractRefreshToken] = useState(0)

  useEffect(() => {
    if (tab !== '契約・期限') return
    let cancelled = false
    void Promise.resolve().then(async () => {
      if (cancelled) return
      setContractState({status: 'loading'})
      try {
        const contracts = await contractReadApi.listContracts(partner.id)
        if (!cancelled) setContractState({status: 'ready', contracts})
      } catch {
        if (!cancelled) setContractState({status: 'error', message: '契約を読み込めませんでした。'})
      }
    })
    return () => { cancelled = true }
  }, [contractReadApi, partner.id, tab, contractRefreshToken])

  useEffect(() => {
    if (tab !== '契約・期限') return
    let cancelled = false
    void Promise.resolve().then(async () => {
      if (cancelled) return
      setContractTypeState({status: 'loading'})
      try {
        const contractTypes = await contractTypeReadApi.listSelectableContractTypes()
        if (!cancelled) setContractTypeState({status: 'ready', contractTypes})
      } catch {
        if (!cancelled) setContractTypeState({status: 'error', message: '契約種類を読み込めませんでした。'})
      }
    })
    return () => { cancelled = true }
  }, [contractTypeReadApi, tab])

  useEffect(() => {
    if (tab !== '契約・期限' && tab !== '基本情報') return
    let cancelled = false
    void Promise.resolve().then(async () => {
      if (cancelled) return
      setContractApprovalPolicyState({status: 'loading'})
      try {
        const policy = await contractApprovalPolicyReadApi.getActivePolicy()
        if (!cancelled) setContractApprovalPolicyState({status: 'ready', policy})
      } catch {
        if (!cancelled) setContractApprovalPolicyState({status: 'error', message: '承認設定を読み込めませんでした。'})
      }
    })
    return () => { cancelled = true }
  }, [contractApprovalPolicyReadApi, tab, contractRefreshToken])

  const availableContractTypes = contractTypeState.status === 'ready' ? contractTypeState.contractTypes : []
  const handleContractSaved = () => {
    setContractRegistrationOpen(false)
    setContractRefreshToken(current => current + 1)
  }
  const handleContractDirectUpdated = () => {
    setContractDirectUpdateTarget(undefined)
    setContractRefreshToken(current => current + 1)
  }

  return <aside className="panel detail-panel detail-drawer" role="dialog" aria-modal="true" aria-label={`${partner.name}の詳細`}>
    <div className="detail-title"><div><h2>{partner.name}</h2></div><div className="detail-title-actions"><button className="drawer-close" aria-label="詳細を閉じる" title="詳細を閉じる" onClick={onClose}><X className="nav-icon" aria-hidden="true" strokeWidth={1.8}/></button></div></div>
    <div className="detail-tabs" aria-label="取引先の詳細切替">{(['基本情報', '担当者', '契約・期限', '掲示板', '共有設定'] as const).map(currentTab => <button aria-pressed={tab === currentTab} key={currentTab} onClick={() => setTab(currentTab)}>{currentTab}</button>)}</div>
    <div className="tab-content">
      {tab === '基本情報' && (partnerEditTarget ? <LivePartnerEditForm partner={partner} target={partnerEditTarget} owners={owners} policy={contractApprovalPolicyState.status === 'ready' ? contractApprovalPolicyState.policy : {companyName: false, tradingStatus: false, address: false, phone: false, contractUpdate: false, mainOwner: true, settingsVersion: 0}} updateApi={partnerUpdateApi!} approvalApi={approvalWriteApi!} onCancel={() => setPartnerEditTarget(undefined)} onUpdated={partnerId => { setPartnerEditTarget(undefined); onPartnerUpdated?.(partnerId) }}/> : <><dl className="detail-grid"><Info label="業種" value={partner.industry ?? '未登録'}/><Info label="住所" value={partner.address ?? '未登録'}/><Info label="代表電話" value={partner.phone ?? '未登録'}/><Info label="取引状態" value={partner.tradingStatus}/><Info label="主担当" value={partner.mainOwnerName ?? '—'}/><Info label="登録者" value={partner.registeredBy ?? '—'}/><Info label="登録日" value={formatDirectoryDate(partner.registeredAt)}/></dl><section className="detail-operations"><h3>取引先を編集</h3>{contractApprovalPolicyState.status === 'loading' || contractApprovalPolicyState.status === 'idle' ? <p className="empty-state" role="status">読み込み中…</p> : contractApprovalPolicyState.status === 'error' ? <p className="empty-state" role="alert">{contractApprovalPolicyState.message}</p> : <><div className="detail-operation-buttons partner-field-actions">{partnerApprovalTargetKeys.map(target => <button key={target} className={contractApprovalPolicyState.policy[target] ? 'primary-button' : 'secondary-button'} type="button" onClick={() => setPartnerEditTarget(target)}>{contractApprovalPolicyState.policy[target] ? `${partnerTargetLabel(target)}を変更申請` : `${partnerTargetLabel(target)}を編集`}</button>)}</div><p className="form-note">「変更申請」の項目は、承認されると反映されます。</p></>}</section></>)}
      {tab === '契約・期限' && (contractRegistrationOpen ? <LiveContractRegistrationForm partner={partner} contractTypes={availableContractTypes} api={contractRegistrationApi} onSaved={handleContractSaved} onCancel={() => setContractRegistrationOpen(false)}/> : contractApprovalTarget ? <LiveContractApprovalDraftForm partner={partner} contract={contractApprovalTarget} api={approvalWriteApi} onCancel={() => setContractApprovalTarget(undefined)}/> : contractDirectUpdateTarget ? <LiveContractDirectUpdateForm partner={partner} contract={contractDirectUpdateTarget} api={contractDirectUpdateApi} onSaved={handleContractDirectUpdated} onCancel={() => setContractDirectUpdateTarget(undefined)}/> : <LiveContractReadSection state={contractState} contractTypeState={contractTypeState} approvalPolicyState={contractApprovalPolicyState} onRegister={availableContractTypes.length ? () => setContractRegistrationOpen(true) : undefined} onRequestUpdate={contractApprovalPolicyState.status === 'ready' && contractApprovalPolicyState.policy.contractUpdate ? setContractApprovalTarget : undefined} onDirectUpdate={contractApprovalPolicyState.status === 'ready' && !contractApprovalPolicyState.policy.contractUpdate ? setContractDirectUpdateTarget : undefined}/>)}
      {tab === '担当者' && <PartnerContactsTab partnerId={partner.id} api={contactDirectoryApi} onAddContact={onAddContact} onShowContact={onShowContact}/>}
      {tab === '掲示板' && <LiveMessageBoard api={bulletinBoardApi} scope={{partnerId: partner.id}}/>}
      {tab === '共有設定' && <LivePartnerShareSettingsPanel key={partner.id} partnerId={partner.id} api={shareSettingReadApi} writeApi={shareSettingWriteApi} principalDirectoryApi={sharePrincipalDirectoryApi}/>}
    </div>
  </aside>
}

function contractReadStatusClass(status: string) {
  return status === '締結済み' ? 'active' : status === '終了' ? 'ended' : 'unconfirmed'
}

export function LiveContractReadSection({state, contractTypeState, approvalPolicyState, onRegister, onRequestUpdate, onDirectUpdate}: {state: ContractReadLoadState; contractTypeState?: ContractTypeReadLoadState; approvalPolicyState?: ContractApprovalPolicyLoadState; onRegister?: () => void; onRequestUpdate?: (contract: ContractReadItem) => void; onDirectUpdate?: (contract: ContractReadItem) => void}) {
  const contracts = state.status === 'ready' ? state.contracts : []
  const canRegister = Boolean(onRegister && contractTypeState?.status === 'ready' && contractTypeState.contractTypes.length)
  const policyLoading = approvalPolicyState?.status === 'loading' || approvalPolicyState?.status === 'idle'
  return <section className="contract-detail live-contract-detail" aria-label="契約一覧">
    <div className="contract-detail-heading"><div><h3>契約・期限</h3></div><div className="detail-operation-buttons">{canRegister && <button className="primary-button" type="button" onClick={onRegister}>契約を登録</button>}</div></div>
    {contractTypeState?.status === 'error' && <p className="empty-state" role="alert">{contractTypeState.message}</p>}
    {onRegister === undefined && contractTypeState?.status === 'ready' && !contractTypeState.contractTypes.length && <p className="form-note">契約種類が登録されていないため、契約を登録できません。システム管理者が「設定」から契約種類を追加してください。</p>}
    {approvalPolicyState?.status === 'error' && <p className="empty-state" role="alert">{approvalPolicyState.message}</p>}
    {(state.status === 'idle' || state.status === 'loading' || policyLoading) && <p className="empty-state" role="status">読み込み中…</p>}
    {state.status === 'error' && <p className="empty-state" role="alert">{state.message}</p>}
    {state.status === 'ready' && !policyLoading && <div className="contract-list">{contracts.length ? contracts.map(contract => <article className="contract-card" key={contract.id}><div className="contract-card-heading"><div><span className={'state ' + contractReadStatusClass(contract.status)}><i/>{contract.status}</span><h3>{contract.name}</h3><p>{contract.contractTypeName ?? ''}</p></div>{onRequestUpdate && contract.status === '締結済み' && <button className="secondary-button" type="button" onClick={() => onRequestUpdate(contract)}>更新・終了判断を申請</button>}{onDirectUpdate && contract.status === '締結済み' && <button className="secondary-button" type="button" onClick={() => onDirectUpdate(contract)}>更新・終了判断を登録</button>}</div><dl className="detail-grid"><Info label="契約終了日" value={formatDirectoryDate(contract.endDate)}/><Info label="解約通知期限" value={formatDirectoryDate(contract.noticeDate)}/><Info label="更新判断期限" value={formatDirectoryDate(contract.decisionDate)}/><Info label="自動更新" value={contract.autoRenew === undefined ? '—' : contract.autoRenew ? 'あり' : 'なし'}/><div className="contract-link-item"><dt>契約書リンク</dt><dd>{contract.link && /^https?:\/\//i.test(contract.link) ? <a href={contract.link} target="_blank" rel="noopener noreferrer">{contract.link}</a> : contract.link ?? '未登録'}</dd></div></dl></article>) : <p className="empty-state">この取引先の契約はまだありません。</p>}</div>}
  </section>
}

export function LiveContractRegistrationForm({partner, contractTypes, api = dataverseStandardContractRegistrationApi, onSaved, onCancel}: {partner: PartnerDirectoryItem; contractTypes: ContractTypeReadItem[]; api?: ContractLedgerRegistrationApi; onSaved: (result: {contractId: string; isReplay: boolean}) => void; onCancel: () => void}) {
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState('')
  const [idempotencyKey] = useState(createContractRegistrationIdempotencyKey)

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (saving) return
    const data = new FormData(event.currentTarget)
    setSaving(true)
    setMessage('')
    try {
      const result = await api.registerContract({
        partnerId: partner.id,
        contractTypeId: String(data.get('contractTypeId') ?? ''),
        name: String(data.get('contractName') ?? ''),
        endDate: String(data.get('endDate') ?? ''),
        noticeDate: String(data.get('noticeDate') ?? ''),
        decisionDate: String(data.get('decisionDate') ?? ''),
        autoRenew: data.get('autoRenew') === 'on',
        link: String(data.get('link') ?? ''),
        idempotencyKey,
      })
      onSaved(result)
    } catch (error) {
      setMessage(error instanceof ContractRegistrationApiError ? error.message : '契約を保存できませんでした。入力内容はそのままなので、もう一度お試しください。')
    } finally {
      setSaving(false)
    }
  }

  return <section className="drawer-edit-panel contract-action-panel" aria-label="契約を登録"><div className="drawer-edit-heading"><div><h3>契約を登録</h3><p className="drawer-context">対象：{partner.name}</p></div><button className="secondary-button" type="button" onClick={onCancel} disabled={saving}>契約一覧へ戻る</button></div><form onSubmit={event => void save(event)}><fieldset className="field-grid registration-fields" disabled={saving}><label>契約名<span className="required">必須</span><input name="contractName" required maxLength={200} placeholder="例：基本取引契約（2026年度）"/></label><label>契約種類<span className="required">必須</span><select name="contractTypeId" required defaultValue=""><option value="">契約種類を選択</option>{contractTypes.map(contractType => <option key={contractType.id} value={contractType.id}>{contractType.name}</option>)}</select></label><label>契約終了日<input name="endDate" type="date"/></label><label>解約通知期限<input name="noticeDate" type="date"/></label><label>更新判断期限<input name="decisionDate" type="date"/></label><label className="checkbox-field"><input name="autoRenew" type="checkbox"/> 自動更新あり</label><label>契約書リンク<input name="link" type="url" maxLength={200} placeholder="契約書・関連ファイルのリンク"/></label></fieldset>{message && <p className="settings-message" role="alert">{message}</p>}<div className="form-actions"><button type="button" className="secondary-button" onClick={onCancel} disabled={saving}>キャンセル</button><button type="submit" className="primary-button" disabled={saving}>{saving ? '保存中…' : '保存'}</button></div></form></section>
}

export function LiveContactDirectory({state, query, selectedId, onQueryChange, onShowDetail, onRefresh}: {
  state: ContactDirectoryLoadState
  query: string
  selectedId: string
  onQueryChange: (query: string) => void
  onShowDetail: (id: string) => void
  onRefresh: () => void
}) {
  const table = useTableView()
  if (state.status === 'idle' || state.status === 'loading') return <Loading label="担当者を読み込んでいます…"/>
  if (state.status === 'error') return <LoadError message={state.message} onRetry={onRefresh}/>
  const searched = filterContactDirectory(state.contacts, query)
  const listed = applyTableView(searched, contactColumns, table.state)
  const filtering = activeFilterCount(table.state)
  return <section className="panel table-panel master-list-pane ledger-list-panel live-directory-list" aria-label="担当者一覧">
    <div className="table-tools"><div><h2>担当者 <span>{listed.length}</span></h2><FilterSummary count={listed.length} total={searched.length} activeFilters={filtering} onClear={table.clearFilters}/></div><div className="table-filters"><SearchField label="担当者を検索" placeholder="担当者・取引先・部署・役職で検索" value={query} onChange={onQueryChange}/><RefreshButton onClick={onRefresh}/></div></div>
    <div className="table-scroll"><table><thead><tr><th className="row-open-cell"><span className="sr-only">詳細</span></th>{contactColumns.map(column => <ColumnHeader key={column.key} column={column} rows={searched} state={table.state} onSort={table.toggleSort} onFilter={table.setFilter}/>)}</tr></thead><tbody>{listed.map(contact => <tr key={contact.id} className={selectedId === contact.id ? 'row-selected' : ''}><td className="row-open-cell"><RowOpenButton label={contact.name + 'の詳細を開く'} onClick={() => onShowDetail(contact.id)}/></td><td><button className="contact-link" type="button" onClick={() => onShowDetail(contact.id)}><span className="contact-avatar">{nameInitial(contact.name)}</span><span><b>{contact.name}</b></span></button></td><td>{contact.partnerName}</td><td>{contact.departmentRole ?? '—'}</td><td><span className={'state ' + contactDirectoryStateClass(contact.status)}><i/>{contact.status}</span></td><td>{formatDirectoryDate(contact.registeredAt)}</td></tr>)}</tbody></table></div>
    {!listed.length && <p className="empty-state">{query || filtering ? '該当する担当者がいません。' : '担当者はまだいません。'}</p>}
  </section>
}

export function LiveContactDetail({contact, api = dataverseContactUpdateApi, bulletinBoardApi = dataverseBulletinBoardApi, onUpdated = () => {}, onClose}: {contact: ContactDirectoryItem; api?: ContactUpdateApi; bulletinBoardApi?: BulletinBoardApi; onUpdated?: (result: ContactUpdateResult) => void; onClose: () => void}) {
  const [editing, setEditing] = useState(false)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState('')
  const [tab, setTab] = useState<'基本情報' | '掲示板'>('基本情報')

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (saving) return
    const data = new FormData(event.currentTarget)
    setSaving(true)
    setMessage('')
    try {
      const result = await api.update({
        contactId: contact.id,
        name: String(data.get('name') ?? ''),
        status: String(data.get('status') ?? '') as ContactUpdateRequest['status'],
        departmentRole: String(data.get('departmentRole') ?? ''),
        email: String(data.get('email') ?? ''),
        phone: String(data.get('phone') ?? ''),
      })
      onUpdated(result)
    } catch (error) {
      setMessage(error instanceof ContactUpdateApiError ? error.message : '担当者を保存できませんでした。入力内容はそのままなので、もう一度お試しください。')
    } finally {
      setSaving(false)
    }
  }

  return <aside className="panel detail-panel detail-drawer contact-detail-drawer" role="dialog" aria-modal="true" aria-label={`${contact.name}の詳細`}>
    <div className="contact-detail-header"><span className="contact-avatar contact-avatar-large">{nameInitial(contact.name)}</span><div><h2>{contact.name}</h2><p>{contact.partnerName} · {contact.departmentRole ?? '部署・役職未登録'}</p></div><div className="detail-title-actions"><span className={'state ' + contactDirectoryStateClass(contact.status)}><i/>{contact.status}</span><button className="drawer-close" aria-label="詳細を閉じる" title="詳細を閉じる" onClick={onClose}><X className="nav-icon" aria-hidden="true" strokeWidth={1.8}/></button></div></div>
    {editing ? <form className="contact-edit-form" onSubmit={event => void save(event)}><fieldset disabled={saving}><label>取引先<input defaultValue={contact.partnerName} readOnly aria-readonly="true"/></label><label>氏名<span className="required">必須</span><input name="name" required maxLength={850} defaultValue={contact.name}/></label><label>在籍状態<span className="required">必須</span><select name="status" required defaultValue={contact.status === '未設定' ? '' : contact.status}><option value="">在籍状態を選択</option><option value="在籍">在籍</option><option value="休職">休職</option><option value="退職">退職</option></select></label><label>部署・役職<input name="departmentRole" maxLength={200} defaultValue={contact.departmentRole ?? ''}/></label><label>メールアドレス<input name="email" type="email" maxLength={200} defaultValue={contact.email ?? ''}/></label><label>電話番号<input name="phone" maxLength={200} defaultValue={contact.phone ?? ''} placeholder="03-0000-0000"/></label></fieldset><p className="form-note">所属会社が変わった場合は、この担当者を「退職」にして、新しい取引先で登録し直してください。</p>{message && <p className="settings-message" role="alert">{message}</p>}<div className="form-actions"><button type="button" className="secondary-button" onClick={() => {setEditing(false); setMessage('')}} disabled={saving}>キャンセル</button><button type="submit" className="primary-button" disabled={saving}>{saving ? '保存中…' : '保存'}</button></div></form> : <><div className="detail-tabs" aria-label="担当者の詳細切替">{(['基本情報', '掲示板'] as const).map(currentTab => <button aria-pressed={tab === currentTab} key={currentTab} onClick={() => setTab(currentTab)}>{currentTab}</button>)}</div><div className="tab-content">{tab === '基本情報' && <><dl className="detail-grid contact-detail-grid"><Info label="取引先" value={contact.partnerName}/><Info label="部署・役職" value={contact.departmentRole ?? '未登録'}/><Info label="メールアドレス" value={contact.email ?? '未登録'}/><Info label="電話番号" value={contact.phone ?? '未登録'}/><Info label="在籍状態" value={contact.status}/><Info label="登録日" value={formatDirectoryDate(contact.registeredAt)}/></dl><div className="contact-detail-section"><button className="secondary-button" type="button" onClick={() => {setMessage(''); setEditing(true)}}>担当者を編集</button>{message && <p className="settings-message" role="alert">{message}</p>}</div></>}{tab === '掲示板' && <LiveMessageBoard api={bulletinBoardApi} scope={{partnerId: contact.partnerId, contactId: contact.id}}/>}</div></>}
  </aside>
}

function businessCardQueueStatusClass(status: BusinessCardQueueItem['captureStatus']) {
  return status === '失敗' || status === '結果不明' ? 'paused' : status === '確認待ち' ? 'active' : 'unconfirmed'
}

const businessCardQueueStatusLabels: Record<BusinessCardQueueItem['captureStatus'], string> = {
  未登録: '読み取り待ち',
  処理中: '読み取り中',
  確認待ち: '確認待ち',
  登録済み: '登録済み',
  失敗: '読み取り失敗',
  結果不明: '状態を確認できません',
}

function businessCardQueueDate(item: BusinessCardQueueItem) {
  return formatDirectoryDate(item.registeredAt ?? item.receivedAt)
}

/** 名刺の確認は取引先の詳細と同じく、右から開くモーダルで出す。Escキーと背景のクリックで閉じる。 */
function ReviewDrawer({onClose, children}: {onClose: () => void; children: ReactNode}) {
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [onClose])
  return <><button className="detail-drawer-backdrop" type="button" aria-label="名刺の確認を閉じる" onClick={onClose}/><div className="panel detail-panel detail-drawer review-drawer" role="dialog" aria-modal="true" aria-label="名刺の内容を確認">{children}</div></>
}

function BusinessCardQueueAction({item, currentUserId, dismissApi, onOpenReview, onRegisterCard, onDismissed}: {item: BusinessCardQueueItem; currentUserId?: string; dismissApi?: BusinessCardDismissApi; onOpenReview?: (captureId: string) => void; onRegisterCard?: () => void; onDismissed: (message: string) => void}) {
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  if (item.captureStatus === '確認待ち' && onOpenReview) return <button className="secondary-button" type="button" onClick={() => onOpenReview(item.id)}>確認する</button>
  if (!dismissApi || !canDismissBusinessCard(item, currentUserId)) return null
  const dismiss = async () => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await dismissApi.dismiss(item)
      onDismissed('名刺を処理待ちから外しました。')
    } catch (caught) {
      setError(caught instanceof BusinessCardDismissError ? caught.message : '一覧から外せませんでした。')
      setBusy(false)
    }
  }
  return <span className="queue-actions">
    <span className="queue-action-buttons">{confirming
      ? <><button className="secondary-button danger-button" type="button" disabled={busy} onClick={() => void dismiss()}>{busy ? '外しています…' : '外す'}</button><button className="secondary-button" type="button" disabled={busy} onClick={() => setConfirming(false)}>やめる</button></>
      : <>{onRegisterCard && <button className="secondary-button" type="button" onClick={onRegisterCard}>もう一度登録</button>}<button className="secondary-button" type="button" onClick={() => setConfirming(true)}>一覧から外す</button></>}</span>
    {error && <small className="queue-action-error" role="alert">{error}</small>}
  </span>
}

export function LiveBusinessCardQueue({state, reviewCaptureId, registrationOwners = [], registrationOwnerState = 'idle', formalRegistrationApi, currentUserId, dismissApi, onRefresh, onOpenReview, onRegisterCard, onRegistered, onCloseReview}: {state: BusinessCardQueueLoadState; reviewCaptureId?: string; registrationOwners?: BusinessCardRegistrationOwner[]; registrationOwnerState?: BusinessCardRegistrationOwnerState; formalRegistrationApi?: BusinessCardFormalRegistrationApi; currentUserId?: string; dismissApi?: BusinessCardDismissApi; onRefresh: () => void; onOpenReview?: (captureId: string) => void; onRegisterCard?: () => void; onRegistered?: () => void; onCloseReview?: () => void}) {
  const [message, setMessage] = useState('')
  // 登録済みの名刺は処理が終わっているので、処理待ちには出さない。
  const items = state.status === 'ready' ? state.items.filter(item => item.captureStatus !== '登録済み') : []
  // 案内は、自分で操作できる失敗・結果不明の行があるときだけ出す。
  const hasOwnFailures = Boolean(dismissApi) && items.some(item => canDismissBusinessCard(item, currentUserId))
  const handleDismissed = (next: string) => { setMessage(next); onRefresh() }
  return <>
    <section className="panel home-summary" aria-label="処理待ちの名刺">
      <div className="table-tools home-summary-tools"><div><h2>処理待ちの名刺 <span>{state.status === 'ready' ? items.length : '…'}</span></h2></div><div className="heading-actions"><RefreshButton onClick={onRefresh} disabled={state.status === 'loading'}/></div></div>
      {(state.status === 'idle' || state.status === 'loading') && <p className="empty-state" role="status">読み込み中…</p>}
      {state.status === 'error' && <p className="empty-state" role="alert">{state.message}</p>}
      {state.status === 'ready' && (items.length ? <div className="table-scroll"><table><thead><tr><th className="queue-action-cell"><span className="sr-only">操作</span></th><th>登録者</th><th>受付日</th><th>状態</th></tr></thead><tbody>{items.map(item => <tr key={item.id}><td className="queue-action-cell"><BusinessCardQueueAction item={item} currentUserId={currentUserId} dismissApi={dismissApi} onOpenReview={onOpenReview} onRegisterCard={onRegisterCard} onDismissed={handleDismissed}/></td><td>{item.registeredBy}</td><td>{businessCardQueueDate(item)}</td><td><span className={'state ' + businessCardQueueStatusClass(item.captureStatus)}><i/>{businessCardQueueStatusLabels[item.captureStatus]}</span></td></tr>)}</tbody></table></div> : <p className="empty-state">処理待ちの名刺はありません。</p>)}
      {message && <p className="approval-editor-success" role="status">{message}</p>}
      {hasOwnFailures && <p className="form-note queue-note">読み取れなかった名刺は、「もう一度登録」から画像を選び直してください。不要になったものは「一覧から外す」で処理待ちから外せます。</p>}
    </section>
    {reviewCaptureId && <ReviewDrawer onClose={onCloseReview ?? (() => {})}><DataverseBusinessCardReview key={reviewCaptureId} captureId={reviewCaptureId} formalRegistrationApi={formalRegistrationApi} registrationOwners={registrationOwners} registrationOwnerState={registrationOwnerState} onRegistered={onRegistered} onClose={onCloseReview ?? (() => {})}/></ReviewDrawer>}
  </>
}

export function HomeView({queueState, reviewCaptureId, registrationOwners = [], registrationOwnerState = 'idle', formalRegistrationApi, currentUserId, dismissApi, onRefreshQueue, onOpenReview, onRegisterCard, onRegistered, onCloseReview, onLedger, onRegister, onContacts}: {queueState: BusinessCardQueueLoadState; reviewCaptureId?: string; registrationOwners?: BusinessCardRegistrationOwner[]; registrationOwnerState?: BusinessCardRegistrationOwnerState; formalRegistrationApi?: BusinessCardFormalRegistrationApi; currentUserId?: string; dismissApi?: BusinessCardDismissApi; onRefreshQueue: () => void; onOpenReview?: (captureId: string) => void; onRegisterCard?: () => void; onRegistered?: () => void; onCloseReview?: () => void; onLedger: () => void; onRegister: () => void; onContacts: () => void}) {
  return <>
    <section className="home-grid">
      <button className="panel home-card" onClick={onRegister}><span className="home-icon"><NavIcon name="add" /></span><span><b>新規登録</b><small>取引先・担当者・名刺を登録</small></span><span className="home-arrow"><ArrowRight aria-hidden="true" strokeWidth={1.8}/></span></button>
      <button className="panel home-card" onClick={onLedger}><span className="home-icon"><NavIcon name="building" /></span><span><b>取引先リスト</b><small>会社・契約・状態を確認</small></span><span className="home-arrow"><ArrowRight aria-hidden="true" strokeWidth={1.8}/></span></button>
      <button className="panel home-card" onClick={onContacts}><span className="home-icon"><NavIcon name="users" /></span><span><b>担当者リスト</b><small>担当者を検索・確認</small></span><span className="home-arrow"><ArrowRight aria-hidden="true" strokeWidth={1.8}/></span></button>
    </section>
    <section className="home-overview">
      <LiveBusinessCardQueue state={queueState} reviewCaptureId={reviewCaptureId} registrationOwners={registrationOwners} registrationOwnerState={registrationOwnerState} formalRegistrationApi={formalRegistrationApi} currentUserId={currentUserId} dismissApi={dismissApi} onRefresh={onRefreshQueue} onOpenReview={onOpenReview} onRegisterCard={onRegisterCard} onRegistered={onRegistered} onCloseReview={onCloseReview}/>
    </section>
  </>
}

export function NewRegistrationView({onCompany, onContact, onBusinessCard}: {onCompany: () => void; onContact: () => void; onBusinessCard: () => void}) {
  return <section className="registration-choice-grid">
    <button className="panel registration-choice" onClick={onCompany}><span className="choice-icon"><NavIcon name="building" /></span><span><b>取引先を登録</b><small>新しい会社を取引先リストに追加します。</small></span><span className="choice-action">登録へ <ArrowRight aria-hidden="true" strokeWidth={1.8}/></span></button>
    <button className="panel registration-choice" onClick={onContact}><span className="choice-icon"><NavIcon name="users" /></span><span><b>担当者を登録</b><small>登録済みの取引先に担当者を追加します。</small></span><span className="choice-action">登録へ <ArrowRight aria-hidden="true" strokeWidth={1.8}/></span></button>
    <button className="panel registration-choice registration-choice-secondary" onClick={onBusinessCard}><span className="choice-icon"><NavIcon name="image" /></span><span><b>名刺で登録</b><small>名刺の画像を読み取り、内容を確認して登録します。1枚でも、まとめて複数枚でも選べます。</small></span><span className="choice-action">登録へ <ArrowRight aria-hidden="true" strokeWidth={1.8}/></span></button>
  </section>
}

function archiveMatches(query: string, values: (string | undefined)[]) {
  const needle = query.trim().toLocaleLowerCase('ja-JP')
  return !needle || values.some(value => value?.toLocaleLowerCase('ja-JP').includes(needle))
}

export function LiveArchiveView({state, onRefresh}: {state: ArchiveDirectoryLoadState; onRefresh: () => void}) {
  const [tab, setTab] = useState<'partners' | 'contacts'>('partners')
  const [query, setQuery] = useState('')
  const partnerTable = useTableView()
  const contactTable = useTableView()
  if (state.status === 'loading' || state.status === 'idle') return <Loading label="アーカイブを読み込んでいます…"/>
  if (state.status === 'error') return <LoadError message={state.message} onRetry={onRefresh}/>
  const partners = state.partners.filter(partner => archiveMatches(query, [partner.name, partner.mainOwnerName, partner.registeredBy]))
  const contacts = state.contacts.filter(contact => archiveMatches(query, [contact.name, contact.partnerName, contact.departmentRole]))
  const listedPartners = applyTableView(partners, archivePartnerColumns, partnerTable.state)
  const listedContacts = applyTableView(contacts, archiveContactColumns, contactTable.state)
  const table = tab === 'partners' ? partnerTable : contactTable
  const listedCount = tab === 'partners' ? listedPartners.length : listedContacts.length
  const searchedCount = tab === 'partners' ? partners.length : contacts.length
  const filtering = activeFilterCount(table.state)
  return <section className="panel table-panel live-directory-list archive-panel" aria-label="アーカイブ">
    <div className="table-tools"><div><h2>アーカイブ <span>{listedCount}</span></h2><FilterSummary count={listedCount} total={searchedCount} activeFilters={filtering} onClear={table.clearFilters}/></div><div className="table-filters"><SearchField label="アーカイブを検索" placeholder="名前・取引先・部署で検索" value={query} onChange={setQuery}/><RefreshButton onClick={onRefresh}/></div></div>
    <div className="detail-tabs panel-tabs" aria-label="アーカイブの切替"><button type="button" aria-pressed={tab === 'partners'} onClick={() => setTab('partners')}>終了した取引先 <span>{partners.length}</span></button><button type="button" aria-pressed={tab === 'contacts'} onClick={() => setTab('contacts')}>退職した担当者 <span>{contacts.length}</span></button></div>
    {tab === 'partners' ? <>
      <div className="table-scroll"><table><thead><tr>{archivePartnerColumns.map(column => <ColumnHeader key={column.key} column={column} rows={partners} state={partnerTable.state} onSort={partnerTable.toggleSort} onFilter={partnerTable.setFilter}/>)}</tr></thead><tbody>{listedPartners.map(partner => <tr key={partner.id}><td><span className="company-link"><span className="company-symbol">{nameInitial(partner.name)}</span><span><b>{partner.name}</b><small>{partner.tradingStatus}</small></span></span></td><td>{partner.mainOwnerName ?? '—'}</td><td>{partner.registeredBy ?? '—'}<small className="live-directory-date">{formatDirectoryDate(partner.registeredAt)}</small></td></tr>)}</tbody></table></div>
      {!listedPartners.length && <p className="empty-state">{query || filtering ? '該当する取引先がありません。' : '終了した取引先はありません。'}</p>}
    </> : <>
      <div className="table-scroll"><table><thead><tr>{archiveContactColumns.map(column => <ColumnHeader key={column.key} column={column} rows={contacts} state={contactTable.state} onSort={contactTable.toggleSort} onFilter={contactTable.setFilter}/>)}</tr></thead><tbody>{listedContacts.map(contact => <tr key={contact.id}><td><span className="contact-link"><span className="contact-avatar">{nameInitial(contact.name)}</span><span><b>{contact.name}</b></span></span></td><td>{contact.partnerName}</td><td>{contact.departmentRole ?? '—'}</td><td>{formatDirectoryDate(contact.registeredAt)}</td></tr>)}</tbody></table></div>
      {!listedContacts.length && <p className="empty-state">{query || filtering ? '該当する担当者がいません。' : '退職した担当者はいません。'}</p>}
    </>}
  </section>
}

type ApprovalPolicySettingsMessage = {kind: 'success' | 'error'; text: string}

function policyFlags(policy: ContractApprovalPolicy): ApprovalPolicyFlags {
  return {companyName: policy.companyName, tradingStatus: policy.tradingStatus, address: policy.address, phone: policy.phone, contractUpdate: policy.contractUpdate, mainOwner: policy.mainOwner}
}

/** 承認設定の表示・変更欄。管理者（canEdit）だけがチェックを変えて保存できる。 */
export function ApprovalPolicySettingsForm({policy, draft, canEdit, saving, message, onToggle, onSave, onRevert}: {policy: ContractApprovalPolicy; draft: ApprovalPolicyFlags; canEdit: boolean; saving: boolean; message?: ApprovalPolicySettingsMessage; onToggle: (key: ApprovalTarget) => void; onSave: () => void; onRevert: () => void}) {
  const changed = approvalTargetKeys.some(key => draft[key] !== policy[key])
  const enabledCount = approvalTargetKeys.filter(key => draft[key]).length
  return <>
    <div className="settings-group">{approvalTargetKeys.map(key => <label key={key}><input type="checkbox" checked={draft[key]} disabled={!canEdit || saving} onChange={() => onToggle(key)}/> {approvalTargetLabels[key]}</label>)}</div>
    <span className="status-chip">承認が必要 {enabledCount}件</span>
    <small className="settings-caption">{policy.settingsVersion > 0 ? `現在の設定：版${policy.settingsVersion}` : '現在の設定：既定値'}{canEdit ? '。変更は、これから提出する申請に使われます。' : '。この設定は管理者が変更します。'}</small>
    {canEdit && changed && <div className="settings-save">
      <p className="warning" role="note">承認待ちの申請は、承認されても反映されず「反映失敗」になります。差戻し中の申請は再提出できなくなるため、取り下げて出し直してもらいます。</p>
      <div className="detail-operation-buttons"><button className="primary-button" type="button" disabled={saving} onClick={onSave}>{saving ? '保存中…' : '保存する'}</button><button className="secondary-button" type="button" disabled={saving} onClick={onRevert}>元に戻す</button></div>
    </div>}
    {message && <p className={message.kind === 'success' ? 'success-note' : 'warning'} role={message.kind === 'success' ? 'status' : 'alert'}>{message.text}</p>}
  </>
}

export function LiveApprovalPolicySettings({api, writeApi, canEdit}: {api: ContractApprovalPolicyReadApi; writeApi?: ApprovalSettingsWriteApi; canEdit: boolean}) {
  const [state, setState] = useState<ContractApprovalPolicyLoadState>({status: 'idle'})
  const [draft, setDraft] = useState<ApprovalPolicyFlags>()
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<ApprovalPolicySettingsMessage>()
  const load = useCallback((nextMessage?: ApprovalPolicySettingsMessage) => {
    setState({status: 'loading'})
    setMessage(nextMessage)
    void api.getActivePolicy()
      .then(policy => { setState({status: 'ready', policy}); setDraft(policyFlags(policy)) })
      .catch(() => setState({status: 'error', message: '承認設定を読み込めませんでした。'}))
  }, [api])

  useEffect(() => {
    let cancelled = false
    void Promise.resolve().then(() => {
      if (!cancelled) void load()
    })
    return () => { cancelled = true }
  }, [load])

  const save = async () => {
    if (state.status !== 'ready' || !draft || !writeApi) return
    setSaving(true)
    setMessage(undefined)
    try {
      const saved = await writeApi.saveNewVersion({policy: draft})
      load({kind: 'success', text: `保存しました（版${saved.version}）。`})
    } catch (error) {
      setMessage({kind: 'error', text: error instanceof ApprovalSettingsWriteError ? error.message : '承認設定を保存できませんでした。'})
    } finally {
      setSaving(false)
    }
  }

  const editable = canEdit && Boolean(writeApi)
  return <section className="panel settings-card" aria-label="承認設定"><div className="section-heading"><div><h2>承認が必要な変更</h2><p>チェックの付いた変更は、承認されてから反映されます。</p></div><div className="detail-operation-buttons"><RefreshButton onClick={() => load()} disabled={state.status === 'loading' || saving}/></div></div>{(state.status === 'loading' || state.status === 'idle') && <p className="empty-state" role="status">読み込み中…</p>}{state.status === 'error' && <p className="empty-state" role="alert">{state.message}</p>}{state.status === 'ready' && draft && <ApprovalPolicySettingsForm policy={state.policy} draft={draft} canEdit={editable} saving={saving} message={message} onToggle={key => setDraft(current => current ? {...current, [key]: !current[key]} : current)} onSave={() => void save()} onRevert={() => { setDraft(policyFlags(state.policy)); setMessage(undefined) }}/>}</section>
}

export function LiveSettingsView({approvalPolicyReadApi, approvalSettingsWriteApi, canEditApprovalPolicy = false, contractTypeManagementApi}: {approvalPolicyReadApi: ContractApprovalPolicyReadApi; approvalSettingsWriteApi?: ApprovalSettingsWriteApi; canEditApprovalPolicy?: boolean; contractTypeManagementApi: ContractTypeManagementApi}) {
  return <section className="settings-grid" aria-label="設定">
    <LiveApprovalPolicySettings api={approvalPolicyReadApi} writeApi={approvalSettingsWriteApi} canEdit={canEditApprovalPolicy}/>
    <LiveContractTypeSettings api={contractTypeManagementApi}/>
  </section>
}

export function LiveContractTypeSettings({api}: {api: ContractTypeManagementApi}) {
  const [state, setState] = useState<{status: 'idle' | 'loading' | 'ready' | 'error'; items: ContractTypeAdminItem[]; message?: string}>({status: 'idle', items: []})
  const [newName, setNewName] = useState('')
  const [newDisplayOrder, setNewDisplayOrder] = useState('')
  const [editingId, setEditingId] = useState<string>()
  const [editingName, setEditingName] = useState('')
  const [busy, setBusy] = useState(false)

  const load = useCallback(async (message?: string) => {
    setState(current => ({status: 'loading', items: current.items, message}))
    try {
      const items = await api.list()
      setState({status: 'ready', items, message})
    } catch (error) {
      setState({status: 'error', items: [], message: error instanceof ContractTypeManagementError ? error.message : '契約種類を読み込めませんでした。'})
    }
  }, [api])

  useEffect(() => { void load() }, [load])

  const hasName = (name: string, exceptId?: string) => state.items.some(item => item.id !== exceptId && item.name.localeCompare(name.trim(), 'ja', {sensitivity: 'base'}) === 0)
  const run = async (operation: () => Promise<void>, successMessage: string) => {
    if (busy) return
    setBusy(true)
    try {
      await operation()
      await load(successMessage)
    } catch (error) {
      setState(current => ({...current, status: current.status === 'idle' ? 'error' : current.status, message: error instanceof ContractTypeManagementError ? error.message : '契約種類を保存できませんでした。'}))
    } finally {
      setBusy(false)
    }
  }

  const add = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (state.status !== 'ready') return
    const name = newName.trim()
    if (hasName(name)) { setState(current => ({...current, message: '同じ契約種類名は追加できません。'})); return }
    const displayOrder = newDisplayOrder.trim() ? Number(newDisplayOrder) : undefined
    if (displayOrder !== undefined && (!Number.isInteger(displayOrder) || displayOrder < 0)) { setState(current => ({...current, message: '表示順は0以上の整数で指定してください。'})); return }
    await run(async () => { await api.create({name, displayOrder}); setNewName(''); setNewDisplayOrder('') }, '契約種類を追加しました。')
  }

  const rename = async (id: string) => {
    const name = editingName.trim()
    if (hasName(name, id)) { setState(current => ({...current, message: '同じ契約種類名は設定できません。'})); return }
    await run(async () => { await api.rename(id, name); setEditingId(undefined); setEditingName('') }, '契約種類名を変更しました。')
  }

  const retire = async (item: ContractTypeAdminItem) => {
    if (item.state === 'Inactive') return
    await run(() => api.retire(item.id), '契約種類を廃止しました。既存契約の記録は保持します。')
  }

  const activeCount = state.items.filter(item => item.state === 'Active').length
  return <section className="panel settings-card contract-type-settings live-contract-type-settings" aria-label="契約種類の管理"><div className="section-heading"><div><h2>契約種類</h2><p>契約を登録するときに選べる種類です。追加・名前の変更・廃止はシステム管理者が行います。</p></div><div className="detail-operation-buttons"><RefreshButton onClick={() => void load()} disabled={busy}/></div></div>{state.message && <p className={state.status === 'error' ? 'notice error-notice' : 'settings-message'} role={state.status === 'error' ? 'alert' : 'status'}>{state.message}</p>}{state.status === 'loading' && <p className="empty-state" role="status">読み込み中…</p>}{state.status === 'error' && <p className="empty-state">再読み込みしてください。</p>}{state.status !== 'error' && <><form className="master-add-form" onSubmit={add}><label>契約種類名<input required maxLength={200} value={newName} onChange={event => setNewName(event.target.value)} placeholder="例：秘密保持契約" disabled={busy || state.status !== 'ready'}/></label><label>表示順<input type="number" min="0" step="1" value={newDisplayOrder} onChange={event => setNewDisplayOrder(event.target.value)} placeholder="任意" disabled={busy || state.status !== 'ready'}/></label><button className="primary-button" type="submit" disabled={busy || state.status !== 'ready'}>{busy ? '保存中…' : '追加'}</button></form><p className="settings-caption">有効 {activeCount}件 ／ 全{state.items.length}件。廃止しても、登録済みの契約はそのまま残ります。</p><ul className="contract-type-list">{state.items.map(item => <li key={item.id} className="contract-type-row">{editingId === item.id ? <><input aria-label={`${item.name}の契約種類名`} maxLength={200} value={editingName} onChange={event => setEditingName(event.target.value)} disabled={busy}/><div><button className="primary-button" type="button" onClick={() => void rename(item.id)} disabled={busy}>保存</button><button className="secondary-button" type="button" onClick={() => {setEditingId(undefined); setEditingName('')}} disabled={busy}>取消</button></div></> : <><span><b>{item.name}</b><small>{item.state === 'Active' ? (item.selectable ? '有効' : '有効（選択不可）') : '廃止'}{item.displayOrder === undefined ? '' : ` · 表示順 ${item.displayOrder}`}</small></span><div><button className="secondary-button" type="button" onClick={() => {setEditingId(item.id); setEditingName(item.name)}} disabled={busy}>名称変更</button><button className="secondary-button danger-button" type="button" onClick={() => void retire(item)} disabled={busy || item.state === 'Inactive'}>廃止</button></div></>}</li>)}</ul>{!state.items.length && <p className="empty-state">契約種類がありません。追加できます。</p>}</>}</section>
}

export function RegistrationForm({api, owners, ownersState, onCancel, onSaved}: {api: PartnerLedgerRegistrationApi; owners: PartnerRegistrationOwner[]; ownersState: PartnerRegistrationOwnerLoadState; onCancel: () => void; onSaved: (partnerId: string) => void}) {
  const [status, setStatus] = useState<'idle' | 'submitting' | 'success' | 'error'>('idle')
  const [error, setError] = useState<PartnerRegistrationApiError>()
  const [result, setResult] = useState<{partnerId: string; isReplay: boolean}>()
  const [mainOwnerId, setMainOwnerId] = useState('')
  const [idempotencyKey] = useState(() => createPartnerRegistrationIdempotencyKey())
  const selectedOwnerId = mainOwnerId || owners[0]?.id || ''

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (status === 'submitting' || status === 'success') return
    const form = new FormData(event.currentTarget)
    setStatus('submitting')
    setError(undefined)
    try {
      const saved = await api.registerPartner({
        name: String(form.get('name') ?? ''),
        industry: String(form.get('industry') ?? ''),
        address: String(form.get('address') ?? ''),
        phone: String(form.get('phone') ?? ''),
        mainOwnerId: String(form.get('mainOwnerId') ?? ''),
        idempotencyKey,
      })
      setResult(saved)
      setStatus('success')
    } catch (caught) {
      const nextError = caught instanceof PartnerRegistrationApiError
        ? caught
        : new PartnerRegistrationApiError('登録できませんでした。入力内容はそのままなので、もう一度お試しください。', 'unknown')
      setError(nextError)
      setStatus('error')
    }
  }

  const busy = status === 'submitting'
  const locked = busy || status === 'success'
  const ownerMessage = ownersState === 'loading'
    ? '主担当候補を読み込んでいます。'
    : ownersState === 'error'
      ? '主担当候補を取得できません。ページを再読み込みして再試行してください。'
      : !owners.length
        ? '選択できる有効な主担当がありません。'
        : undefined

  return <section className="registration-layout registration-form-layout">
    <form className="panel registration-panel" onSubmit={submit}>
      <div className="section-heading"><h2>取引先情報</h2></div>
      <fieldset className="field-grid registration-fields" disabled={locked}>
        <label>会社名<span className="required">必須</span><input name="name" required placeholder="株式会社〇〇"/></label>
        <label>業種<input name="industry" placeholder="業種"/></label>
        <label>主担当<span className="required">必須</span><select name="mainOwnerId" required value={selectedOwnerId} onChange={event => setMainOwnerId(event.target.value)} disabled={locked || ownersState !== 'ready'}><option value="">主担当を選択</option>{owners.map(owner => <option key={owner.id} value={owner.id}>{owner.name}</option>)}</select></label>
        <label>住所<input name="address" placeholder="都道府県・市区町村"/></label>
        <label>代表電話<input name="phone" placeholder="03-0000-0000"/></label>
      </fieldset>
      {ownerMessage && <p className="form-note" role="status">{ownerMessage}</p>}
      <div className="form-actions"><button type="button" className="secondary-button" onClick={onCancel}>キャンセル</button><button type="submit" className="primary-button" disabled={locked || ownersState !== 'ready' || !owners.length}>{busy ? '登録中…' : status === 'success' ? '登録済み' : '登録'}</button></div>
      {status === 'error' && error && <p className="notice error-notice" role="alert">{error.message}</p>}
      {status === 'success' && result && <p className="notice" role="status">取引先を登録しました。新しい取引先は「未承認」の状態で始まります。</p>}
      {status === 'success' && result && <button type="button" className="secondary-button" onClick={() => onSaved(result.partnerId)}>取引先リストで確認</button>}
    </form>
  </section>
}

export function ContactRegistrationForm({api, parents, parentsState, initialPartnerId, onCancel, onSaved}: {api: ContactLedgerRegistrationApi; parents: ContactRegistrationParent[]; parentsState: ContactRegistrationParentLoadState; initialPartnerId?: string; onCancel: () => void; onSaved: (contactId: string) => void}) {
  const [status, setStatus] = useState<'idle' | 'submitting' | 'success' | 'error'>('idle')
  const [error, setError] = useState<ContactRegistrationApiError>()
  const [result, setResult] = useState<{contactId: string; isReplay: boolean}>()
  // 取引先の画面の「担当者を追加」から開いたときは、その取引先を選んだ状態で始める。
  const [partnerId, setPartnerId] = useState(initialPartnerId ?? '')
  const [idempotencyKey] = useState(() => createContactRegistrationIdempotencyKey())

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (status === 'submitting' || status === 'success') return
    const form = new FormData(event.currentTarget)
    setStatus('submitting')
    setError(undefined)
    try {
      const saved = await api.registerContact({
        partnerId: String(form.get('partnerId') ?? ''),
        name: String(form.get('name') ?? ''),
        statusCode: Number(form.get('statusCode') ?? 100000000) as ContactStatusCode,
        departmentRole: String(form.get('departmentRole') ?? ''),
        email: String(form.get('email') ?? ''),
        phone: String(form.get('phone') ?? ''),
        idempotencyKey,
      })
      setResult(saved)
      setStatus('success')
    } catch (caught) {
      const nextError = caught instanceof ContactRegistrationApiError
        ? caught
        : new ContactRegistrationApiError('登録できませんでした。入力内容はそのままなので、もう一度お試しください。', 'unknown')
      setError(nextError)
      setStatus('error')
    }
  }

  const busy = status === 'submitting'
  const locked = busy || status === 'success'
  const parentMessage = parentsState === 'loading'
    ? '取引先を読み込んでいます…'
    : parentsState === 'error'
      ? '取引先を読み込めませんでした。ページを再読み込みしてください。'
      : !parents.length
        ? '選択できる取引先がありません。'
        : undefined

  return <section className="registration-layout registration-form-layout">
    <form className="panel registration-panel" onSubmit={submit}>
      <div className="section-heading"><h2>担当者情報</h2></div>
      <fieldset className="field-grid registration-fields" disabled={locked}>
        <label>取引先<span className="required">必須</span><select name="partnerId" required value={partnerId} onChange={event => setPartnerId(event.target.value)} disabled={locked || parentsState !== 'ready'}><option value="">取引先を選択</option>{parents.map(parent => <option key={parent.id} value={parent.id}>{parent.name}</option>)}</select></label>
        <label>氏名<span className="required">必須</span><input name="name" required placeholder="氏名"/></label>
        <label>在籍状態<span className="required">必須</span><select name="statusCode" required defaultValue="100000000"><option value="100000000">在籍</option><option value="100000001">休職</option><option value="100000002">退職</option></select></label>
        <label>部署・役職<input name="departmentRole" placeholder="部署・役職"/></label>
        <label>メールアドレス<input name="email" type="email" placeholder="name@example.com"/></label>
        <label>電話番号<input name="phone" type="tel" placeholder="03-0000-0000"/></label>
      </fieldset>
      {parentMessage && <p className="form-note" role="status">{parentMessage}</p>}
      <div className="form-actions"><button type="button" className="secondary-button" onClick={onCancel}>キャンセル</button><button type="submit" className="primary-button" disabled={locked || parentsState !== 'ready' || !parents.length}>{busy ? '登録中…' : status === 'success' ? '登録済み' : '登録'}</button></div>
      {status === 'error' && error && <p className="notice error-notice" role="alert">{error.message}</p>}
      {status === 'success' && result && <p className="notice" role="status">担当者を登録しました。</p>}
      {status === 'success' && result && <button type="button" className="secondary-button" onClick={() => onSaved(result.contactId)}>担当者リストで確認</button>}
    </form>
  </section>
}
