import { isDataverseGuid } from './dataverseIds'
import type { BusinessCardOcrFields } from './businessCardOcrResult'
import type { BusinessCardReviewItem } from './businessCardReview'
import type { ContactLedgerRegistrationApi, ContactRegistrationReplayRecord } from './contactRegistrationApi'
import type { PartnerLedgerRegistrationApi } from './partnerRegistrationApi'

const MAX_PARTNER_ADDRESS_LENGTH = 500
const MAX_CONTACT_ROLE_LENGTH = 200

export type PartnerFormalRegistrationRecord = {
  pl_partnerid?: unknown
  pl_name?: unknown
  pl_industry?: unknown
  pl_address?: unknown
  pl_phone?: unknown
  pl_registrationkey?: unknown
  _pl_mainownerlookup_value?: unknown
}

export type FormalRegistrationReadResult<T> = {
  success: boolean
  data: T[]
  error?: unknown
}

export type BusinessCardFormalRegistrationInvoker = {
  findPartnerByRegistrationKey: (registrationKey: string) => Promise<FormalRegistrationReadResult<PartnerFormalRegistrationRecord>>
  registerPartner: PartnerLedgerRegistrationApi['registerPartner']
  findContactByRegistrationKey: (registrationKey: string) => Promise<FormalRegistrationReadResult<ContactRegistrationReplayRecord>>
  registerContact: ContactLedgerRegistrationApi['registerContact']
  updateCaptureStatus: (captureId: string, status: '登録済み') => Promise<{success: boolean; error?: unknown}>
}

/**
 * 新しい取引先として登録する（mainOwnerIdが必要）か、利用者が見られる既存の取引先へ担当者として追加する
 * （existingPartnerId。2026-09-27、要件PL-029）。既存の取引先を使うときは取引先を作らない。
 */
export type BusinessCardFormalRegistrationRequest = {
  item: BusinessCardReviewItem
  mainOwnerId?: string
  existingPartnerId?: string
}

export type BusinessCardFormalRegistrationResult =
  | {success: true; partnerId: string; contactId: string; partnerReplayed: boolean; contactReplayed: boolean; addedToExistingPartner?: true}
  | {
      success: false
      kind: 'blocked' | 'conflict' | 'operation-failed' | 'result-unknown' | 'partial'
      message: string
      partnerId?: string
      contactId?: string
      error?: unknown
    }

export type BusinessCardFormalRegistrationApi = {
  register: (request: BusinessCardFormalRegistrationRequest) => Promise<BusinessCardFormalRegistrationResult>
}

export class BusinessCardFormalRegistrationError extends Error {
  readonly code: 'invalid-input' | 'conflict' | 'operation-failed' | 'result-unknown'

  constructor(message: string, code: BusinessCardFormalRegistrationError['code'], cause?: unknown) {
    super(message)
    this.name = 'BusinessCardFormalRegistrationError'
    this.code = code
    if (cause !== undefined) this.cause = cause
  }
}

type NormalizedFields = {
  companyName: string
  fullName: string
  industry?: string
  address?: string
  partnerPhone?: string
  departmentRole?: string
  email?: string
  contactPhone?: string
}

type RegistrationKeys = {partnerKey: string; contactKey: string}

function optionalText(value: unknown, maxLength: number, fieldName: string): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new BusinessCardFormalRegistrationError(`${fieldName}が不正です。`, 'invalid-input')
  const normalized = value.trim()
  if (!normalized) return undefined
  if (normalized.length > maxLength) throw new BusinessCardFormalRegistrationError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'invalid-input')
  return normalized
}

function requiredText(value: unknown, fieldName: string, maxLength: number): string {
  const normalized = optionalText(value, maxLength, fieldName)
  if (!normalized) throw new BusinessCardFormalRegistrationError(`${fieldName}を指定してください。`, 'invalid-input')
  return normalized
}

function field(fields: BusinessCardOcrFields, key: keyof BusinessCardOcrFields): unknown {
  return fields[key]
}

