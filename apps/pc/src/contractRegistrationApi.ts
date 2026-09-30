import { isDataverseGuid } from './dataverseIds'

export type ContractRegistrationRequest = {
  partnerId: string
  contractTypeId: string
  name: string
  endDate?: string
  noticeDate?: string
  decisionDate?: string
  autoRenew?: boolean
  link?: string
  idempotencyKey?: string
}

export type ContractRegistrationReplayRecord = {
  pl_contractid?: unknown
  pl_name?: unknown
  _pl_partnerlookup_value?: unknown
  _pl_contracttypelookup_value?: unknown
  pl_contractstatuscode?: unknown
  pl_enddate?: unknown
  pl_noticedate?: unknown
  pl_decisiondate?: unknown
  pl_autorenew?: unknown
  pl_link?: unknown
  pl_registrationkey?: unknown
}

export type ContractRegistrationResult = {
  contractId: string
  isReplay: boolean
}

export type ContractRegistrationExistingReader = {
  findByRegistrationKey: (registrationKey: string) => Promise<{
    success: boolean
    data: ContractRegistrationReplayRecord[]
    error?: unknown
  }>
}

export type ContractLedgerRegistrationApi = {
  registerContract: (request: ContractRegistrationRequest) => Promise<ContractRegistrationResult>
}

export class ContractRegistrationApiError extends Error {
  readonly code?: string

  constructor(message: string, code?: string) {
    super(message)
    this.name = 'ContractRegistrationApiError'
    this.code = code
  }
}

let idempotencySequence = 0

export function createContractRegistrationIdempotencyKey(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `CreateContract-${uuid}`
  idempotencySequence += 1
  return `CreateContract-${Date.now().toString(36)}-${idempotencySequence.toString(36)}`
}

function requireText(value: string | undefined, fieldName: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new ContractRegistrationApiError(`${fieldName}を指定してください。`, 'required')
  return value.trim()
}

function optionalText(value: string | undefined, fieldName: string, maxLength: number): string | undefined {
  if (value === undefined) return undefined
  if (typeof value !== 'string') throw new ContractRegistrationApiError(`${fieldName}が不正です。`, 'invalid-input')
  if (value.trim() === '') return undefined
  const normalized = value.trim()
  if (normalized.length > maxLength) throw new ContractRegistrationApiError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  return normalized
}

function requireGuid(value: string, fieldName: string): string {
  const normalized = requireText(value, fieldName)
  if (!isDataverseGuid(normalized)) throw new ContractRegistrationApiError(`${fieldName}が正しくありません。`, 'invalid-guid')
  return normalized
}

function normalizeDate(value: string | undefined, fieldName: string): string | undefined {
  const normalized = optionalText(value, fieldName, 10)
  if (normalized === undefined) return undefined
  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) throw new ContractRegistrationApiError(`${fieldName}はYYYY-MM-DD形式で指定してください。`, 'invalid-date')
  const parsed = new Date(`${normalized}T00:00:00.000Z`)
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== normalized) throw new ContractRegistrationApiError(`${fieldName}が不正です。`, 'invalid-date')
  return normalized
}

function resolveIdempotencyKey(value?: string): string {
  const normalized = typeof value === 'string' ? value.trim() : undefined
  return normalized || createContractRegistrationIdempotencyKey()
}

function normalizeRequest(request: ContractRegistrationRequest) {
  const partnerId = requireGuid(request.partnerId, '取引先')
  const contractTypeId = requireGuid(request.contractTypeId, '契約種類')
  const name = requireText(request.name, '契約名')
  if (name.length > 200) throw new ContractRegistrationApiError('契約名は200文字以内で指定してください。', 'too-long')
  const endDate = normalizeDate(request.endDate, '契約終了日')
  const noticeDate = normalizeDate(request.noticeDate, '解約通知期限')
  const decisionDate = normalizeDate(request.decisionDate, '更新判断期限')
  if (request.autoRenew !== undefined && typeof request.autoRenew !== 'boolean') throw new ContractRegistrationApiError('自動更新が不正です。', 'invalid-input')
  const autoRenew = request.autoRenew ?? false
  const link = optionalText(request.link, '契約書リンク', 200)
  const idempotencyKey = resolveIdempotencyKey(request.idempotencyKey)
  if (idempotencyKey.length > 200) throw new ContractRegistrationApiError('登録の識別子が長すぎます。', 'too-long')
  return {partnerId, contractTypeId, name, endDate, noticeDate, decisionDate, autoRenew, link, idempotencyKey}
}

