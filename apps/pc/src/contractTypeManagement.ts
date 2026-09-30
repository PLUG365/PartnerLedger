import { isDataverseGuid } from './dataverseIds'

export type ContractTypeAdminItem = {
  id: string
  name: string
  displayOrder?: number
  lifecycleCode?: string
  selectable: boolean
  state: 'Active' | 'Inactive'
}

export type ContractTypeCreateInput = {
  name: string
  displayOrder?: number
  lifecycleCode?: string
}

export type ContractTypeManagementApi = {
  list: () => Promise<ContractTypeAdminItem[]>
  create: (input: ContractTypeCreateInput) => Promise<{id: string}>
  rename: (id: string, name: string) => Promise<void>
  retire: (id: string) => Promise<void>
}

export type ContractTypeManagementInvoker = {
  list: () => Promise<{success: boolean; data: unknown; error?: unknown}>
  create: (record: Record<string, unknown>) => Promise<{success: boolean; data: unknown; error?: unknown}>
  update: (id: string, fields: Record<string, unknown>) => Promise<{success: boolean; data: unknown; error?: unknown}>
}

export class ContractTypeManagementError extends Error {
  readonly code: string

  constructor(message: string, code = 'invalid-input') {
    super(message)
    this.name = 'ContractTypeManagementError'
    this.code = code
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireGuid(value: unknown, field: string): string {
  if (typeof value !== 'string' || !isDataverseGuid(value.trim())) throw new ContractTypeManagementError(`${field}が不正です。`, 'invalid-response')
  return value.trim()
}

function requireName(value: unknown, field = '契約種類名'): string {
  if (typeof value !== 'string' || !value.trim()) throw new ContractTypeManagementError(`${field}を指定してください。`)
  const name = value.trim()
  if (name.length > 200) throw new ContractTypeManagementError(`${field}は200文字以内で指定してください。`)
  return name
}

function optionalDisplayOrder(value: unknown): number | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'number' || !Number.isInteger(value)) throw new ContractTypeManagementError('表示順が不正です。', 'invalid-response')
  return value
}

function optionalLifecycleCode(value: unknown): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new ContractTypeManagementError('ライフサイクルコードが不正です。', 'invalid-response')
  const lifecycleCode = value.trim()
  if (lifecycleCode.length > 200) throw new ContractTypeManagementError('ライフサイクルコードが不正です。', 'invalid-response')
  return lifecycleCode || undefined
}

function requireBoolean(value: unknown, field: string): boolean {
  if (typeof value !== 'boolean') throw new ContractTypeManagementError(`${field}が不正です。`, 'invalid-response')
  return value
}

function parseItem(value: unknown, index: number): ContractTypeAdminItem {
  if (!isRecord(value)) throw new ContractTypeManagementError(`契約種類マスタの${index + 1}件目が不正です。`, 'invalid-response')
  const id = requireGuid(value.pl_contracttypeid, `契約種類マスタの${index + 1}件目のId`)
  const name = requireName(value.pl_name, '契約種類名')
  const stateCode = value.statecode
  const statusCode = value.statuscode
  const state = stateCode === 0 || stateCode === '0' ? 'Active' : stateCode === 1 || stateCode === '1' ? 'Inactive' : undefined
  if (!state) throw new ContractTypeManagementError(`契約種類マスタの${index + 1}件目の状態が不正です。`, 'invalid-response')
  const statusIsActive = statusCode === undefined || statusCode === null || statusCode === '' || statusCode === 1 || statusCode === '1'
  const statusIsInactive = statusCode === 2 || statusCode === '2'
  if ((state === 'Active' && !statusIsActive) || (state === 'Inactive' && !statusIsInactive)) {
    throw new ContractTypeManagementError(`契約種類マスタの${index + 1}件目の状態組合せが不正です。`, 'invalid-response')
  }
  return {
    id,
    name,
    displayOrder: optionalDisplayOrder(value.pl_displayorder),
    lifecycleCode: optionalLifecycleCode(value.pl_lifecyclecode),
    selectable: requireBoolean(value.pl_selectable, '選択可否'),
    state,
  }
}

