import { isDataverseGuid } from './dataverseIds'

export type PartnerSharePrincipalKind = 'User' | 'Team'

export type PartnerSharePrincipal = {
  id: string
  name: string
  kind: PartnerSharePrincipalKind
  detail?: string
}

export type PartnerSharePrincipalDirectoryInvoker = {
  list: (query?: string) => Promise<{success: boolean; data: unknown[]; error?: unknown}>
}

export type PartnerSharePrincipalDirectoryApi = {
  list: (query?: string) => Promise<PartnerSharePrincipal[]>
}

export class PartnerSharePrincipalDirectoryError extends Error {
  readonly code: string

  constructor(message: string, code = 'invalid-response') {
    super(message)
    this.name = 'PartnerSharePrincipalDirectoryError'
    this.code = code
  }
}

type RawPrincipal = {
  id?: unknown
  name?: unknown
  kind?: unknown
  detail?: unknown
}

function isRecord(value: unknown): value is RawPrincipal {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requiredText(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new PartnerSharePrincipalDirectoryError(`${field}が不正です。`)
  return value.trim()
}

function parsePrincipal(value: unknown, index: number): PartnerSharePrincipal {
  if (!isRecord(value)) throw new PartnerSharePrincipalDirectoryError(`共有先候補の${index + 1}件目が不正です。`)
  const id = requiredText(value.id, '共有先ID')
  if (!isDataverseGuid(id)) throw new PartnerSharePrincipalDirectoryError(`共有先候補の${index + 1}件目のIDが不正です。`)
  const name = requiredText(value.name, '共有先名')
  if (value.kind !== 'User' && value.kind !== 'Team') throw new PartnerSharePrincipalDirectoryError(`共有先候補の${index + 1}件目の種別が不正です。`)
  const detail = value.detail === undefined || value.detail === null || value.detail === ''
    ? undefined
    : requiredText(value.detail, '共有先補足')
  return {id, name, kind: value.kind, detail}
}

export function createPartnerSharePrincipalDirectoryApi(invoker: PartnerSharePrincipalDirectoryInvoker): PartnerSharePrincipalDirectoryApi {
  return {
    list: async (query?: string) => {
      const result = await invoker.list(query)
      if (!result.success || !Array.isArray(result.data)) throw new PartnerSharePrincipalDirectoryError('共有先候補を取得できませんでした。', 'transport-error')
      const principals = result.data.map(parsePrincipal)
      const ids = new Set(principals.map(principal => principal.id.toLowerCase()))
      if (ids.size !== principals.length) throw new PartnerSharePrincipalDirectoryError('共有先候補に重複したIDがあります。')
      return principals.sort((left, right) => left.name.localeCompare(right.name, 'ja'))
    },
  }
}
