import { isDataverseGuid } from './dataverseIds'

export type ContractTypeReadItem = {
  id: string
  name: string
  displayOrder?: number
}

export type ContractTypeReadApi = {
  listSelectableContractTypes: () => Promise<ContractTypeReadItem[]>
}

export class ContractTypeReadError extends Error {
  readonly code: string

  constructor(message: string, code = 'invalid-response') {
    super(message)
    this.name = 'ContractTypeReadError'
    this.code = code
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireText(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new ContractTypeReadError(`${field}が不正です。`)
  return value.trim()
}

function optionalDisplayOrder(value: unknown): number | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new ContractTypeReadError('契約種類の表示順が不正です。')
  return value
}

export function parseSelectableContractTypes(data: unknown): ContractTypeReadItem[] {
  if (!Array.isArray(data)) throw new ContractTypeReadError('契約種類マスタの配列応答がありません。')
  const contractTypes = data.map((value, index) => {
    if (!isRecord(value)) throw new ContractTypeReadError(`契約種類マスタの${index + 1}件目が不正です。`)
    const id = requireText(value.pl_contracttypeid, '契約種類Id')
    const name = requireText(value.pl_name, '契約種類名')
    if (!isDataverseGuid(id)) throw new ContractTypeReadError(`契約種類マスタの${index + 1}件目のIdが不正です。`)
    if (!(value.statecode === 0 || value.statecode === '0')) throw new ContractTypeReadError(`契約種類マスタの${index + 1}件目がActiveではありません。`)
    if (value.pl_selectable !== true) throw new ContractTypeReadError(`契約種類マスタの${index + 1}件目が選択不可です。`)
    return {id, name, displayOrder: optionalDisplayOrder(value.pl_displayorder)}
  })
  const ids = new Set(contractTypes.map(contractType => contractType.id.toLowerCase()))
  if (ids.size !== contractTypes.length) throw new ContractTypeReadError('契約種類マスタに重複したIdがあります。')
  return [...contractTypes].sort((left, right) => {
    const orderDifference = (left.displayOrder ?? Number.MAX_SAFE_INTEGER) - (right.displayOrder ?? Number.MAX_SAFE_INTEGER)
    return orderDifference || left.name.localeCompare(right.name, 'ja')
  })
}

export type ContractTypeReadInvoker = {
  listSelectableContractTypes: () => Promise<{success: boolean; data: unknown; error?: unknown}>
}

export function createContractTypeReadApi(invoker: ContractTypeReadInvoker): ContractTypeReadApi {
  return {
    listSelectableContractTypes: async () => {
      let result
      try {
        result = await invoker.listSelectableContractTypes()
      } catch {
        throw new ContractTypeReadError('契約種類マスタを取得できません。', 'transport-error')
      }
      if (!result.success) throw new ContractTypeReadError('契約種類マスタを取得できません。', 'transport-error')
      return parseSelectableContractTypes(result.data)
    },
  }
}