export function parseContractTypeAdminItems(data: unknown): ContractTypeAdminItem[] {
  if (!Array.isArray(data)) throw new ContractTypeManagementError('契約種類マスタの配列応答がありません。', 'invalid-response')
  const items = data.map(parseItem)
  const ids = new Set<string>()
  for (const item of items) {
    const normalizedId = item.id.toLowerCase()
    if (ids.has(normalizedId)) throw new ContractTypeManagementError('契約種類マスタに重複したIdがあります。', 'invalid-response')
    ids.add(normalizedId)
  }
  return [...items].sort((left, right) => (left.displayOrder ?? Number.MAX_SAFE_INTEGER) - (right.displayOrder ?? Number.MAX_SAFE_INTEGER) || left.name.localeCompare(right.name, 'ja') || left.id.localeCompare(right.id))
}

export function buildContractTypeCreateRecord(input: ContractTypeCreateInput): Record<string, unknown> {
  const name = requireName(input.name)
  const displayOrder = optionalDisplayOrder(input.displayOrder)
  if (displayOrder !== undefined && displayOrder < 0) throw new ContractTypeManagementError('表示順は0以上で指定してください。')
  const lifecycleCode = optionalLifecycleCode(input.lifecycleCode)
  return {
    pl_name: name,
    ...(displayOrder === undefined ? {} : {pl_displayorder: displayOrder}),
    ...(lifecycleCode === undefined ? {} : {pl_lifecyclecode: lifecycleCode}),
    pl_selectable: true,
  }
}

function requireOperationSuccess(result: {success: boolean}, operation: string): void {
  if (!result.success) throw new ContractTypeManagementError(`${operation}に失敗しました。権限と接続を確認してください。`, 'operation-rejected')
}

function requireCreatedId(data: unknown): string {
  if (!isRecord(data)) throw new ContractTypeManagementError('保存結果の契約種類Idを確認できません。', 'invalid-response')
  return requireGuid(data.pl_contracttypeid, '保存結果の契約種類Id')
}

export function createContractTypeManagementApi(invoker: ContractTypeManagementInvoker): ContractTypeManagementApi {
  return {
    list: async () => {
      let result
      try {
        result = await invoker.list()
      } catch {
        throw new ContractTypeManagementError('契約種類マスタを取得できません。', 'transport-error')
      }
      requireOperationSuccess(result, '契約種類マスタの取得')
      return parseContractTypeAdminItems(result.data)
    },
    create: async input => {
      const record = buildContractTypeCreateRecord(input)
      let result
      try {
        result = await invoker.create(record)
      } catch {
        throw new ContractTypeManagementError('契約種類の追加に失敗しました。', 'transport-error')
      }
      requireOperationSuccess(result, '契約種類の追加')
      return {id: requireCreatedId(result.data)}
    },
    rename: async (id, name) => {
      const contractTypeId = requireGuid(id, '契約種類Id')
      const contractTypeName = requireName(name)
      let result
      try {
        result = await invoker.update(contractTypeId, {pl_name: contractTypeName})
      } catch {
        throw new ContractTypeManagementError('契約種類名の変更に失敗しました。', 'transport-error')
      }
      requireOperationSuccess(result, '契約種類名の変更')
    },
    retire: async id => {
      const contractTypeId = requireGuid(id, '契約種類Id')
      let result
      try {
        result = await invoker.update(contractTypeId, {statecode: 1, statuscode: 2, pl_selectable: false})
      } catch {
        throw new ContractTypeManagementError('契約種類の廃止に失敗しました。', 'transport-error')
      }
      requireOperationSuccess(result, '契約種類の廃止')
    },
  }
}
