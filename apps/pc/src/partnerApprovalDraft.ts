import { isDataverseGuid } from './dataverseIds'
import type { PartnerDirectoryItem } from './partnerDirectory'
import { StandardApprovalWriteError, type StandardApprovalWriteApi } from './standardApprovalTransitionApi'
import type { PartnerEditTarget } from './partnerDirectUpdate'

export type PartnerApprovalDraftInput = {
  partner: PartnerDirectoryItem
  target: PartnerEditTarget
  value: string
}

export class PartnerApprovalDraftError extends Error {
  readonly code: 'required' | 'invalid-guid' | 'invalid-input' | 'too-long'

  constructor(message: string, code: PartnerApprovalDraftError['code']) {
    super(message)
    this.name = 'PartnerApprovalDraftError'
    this.code = code
  }
}

export class PartnerApprovalDraftSaveError extends Error {
  readonly phase: 'request' | 'submission'
  readonly requestId?: string
  readonly original: unknown

  constructor(message: string, phase: 'request' | 'submission', original: unknown, requestId?: string) {
    super(message)
    this.name = 'PartnerApprovalDraftSaveError'
    this.phase = phase
    this.requestId = requestId
    this.original = original
  }
}

const requestTypes: Record<PartnerEditTarget, string> = {
  companyName: 'partner.company-name',
  tradingStatus: 'partner.trading-status',
  address: 'partner.address',
  phone: 'partner.phone',
  mainOwner: 'partner.main-owner',
}

const attributes: Record<PartnerEditTarget, string> = {
  companyName: 'pl_name',
  tradingStatus: 'pl_tradingstatuscode',
  address: 'pl_address',
  phone: 'pl_phone',
  mainOwner: 'pl_mainownerlookup',
}

const tradingStatusCode: Record<string, number> = {
  '未承認': 100000000,
  '取引中': 100000001,
  '休止中': 100000002,
  '終了': 100000003,
}

let keySequence = 0

export function createPartnerApprovalRequestKey(target: PartnerEditTarget): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `Partner-${target}-${uuid}`
  keySequence += 1
  return `Partner-${target}-${Date.now().toString(36)}-${keySequence.toString(36)}`
}

function requirePartnerId(partner: PartnerDirectoryItem): string {
  if (!isDataverseGuid(partner.id)) throw new PartnerApprovalDraftError('対象の取引先が正しくありません。', 'invalid-guid')
  return partner.id
}

function normalizeValue(input: PartnerApprovalDraftInput): string | number | null {
  if (typeof input.value !== 'string') throw new PartnerApprovalDraftError('変更値が不正です。', 'invalid-input')
  const value = input.value.trim()
  if (input.target === 'tradingStatus') {
    const code = tradingStatusCode[value]
    if (code === undefined) throw new PartnerApprovalDraftError('取引状態が不正です。', 'invalid-input')
    return code
  }
  if (input.target === 'mainOwner') {
    // 値は新しい主担当のsystemuserid。サーバーが通常の利用者か・今と違う人かを確かめる（2026-09-29）。
    if (!isDataverseGuid(value)) throw new PartnerApprovalDraftError('主担当を選択してください。', 'required')
    return value.toLowerCase()
  }
  if (input.target === 'companyName' && !value) throw new PartnerApprovalDraftError('会社名を指定してください。', 'required')
  if (value.length > 200) throw new PartnerApprovalDraftError('変更値は200文字以内で指定してください。', 'too-long')
  return value || null
}

export function buildPartnerApprovalChangeSetJson(input: PartnerApprovalDraftInput): string {
  const partnerId = requirePartnerId(input.partner)
  return JSON.stringify({
    schemaVersion: 1,
    target: {entity: 'pl_partner', id: partnerId},
    changes: [{attribute: attributes[input.target], value: normalizeValue(input)}],
  })
}

export async function savePartnerApprovalDraft(
  api: Pick<StandardApprovalWriteApi, 'createRequest' | 'createSubmissionDraft'>,
  input: PartnerApprovalDraftInput,
  options: {idempotencyKey: string; requestId?: string},
): Promise<{requestId: string; submissionVersionId: string}> {
  const changeSetJson = buildPartnerApprovalChangeSetJson(input)
  const partnerId = requirePartnerId(input.partner)
  let requestId = options.requestId
  if (!requestId) {
    try {
      const result = await api.createRequest({
        name: `${input.partner.name} ${input.target === 'companyName' ? '会社名' : input.target === 'tradingStatus' ? '取引状態' : input.target === 'address' ? '住所' : input.target === 'mainOwner' ? '主担当' : '代表電話'}変更`,
        requestTypeCode: requestTypes[input.target],
        partnerId,
        idempotencyKey: options.idempotencyKey,
      })
      if (!isDataverseGuid(result.requestId)) throw new Error('申請IDが不正です。')
      requestId = result.requestId
    } catch (error) {
      throw new PartnerApprovalDraftSaveError('申請を保存できませんでした。もう一度お試しください。', 'request', error)
    }
  }
  try {
    const result = await api.createSubmissionDraft({requestId, name: `${input.partner.name} 変更提出版`, changeSetJson})
    if (!isDataverseGuid(result.submissionVersionId)) throw new Error('提出版IDが不正です。')
    return {requestId, submissionVersionId: result.submissionVersionId}
  } catch (error) {
    throw new PartnerApprovalDraftSaveError('提出版の保存結果を確認できません。重複作成を避けるため再送しません。', 'submission', error, requestId)
  }
}

const submitFailureFallback = '申請を保存しましたが、提出できませんでした。「承認申請」画面から提出してください。'

/** サーバーが提出を断った理由が分かっているか（主担当の変更）。 */
export function isReasonedSubmitFailure(error: unknown): error is StandardApprovalWriteError {
  return error instanceof StandardApprovalWriteError && (error.code === 'main-owner-unavailable' || error.code === 'main-owner-unchanged')
}

/** 申請を保存した後、提出が失敗したときの文言。サーバーが理由を返したら、それを出して取り下げを案内する（2026-09-30）。 */
export function submitFailureMessage(error: unknown): string {
  if (isReasonedSubmitFailure(error)) {
    return `申請を保存しましたが、提出できませんでした。${error.message}「承認申請」画面でこの申請を取り下げてから、別の人を選んで申請し直してください。`
  }
  return submitFailureFallback
}