function normalizedFields(item: BusinessCardReviewItem): NormalizedFields {
  if (!item.correctedFields) throw new BusinessCardFormalRegistrationError('確認確定済みの人確認値がありません。', 'invalid-input')
  const confirmed = item.correctedFields
  const companyName = requiredText(field(confirmed, 'companyName'), '会社名', 2000)
  const fullName = requiredText(field(confirmed, 'fullName'), '氏名', 2000)
  const address = optionalText(
    field(confirmed, 'fullAddress')
      ?? [
        field(confirmed, 'addressCountry'),
        field(confirmed, 'addressState'),
        field(confirmed, 'addressCity'),
        field(confirmed, 'addressStreet'),
        field(confirmed, 'addressPostOfficeBox'),
      ].filter(value => typeof value === 'string' && value.trim()).join(''),
    MAX_PARTNER_ADDRESS_LENGTH,
    '住所',
  )
  const department = optionalText(field(confirmed, 'department'), MAX_CONTACT_ROLE_LENGTH, '部署')
  const jobTitle = optionalText(field(confirmed, 'jobTitle'), MAX_CONTACT_ROLE_LENGTH, '役職')
  const departmentRole = optionalText([department, jobTitle].filter(Boolean).join(' / '), MAX_CONTACT_ROLE_LENGTH, '部署・役職')
  const businessPhone = optionalText(field(confirmed, 'businessPhone'), 200, '代表電話')
  const mobilePhone = optionalText(field(confirmed, 'mobilePhone'), 200, '携帯電話')
  return {
    companyName,
    fullName,
    address,
    partnerPhone: businessPhone,
    departmentRole,
    email: optionalText(field(confirmed, 'email'), 200, 'メールアドレス'),
    contactPhone: mobilePhone ?? businessPhone,
  }
}

function registrationKeys(item: BusinessCardReviewItem): RegistrationKeys {
  if (!isDataverseGuid(item.reviewId) || !Number.isInteger(item.inputVersionNumber) || item.inputVersionNumber < 1) {
    throw new BusinessCardFormalRegistrationError('確認版の識別値が不正です。', 'invalid-input')
  }
  const scope = `BusinessCardReview-${item.reviewId}-V${item.inputVersionNumber}`
  return {partnerKey: `${scope}-Partner`, contactKey: `${scope}-Contact`}
}

function ensureReviewCanRegister(request: BusinessCardFormalRegistrationRequest): void {
  const {item, mainOwnerId, existingPartnerId} = request
  if (!isDataverseGuid(item.reviewId) || !isDataverseGuid(item.captureId) || !isDataverseGuid(item.inputVersionId) || !isDataverseGuid(item.ocrJobId)) {
    throw new BusinessCardFormalRegistrationError('確認版の関連IDが不正です。', 'invalid-input')
  }
  if (item.reviewStatus !== '確認確定' || item.captureStatus !== '確認待ち' || item.inputState !== '固定済み' || item.imageState !== '画像到着確認済み' || item.ocrJobStatus !== '成功') {
    throw new BusinessCardFormalRegistrationError('確認確定済みで、OCR成功の名刺だけ正式登録できます。', 'invalid-input')
  }
  if (existingPartnerId !== undefined) {
    if (!isDataverseGuid(existingPartnerId)) throw new BusinessCardFormalRegistrationError('追加先の取引先が不正です。選び直してください。', 'invalid-input')
    return
  }
  if (!mainOwnerId || !isDataverseGuid(mainOwnerId)) throw new BusinessCardFormalRegistrationError('主担当を選択してください。', 'invalid-input')
}

function asText(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined
}

function sameOptional(actual: unknown, expected?: string): boolean {
  return (asText(actual) ?? undefined) === expected
}

function requireRecordGuid(value: unknown, fieldName: string): string {
  if (typeof value !== 'string' || !isDataverseGuid(value)) throw new BusinessCardFormalRegistrationError(`${fieldName}が不正です。`, 'result-unknown')
  return value
}

