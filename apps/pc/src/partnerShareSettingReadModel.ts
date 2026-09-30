import { isDataverseGuid } from './dataverseIds'

export type PartnerShareSettingPrincipalKind = 'User' | 'Team'
export type PartnerShareSettingAccessLevel = 'Read' | 'Write'
export type PartnerShareSettingStatus = 'Active' | 'Revoked'

export type PartnerShareSettingView = {
  settingId: string
  partnerId: string
  principalKind: PartnerShareSettingPrincipalKind
  principalId: string
  principalName?: string
  accessLevel: PartnerShareSettingAccessLevel
  status: PartnerShareSettingStatus
  requestKey: string
  isProtectedManagementPath: boolean
  isManagedProjection: boolean
}

export type PartnerShareSettingReadOperationResult = {
  success: boolean
  data: unknown[]
  error?: unknown
}

export type PartnerShareSettingReadInvoker = {
  list: (partnerId: string) => Promise<PartnerShareSettingReadOperationResult>
}

export type PartnerShareSettingReadApi = {
  list: (partnerId: string) => Promise<PartnerShareSettingView[]>
}

export class PartnerShareSettingReadError extends Error {
  readonly code?: string

  constructor(message: string, code?: string) {
    super(message)
    this.name = 'PartnerShareSettingReadError'
    this.code = code
  }
}

type RawPartnerShareSetting = {
  pl_partnersharesettingid?: unknown
  _pl_partnerlookup_value?: unknown
  pl_principalkindcode?: unknown
  _pl_userprincipallookup_value?: unknown
  _pl_teamprincipallookup_value?: unknown
  pl_userprincipallookupname?: unknown
  pl_teamprincipallookupname?: unknown
  '_pl_userprincipallookup_value@OData.Community.Display.V1.FormattedValue'?: unknown
  '_pl_teamprincipallookup_value@OData.Community.Display.V1.FormattedValue'?: unknown
  pl_accesslevelcode?: unknown
  pl_settingstatuscode?: unknown
  pl_requestkey?: unknown
  pl_isprotectedmanagementpath?: unknown
  pl_ismanagedprojection?: unknown
}

const formattedUserPrincipalLookupName = '_pl_userprincipallookup_value@OData.Community.Display.V1.FormattedValue'
const formattedTeamPrincipalLookupName = '_pl_teamprincipallookup_value@OData.Community.Display.V1.FormattedValue'

function requireGuid(value: unknown, fieldName: string): string {
  if (typeof value !== 'string' || !isDataverseGuid(value)) throw new PartnerShareSettingReadError(`${fieldName}の応答が不正です。`, 'invalid-response')
  return value
}

function parseChoice(value: unknown, fieldName: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value)) throw new PartnerShareSettingReadError(`${fieldName}の応答が不正です。`, 'invalid-response')
  return value
}

export function parsePartnerShareSettingView(raw: unknown, requestedPartnerId: string): PartnerShareSettingView {
  if (!raw || typeof raw !== 'object') throw new PartnerShareSettingReadError('共有設定の応答が不正です。', 'invalid-response')
  const value = raw as RawPartnerShareSetting
  const settingId = requireGuid(value.pl_partnersharesettingid, '共有設定ID')
  const partnerId = requireGuid(value._pl_partnerlookup_value, 'PartnerId')
  if (partnerId.toLowerCase() !== requestedPartnerId.toLowerCase()) throw new PartnerShareSettingReadError('別会社の共有設定が応答に含まれています。', 'invalid-response')

  const principalKindCode = parseChoice(value.pl_principalkindcode, 'PrincipalKind')
  const userId = value._pl_userprincipallookup_value
  const teamId = value._pl_teamprincipallookup_value
  const hasUser = typeof userId === 'string' && userId.length > 0
  const hasTeam = typeof teamId === 'string' && teamId.length > 0
  if (hasUser === hasTeam) throw new PartnerShareSettingReadError('共有設定のprincipal応答が排他的ではありません。', 'invalid-response')

  const principalKind = principalKindCode === 100000000 ? 'User' : principalKindCode === 100000001 ? 'Team' : undefined
  if (!principalKind) throw new PartnerShareSettingReadError('PrincipalKindの応答が不正です。', 'invalid-response')
  const principalId = requireGuid(principalKind === 'User' ? userId : teamId, 'PrincipalId')
  const accessLevelCode = parseChoice(value.pl_accesslevelcode, 'AccessLevel')
  const accessLevel = accessLevelCode === 100000000 ? 'Read' : accessLevelCode === 100000001 ? 'Write' : undefined
  if (!accessLevel) throw new PartnerShareSettingReadError('AccessLevelの応答が不正です。', 'invalid-response')
  const statusCode = parseChoice(value.pl_settingstatuscode, 'SettingStatus')
  const status = statusCode === 100000000 ? 'Active' : statusCode === 100000001 ? 'Revoked' : undefined
  if (!status) throw new PartnerShareSettingReadError('SettingStatusの応答が不正です。', 'invalid-response')
  if (typeof value.pl_requestkey !== 'string' || !value.pl_requestkey.trim()) throw new PartnerShareSettingReadError('共有設定を正しく読み込めませんでした。', 'invalid-response')
  if (typeof value.pl_isprotectedmanagementpath !== 'boolean') throw new PartnerShareSettingReadError('保護経路フラグの応答が不正です。', 'invalid-response')
  if (value.pl_ismanagedprojection !== undefined && value.pl_ismanagedprojection !== null && typeof value.pl_ismanagedprojection !== 'boolean') throw new PartnerShareSettingReadError('PL管理フラグの応答が不正です。', 'invalid-response')

  const principalName = principalKind === 'User'
    ? value.pl_userprincipallookupname ?? value[formattedUserPrincipalLookupName]
    : value.pl_teamprincipallookupname ?? value[formattedTeamPrincipalLookupName]
  return {
    settingId,
    partnerId,
    principalKind,
    principalId,
    principalName: typeof principalName === 'string' && principalName ? principalName : undefined,
    accessLevel,
    status,
    requestKey: value.pl_requestkey.trim(),
    isProtectedManagementPath: value.pl_isprotectedmanagementpath,
    isManagedProjection: value.pl_ismanagedprojection === true,
  }
}

export function createPartnerShareSettingReadApi(invoker: PartnerShareSettingReadInvoker): PartnerShareSettingReadApi {
  return {
    list: async partnerId => {
      if (typeof partnerId !== 'string' || !isDataverseGuid(partnerId)) throw new PartnerShareSettingReadError('PartnerIdが正しくありません。', 'invalid-guid')
      const result = await invoker.list(partnerId)
      if (!result.success || !Array.isArray(result.data)) throw new PartnerShareSettingReadError('共有設定を取得できませんでした。', 'transport-error')
      return result.data.map(value => parsePartnerShareSettingView(value, partnerId))
    },
  }
}
