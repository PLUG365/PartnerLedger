import { isDataverseGuid } from './dataverseIds'

export type ContactStatusCode = 100000000 | 100000001 | 100000002

export type ContactRegistrationRequest = {
  partnerId: string
  name: string
  statusCode?: ContactStatusCode
  departmentRole?: string
  email?: string
  phone?: string
  idempotencyKey?: string
}

export type ContactRegistrationReplayRecord = {
  pl_contactid?: unknown
  pl_name?: unknown
  _pl_partnerlookup_value?: unknown
  pl_statuscode?: unknown
  pl_departmentrole?: unknown
  pl_email?: unknown
  pl_phone?: unknown
  pl_registrationkey?: unknown
}

export type ContactRegistrationResult = {
  contactId: string
  isReplay: boolean
}

export type ContactRegistrationExistingReader = {
  findByRegistrationKey: (registrationKey: string) => Promise<{
    success: boolean
    data: ContactRegistrationReplayRecord[]
    error?: unknown
  }>
}

export type ContactLedgerRegistrationApi = {
  registerContact: (request: ContactRegistrationRequest) => Promise<ContactRegistrationResult>
}

export class ContactRegistrationApiError extends Error {
  readonly code?: string

  constructor(message: string, code?: string) {
    super(message)
    this.name = 'ContactRegistrationApiError'
    this.code = code
  }
}

let idempotencySequence = 0

export function createContactRegistrationIdempotencyKey(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `CreateContact-${uuid}`
  idempotencySequence += 1
  return `CreateContact-${Date.now().toString(36)}-${idempotencySequence.toString(36)}`
}

function requireText(value: string | undefined, fieldName: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new ContactRegistrationApiError(`${fieldName}を指定してください。`, 'required')
  return value.trim()
}

function optionalText(value: string | undefined, fieldName: string, maxLength: number): string | undefined {
  if (value === undefined || value.trim() === '') return undefined
  const normalized = value.trim()
  if (normalized.length > maxLength) throw new ContactRegistrationApiError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  return normalized
}

function requireGuid(value: string, fieldName: string): string {
  const normalized = requireText(value, fieldName)
  if (!isDataverseGuid(normalized)) throw new ContactRegistrationApiError(`${fieldName}が正しくありません。`, 'invalid-guid')
  return normalized
}

function resolveStatusCode(value?: ContactStatusCode): ContactStatusCode {
  return value === undefined ? 100000000 : value
}

function resolveIdempotencyKey(value?: string): string {
  const normalized = value?.trim()
  return normalized || createContactRegistrationIdempotencyKey()
}

function requireStatusCode(value: number | undefined): ContactStatusCode {
  const statusCode = resolveStatusCode(value as ContactStatusCode | undefined)
  if (statusCode !== 100000000 && statusCode !== 100000001 && statusCode !== 100000002) {
    throw new ContactRegistrationApiError('在籍状態が不正です。', 'invalid-status')
  }
  return statusCode
}

function normalizeRequest(request: ContactRegistrationRequest) {
  const partnerId = requireGuid(request.partnerId, '取引先')
  const name = requireText(request.name, '氏名')
  if (name.length > 850) throw new ContactRegistrationApiError('氏名は850文字以内で指定してください。', 'too-long')
  const departmentRole = optionalText(request.departmentRole, '部署・役職', 200)
  const email = optionalText(request.email, 'メールアドレス', 200)
  const phone = optionalText(request.phone, '電話番号', 200)
  const statusCode = requireStatusCode(request.statusCode)
  const idempotencyKey = resolveIdempotencyKey(request.idempotencyKey)
  if (idempotencyKey.length > 200) throw new ContactRegistrationApiError('登録の識別子が長すぎます。', 'too-long')
  return {partnerId, name, statusCode, departmentRole, email, phone, idempotencyKey}
}

function normalizeReplayRecord(value: ContactRegistrationReplayRecord): ContactRegistrationReplayRecord {
  if (typeof value.pl_contactid !== 'string' || !isDataverseGuid(value.pl_contactid)) {
    throw new ContactRegistrationApiError('再送結果のContactIdが不正です。', 'invalid-response')
  }
  return value
}

function optionalRecordString(value: unknown): string | undefined {
  return value === undefined || value === null || value === '' ? undefined : typeof value === 'string' ? value.trim() : undefined
}

function replayMatches(value: ContactRegistrationReplayRecord, request: ReturnType<typeof normalizeRequest>): boolean {
  const existingStatus = typeof value.pl_statuscode === 'number' || typeof value.pl_statuscode === 'string' ? Number(value.pl_statuscode) : NaN
  const existingPartnerId = typeof value._pl_partnerlookup_value === 'string' ? value._pl_partnerlookup_value.toLowerCase() : ''
  return optionalRecordString(value.pl_name) === request.name
    && existingPartnerId === request.partnerId.toLowerCase()
    && existingStatus === request.statusCode
    && optionalRecordString(value.pl_departmentrole) === request.departmentRole
    && optionalRecordString(value.pl_email) === request.email
    && optionalRecordString(value.pl_phone) === request.phone
}

async function resolveExisting(
  reader: ContactRegistrationExistingReader,
  request: ReturnType<typeof normalizeRequest>,
): Promise<ContactRegistrationResult | undefined> {
  let result
  try {
    result = await reader.findByRegistrationKey(request.idempotencyKey)
  } catch {
    throw new ContactRegistrationApiError('保存結果を確認できません。入力を保持したまま再試行してください。', 'transport-error')
  }
  if (!result.success) throw new ContactRegistrationApiError('保存結果を確認できません。入力を保持したまま再試行してください。', 'transport-error')
  if (result.data.length === 0) return undefined
  if (result.data.length > 1) throw new ContactRegistrationApiError('同じ登録が複数見つかったため、保存を中止しました。管理者に連絡してください。', 'invalid-response')
  const existing = normalizeReplayRecord(result.data[0])
  if (!replayMatches(existing, request)) throw new ContactRegistrationApiError('前回の保存と内容が異なります。再読み込みしてから、もう一度お試しください。', 'idempotency-conflict')
  return {contactId: existing.pl_contactid as string, isReplay: true}
}

export function createContactLedgerRegistrationApi(
  reader: ContactRegistrationExistingReader,
  create: (request: ReturnType<typeof normalizeRequest>) => Promise<{success: boolean; contactId?: string; error?: unknown}>,
): ContactLedgerRegistrationApi {
  return {
    registerContact: async request => {
      const normalized = normalizeRequest(request)
      const beforeCreate = await resolveExisting(reader, normalized)
      if (beforeCreate) return beforeCreate

      const created = await create(normalized)
      if (created.success && typeof created.contactId === 'string' && isDataverseGuid(created.contactId)) {
        return {contactId: created.contactId, isReplay: false}
      }

      const afterCreate = await resolveExisting(reader, normalized)
      if (afterCreate) return afterCreate
      throw new ContactRegistrationApiError('担当者の保存結果が未確定です。入力を保持したまま同じ操作を再試行してください。', 'transport-error')
    },
  }
}
