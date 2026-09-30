import { useState, type FormEvent } from 'react'
import { approvalCancellationAction, type ApprovalActor } from './approvalActor'
import type { ApprovalInboxItem, ApprovalReadLoadState } from './approvalReadModel'
import { approvalRequestTypeLabels, approvalStatusClass, changeAttributeLabel, choiceLabels, dateAttributes, filterLabels, formatApprovalDate, formatChangeValue, parseChangeSet, saveResubmissionDraft, sortApprovalItems, statusGroup, statusReasonLabel, writeErrorMessage, type ApprovalFilter, type ChangeValue, type ParsedChangeSet } from './approvalPresentation'
import type { StandardApprovalWriteApi } from './standardApprovalTransitionApi'
import { RefreshButton, SearchField } from './TableControls'

type ApprovalUser = {id: string; name: string}

function userNameMap(users: ApprovalUser[]): Record<string, string> {
  return Object.fromEntries(users.map(user => [user.id.toLowerCase(), user.name]))
}

function ChangeSummary({changeSet, users = []}: {changeSet: ParsedChangeSet; users?: ApprovalUser[]}) {
  const names = userNameMap(users)
  return <dl className="detail-grid approval-change-summary">{changeSet.changes.map(change => <div key={change.attribute}><dt>{changeAttributeLabel(changeSet.target.entity, change.attribute)}</dt><dd>{formatChangeValue(change.attribute, change.value, names)}</dd></div>)}</dl>
}

function ResubmitForm({item, changeSet, api, users = [], onCancel, onDone}: {item: ApprovalInboxItem; changeSet: ParsedChangeSet; api: StandardApprovalWriteApi; users?: ApprovalUser[]; onCancel: () => void; onDone: (message: string) => void}) {
  const [values, setValues] = useState<Record<string, ChangeValue>>(() => Object.fromEntries(changeSet.changes.map(change => [change.attribute, change.value])))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const set = (attribute: string, value: ChangeValue) => setValues(current => ({...current, [attribute]: value}))

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (saving) return
    const changes = changeSet.changes.map(change => {
      const value = values[change.attribute]
      if (typeof value === 'string') {
        const trimmed = value.trim()
        if (dateAttributes.has(change.attribute)) return {attribute: change.attribute, value: trimmed ? `${trimmed.slice(0, 10)}T00:00:00.000Z` : null}
        return {attribute: change.attribute, value: trimmed || null}
      }
      return {attribute: change.attribute, value}
    })
    if (changes.some(change => change.attribute === 'pl_name' && !change.value)) {
      setError(`${changeAttributeLabel(changeSet.target.entity, 'pl_name')}を入力してください。`)
      return
    }
    setSaving(true)
    setError('')
    const changeSetJson = JSON.stringify({...changeSet, changes})
    try {
      await saveResubmissionDraft(api, item, changeSetJson)
    } catch (caught) {
      setError(writeErrorMessage(caught, '修正内容を保存できませんでした。もう一度お試しください。'))
      setSaving(false)
      return
    }
    try {
      await api.submit({requestId: item.request.id})
      onDone('修正して再提出しました。')
    } catch {
      onDone('修正内容を保存しましたが、提出できませんでした。「提出する」から提出してください。')
    }
  }

  return <form className="approval-resubmit-form" onSubmit={event => void submit(event)}>
    <h4>内容を修正して再提出</h4>
    <fieldset className="field-grid registration-fields" disabled={saving}>
      {changeSet.changes.map(change => {
        const label = changeAttributeLabel(changeSet.target.entity, change.attribute)
        const value = values[change.attribute]
        // 主担当は利用者の一覧から選び直す（2026-09-29）。
        if (change.attribute === 'pl_mainownerlookup') return <label key={change.attribute}>{label}<span className="required">必須</span><select required value={typeof value === 'string' ? value.toLowerCase() : ''} onChange={event => set(change.attribute, event.target.value)}><option value="">新しい主担当を選択</option>{users.map(user => <option key={user.id} value={user.id.toLowerCase()}>{user.name}</option>)}</select></label>
        if (choiceLabels[change.attribute]) return <label key={change.attribute}>{label}<select value={typeof value === 'number' ? String(value) : ''} onChange={event => set(change.attribute, Number(event.target.value))}>{Object.entries(choiceLabels[change.attribute]).map(([code, text]) => <option key={code} value={code}>{text}</option>)}</select></label>
        if (typeof change.value === 'boolean') return <label key={change.attribute} className="checkbox-field"><input type="checkbox" checked={value === true} onChange={event => set(change.attribute, event.target.checked)}/> {label}</label>
        if (dateAttributes.has(change.attribute)) return <label key={change.attribute}>{label}<input type="date" value={typeof value === 'string' ? value.slice(0, 10) : ''} onChange={event => set(change.attribute, event.target.value)}/></label>
        return <label key={change.attribute}>{label}{change.attribute === 'pl_name' && <span className="required">必須</span>}<input maxLength={200} value={typeof value === 'string' ? value : ''} onChange={event => set(change.attribute, event.target.value)}/></label>
      })}
    </fieldset>
    {error && <p className="approval-editor-error" role="alert">{error}</p>}
    <div className="form-actions"><button type="button" className="secondary-button" onClick={onCancel} disabled={saving}>キャンセル</button><button type="submit" className="primary-button" disabled={saving}>{saving ? '再提出中…' : '再提出する'}</button></div>
  </form>
}

