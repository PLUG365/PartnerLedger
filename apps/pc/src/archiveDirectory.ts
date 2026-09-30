import { isDataverseGuid } from './dataverseIds'

export type ArchivePartnerItem = {
  id: string
  name: string
  tradingStatus: '終了'
  mainOwnerName?: string
  ownerName?: string
  registeredBy?: string
  registeredAt?: string
  state: 'Active' | 'Inactive'
}

export type ArchiveContactItem = {
  id: string
  name: string
  partnerId: string
  partnerName: string
  departmentRole?: string
  status: '退職'
  registeredAt?: string
  state: 'Active' | 'Inactive'
}

export type ArchiveDirectoryOperationResult = {
  success: boolean
  data: unknown
  error?: unknown
}

export type ArchiveDirectoryInvoker = {
  listArchivedPartners: () => Promise<ArchiveDirectoryOperationResult>
  listArchivedContacts: () => Promise<ArchiveDirectoryOperationResult>
}

export type ArchiveDirectoryApi = {
  listArchivedPartners: () => Promise<ArchivePartnerItem[]>
  listArchivedContacts: () => Promise<ArchiveContactItem[]>
}

export type ArchiveDirectoryLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; partners: ArchivePartnerItem[]; contacts: ArchiveContactItem[]}
  | {status: 'error'; message: string}

export class ArchiveDirectoryError extends Error {
  readonly code: 'invalid-response' | 'transport-error' | 'operation-rejected'

  constructor(message: string, code: ArchiveDirectoryError['code']) {
    super(message)
    this.name = 'ArchiveDirectoryError'
    this.code = code
  }
}

const formattedPartnerLookupName = '_pl_partnerlookup_value@OData.Community.Display.V1.FormattedValue'
const formattedMainOwnerName = '_pl_mainownerlookup_value@OData.Community.Display.V1.FormattedValue'
const formattedRegisteredByName = '_pl_registeredbylookup_value@OData.Community.Display.V1.FormattedValue'
const formattedOwnerName = '_ownerid_value@OData.Community.Display.V1.FormattedValue'
const formattedCreatedByName = '_createdby_value@OData.Community.Display.V1.FormattedValue'

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireString(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new ArchiveDirectoryError(field + 'が不正です。', 'invalid-response')
  return value.trim()
}

function optionalString(value: unknown, field: string): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new ArchiveDirectoryError(field + 'が不正です。', 'invalid-response')
  return value.trim() || undefined
}

function requireGuid(value: unknown, field: string): string {
  const guid = requireString(value, field)
  if (!isDataverseGuid(guid)) throw new ArchiveDirectoryError(field + 'が不正です。', 'invalid-response')
  return guid
}

function requireState(value: unknown, field: string): ArchivePartnerItem['state'] {
  if (value === 0 || value === '0') return 'Active'
  if (value === 1 || value === '1') return 'Inactive'
  throw new ArchiveDirectoryError(field + 'が不正です。', 'invalid-response')
}

function requireEndedTradingStatus(value: Record<string, unknown>): void {
  const name = optionalString(value.pl_tradingstatuscodename, '取引状態')
  const code = value.pl_tradingstatuscode
  const isEnded = name === '終了' || code === 100000003 || code === '100000003'
  if (!isEnded) throw new ArchiveDirectoryError('アーカイブ取引先の取引状態が不正です。', 'invalid-response')
}

function requireRetiredStatus(value: Record<string, unknown>): void {
  const name = optionalString(value.pl_statuscodename, '在籍状態')
  const code = value.pl_statuscode
  const isRetired = name === '退職' || code === 100000002 || code === '100000002'
  if (!isRetired) throw new ArchiveDirectoryError('アーカイブ担当者の在籍状態が不正です。', 'invalid-response')
}

function optionalDate(value: unknown, field: string): string | undefined {
  const date = optionalString(value, field)
  if (date !== undefined && Number.isNaN(Date.parse(date))) {
    throw new ArchiveDirectoryError(field + 'が不正です。', 'invalid-response')
  }
  return date
}

