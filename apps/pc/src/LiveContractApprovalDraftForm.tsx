import { useState } from 'react'
import type { PartnerDirectoryItem } from './partnerDirectory'
import type { ContractReadItem } from './contractReadModel'
import { ContractApprovalDraftError, ContractApprovalDraftSaveError, createContractApprovalRequestKey, saveContractApprovalDraft, type ContractApprovalDecision } from './contractApprovalDraft'
import { StandardApprovalWriteError, type StandardApprovalWriteApi } from './standardApprovalTransitionApi'

function dateInputValue(value?: string): string {
  return value?.slice(0, 10) ?? ''
}

function linkInputValue(value?: string): string {
  return value && value !== '未登録' ? value : ''
}

export function LiveContractApprovalDraftForm({partner, contract, api, onCancel, onSaved}: {
  partner: PartnerDirectoryItem
  contract: ContractReadItem
  api: StandardApprovalWriteApi
  onCancel: () => void
  onSaved?: (result: {requestId: string; submissionVersionId: string}) => void
}) {
  const [name, setName] = useState(`${contract.name} 更新・終了判断`)
  const [decision, setDecision] = useState<ContractApprovalDecision>('更新')
  const [endDate, setEndDate] = useState(dateInputValue(contract.endDate))
  const [noticeDate, setNoticeDate] = useState(dateInputValue(contract.noticeDate))
  const [decisionDate, setDecisionDate] = useState(dateInputValue(contract.decisionDate))
  const [autoRenew, setAutoRenew] = useState(contract.autoRenew ?? false)
  const [link, setLink] = useState(linkInputValue(contract.link))
  const [requestId, setRequestId] = useState<string>()
  const [idempotencyKey] = useState(createContractApprovalRequestKey)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const [submissionUncertain, setSubmissionUncertain] = useState(false)
  const [message, setMessage] = useState('')

  const save = async () => {
    if (status === 'saving' || status === 'saved' || submissionUncertain) return

    setStatus('saving')
    setMessage('')
    let savedResult: {requestId: string; submissionVersionId: string} | undefined
    try {
      const result = await saveContractApprovalDraft(api, {
        contract,
        name,
        decision,
        endDate,
        noticeDate,
        decisionDate,
        autoRenew,
        link,
      }, {
        idempotencyKey,
        requestId,
      })
      setRequestId(result.requestId)
      savedResult = result
      try {
        await api.submit({requestId: result.requestId})
        setMessage('申請しました。承認されると契約に反映されます。')
      } catch {
        setMessage('申請を保存しましたが、提出できませんでした。「承認申請」画面から提出してください。')
      }
      setStatus('saved')
    } catch (error) {
      if (error instanceof ContractApprovalDraftSaveError && error.phase === 'submission') {
        if (error.requestId) setRequestId(error.requestId)
        setSubmissionUncertain(true)
        setMessage('申請を保存できたか確認できませんでした。「承認申請」画面で確認してください。')
      } else if (error instanceof ContractApprovalDraftSaveError) {
        const original = error.original
        setMessage(original instanceof ContractApprovalDraftError || original instanceof StandardApprovalWriteError ? original.message : error.message)
      } else {
        setMessage(error instanceof ContractApprovalDraftError || error instanceof StandardApprovalWriteError
          ? error.message
          : '申請を保存できませんでした。もう一度お試しください。')
      }
      setStatus('error')
    }
    if (savedResult) onSaved?.(savedResult)
  }

  const locked = status === 'saving' || status === 'saved' || submissionUncertain

  return <section className="approval-draft-editor contract-approval-draft-editor" aria-label="契約の更新・終了判断を申請">
    <div className="approval-draft-editor-heading">
      <div><h3>更新・終了判断を申請</h3><p>{partner.name} ／ {contract.name}</p></div>
    </div>
    {message && <p className={status === 'saved' ? 'approval-editor-success' : 'approval-editor-error'} role={status === 'saved' ? 'status' : 'alert'}>{message}</p>}
    <form className="approval-draft-form" onSubmit={event => { event.preventDefault(); void save() }}>
      <fieldset disabled={locked}>
        <div className="field-grid approval-editor-fields">
          <label>申請名<span className="required">必須</span><input required maxLength={200} value={name} onChange={event => setName(event.target.value)} /></label>
          <label>判断<span className="required">必須</span><select required value={decision} onChange={event => setDecision(event.target.value as ContractApprovalDecision)}><option value="更新">更新（締結済み）</option><option value="終了">終了</option></select></label>
          <label>契約終了日<input type="date" value={endDate} onChange={event => setEndDate(event.target.value)} /></label>
          <label>解約通知期限<input type="date" value={noticeDate} onChange={event => setNoticeDate(event.target.value)} /></label>
          <label>更新判断期限<input type="date" value={decisionDate} onChange={event => setDecisionDate(event.target.value)} /></label>
          <label className="checkbox-field"><input type="checkbox" checked={autoRenew} onChange={event => setAutoRenew(event.target.checked)} /> 自動更新あり</label>
          <label>契約書リンク<input type="url" maxLength={200} value={link} onChange={event => setLink(event.target.value)} placeholder="契約書・関連ファイルのリンク" /></label>
        </div>
      </fieldset>
      <p className="form-note">この変更は承認が必要です。承認されると契約に反映されます。</p>
      <div className="approval-editor-actions"><button type="button" className="secondary-button" onClick={onCancel} disabled={status === 'saving'}>契約一覧へ戻る</button><button type="submit" className="primary-button" disabled={locked}>{status === 'saving' ? '申請中…' : status === 'saved' ? '申請済み' : '申請する'}</button></div>
    </form>
  </section>
}
