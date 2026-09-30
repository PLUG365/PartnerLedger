import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { BusinessCardReviewReadError, type BusinessCardReviewItem, type BusinessCardReviewReadApi } from './businessCardReview'
import type { BusinessCardOcrFields } from './businessCardOcrResult'
import type { BusinessCardReviewConfirmationResult, BusinessCardReviewConfirmationApi } from './businessCardReviewConfirmation'
import type { BusinessCardFormalRegistrationApi, BusinessCardFormalRegistrationResult } from './businessCardFormalRegistration'
import { dataverseBusinessCardReviewApi } from './dataverseBusinessCardReviewApi'
import { dataverseBusinessCardReviewConfirmationApi } from './dataverseBusinessCardReviewConfirmationApi'
import { initialRegistrationChoice, type CardPartnerCandidate, type CardPartnerSearchApi } from './businessCardPartnerSearch'
import { dataverseCardPartnerSearchApi } from './dataverseCardPartnerSearchApi'

export type BusinessCardRegistrationOwner = {id: string; name: string; email?: string}
export type BusinessCardRegistrationOwnerState = 'idle' | 'loading' | 'ready' | 'error'

type ReviewLoadState =
  | {status: 'loading'}
  | {status: 'ready'; item?: BusinessCardReviewItem}
  | {status: 'error'; message: string}

const fieldLabels: Record<keyof BusinessCardOcrFields, string> = {
  companyName: '会社名',
  fullName: '氏名',
  firstName: '名',
  lastName: '姓',
  department: '部署',
  jobTitle: '役職',
  email: 'メール',
  businessPhone: '代表電話',
  mobilePhone: '携帯電話',
  fax: 'FAX',
  website: 'Webサイト',
  fullAddress: '住所',
  addressPostalCode: '郵便番号',
  addressCountry: '国',
  addressState: '都道府県',
  addressCity: '市区町村',
  addressStreet: '番地',
  addressPostOfficeBox: '私書箱',
}

const fieldOrder: (keyof BusinessCardOcrFields)[] = [
  'companyName',
  'fullName',
  'department',
  'jobTitle',
  'email',
  'businessPhone',
  'mobilePhone',
  'fax',
  'website',
  'fullAddress',
  'addressPostalCode',
  'addressCountry',
  'addressState',
  'addressCity',
  'addressStreet',
  'addressPostOfficeBox',
  'firstName',
  'lastName',
]

function formatDate(value?: string): string {
  if (!value) return '未取得'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '未取得' : date.toLocaleString('ja-JP')
}

function OcrFields({title, fields}: {title: string; fields: BusinessCardOcrFields}) {
  const entries = fieldOrder.filter(key => fields[key] !== undefined).map(key => [key, fields[key]!] as const)
  return <section className="review-fields" aria-label={title}>
    <h3>{title}</h3>
    {entries.length ? <dl className="detail-grid">{entries.map(([key, value]) => <div key={key}><dt>{fieldLabels[key]}</dt><dd>{value}</dd></div>)}</dl> : <p className="empty-state">表示できる項目はありません。</p>}
  </section>
}

type PartnerSearchStatus = 'idle' | 'loading' | 'ready' | 'error'

/** 既存の取引先から追加先を選ぶ欄。候補は利用者が見られる取引先だけ（検索はDataverseの権限で絞られる）。 */
export function ExistingPartnerPicker({query, candidates, selectedId, searching, searchFailed = false, locked, saving, onQueryChange, onSearch, onSelect}: {query: string; candidates: CardPartnerCandidate[]; selectedId: string; searching: boolean; searchFailed?: boolean; locked: boolean; saving: boolean; onQueryChange: (value: string) => void; onSearch: () => void; onSelect: (id: string) => void}) {
  return <div className="existing-partner-picker">
    <div className="partner-search-row"><label>取引先を検索<input value={query} disabled={locked} onChange={event => onQueryChange(event.target.value)}/></label><button className="secondary-button" type="button" disabled={locked || searching || !query.trim()} onClick={onSearch}>{searching ? '検索中…' : '検索'}</button></div>
    {searching && !candidates.length
      ? <p className="caption" role="status">検索しています…</p>
      : candidates.length
      ? <ul className="partner-candidates">{candidates.map(candidate => <li key={candidate.id}><label><input type="radio" name="existing-partner" checked={selectedId === candidate.id} disabled={locked} onChange={() => onSelect(candidate.id)}/><span><b>{candidate.name}</b>{candidate.mainOwnerName ? <small>主担当：{candidate.mainOwnerName}</small> : null}</span></label></li>)}</ul>
      : searchFailed ? null : <p className="caption">該当する取引先はありません。</p>}
    <button className="primary-button" type="submit" disabled={locked || !selectedId}>{saving ? '登録中…' : locked ? '登録済み' : '担当者を追加する'}</button>
  </div>
}

