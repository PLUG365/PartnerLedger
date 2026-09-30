import { isDataverseGuid } from './dataverseIds'
import type { ContractReadItem } from './contractReadModel'
import type { StandardApprovalWriteApi } from './standardApprovalTransitionApi'

export const contractUpdateRequestType = 'contract.update' as const

export type ContractApprovalDecision = '更新' | '終了'

export type ContractApprovalDraftInput = {
  contract: ContractReadItem
  name: string
  decision: ContractApprovalDecision
  endDate?: string
  noticeDate?: string
  decisionDate?: string
  autoRenew: boolean
  link?: string
}

export class ContractApprovalDraftError extends Error {
  readonly code: 'required' | 'invalid-guid' | 'invalid-date' | 'invalid-input' | 'too-long'

  constructor(message: string, code: ContractApprovalDraftError['code']) {
    super(message)
    this.name = 'ContractApprovalDraftError'
    this.code = code
  }
}

export class ContractApprovalDraftSaveError extends Error {
  readonly phase: 'request' | 'submission'
  readonly requestId?: string
  readonly original: unknown

  constructor(message: string, phase: 'request' | 'submission', original: unknown, requestId?: string) {
    super(message)
    this.name = 'ContractApprovalDraftSaveError'
    this.phase = phase
    this.original = original
    this.requestId = requestId
  }
}

let idempotencySequence = 0

export function createContractApprovalRequestKey(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `ContractUpdate-${uuid}`
  idempotencySequence += 1
  return `ContractUpdate-${Date.now().toString(36)}-${idempotencySequence.toString(36)}`
}

function requiredText(value: string | undefined, fieldName: string, maxLength: number): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new ContractApprovalDraftError(`${fieldName}を指定してください。`, 'required')
  }
  const normalized = value.trim()
  if (normalized.length > maxLength) {
    throw new ContractApprovalDraftError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  }
  return normalized
}

function optionalText(value: string | undefined, fieldName: string, maxLength: number): string | null {
  if (value === undefined) return null
  if (typeof value !== 'string') {
    throw new ContractApprovalDraftError(`${fieldName}が不正です。`, 'invalid-input')
  }
  if (value.trim() === '') return null
  const normalized = value.trim()
  if (normalized.length > maxLength) {
    throw new ContractApprovalDraftError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  }
  return normalized
}

function normalizeDate(value: string | undefined, fieldName: string): string | null {
  const normalized = optionalText(value, fieldName, 10)
  if (normalized === null) return null
  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) {
    throw new ContractApprovalDraftError(`${fieldName}はYYYY-MM-DD形式で指定してください。`, 'invalid-date')
  }
  const parsed = new Date(`${normalized}T00:00:00.000Z`)
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== normalized) {
    throw new ContractApprovalDraftError(`${fieldName}が不正です。`, 'invalid-date')
  }
  return `${normalized}T00:00:00.000Z`
}

function requireContractId(contract: ContractReadItem): string {
  if (!isDataverseGuid(contract.id)) {
    throw new ContractApprovalDraftError('対象の契約が正しくありません。', 'invalid-guid')
  }
  return contract.id
}

function statusCode(decision: ContractApprovalDecision): number {
  if (decision === '更新') return 100000000
  if (decision === '終了') return 100000001
  throw new ContractApprovalDraftError('契約判断が不正です。', 'invalid-input')
}

/**
 * Server-side ApprovalChangeSetContract が受理する、契約更新用の固定順序JSONを作る。
 * 契約種類・親・所有者・監査列などはこの入力型に存在せず、変更セットへも出さない。
 */
export function buildContractApprovalChangeSetJson(input: ContractApprovalDraftInput): string {
  const contractId = requireContractId(input.contract)
  const name = requiredText(input.name, '契約名', 200)
  const decision = input.decision
  const endDate = normalizeDate(input.endDate, '契約終了日')
  const noticeDate = normalizeDate(input.noticeDate, '解約通知期限')
  const decisionDate = normalizeDate(input.decisionDate, '更新判断期限')
  if (typeof input.autoRenew !== 'boolean') {
    throw new ContractApprovalDraftError('自動更新が不正です。', 'invalid-input')
  }
  const link = optionalText(input.link, '契約書リンク', 200)

  return JSON.stringify({
    schemaVersion: 1,
    target: {entity: 'pl_contract', id: contractId},
    changes: [
      {attribute: 'pl_name', value: name},
      {attribute: 'pl_contractstatuscode', value: statusCode(decision)},
      {attribute: 'pl_enddate', value: endDate},
      {attribute: 'pl_noticedate', value: noticeDate},
      {attribute: 'pl_decisiondate', value: decisionDate},
      {attribute: 'pl_autorenew', value: input.autoRenew},
      {attribute: 'pl_link', value: link},
    ],
  })
}

export async function saveContractApprovalDraft(api: Pick<StandardApprovalWriteApi, 'createRequest' | 'createSubmissionDraft'>, input: ContractApprovalDraftInput, options: {idempotencyKey: string; requestId?: string}): Promise<{requestId: string; submissionVersionId: string}> {
  const changeSetJson = buildContractApprovalChangeSetJson(input)
  let requestId = options.requestId

  if (!requestId) {
    try {
      const createdRequest = await api.createRequest({
        name: input.name,
        requestTypeCode: contractUpdateRequestType,
        partnerId: input.contract.partnerId,
        contractId: input.contract.id,
        idempotencyKey: options.idempotencyKey,
      })
      if (!isDataverseGuid(createdRequest.requestId)) throw new Error('申請IDが不正です。')
      requestId = createdRequest.requestId
    } catch (error) {
      throw new ContractApprovalDraftSaveError('申請を保存できませんでした。もう一度お試しください。', 'request', error)
    }
  }

  try {
    const createdSubmission = await api.createSubmissionDraft({
      requestId,
      name: `${input.name} 提出版`,
      changeSetJson,
    })
    if (!isDataverseGuid(createdSubmission.submissionVersionId)) throw new Error('提出版IDが不正です。')
    return {requestId, submissionVersionId: createdSubmission.submissionVersionId}
  } catch (error) {
    throw new ContractApprovalDraftSaveError('提出版の保存結果を確認できません。', 'submission', error, requestId)
  }
}