const cancellationLabels = {
  withdraw: {open: '取り下げる', confirm: '申請を取り下げる', busy: '取下げ中…', reason: '取り下げの理由（任意）', done: '申請を取り下げました。同じ対象について新しく申請できます。'},
  'administrator-withdraw': {open: '取り消す', confirm: '申請を取り消す', busy: '取消中…', reason: '取消の理由', done: '申請を取り消しました。'},
  'administrator-cancel': {open: '取り消す', confirm: '承認申請を取り消す', busy: '取消中…', reason: '取消の理由', done: '承認申請を取り消しました。承認者に届いた承認依頼は残りますが、応答しても反映されません。'},
} as const

export function ApprovalRequestCard({item, api, actor, users = [], onChanged}: {item: ApprovalInboxItem; api?: StandardApprovalWriteApi; actor?: ApprovalActor; users?: ApprovalUser[]; onChanged?: (message: string) => void}) {
  const {request, latestSubmission, draftSubmission} = item
  const changeSet = parseChangeSet((draftSubmission ?? latestSubmission)?.changeSetJson)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [resubmitting, setResubmitting] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [reason, setReason] = useState('')
  const canSubmitDraft = Boolean(api && (request.status === '下書き' || request.status === '差戻し') && draftSubmission)
  const canResubmit = Boolean(api && request.status === '差戻し' && changeSet)
  const cancellation = api?.cancel ? approvalCancellationAction(request, actor) : undefined
  const labels = cancellation ? cancellationLabels[cancellation.kind] : undefined
  const reasonMissing = Boolean(cancellation?.reasonRequired && !reason.trim())

  const submitDraft = async () => {
    if (!api || busy) return
    setBusy(true)
    setError('')
    try {
      await api.submit({requestId: request.id})
      onChanged?.('提出しました。承認者に通知されます。')
    } catch (caught) {
      setError(writeErrorMessage(caught, '提出できませんでした。もう一度お試しください。'))
      setBusy(false)
    }
  }

  const cancel = async () => {
    if (!api?.cancel || !labels || busy || reasonMissing) return
    setBusy(true)
    setError('')
    try {
      await api.cancel({requestId: request.id, reason})
      onChanged?.(labels.done)
    } catch (caught) {
      setError(writeErrorMessage(caught, '取り消せませんでした。申請の状態を確認してください。'))
      setBusy(false)
    }
  }

  return <article className="approval-card">
    <div className="approval-card-heading">
      <div><span className={'state ' + approvalStatusClass(request.status)}><i/>{request.status}</span><h3>{request.name}</h3><p>{approvalRequestTypeLabels[request.requestType] ?? request.requestType}{request.partnerName ? ` · ${request.partnerName}` : ''}</p></div>
      <div className="approval-card-actions">
        {canSubmitDraft && <button className="primary-button" type="button" disabled={busy} onClick={() => void submitDraft()}>{busy ? '提出中…' : '提出する'}</button>}
        {canResubmit && !resubmitting && <button className="primary-button" type="button" onClick={() => setResubmitting(true)}>修正して再提出</button>}
        {labels && !cancelOpen && <button className="secondary-button" type="button" onClick={() => setCancelOpen(true)}>{labels.open}</button>}
      </div>
    </div>
    <p className="approval-meta"><span>申請者：{request.requestingUserName ?? '—'}</span><span>申請日時：{formatApprovalDate(request.requestedAt)}</span>{latestSubmission?.versionNumber !== undefined && <span>提出：{latestSubmission.versionNumber}回目</span>}</p>
    {changeSet && <div className="approval-change"><b>{draftSubmission && request.status === '差戻し' ? '変更内容（修正後・未提出）' : '変更内容'}</b><ChangeSummary users={users} changeSet={changeSet}/></div>}
    {resubmitting && changeSet && api && <ResubmitForm item={item} changeSet={changeSet} api={api} users={users} onCancel={() => setResubmitting(false)} onDone={message => { setResubmitting(false); onChanged?.(message) }}/>}
    {cancelOpen && labels && <div className="approval-cancellation"><label>{labels.reason}<textarea rows={3} maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} placeholder={cancellation?.kind === 'administrator-cancel' ? '例：承認者が休職中のため' : undefined}/></label><div className="form-actions"><button className="secondary-button" type="button" disabled={busy} onClick={() => setCancelOpen(false)}>閉じる</button><button className="secondary-button danger-button" type="button" disabled={reasonMissing || busy} onClick={() => void cancel()}>{busy ? labels.busy : labels.confirm}</button></div></div>}
    {error && <p className="approval-editor-error" role="alert">{error}</p>}
    {request.cancellationReason && <p className="caption">{statusReasonLabel(request.status)}：{request.cancellationReason}</p>}
  </article>
}