function FormalRegistrationForm({item, api, owners, ownersState, partnerSearchApi, onRegistered}: {item: BusinessCardReviewItem; api: BusinessCardFormalRegistrationApi; owners: BusinessCardRegistrationOwner[]; ownersState: BusinessCardRegistrationOwnerState; partnerSearchApi?: CardPartnerSearchApi; onRegistered?: () => void}) {
  const companyName = typeof item.correctedFields?.companyName === 'string' ? item.correctedFields.companyName : ''
  const searchOnOpen = Boolean(partnerSearchApi && companyName.trim())
  const [mainOwnerId, setMainOwnerId] = useState('')
  const [saving, setSaving] = useState(false)
  const [result, setResult] = useState<BusinessCardFormalRegistrationResult | null>(null)
  // 候補は検索し直している間も残し、検索の状態とは分けて持つ。
  const [searchStatus, setSearchStatus] = useState<PartnerSearchStatus>(searchOnOpen ? 'loading' : 'idle')
  const [candidates, setCandidates] = useState<CardPartnerCandidate[]>([])
  const [searchedOnOpen, setSearchedOnOpen] = useState(!searchOnOpen)
  const [query, setQuery] = useState(companyName)
  const [mode, setMode] = useState<'existing' | 'new'>('new')
  const [partnerId, setPartnerId] = useState('')
  const [manualSearch, setManualSearch] = useState(false)

  const runSearch = useCallback(async (text: string, onOpen: boolean) => {
    if (!partnerSearchApi) return
    setSearchStatus('loading')
    try {
      const found = await partnerSearchApi.search(text)
      setCandidates(found)
      setSearchStatus('ready')
      if (onOpen) {
        const choice = initialRegistrationChoice(found, companyName)
        setMode(choice.mode)
        setPartnerId(choice.partnerId)
      } else if (!found.some(candidate => candidate.id === partnerId)) {
        setPartnerId('')
      }
    } catch {
      setSearchStatus('error')
      if (onOpen) setMode('new')
    } finally {
      if (onOpen) setSearchedOnOpen(true)
    }
  }, [partnerSearchApi, companyName, partnerId])

  useEffect(() => {
    let cancelled = false
    if (!searchOnOpen) return
    void Promise.resolve().then(() => {
      if (!cancelled) void runSearch(companyName, true)
    })
    return () => { cancelled = true }
    // 画面を開いたときに1回だけ探す。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (saving || result?.success) return
    const request = mode === 'existing' ? (partnerId ? {item, existingPartnerId: partnerId} : undefined) : (mainOwnerId ? {item, mainOwnerId} : undefined)
    if (!request) return
    setSaving(true)
    setResult(null)
    try {
      const nextResult = await api.register(request)
      setResult(nextResult)
      if (nextResult.success) onRegistered?.()
    } catch (error) {
      setResult({success: false, kind: 'result-unknown', message: '登録できたか確認できませんでした。再読み込みして確認してください。', error})
    } finally {
      setSaving(false)
    }
  }

  const locked = saving || result?.success === true
  const showChooser = searchedOnOpen && (candidates.length > 0 || manualSearch)
  return <section className="review-formal-registration" aria-label="取引先・担当者として登録">
    <h3>取引先・担当者として登録</h3>
    {!searchedOnOpen && <p className="caption" role="status">同じ会社の取引先を探しています…</p>}
    {searchedOnOpen && searchStatus === 'error' && <p className="warning" role="alert">取引先を検索できませんでした。新しい取引先として登録するか、もう一度検索してください。</p>}
    {showChooser && <div className="registration-destination" role="radiogroup" aria-label="登録先">
      <label><input type="radio" name="registration-destination" checked={mode === 'existing'} disabled={locked} onChange={() => setMode('existing')}/> 既存の取引先に担当者として追加</label>
      <label><input type="radio" name="registration-destination" checked={mode === 'new'} disabled={locked} onChange={() => setMode('new')}/> 新しい取引先として登録</label>
    </div>}
    {searchedOnOpen && partnerSearchApi && !showChooser && <p>{searchStatus === 'ready' ? '見られる取引先に同じ会社は見つかりませんでした。新しい取引先として登録します。' : ''}<button className="link-button" type="button" disabled={locked} onClick={() => { setManualSearch(true); setMode('existing') }}>既存の取引先を名前で探す</button></p>}
    {searchedOnOpen && mode === 'existing' && <form onSubmit={submit}>
      <ExistingPartnerPicker query={query} candidates={candidates} selectedId={partnerId} searching={searchStatus === 'loading'} searchFailed={searchStatus === 'error'} locked={locked} saving={saving} onQueryChange={setQuery} onSearch={() => { setManualSearch(true); void runSearch(query, false) }} onSelect={setPartnerId}/>
    </form>}
    {searchedOnOpen && mode === 'new' && <>
      <p>取引先は「未承認」、担当者は「在籍」で登録します。</p>
      {ownersState === 'loading' && <p className="caption">主担当の候補を読み込んでいます…</p>}
      {ownersState === 'error' && <p className="warning" role="alert">主担当の候補を読み込めませんでした。再読み込みしてください。</p>}
      {ownersState === 'ready' && <form onSubmit={submit}>
        <label>主担当<span className="required">必須</span><select aria-label="主担当" value={mainOwnerId} disabled={locked} onChange={event => setMainOwnerId(event.target.value)}><option value="" disabled>主担当を選択</option>{owners.map(owner => <option key={owner.id} value={owner.id}>{owner.name}{owner.email ? `（${owner.email}）` : ''}</option>)}</select></label>
        <button className="primary-button" type="submit" disabled={locked || !mainOwnerId}>{saving ? '登録中…' : locked ? '登録済み' : '登録する'}</button>
      </form>}
    </>}
    {result && <p className={result.success ? 'success-note' : 'warning'} role="status">{result.success ? (result.addedToExistingPartner ? '選んだ取引先に担当者を追加しました。' : '取引先・担当者を登録しました。') : result.message}</p>}
  </section>
}

function ReviewConfirmationForm({item, onConfirm, formalRegistrationApi, partnerSearchApi, registrationOwners = [], registrationOwnerState = 'idle', onRegistered}: {item: BusinessCardReviewItem; onConfirm?: (fields: BusinessCardOcrFields) => Promise<BusinessCardReviewConfirmationResult>; formalRegistrationApi?: BusinessCardFormalRegistrationApi; partnerSearchApi?: CardPartnerSearchApi; registrationOwners?: BusinessCardRegistrationOwner[]; registrationOwnerState?: BusinessCardRegistrationOwnerState; onRegistered?: () => void}) {
  const editableKeys = fieldOrder.filter(key => item.ocrFields[key] !== undefined || item.correctedFields?.[key] !== undefined || key === 'companyName' || key === 'fullName')
  const [fields, setFields] = useState<BusinessCardOcrFields>(() => Object.fromEntries(editableKeys.map(key => [key, item.correctedFields?.[key] ?? item.ocrFields[key] ?? ''])) as BusinessCardOcrFields)
  const [saving, setSaving] = useState(false)
  const [result, setResult] = useState<BusinessCardReviewConfirmationResult | null>(null)
  const [confirmed, setConfirmed] = useState(item.reviewStatus === '確認確定')
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (saving || confirmed || !onConfirm) return
    setSaving(true)
    try {
      const nextResult = await onConfirm(fields)
      setResult(nextResult)
      if (nextResult.success) setConfirmed(true)
    } catch (error) {
      setResult({success: false, kind: 'result-unknown', message: '保存できたか確認できませんでした。再読み込みして確認してください。', error})
    } finally {
      setSaving(false)
    }
  }
  return <>
    <form className="review-confirmation-form" aria-label="名刺の内容を確定" onSubmit={submit}>
      <h3>内容を確認して確定</h3>
      <p>読み取り結果を確認し、必要なら修正して確定してください。</p>
      <div className="field-grid">{editableKeys.map(key => <label key={key}>{fieldLabels[key]}<input value={fields[key] ?? ''} disabled={saving || confirmed} onChange={event => setFields(current => ({...current, [key]: event.target.value}))}/></label>)}</div>
      <button className="primary-button" type="submit" disabled={saving || confirmed || !onConfirm}>{confirmed ? '確定済み' : saving ? '保存中…' : '確定する'}</button>
      {result && <p className={result.success ? 'success-note' : 'warning'} role="status">{result.success ? '確定しました。続けて取引先・担当者として登録できます。' : result.message}</p>}
    </form>
    {formalRegistrationApi && confirmed && <FormalRegistrationForm item={{...item, reviewStatus: '確認確定', correctedFields: fields}} api={formalRegistrationApi} owners={registrationOwners} ownersState={registrationOwnerState} partnerSearchApi={partnerSearchApi} onRegistered={onRegistered}/>}
  </>
}