function ensureSingle<T>(rows: T[], label: string): T | undefined {
  if (rows.length > 1) throw new BusinessCardFormalRegistrationError(`同じ登録の${label}が複数見つかりました。管理者に連絡してください。`, 'conflict')
  return rows[0]
}

async function readRows<T>(read: () => Promise<FormalRegistrationReadResult<T>>, label: string): Promise<T[]> {
  try {
    const result = await read()
    if (!result.success || !Array.isArray(result.data)) throw new Error(label)
    return result.data
  } catch (error) {
    if (error instanceof BusinessCardFormalRegistrationError) throw error
    throw new BusinessCardFormalRegistrationError(`${label}の照合結果を確認できません。再送せず再読取してください。`, 'result-unknown', error)
  }
}

function partnerMatches(record: PartnerFormalRegistrationRecord, request: {key: string; fields: NormalizedFields; mainOwnerId: string}): string {
  const id = requireRecordGuid(record.pl_partnerid, 'PartnerId')
  const ownerId = asText(record._pl_mainownerlookup_value)?.toLowerCase()
  if (asText(record.pl_registrationkey) !== request.key
    || asText(record.pl_name) !== request.fields.companyName
    || !sameOptional(record.pl_industry, request.fields.industry)
    || !sameOptional(record.pl_address, request.fields.address)
    || !sameOptional(record.pl_phone, request.fields.partnerPhone)
    || ownerId !== request.mainOwnerId.toLowerCase()) {
    throw new BusinessCardFormalRegistrationError('前回の登録と取引先の内容が異なります。再読み込みしてから、もう一度お試しください。', 'conflict')
  }
  return id
}

function contactMatches(record: ContactRegistrationReplayRecord, request: {key: string; partnerId: string; fields: NormalizedFields}): string {
  const id = requireRecordGuid(record.pl_contactid, 'ContactId')
  const partnerId = asText(record._pl_partnerlookup_value)?.toLowerCase()
  const statusCode = typeof record.pl_statuscode === 'number' || typeof record.pl_statuscode === 'string' ? Number(record.pl_statuscode) : NaN
  if (asText(record.pl_registrationkey) !== request.key
    || asText(record.pl_name) !== request.fields.fullName
    || partnerId !== request.partnerId.toLowerCase()
    || statusCode !== 100000000
    || !sameOptional(record.pl_departmentrole, request.fields.departmentRole)
    || !sameOptional(record.pl_email, request.fields.email)
    || !sameOptional(record.pl_phone, request.fields.contactPhone)) {
    throw new BusinessCardFormalRegistrationError('前回の登録と担当者の内容が異なります。再読み込みしてから、もう一度お試しください。', 'conflict')
  }
  return id
}

async function resolvePartner(
  invoker: BusinessCardFormalRegistrationInvoker,
  key: string,
  fields: NormalizedFields,
  mainOwnerId: string,
): Promise<{id: string; replay: boolean}> {
  const existing = ensureSingle(await readRows(() => invoker.findPartnerByRegistrationKey(key), '取引先'), '取引先')
  if (existing) return {id: partnerMatches(existing, {key, fields, mainOwnerId}), replay: true}

  try {
    const created = await invoker.registerPartner({
      name: fields.companyName,
      industry: fields.industry,
      address: fields.address,
      phone: fields.partnerPhone,
      mainOwnerId,
      idempotencyKey: key,
    })
    return {id: created.partnerId, replay: created.isReplay}
  } catch (error) {
    let afterCreate: PartnerFormalRegistrationRecord | undefined
    try {
      afterCreate = ensureSingle(await readRows(() => invoker.findPartnerByRegistrationKey(key), '取引先'), '取引先')
    } catch (readError) {
      if (readError instanceof BusinessCardFormalRegistrationError) {
        throw readError
      }
      throw new BusinessCardFormalRegistrationError('取引先登録の結果を確認できません。', 'result-unknown', error)
    }
    if (afterCreate) return {id: partnerMatches(afterCreate, {key, fields, mainOwnerId}), replay: true}
    throw new BusinessCardFormalRegistrationError('取引先を登録できませんでした。入力を保持したまま再試行してください。', 'operation-failed', error)
  }
}

