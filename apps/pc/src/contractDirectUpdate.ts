import { isDataverseGuid } from './dataverseIds'
import type { ContractReadItem } from './contractReadModel'

export type ContractDirectUpdateDecision = '更新' | '終了'

export type ContractDirectUpdateInput = {
  contract: ContractReadItem
  name: string
  decision: ContractDirectUpdateDecision
  endDate?: string
  noticeDate?: string
  decisionDate?: string
  autoRenew: boolean
  link?: string
}

export type ContractDirectUpdateService = {
  update: (contractId: string, fields: Record<string, unknown>) => Promise<{success: boolean; data?: unknown; error?: unknown}>
}

export type ContractDirectUpdateApi = {
  updateContract: (input: ContractDirectUpdateInput) => Promise<{contractId: string}>
}

export class ContractDirectUpdateError extends Error {
  readonly code: 'required' | 'invalid-guid' | 'invalid-date' | 'invalid-input' | 'too-long' | 'operation-rejected' | 'transport-error'

  constructor(message: string, code: ContractDirectUpdateError['code']) {
    super(message)
    this.name = 'ContractDirectUpdateError'
    this.code = code
  }
}

function requiredText(value: string | undefined, fieldName: string, maxLength: number): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new ContractDirectUpdateError(`${fieldName}を指定してください。`, 'required')
  }
  const normalized = value.trim()
  if (normalized.length > maxLength) {
    throw new ContractDirectUpdateError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  }
  return normalized
}

function optionalText(value: string | undefined, fieldName: string, maxLength: number): string | null {
  if (value === undefined) return null
  if (typeof value !== 'string') {
    throw new ContractDirectUpdateError(`${fieldName}が不正です。`, 'invalid-input')
  }
  const normalized = value.trim()
  if (normalized.length > maxLength) {
    throw new ContractDirectUpdateError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  }
  return normalized || null
}

function normalizeDate(value: string | undefined, fieldName: string): string | null {
  const normalized = optionalText(value, fieldName, 10)
  if (normalized === null) return null
  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) {
    throw new ContractDirectUpdateError(`${fieldName}はYYYY-MM-DD形式で指定してください。`, 'invalid-date')
  }
  const parsed = new Date(`${normalized}T00:00:00.000Z`)
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== normalized) {
    throw new ContractDirectUpdateError(`${fieldName}が不正です。`, 'invalid-date')
  }
  return `${normalized}T00:00:00.000Z`
}

function requireContractId(contract: ContractReadItem): string {
  if (!isDataverseGuid(contract.id)) {
    throw new ContractDirectUpdateError('対象の契約が正しくありません。', 'invalid-guid')
  }
  return contract.id
}

function statusCode(decision: ContractDirectUpdateDecision): number {
  if (decision === '更新') return 100000000
  if (decision === '終了') return 100000001
  throw new ContractDirectUpdateError('契約判断が不正です。', 'invalid-input')
}

export function buildStandardContractUpdateRecord(input: ContractDirectUpdateInput): {contractId: string; fields: Record<string, unknown>} {
  const contractId = requireContractId(input.contract)
  const name = requiredText(input.name, '契約名', 200)
  const endDate = normalizeDate(input.endDate, '契約終了日')
  const noticeDate = normalizeDate(input.noticeDate, '解約通知期限')
  const decisionDate = normalizeDate(input.decisionDate, '更新判断期限')
  if (typeof input.autoRenew !== 'boolean') {
    throw new ContractDirectUpdateError('自動更新が不正です。', 'invalid-input')
  }
  const link = optionalText(input.link, '契約書リンク', 200)

  return {
    contractId,
    fields: {
      pl_name: name,
      pl_contractstatuscode: statusCode(input.decision),
      pl_enddate: endDate,
      pl_noticedate: noticeDate,
      pl_decisiondate: decisionDate,
      pl_autorenew: input.autoRenew,
      pl_link: link,
    },
  }
}

export function createContractDirectUpdateApi(service: ContractDirectUpdateService): ContractDirectUpdateApi {
  return {
    updateContract: async input => {
      const {contractId, fields} = buildStandardContractUpdateRecord(input)
      let result
      try {
        result = await service.update(contractId, fields)
      } catch {
        throw new ContractDirectUpdateError('契約の直接更新に失敗しました。入力を保持したまま再試行してください。', 'transport-error')
      }
      if (!result.success) {
        throw new ContractDirectUpdateError('契約を更新できませんでした。承認設定と権限を確認してください。', 'operation-rejected')
      }
      return {contractId}
    },
  }
}
