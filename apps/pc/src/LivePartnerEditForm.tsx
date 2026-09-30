import { useState } from 'react'
import type { PartnerDirectoryItem } from './partnerDirectory'
import { PartnerDirectUpdateError, type PartnerDirectUpdateApi, type PartnerEditTarget } from './partnerDirectUpdate'
import { PartnerApprovalDraftError, PartnerApprovalDraftSaveError, createPartnerApprovalRequestKey, isReasonedSubmitFailure, savePartnerApprovalDraft, submitFailureMessage } from './partnerApprovalDraft'
import { StandardApprovalWriteError, type StandardApprovalWriteApi } from './standardApprovalTransitionApi'
import type { ContractApprovalPolicy } from './contractApprovalPolicy'

const labels: Record<PartnerEditTarget, string> = {
  companyName: '会社名',
  tradingStatus: '取引状態',
  address: '住所',
  phone: '代表電話',
  mainOwner: '主担当',
}

function initialValue(partner: PartnerDirectoryItem, target: PartnerEditTarget): string {
  if (target === 'companyName') return partner.name
  if (target === 'tradingStatus') return partner.tradingStatus
  if (target === 'address') return partner.address ?? ''
  if (target === 'mainOwner') return ''
  return partner.phone ?? ''
}

export function LivePartnerEditForm({partner, target, owners = [], policy, updateApi, approvalApi, onCancel, onUpdated}: {
  partner: PartnerDirectoryItem
  target: PartnerEditTarget
  /** 主担当の候補（通常の有効な利用者）。主担当の変更のときだけ使う。 */
  owners?: {id: string; name: string}[]
  policy: ContractApprovalPolicy
  updateApi: PartnerDirectUpdateApi
  approvalApi: StandardApprovalWriteApi
  onCancel: () => void
  onUpdated?: (partnerId: string) => void
}) {
  const [value, setValue] = useState(initialValue(partner, target))
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const [requestId, setRequestId] = useState<string>()
  const [uncertain, setUncertain] = useState(false)
  const [message, setMessage] = useState('')
  const [submitRejected, setSubmitRejected] = useState(false)
  const requiresApproval = policy[target]
  const label = labels[target]
  const [idempotencyKey] = useState(() => createPartnerApprovalRequestKey(target))

  const save = async () => {
    if (status === 'saving' || status === 'saved' || uncertain) return
    setStatus('saving')
    setMessage('')
    try {
      if (requiresApproval) {
        const result = await savePartnerApprovalDraft(approvalApi, {partner, target, value}, {idempotencyKey, requestId})
        setRequestId(result.requestId)
        try {
          await approvalApi.submit({requestId: result.requestId})
        } catch (error) {
          setStatus('saved')
          setSubmitRejected(isReasonedSubmitFailure(error))
          setMessage(submitFailureMessage(error))
          return
        }
        setStatus('saved')
        setMessage('申請しました。承認されると反映されます。')
      } else {
        const result = await updateApi.updatePartner({partner, target, value})
        setStatus('saved')
        setMessage('更新しました。')
        onUpdated?.(result.partnerId)
      }
    } catch (error) {
      if (error instanceof PartnerApprovalDraftSaveError && error.phase === 'submission') {
        if (error.requestId) setRequestId(error.requestId)
        setUncertain(true)
        setMessage('申請を保存できたか確認できませんでした。「承認申請」画面で確認してください。')
      } else {
        setMessage(error instanceof PartnerDirectUpdateError || error instanceof PartnerApprovalDraftError || error instanceof PartnerApprovalDraftSaveError || error instanceof StandardApprovalWriteError
          ? error.message
          : '保存できませんでした。もう一度お試しください。')
      }
      setStatus('error')
    }
  }

  const locked = status === 'saving' || status === 'saved' || uncertain
  return <section className="approval-draft-editor partner-approval-draft-editor" aria-label={`${label}${requiresApproval ? '変更申請' : '編集'}`}>
    <div className="approval-draft-editor-heading"><div><h3>{label}{requiresApproval ? 'の変更を申請' : 'を編集'}</h3><p>{partner.name}</p></div></div>
    {message && <p className={status === 'saved' && !submitRejected ? 'approval-editor-success' : 'approval-editor-error'} role={status === 'saved' && !submitRejected ? 'status' : 'alert'}>{message}</p>}
    <form className="approval-draft-form" onSubmit={event => { event.preventDefault(); void save() }}>
      <fieldset disabled={locked} className="field-grid approval-editor-fields">
        <label>{label}<span className="required">{target === 'companyName' || target === 'mainOwner' ? '必須' : '任意'}</span>{target === 'mainOwner' ? <select required value={value} onChange={event => setValue(event.target.value)}><option value="">新しい主担当を選択</option>{owners.filter(owner => owner.id.toLowerCase() !== partner.mainOwnerId?.toLowerCase()).map(owner => <option key={owner.id} value={owner.id}>{owner.name}</option>)}</select> : target === 'tradingStatus' ? <select required value={value} onChange={event => setValue(event.target.value)}><option value="未承認">未承認</option><option value="取引中">取引中</option><option value="休止中">休止中</option><option value="終了">終了</option></select> : <input required={target === 'companyName'} maxLength={200} value={value} onChange={event => setValue(event.target.value)} placeholder={target === 'phone' ? '03-0000-0000' : undefined}/>}</label>
      </fieldset>
      {target === 'mainOwner' && <p className="form-note">新しい主担当には、この取引先の編集の共有が自動で付きます。今の主担当（{partner.mainOwnerName ?? '未設定'}）の共有はそのまま残ります。</p>}
      {requiresApproval && <p className="form-note">この変更は承認が必要です。承認されると反映されます。</p>}
      <div className="approval-editor-actions"><button type="button" className="secondary-button" onClick={onCancel} disabled={status === 'saving'}>戻る</button><button type="submit" className="primary-button" disabled={locked}>{status === 'saving' ? (requiresApproval ? '申請中…' : '保存中…') : status === 'saved' ? (requiresApproval ? '申請済み' : '保存済み') : requiresApproval ? '申請する' : '保存'}</button></div>
    </form>
  </section>
}