async function resolveContact(
  invoker: BusinessCardFormalRegistrationInvoker,
  key: string,
  partnerId: string,
  fields: NormalizedFields,
): Promise<{id: string; replay: boolean}> {
  const existing = ensureSingle(await readRows(() => invoker.findContactByRegistrationKey(key), '担当者'), '担当者')
  const expected = {key, partnerId, fields}
  if (existing) return {id: contactMatches(existing, expected), replay: true}

  try {
    const created = await invoker.registerContact({
      partnerId,
      name: fields.fullName,
      statusCode: 100000000,
      departmentRole: fields.departmentRole,
      email: fields.email,
      phone: fields.contactPhone,
      idempotencyKey: key,
    })
    return {id: created.contactId, replay: created.isReplay}
  } catch (error) {
    let afterCreate: ContactRegistrationReplayRecord | undefined
    try {
      afterCreate = ensureSingle(await readRows(() => invoker.findContactByRegistrationKey(key), '担当者'), '担当者')
    } catch (readError) {
      if (readError instanceof BusinessCardFormalRegistrationError) throw readError
      throw new BusinessCardFormalRegistrationError('担当者登録の結果を確認できません。Partnerを保持したまま照合してください。', 'result-unknown', error)
    }
    if (afterCreate) return {id: contactMatches(afterCreate, expected), replay: true}
    throw new BusinessCardFormalRegistrationError('担当者を登録できませんでした。Partnerを保持したまま再試行してください。', 'operation-failed', error)
  }
}

export function createBusinessCardFormalRegistrationApi(invoker: BusinessCardFormalRegistrationInvoker): BusinessCardFormalRegistrationApi {
  return {
    register: async request => {
      try {
        ensureReviewCanRegister(request)
        const fields = normalizedFields(request.item)
        const keys = registrationKeys(request.item)
        const toExisting = request.existingPartnerId !== undefined
        const partner = toExisting
          ? {id: request.existingPartnerId as string, replay: false}
          : await resolvePartner(invoker, keys.partnerKey, fields, request.mainOwnerId as string)
        let contact: {id: string; replay: boolean}
        try {
          contact = await resolveContact(invoker, keys.contactKey, partner.id, fields)
        } catch (error) {
          if (error instanceof BusinessCardFormalRegistrationError) {
            const kind = error.code === 'conflict' ? 'conflict' : error.code === 'invalid-input' ? 'blocked' : error.code
            return {success: false, kind, message: error.message, partnerId: partner.id, error}
          }
          return {success: false, kind: 'result-unknown', message: '担当者登録の結果を確認できません。Partnerを保持したまま照合してください。', partnerId: partner.id, error}
        }

        try {
          const captureUpdate = await invoker.updateCaptureStatus(request.item.captureId, '登録済み')
          if (!captureUpdate.success) {
            return {success: false, kind: 'partial', message: '取引先・担当者は登録済みですが、名刺取込の完了状態を更新できませんでした。再読取して確認してください。', partnerId: partner.id, contactId: contact.id, error: captureUpdate.error}
          }
        } catch (error) {
          return {success: false, kind: 'partial', message: '取引先・担当者は登録済みですが、名刺取込の完了状態が未確定です。再送せず再読取してください。', partnerId: partner.id, contactId: contact.id, error}
        }
        return {success: true, partnerId: partner.id, contactId: contact.id, partnerReplayed: partner.replay, contactReplayed: contact.replay, ...(toExisting ? {addedToExistingPartner: true as const} : {})}
      } catch (error) {
        if (error instanceof BusinessCardFormalRegistrationError) {
          return {success: false, kind: error.code === 'invalid-input' ? 'blocked' : error.code, message: error.message, error}
        }
        return {success: false, kind: 'result-unknown', message: '正式登録の結果を確認できません。再送せず再読取してください。', error}
      }
    },
  }
}
