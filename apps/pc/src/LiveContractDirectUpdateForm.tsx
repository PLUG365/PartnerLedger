import { useState } from 'react'
import type { PartnerDirectoryItem } from './partnerDirectory'
import type { ContractReadItem } from './contractReadModel'
import { ContractDirectUpdateError, type ContractDirectUpdateApi, type ContractDirectUpdateDecision } from './contractDirectUpdate'

function dateInputValue(value?: string): string {
  return value?.slice(0, 10) ?? ''
}

function linkInputValue(value?: string): string {
  return value && value !== '未登録' ? value : ''
}

export function LiveContractDirectUpdateForm({partner, contract, api, onCancel, onSaved}: {
  partner: PartnerDirectoryItem
  contract: ContractReadItem
  api: ContractDirectUpdateApi
  onCancel: () => void
  onSaved?: (result: {contractId: string}) => void
}) {
  const [name, setName] = useState(contract.name)
  const [decision, setDecision] = useState<ContractDirectUpdateDecision>('更新')
  const [endDate, setEndDate] = useState(dateInputValue(contract.endDate))
  const [noticeDate, setNoticeDate] = useState(dateInputValue(contract.noticeDate))
  const [decisionDate, setDecisionDate] = useState(dateInputValue(contract.decisionDate))
  const [autoRenew, setAutoRenew] = useState(contract.autoRenew ?? false)
  const [link, setLink] = useState(linkInputValue(contract.link))
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const [message, setMessage] = useState('')

  const save = async () => {
    if (status === 'saving' || status === 'saved') return
    setStatus('saving')
    setMessage('')
    try {
      const result = await api.updateContract({
        contract,
        name,
        decision,
        endDate,
        noticeDate,
        decisionDate,
        autoRenew,
        link,
      })
      setStatus('saved')
      setMessage('契約を更新しました。')
      onSaved?.(result)
    } catch (error) {
      setStatus('error')
      setMessage(error instanceof ContractDirectUpdateError
        ? error.message
        : '契約を更新できませんでした。もう一度お試しください。')
    }
  }

  const locked = status === 'saving' || status === 'saved'

  return <section className="approval-draft-editor contract-approval-draft-editor" aria-label="契約直接更新">
    <div className="approval-draft-editor-heading">
      <div><h3>更新・終了判断を登録</h3><p>{partner.name} ／ {contract.name}</p></div>
    </div>
    {message && <p className={status === 'saved' ? 'approval-editor-success' : 'approval-editor-error'} role={status === 'saved' ? 'status' : 'alert'}>{message}</p>}
    <form className="approval-draft-form" onSubmit={event => { event.preventDefault(); void save() }}>
      <fieldset disabled={locked}>
        <div className="field-grid approval-editor-fields">
          <label>契約名<span className="required">必須</span><input required maxLength={200} value={name} onChange={event => setName(event.target.value)} /></label>
          <label>判断<span className="required">必須</span><select required value={decision} onChange={event => setDecision(event.target.value as ContractDirectUpdateDecision)}><option value="更新">更新（締結済み）</option><option value="終了">終了</option></select></label>
          <label>契約終了日<input type="date" value={endDate} onChange={event => setEndDate(event.target.value)} /></label>
          <label>解約通知期限<input type="date" value={noticeDate} onChange={event => setNoticeDate(event.target.value)} /></label>
          <label>更新判断期限<input type="date" value={decisionDate} onChange={event => setDecisionDate(event.target.value)} /></label>
          <label className="checkbox-field"><input type="checkbox" checked={autoRenew} onChange={event => setAutoRenew(event.target.checked)} /> 自動更新あり</label>
          <label>契約書リンク<input type="url" maxLength={200} value={link} onChange={event => setLink(event.target.value)} placeholder="契約書・関連ファイルのリンク" /></label>
        </div>
      </fieldset>
      <div className="approval-editor-actions"><button type="button" className="secondary-button" onClick={onCancel} disabled={status === 'saving'}>契約一覧へ戻る</button><button type="submit" className="primary-button" disabled={locked}>{status === 'saving' ? '保存中…' : status === 'saved' ? '保存済み' : '保存'}</button></div>
    </form>
  </section>
}