export function ApprovalCenterView({state, writeApi, actor, focusRequestId, users = [], onRefresh}: {state: ApprovalReadLoadState; writeApi?: StandardApprovalWriteApi; actor?: ApprovalActor; focusRequestId?: string; users?: ApprovalUser[]; onRefresh: () => void}) {
  const [filter, setFilter] = useState<ApprovalFilter>('all')
  const [message, setMessage] = useState('')
  const [query, setQuery] = useState('')
  const needle = query.trim().toLocaleLowerCase('ja-JP')
  const items = (state.status === 'ready' ? sortApprovalItems(state.items) : [])
    .filter(item => !needle || [item.request.name, item.request.partnerName, item.request.requestingUserName, approvalRequestTypeLabels[item.request.requestType]].some(value => value?.toLocaleLowerCase('ja-JP').includes(needle)))
  const counts: Record<ApprovalFilter, number> = {
    action: items.filter(item => statusGroup(item.request.status) === 'action').length,
    pending: items.filter(item => statusGroup(item.request.status) === 'pending').length,
    done: items.filter(item => statusGroup(item.request.status) === 'done').length,
    all: items.length,
  }
  // 承認依頼のリンク（requestId）から開いたときは、その申請を一覧の上に出す。閲覧できるかはDataverseの権限のまま。
  const focused = state.status === 'ready' && focusRequestId ? state.items.find(item => item.request.id.toLowerCase() === focusRequestId.toLowerCase()) : undefined
  const listed = (filter === 'all' ? items : items.filter(item => statusGroup(item.request.status) === filter)).filter(item => item !== focused)
  const handleChanged = (next: string) => { setMessage(next); onRefresh() }

  return <section className="panel table-panel approval-list-panel" aria-label="承認申請">
      <div className="table-tools"><div><h2>承認申請 <span>{state.status === 'ready' ? listed.length + (focused ? 1 : 0) : '…'}</span></h2></div><div className="table-filters"><SearchField label="承認申請を検索" placeholder="申請名・取引先・申請者で検索" value={query} onChange={setQuery}/><RefreshButton onClick={onRefresh} disabled={state.status === 'loading'}/></div></div>
      {state.status === 'ready' && <div className="detail-tabs panel-tabs approval-filters" aria-label="申請の絞り込み">{(Object.keys(filterLabels) as ApprovalFilter[]).map(key => <button key={key} type="button" aria-pressed={filter === key} onClick={() => setFilter(key)}>{filterLabels[key]} <span>{counts[key]}</span></button>)}</div>}
      {message && <p className="approval-editor-success" role="status">{message}</p>}
      {state.status === 'ready' && state.submissionSummaryUnavailable && <p className="form-note" role="status">変更内容を読み込めなかったため、申請の一覧だけを表示しています。</p>}
      {(state.status === 'idle' || state.status === 'loading') && <p className="empty-state" role="status">読み込み中…</p>}
      {state.status === 'error' && <p className="empty-state" role="alert">{state.message}</p>}
      {state.status === 'ready' && focusRequestId && (focused
        ? <div className="approval-focus"><p className="form-note">承認依頼のリンクから開いた申請</p><ApprovalRequestCard item={focused} api={writeApi} actor={actor} users={users} onChanged={handleChanged}/></div>
        : <p className="form-note" role="status">リンクの申請が見つかりませんでした。閲覧できる権限がないか、申請が無くなっている可能性があります。</p>)}
      {state.status === 'ready' && <div className="approval-list">{listed.map(item => <ApprovalRequestCard key={item.request.id} item={item} api={writeApi} actor={actor} users={users} onChanged={handleChanged}/>)}{!listed.length && <p className="empty-state">該当する申請はありません。</p>}{filter === 'pending' && <p className="form-note">承認はTeams・Outlookの承認依頼から行います。</p>}</div>}
    </section>
}