function parseArchivedPartner(value: unknown, index: number): ArchivePartnerItem {
  if (!isRecord(value)) throw new ArchiveDirectoryError('終了取引先一覧の' + (index + 1) + '件目が不正です。', 'invalid-response')
  const id = requireGuid(value.pl_partnerid, 'PartnerId')
  const name = requireString(value.pl_name, '取引先名')
  const state = requireState(value.statecode, '取引先の状態')
  requireEndedTradingStatus(value)
  return {
    id,
    name,
    tradingStatus: '終了',
    mainOwnerName: optionalString(value.pl_mainownerlookupname, '主担当')
      ?? optionalString(value[formattedMainOwnerName], '主担当'),
    ownerName: optionalString(value.owneridname, '所有者')
      ?? optionalString(value[formattedOwnerName], '所有者'),
    registeredBy: optionalString(value.pl_registeredbylookupname, '登録者')
      ?? optionalString(value[formattedRegisteredByName], '登録者')
      ?? optionalString(value.createdbyname, '登録者')
      ?? optionalString(value[formattedCreatedByName], '登録者'),
    registeredAt: optionalDate(value.pl_registeredat, '登録日'),
    state,
  }
}

function parseArchivedContact(value: unknown, index: number): ArchiveContactItem {
  if (!isRecord(value)) throw new ArchiveDirectoryError('退職担当者一覧の' + (index + 1) + '件目が不正です。', 'invalid-response')
  const id = requireGuid(value.pl_contactid, 'ContactId')
  const name = requireString(value.pl_name, '担当者名')
  const partnerId = requireGuid(value._pl_partnerlookup_value, '親取引先Id')
  const state = requireState(value.statecode, '担当者の状態')
  requireRetiredStatus(value)
  return {
    id,
    name,
    partnerId,
    partnerName: optionalString(value.pl_partnerlookupname, '親取引先名')
      ?? optionalString(value[formattedPartnerLookupName], '親取引先名')
      ?? '取引先名未取得',
    departmentRole: optionalString(value.pl_departmentrole, '部署・役職'),
    status: '退職',
    registeredAt: optionalDate(value.pl_registeredat, '登録日'),
    state,
  }
}

function parseUnique<T>(data: unknown, label: string, parse: (value: unknown, index: number) => T, idOf: (item: T) => string): T[] {
  if (!Array.isArray(data)) throw new ArchiveDirectoryError(label + 'の配列応答がありません。', 'invalid-response')
  const items = data.map(parse)
  const ids = new Set(items.map(item => idOf(item).toLowerCase()))
  if (ids.size !== items.length) throw new ArchiveDirectoryError(label + 'に重複したIDがあります。', 'invalid-response')
  return items
}

export function parseArchivedPartners(data: unknown): ArchivePartnerItem[] {
  return parseUnique(data, '終了取引先一覧', parseArchivedPartner, item => item.id)
}

export function parseArchivedContacts(data: unknown): ArchiveContactItem[] {
  return parseUnique(data, '退職担当者一覧', parseArchivedContact, item => item.id)
}

function requireOperationSuccess(result: ArchiveDirectoryOperationResult, operation: string): void {
  if (!result.success) throw new ArchiveDirectoryError(operation + 'を取得できませんでした。', 'operation-rejected')
}

export function createArchiveDirectoryApi(invoker: ArchiveDirectoryInvoker): ArchiveDirectoryApi {
  return {
    listArchivedPartners: async () => {
      let result: ArchiveDirectoryOperationResult
      try {
        result = await invoker.listArchivedPartners()
      } catch {
        throw new ArchiveDirectoryError('終了取引先一覧を取得できませんでした。', 'transport-error')
      }
      requireOperationSuccess(result, '終了取引先一覧')
      return parseArchivedPartners(result.data)
    },
    listArchivedContacts: async () => {
      let result: ArchiveDirectoryOperationResult
      try {
        result = await invoker.listArchivedContacts()
      } catch {
        throw new ArchiveDirectoryError('退職担当者一覧を取得できませんでした。', 'transport-error')
      }
      requireOperationSuccess(result, '退職担当者一覧')
      return parseArchivedContacts(result.data)
    },
  }
}