export function BusinessCardReviewContent({state, onRetry, onClose, onConfirm, formalRegistrationApi, partnerSearchApi, registrationOwners, registrationOwnerState, onRegistered}: {state: ReviewLoadState; onRetry: () => void; onClose: () => void; onConfirm?: (fields: BusinessCardOcrFields) => Promise<BusinessCardReviewConfirmationResult>; formalRegistrationApi?: BusinessCardFormalRegistrationApi; partnerSearchApi?: CardPartnerSearchApi; registrationOwners?: BusinessCardRegistrationOwner[]; registrationOwnerState?: BusinessCardRegistrationOwnerState; onRegistered?: () => void}) {
  return <section className="panel business-card-review-panel" aria-label="名刺の内容">
    <div className="table-tools home-summary-tools">
      <div><h2>名刺の内容を確認</h2></div>
      <div className="heading-actions"><button className="secondary-button" type="button" onClick={onClose}>閉じる</button></div>
    </div>
    {state.status === 'loading' && <section className="approval-live-state">読み込み中…</section>}
    {state.status === 'error' && <section className="approval-live-state" role="alert"><b>{state.message}</b><button className="secondary-button approval-retry-button" type="button" onClick={onRetry}>再読み込み</button></section>}
    {state.status === 'ready' && !state.item && <p className="empty-state">読み取り結果はまだありません。</p>}
    {state.status === 'ready' && state.item && <>
      <dl className="detail-grid review-summary-grid">
        <div><dt>状態</dt><dd>{state.item.reviewStatus}</dd></div>
        <div><dt>確認日時</dt><dd>{formatDate(state.item.reviewedAt)}</dd></div>
      </dl>
      <OcrFields title="読み取り結果" fields={state.item.ocrFields}/>
      {state.item.correctedFields && !(onConfirm || formalRegistrationApi) && <OcrFields title="確定した内容" fields={state.item.correctedFields}/>}
      {onConfirm || formalRegistrationApi ? <ReviewConfirmationForm key={`${state.item.reviewId}-${state.item.reviewStatus}`} item={state.item} onConfirm={onConfirm} formalRegistrationApi={formalRegistrationApi} partnerSearchApi={partnerSearchApi} registrationOwners={registrationOwners} registrationOwnerState={registrationOwnerState} onRegistered={onRegistered}/> : null}
    </>}
  </section>
}

