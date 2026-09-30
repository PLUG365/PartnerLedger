import { isDataverseGuid } from './dataverseIds'

export type ContractReadStatus = '締結済み' | '終了' | '未設定'

/**
 * PCのライブ契約表示が扱う最小契約。
 * 契約の登録・更新・承認判断はこのRead契約へ含めない。
 */
export type ContractReadItem = {
  id: string
  name: string
  partnerId: string
  partnerName?: string
  contractTypeId?: string
  contractTypeName?: string
  status: ContractReadStatus
  endDate?: string
  noticeDate?: string
  decisionDate?: string
  autoRenew?: boolean
  link?: string
}

export type ContractReadOperationResult = {
  success: boolean
  data: unknown
  error?: unknown
}

export type ContractReadInvoker = {
  listContracts: (partnerId: string) => Promise<ContractReadOperationResult>
}

export type ContractReadApi = {
  listContracts: (partnerId: string) => Promise<ContractReadItem[]>
}

export type ContractReadLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; contracts: ContractReadItem[]}
  | {status: 'error'; message: string}

export class ContractReadError extends Error {
  readonly code: string

  constructor(message: string, code: string) {
    super(message)
    this.name = 'ContractReadError'
    this.code = code
  }
}

const statusByCode: Record<string, Exclude<ContractReadStatus, '未設定'>> = {
  '100000000': '締結済み',
  '100000001': '終了',
}
const statuses: readonly ContractReadStatus[] = ['締結済み', '終了']
const formattedContractTypeLookupName = '_pl_contracttypelookup_value@OData.Community.Display.V1.FormattedValue'
const formattedPartnerLookupName = '_pl_partnerlookup_value@OData.Community.Display.V1.FormattedValue'

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireString(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new ContractReadError(`${field}が不正です。`, 'invalid-response')
  return value.trim()
}

function optionalString(value: unknown, field: string): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new ContractReadError(`${field}が不正です。`, 'invalid-response')
  return value.trim() || undefined
}

function optionalGuid(value: unknown, field: string): string | undefined {
  const normalized = optionalString(value, field)
  if (normalized === undefined) return undefined
  if (!isDataverseGuid(normalized)) throw new ContractReadError(`${field}が不正です。`, 'invalid-response')
  return normalized
}

function optionalDate(value: unknown, field: string): string | undefined {
  const normalized = optionalString(value, field)
  if (normalized === undefined) return undefined
  if (Number.isNaN(Date.parse(normalized))) throw new ContractReadError(`${field}が不正です。`, 'invalid-response')
  return normalized
}

function optionalBoolean(value: unknown, field: string): boolean | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'boolean') throw new ContractReadError(`${field}が不正です。`, 'invalid-response')
  return value
}

function requireStatus(nameValue: unknown, codeValue: unknown): ContractReadStatus {
  const name = optionalString(nameValue, '契約状態')
  if (name && statuses.includes(name as ContractReadStatus)) return name as ContractReadStatus
  const code = typeof codeValue === 'number' || typeof codeValue === 'string' ? String(codeValue) : undefined
  const resolved = code ? statusByCode[code] : undefined
  if (resolved) return resolved
  if (name === undefined && (codeValue === undefined || codeValue === null || codeValue === '')) return '未設定'
  throw new ContractReadError('契約状態が不正です。', 'invalid-response')
}

function resolveLabel(value: Record<string, unknown>, generatedName: string, formattedName: string, field: string): string | undefined {
  return optionalString(value[generatedName], field) ?? optionalString(value[formattedName], field)
}

function parseItem(value: unknown, index: number, expectedPartnerId?: string): ContractReadItem {
  if (!isRecord(value)) throw new ContractReadError(`契約一覧の${index + 1}件目が不正です。`, 'invalid-response')

  const id = requireString(value.pl_contractid, 'ContractId')
  const name = requireString(value.pl_name, '契約名')
  const partnerId = requireString(value._pl_partnerlookup_value, '親取引先Id')
  if (!isDataverseGuid(id)) throw new ContractReadError(`契約一覧の${index + 1}件目のContractIdが不正です。`, 'invalid-response')
  if (!isDataverseGuid(partnerId)) throw new ContractReadError(`契約一覧の${index + 1}件目の親取引先Idが不正です。`, 'invalid-response')
  if (expectedPartnerId && partnerId.toLowerCase() !== expectedPartnerId.toLowerCase()) {
    throw new ContractReadError(`契約一覧の${index + 1}件目の親取引先が不正です。`, 'invalid-response')
  }

  // The server query filters active rows. Never present a contradictory row as live.
  if (value.statecode !== undefined && value.statecode !== 0 && value.statecode !== '0') {
    throw new ContractReadError(`契約一覧の${index + 1}件目の状態が不正です。`, 'invalid-response')
  }

  return {
    id,
    name,
    partnerId,
    partnerName: resolveLabel(value, 'pl_partnerlookupname', formattedPartnerLookupName, '親取引先名'),
    contractTypeId: optionalGuid(value._pl_contracttypelookup_value, '契約種類Id'),
    contractTypeName: resolveLabel(value, 'pl_contracttypelookupname', formattedContractTypeLookupName, '契約種類名'),
    status: requireStatus(value.pl_contractstatuscodename, value.pl_contractstatuscode),
    endDate: optionalDate(value.pl_enddate, '契約終了日'),
    noticeDate: optionalDate(value.pl_noticedate, '解約通知期限'),
    decisionDate: optionalDate(value.pl_decisiondate, '更新判断期限'),
    autoRenew: optionalBoolean(value.pl_autorenew, '自動更新'),
    link: optionalString(value.pl_link, '契約書リンク'),
  }
}

export function parseContractRead(data: unknown, expectedPartnerId?: string): ContractReadItem[] {
  if (!Array.isArray(data)) throw new ContractReadError('契約一覧の配列応答がありません。', 'invalid-response')
  if (expectedPartnerId && !isDataverseGuid(expectedPartnerId)) throw new ContractReadError('親取引先Idが不正です。', 'invalid-input')
  const contracts = data.map((value, index) => parseItem(value, index, expectedPartnerId))
  const ids = new Set(contracts.map(contract => contract.id))
  if (ids.size !== contracts.length) throw new ContractReadError('契約一覧に重複したIDがあります。', 'invalid-response')
  return contracts
}

export function createContractReadApi(invoker: ContractReadInvoker): ContractReadApi {
  return {
    listContracts: async partnerId => {
      if (!isDataverseGuid(partnerId)) throw new ContractReadError('親取引先Idが不正です。', 'invalid-input')
      const result = await invoker.listContracts(partnerId)
      if (!result.success) throw new ContractReadError('契約一覧を取得できませんでした。', 'transport-error')
      return parseContractRead(result.data, partnerId)
    },
  }
}
