import { isDataverseGuid } from './dataverseIds'

export type PartnerTradingStatus = '未承認' | '取引中' | '休止中' | '終了'

/**
 * PCのライブ取引先一覧が扱う最小表示契約。
 * 担当者・契約・履歴などの未接続データを、架空の空値で補完しない。
 */
export type PartnerDirectoryItem = {
  id: string
  name: string
  industry?: string
  address?: string
  phone?: string
  tradingStatus: PartnerTradingStatus
  /** 今の主担当のsystemuserid（主担当の変更フォームで使う、2026-09-29）。 */
  mainOwnerId?: string
  mainOwnerName?: string
  ownerName?: string
  registeredBy?: string
  registeredAt?: string
  aclVersion: number
}

export type PartnerDirectoryOperationResult = {
  success: boolean
  data: unknown
  error?: unknown
}

export type PartnerDirectoryInvoker = {
  listPartners: () => Promise<PartnerDirectoryOperationResult>
}

export type PartnerDirectoryApi = {
  listPartners: () => Promise<PartnerDirectoryItem[]>
}

export type PartnerDirectoryLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; partners: PartnerDirectoryItem[]}
  | {status: 'error'; message: string}

export class PartnerDirectoryError extends Error {
  readonly code: string

  constructor(message: string, code: string) {
    super(message)
    this.name = 'PartnerDirectoryError'
    this.code = code
  }
}

const tradingStatuses: readonly PartnerTradingStatus[] = ['未承認', '取引中', '休止中', '終了']
const tradingStatusByCode: Record<string, PartnerTradingStatus> = {
  '100000000': '未承認',
  '100000001': '取引中',
  '100000002': '休止中',
  '100000003': '終了',
}
const formattedMainOwnerName = '_pl_mainownerlookup_value@OData.Community.Display.V1.FormattedValue'
const formattedRegisteredByName = '_pl_registeredbylookup_value@OData.Community.Display.V1.FormattedValue'
const formattedOwnerName = '_ownerid_value@OData.Community.Display.V1.FormattedValue'
const formattedCreatedByName = '_createdby_value@OData.Community.Display.V1.FormattedValue'

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireString(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new PartnerDirectoryError(`${field}が不正です。`, 'invalid-response')
  return value.trim()
}

function optionalString(value: unknown, field: string): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new PartnerDirectoryError(`${field}が不正です。`, 'invalid-response')
  return value.trim() || undefined
}

function requireChoice<T extends string>(nameValue: unknown, codeValue: unknown, names: readonly T[], codes: Record<string, T>, field: string): T {
  if (typeof nameValue === 'string' && names.includes(nameValue as T)) return nameValue as T
  const code = typeof codeValue === 'number' || typeof codeValue === 'string' ? String(codeValue) : undefined
  const resolved = code ? codes[code] : undefined
  if (resolved) return resolved
  throw new PartnerDirectoryError(`${field}が不正です。`, 'invalid-response')
}

function requireAclVersion(value: unknown): number {
  const version = typeof value === 'number' || typeof value === 'string' ? Number(value) : NaN
  if (!Number.isInteger(version) || version < 1) throw new PartnerDirectoryError('取引先の情報を正しく読み込めませんでした。', 'invalid-response')
  return version
}

function parseItem(value: unknown, index: number): PartnerDirectoryItem {
  if (!isRecord(value)) throw new PartnerDirectoryError(`取引先一覧の${index + 1}件目が不正です。`, 'invalid-response')

  const id = requireString(value.pl_partnerid, 'PartnerId')
  const name = requireString(value.pl_name, '取引先名')
  if (!isDataverseGuid(id)) throw new PartnerDirectoryError(`取引先一覧の${index + 1}件目のPartnerIdが不正です。`, 'invalid-response')

  // The server query filters active rows. Reject a contradictory response instead of
  // showing an inactive record as if the filter had been applied.
  if (value.statecode !== undefined && value.statecode !== 0 && value.statecode !== '0') {
    throw new PartnerDirectoryError(`取引先一覧の${index + 1}件目の状態が不正です。`, 'invalid-response')
  }

  const tradingStatus = requireChoice(value.pl_tradingstatuscodename, value.pl_tradingstatuscode, tradingStatuses, tradingStatusByCode, '取引状態')
  const registeredAt = optionalString(value.pl_registeredat, '登録日')
  if (registeredAt !== undefined && Number.isNaN(Date.parse(registeredAt))) {
    throw new PartnerDirectoryError(`取引先一覧の${index + 1}件目の登録日が不正です。`, 'invalid-response')
  }

  return {
    id,
    name,
    industry: optionalString(value.pl_industry, '業種'),
    address: optionalString(value.pl_address, '住所'),
    phone: optionalString(value.pl_phone, '代表電話'),
    tradingStatus,
    mainOwnerId: typeof value._pl_mainownerlookup_value === 'string' && isDataverseGuid(value._pl_mainownerlookup_value) ? value._pl_mainownerlookup_value.toLowerCase() : undefined,
    mainOwnerName: optionalString(value.pl_mainownerlookupname, '主担当')
      ?? optionalString(value[formattedMainOwnerName], '主担当'),
    ownerName: optionalString(value.owneridname, '所有者')
      ?? optionalString(value[formattedOwnerName], '所有者'),
    registeredBy: optionalString(value.pl_registeredbylookupname, '登録者')
      ?? optionalString(value[formattedRegisteredByName], '登録者')
      ?? optionalString(value.createdbyname, '登録者')
      ?? optionalString(value[formattedCreatedByName], '登録者'),
    registeredAt,
    aclVersion: requireAclVersion(value.pl_aclversion),
  }
}

export function parsePartnerDirectory(data: unknown): PartnerDirectoryItem[] {
  if (!Array.isArray(data)) throw new PartnerDirectoryError('取引先一覧の配列応答がありません。', 'invalid-response')
  const partners = data.map(parseItem)
  const ids = new Set(partners.map(partner => partner.id))
  if (ids.size !== partners.length) throw new PartnerDirectoryError('取引先一覧に重複したIDがあります。', 'invalid-response')
  return partners
}

export function filterPartnerDirectory(partners: PartnerDirectoryItem[], query: string): PartnerDirectoryItem[] {
  const needle = query.trim().toLocaleLowerCase('ja-JP')
  if (!needle) return partners
  return partners.filter(partner => [partner.id, partner.name, partner.industry, partner.address, partner.phone, partner.tradingStatus, partner.mainOwnerName, partner.ownerName, partner.registeredBy]
    .filter((value): value is string => Boolean(value))
    .join(' ')
    .toLocaleLowerCase('ja-JP')
    .includes(needle))
}

export function createPartnerDirectoryApi(invoker: PartnerDirectoryInvoker): PartnerDirectoryApi {
  return {
    listPartners: async () => {
      const result = await invoker.listPartners()
      if (!result.success) throw new PartnerDirectoryError('取引先一覧を取得できませんでした。', 'transport-error')
      return parsePartnerDirectory(result.data)
    },
  }
}