export function DataverseBusinessCardReview({captureId, api = dataverseBusinessCardReviewApi, confirmationApi = dataverseBusinessCardReviewConfirmationApi, formalRegistrationApi, partnerSearchApi = dataverseCardPartnerSearchApi, registrationOwners = [], registrationOwnerState = 'idle', onRegistered, onClose}: {captureId: string; api?: BusinessCardReviewReadApi; confirmationApi?: BusinessCardReviewConfirmationApi; formalRegistrationApi?: BusinessCardFormalRegistrationApi; partnerSearchApi?: CardPartnerSearchApi; registrationOwners?: BusinessCardRegistrationOwner[]; registrationOwnerState?: BusinessCardRegistrationOwnerState; onRegistered?: () => void; onClose: () => void}) {
  const [retrySequence, setRetrySequence] = useState(0)
  const [state, setState] = useState<ReviewLoadState>({status: 'loading'})

  useEffect(() => {
    let cancelled = false
    void api.getReviewForCapture(captureId)
      .then(item => { if (!cancelled) setState({status: 'ready', item}) })
      .catch(error => {
        if (cancelled) return
        const message = error instanceof BusinessCardReviewReadError && error.code === 'invalid-response'
          ? '名刺の内容を読み込めませんでした。'
          : '名刺の内容を読み込めませんでした。'
        setState({status: 'error', message})
      })
    return () => { cancelled = true }
  }, [api, captureId, retrySequence])

  const retry = () => {
    setState({status: 'loading'})
    setRetrySequence(sequence => sequence + 1)
  }
  const confirm = async (fields: BusinessCardOcrFields): Promise<BusinessCardReviewConfirmationResult> => {
    const current = await api.getReviewForCapture(captureId)
    if (!current) return {success: false, kind: 'blocked', message: '確認版が見つからないため、保存しません。'}
    return confirmationApi.confirm({item: current, correctedFields: fields})
  }
  return <BusinessCardReviewContent state={state} onRetry={retry} onClose={onClose} onConfirm={confirmationApi ? confirm : undefined} formalRegistrationApi={formalRegistrationApi} partnerSearchApi={partnerSearchApi} registrationOwners={registrationOwners} registrationOwnerState={registrationOwnerState} onRegistered={onRegistered}/>
}