function normalizeReplayRecord(value: ContractRegistrationReplayRecord): ContractRegistrationReplayRecord {
  if (typeof value.pl_contractid !== 'string' || !isDataverseGuid(value.pl_contractid)) {
    throw new ContractRegistrationApiError('再送結果のContractIdが不正です。', 'invalid-response')
  }
  return value
}

function optionalRecordString(value: unknown): string | undefined {
  return value === undefined || value === null || value === '' ? undefined : typeof value === 'string' ? value.trim() : undefined
}

function optionalRecordDate(value: unknown): string | undefined {
  const normalized = optionalRecordString(value)
  return normalized?.slice(0, 10)
}

function optionalRecordBoolean(value: unknown): boolean {
  if (value === undefined || value === null || value === '') return false
  if (typeof value !== 'boolean') throw new ContractRegistrationApiError('再送結果の自動更新が不正です。', 'invalid-response')
  return value
}

function recordGuid(value: unknown): string {
  return typeof value === 'string' ? value.toLowerCase() : ''
}

function replayMatches(value: ContractRegistrationReplayRecord, request: ReturnType<typeof normalizeRequest>): boolean {
  const existingStatus = typeof value.pl_contractstatuscode === 'number' || typeof value.pl_contractstatuscode === 'string' ? Number(value.pl_contractstatuscode) : NaN
  return optionalRecordString(value.pl_name) === request.name
    && recordGuid(value._pl_partnerlookup_value) === request.partnerId.toLowerCase()
    && recordGuid(value._pl_contracttypelookup_value) === request.contractTypeId.toLowerCase()
    && existingStatus === 100000000
    && optionalRecordDate(value.pl_enddate) === request.endDate
    && optionalRecordDate(value.pl_noticedate) === request.noticeDate
    && optionalRecordDate(value.pl_decisiondate) === request.decisionDate
    && optionalRecordBoolean(value.pl_autorenew) === request.autoRenew
    && optionalRecordString(value.pl_link) === request.link
}

async function resolveExisting(
  reader: ContractRegistrationExistingReader,
  request: ReturnType<typeof normalizeRequest>,
): Promise<ContractRegistrationResult | undefined> {
  let result
  try {
    result = await reader.findByRegistrationKey(request.idempotencyKey)
  } catch {
    throw new ContractRegistrationApiError('保存結果を確認できません。入力を保持したまま再試行してください。', 'transport-error')
  }
  if (!result.success) throw new ContractRegistrationApiError('保存結果を確認できません。入力を保持したまま再試行してください。', 'transport-error')
  if (result.data.length === 0) return undefined
  if (result.data.length > 1) throw new ContractRegistrationApiError('同じ登録が複数見つかったため、保存を中止しました。管理者に連絡してください。', 'invalid-response')
  const existing = normalizeReplayRecord(result.data[0])
  if (!replayMatches(existing, request)) throw new ContractRegistrationApiError('前回の保存と内容が異なります。再読み込みしてから、もう一度お試しください。', 'idempotency-conflict')
  return {contractId: existing.pl_contractid as string, isReplay: true}
}

export function createContractLedgerRegistrationApi(
  reader: ContractRegistrationExistingReader,
  create: (request: ReturnType<typeof normalizeRequest>) => Promise<{success: boolean; contractId?: string; error?: unknown}>,
): ContractLedgerRegistrationApi {
  return {
    registerContract: async request => {
      const normalized = normalizeRequest(request)
      const beforeCreate = await resolveExisting(reader, normalized)
      if (beforeCreate) return beforeCreate

      const created = await create(normalized)
      if (created.success && typeof created.contractId === 'string' && isDataverseGuid(created.contractId)) {
        return {contractId: created.contractId, isReplay: false}
      }

      const afterCreate = await resolveExisting(reader, normalized)
      if (afterCreate) return afterCreate
      throw new ContractRegistrationApiError('契約の保存結果が未確定です。入力を保持したまま同じ操作を再試行してください。', 'transport-error')
    },
  }
}
