import { isDataverseGuid } from './dataverseIds'

export type ContactDirectoryStatus = '在籍' | '休職' | '退職' | '未設定'

/**
 * PC のライブ担当者一覧が扱う最小表示契約。
 * 親取引先を伴わない行や、未接続の履歴・掲示板・編集状態は補完しない。
 */
export type ContactDirectoryItem = {
  id: string
  name: string
  partnerId: string
  partnerName: string
  departmentRole?: string
  email?: string
  phone?: string
  status: ContactDirectoryStatus
  registeredAt?: string
}

export type ContactDirectoryOperationResult = {
  success: boolean
  data: unknown
  error?: unknown
}

export type ContactDirectoryInvoker = {
  listContacts: () => Promise<ContactDirectoryOperationResult>
}

export type ContactDirectoryApi = {
  listContacts: () => Promise<ContactDirectoryItem[]>
}

export type ContactDirectoryLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; contacts: ContactDirectoryItem[]}
  | {status: 'error'; message: string}

export class ContactDirectoryError extends Error {
  readonly code: string

  constructor(message: string, code: string) {
    super(message)
    this.name = 'ContactDirectoryError'
    this.code = code
  }
}

const statuses: readonly ContactDirectoryStatus[] = ['在籍', '休職', '退職']
const statusByCode: Record<string, ContactDirectoryStatus> = {
  '100000000': '在籍',
  '100000001': '休職',
  '100000002': '退職',
}
const formattedPartnerLookupName = '_pl_partnerlookup_value@OData.Community.Display.V1.FormattedValue'

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireString(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new ContactDirectoryError(`${field}が不正です。`, 'invalid-response')
  return value.trim()
}

function optionalString(value: unknown, field: string): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new ContactDirectoryError(`${field}が不正です。`, 'invalid-response')
  return value.trim() || undefined
}

function requireStatus(nameValue: unknown, codeValue: unknown): ContactDirectoryStatus {
  const name = optionalString(nameValue, '在籍状態')
  if (name && statuses.includes(name as ContactDirectoryStatus)) return name as ContactDirectoryStatus
  const code = typeof codeValue === 'number' || typeof codeValue === 'string' ? String(codeValue) : undefined
  const resolved = code ? statusByCode[code] : undefined
  if (resolved) return resolved
  if (name === undefined && (codeValue === undefined || codeValue === null || codeValue === '')) return '未設定'
  throw new ContactDirectoryError('在籍状態が不正です。', 'invalid-response')
}

function resolvePartnerName(value: Record<string, unknown>): string {
  // Dataverse returns Lookup display labels as an OData formatted-value annotation.
  // Generated services may surface the same label under the generated virtual name.
  // The lookup ID, not its presentation label, is the association integrity boundary.
  return optionalString(value.pl_partnerlookupname, '親取引先名')
    ?? optionalString(value[formattedPartnerLookupName], '親取引先名')
    ?? '取引先名未取得'
}

function parseItem(value: unknown, index: number): ContactDirectoryItem {
  if (!isRecord(value)) throw new ContactDirectoryError(`担当者一覧の${index + 1}件目が不正です。`, 'invalid-response')

  const id = requireString(value.pl_contactid, 'ContactId')
  const name = requireString(value.pl_name, '担当者名')
  const partnerId = requireString(value._pl_partnerlookup_value, '親取引先Id')
  const partnerName = resolvePartnerName(value)
  if (!isDataverseGuid(id)) throw new ContactDirectoryError(`担当者一覧の${index + 1}件目のContactIdが不正です。`, 'invalid-response')
  if (!isDataverseGuid(partnerId)) throw new ContactDirectoryError(`担当者一覧の${index + 1}件目の親取引先Idが不正です。`, 'invalid-response')

  // The server query filters active rows. Reject a contradictory response instead of
  // displaying a non-active record as if the Dataverse filter had been applied.
  if (value.statecode !== undefined && value.statecode !== 0 && value.statecode !== '0') {
    throw new ContactDirectoryError(`担当者一覧の${index + 1}件目の状態が不正です。`, 'invalid-response')
  }

  const registeredAt = optionalString(value.pl_registeredat, '登録日')
  if (registeredAt !== undefined && Number.isNaN(Date.parse(registeredAt))) {
    throw new ContactDirectoryError(`担当者一覧の${index + 1}件目の登録日が不正です。`, 'invalid-response')
  }

  return {
    id,
    name,
    partnerId,
    partnerName,
    departmentRole: optionalString(value.pl_departmentrole, '部署・役職'),
    email: optionalString(value.pl_email, 'メールアドレス'),
    phone: optionalString(value.pl_phone, '電話番号'),
    status: requireStatus(value.pl_statuscodename, value.pl_statuscode),
    registeredAt,
  }
}

export function parseContactDirectory(data: unknown): ContactDirectoryItem[] {
  if (!Array.isArray(data)) throw new ContactDirectoryError('担当者一覧の配列応答がありません。', 'invalid-response')
  const contacts = data.map(parseItem)
  const ids = new Set(contacts.map(contact => contact.id))
  if (ids.size !== contacts.length) throw new ContactDirectoryError('担当者一覧に重複したIDがあります。', 'invalid-response')
  return contacts
}

export function filterContactDirectory(contacts: ContactDirectoryItem[], query: string): ContactDirectoryItem[] {
  const needle = query.trim().toLocaleLowerCase('ja-JP')
  if (!needle) return contacts
  return contacts.filter(contact => [contact.id, contact.name, contact.partnerName, contact.departmentRole, contact.email, contact.phone, contact.status]
    .filter((value): value is string => Boolean(value))
    .join(' ')
    .toLocaleLowerCase('ja-JP')
    .includes(needle))
}

export function createContactDirectoryApi(invoker: ContactDirectoryInvoker): ContactDirectoryApi {
  return {
    listContacts: async () => {
      const result = await invoker.listContacts()
      if (!result.success) throw new ContactDirectoryError('担当者一覧を取得できませんでした。', 'transport-error')
      return parseContactDirectory(result.data)
    },
  }
}
