import { isDataverseGuid } from './dataverseIds'

export type PartnerSharePrincipalKind = 'User' | 'Team'
export type PartnerShareAccessLevel = 'Read' | 'Write'

export type PartnerShareSettingCreateRequest = {
  partnerId: string
  principalKind: PartnerSharePrincipalKind
  principalId: string
  accessLevel: PartnerShareAccessLevel
  requestKey: string
}

export type PartnerShareSettingUpdateRequest = PartnerShareSettingCreateRequest & {
  settingId: string
}

export type PartnerShareSettingResult = {
  settingId: string
  isReplay: boolean
}

export type PartnerShareSettingOperationResult = {
  success: boolean
  data: Record<string, unknown>
  error?: unknown
}

type ResolvedCreateRequest = PartnerShareSettingCreateRequest
type ResolvedUpdateRequest = PartnerShareSettingUpdateRequest

export type PartnerShareSettingInvoker = {
  create: (request: ResolvedCreateRequest) => Promise<PartnerShareSettingOperationResult>
  update: (request: ResolvedUpdateRequest) => Promise<PartnerShareSettingOperationResult>
  revoke: (request: Pick<PartnerShareSettingUpdateRequest, 'settingId'>) => Promise<PartnerShareSettingOperationResult>
}

export type PartnerShareSettingApi = {
  create: (request: PartnerShareSettingCreateRequest) => Promise<PartnerShareSettingResult>
  update: (request: PartnerShareSettingUpdateRequest) => Promise<PartnerShareSettingResult>
  revoke: (settingId: string) => Promise<PartnerShareSettingResult>
}

let requestKeySequence = 0

export function createPartnerShareSettingRequestKey(operation: 'Create' | 'Update' | 'Revoke' = 'Create'): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `PartnerShareSetting-${operation}-${uuid}`
  requestKeySequence += 1
  return `PartnerShareSetting-${operation}-${Date.now().toString(36)}-${requestKeySequence.toString(36)}`
}

export class PartnerShareSettingApiError extends Error {
  readonly code?: string

  constructor(message: string, code?: string) {
    super(message)
    this.name = 'PartnerShareSettingApiError'
    this.code = code
  }
}

function readHttpStatus(error: unknown): number | undefined {
  if (!error || typeof error !== 'object') return undefined
  const candidate = error as {status?: unknown; statusCode?: unknown; response?: unknown; cause?: unknown}
  const direct = candidate.status ?? candidate.statusCode
  if (typeof direct === 'number' && Number.isInteger(direct)) return direct
  if (typeof direct === 'string' && /^\d{3}$/.test(direct)) return Number(direct)
  return readHttpStatus(candidate.response) ?? readHttpStatus(candidate.cause)
}

function readErrorText(error: unknown): string {
  if (!error || typeof error !== 'object') return ''
  const candidate = error as {name?: unknown; message?: unknown; response?: unknown; cause?: unknown}
  const direct = [candidate.name, candidate.message].filter((value): value is string => typeof value === 'string').join(' ')
  const nested = [readErrorText(candidate.response), readErrorText(candidate.cause)].filter(Boolean).join(' ')
  return `${direct} ${nested}`.trim()
}

function failureError(error: unknown): PartnerShareSettingApiError {
  const status = readHttpStatus(error)
  const errorText = readErrorText(error)
  // 共有先の側が理由の拒否。呼び出した人の権限不足と区別する（2026-09-28、検証環境で再現）。
  if (/support\s+user\s+has\s+insufficient\s+privileges/i.test(errorText)) {
    return new PartnerShareSettingApiError('この共有先（Microsoftのサポート用アカウントなど）には共有できません。別の人またはグループを選んでください。入力は保持されています。', 'principal-not-shareable')
  }
  const looksLikeAuthorizationDeny = /\b(?:401|403)\b|forbidden|access\s+denied|insufficient\s+privilege|does\s+not\s+have[^.]{0,120}(?:access|right|privilege)/i.test(errorText)
  if (status === 401 || status === 403 || looksLikeAuthorizationDeny) {
    return new PartnerShareSettingApiError('現在の権限では共有設定を変更できません。入力は保持されています。', 'authorization-denied')
  }
  if (status !== undefined && status >= 400 && status < 500) {
    return new PartnerShareSettingApiError('共有設定を変更できませんでした。入力または現在の状態を確認してください。', 'operation-rejected')
  }
  return new PartnerShareSettingApiError('共有設定を保存できたか確認できませんでした。再読み込みして確認してください。', 'transport-error')
}

function requireText(value: string, fieldName: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new PartnerShareSettingApiError(`${fieldName}を指定してください。`, 'required')
  return value.trim()
}

function requireGuid(value: string, fieldName: string): string {
  const normalized = requireText(value, fieldName)
  if (!isDataverseGuid(normalized)) throw new PartnerShareSettingApiError(`${fieldName}が正しくありません。`, 'invalid-guid')
  return normalized
}

function requireChoice<T extends string>(value: T, allowed: readonly T[], fieldName: string): T {
  if (!allowed.includes(value)) throw new PartnerShareSettingApiError(`${fieldName}が不正です。`, 'invalid-choice')
  return value
}

function resolveCreateRequest(request: PartnerShareSettingCreateRequest): ResolvedCreateRequest {
  return {
    partnerId: requireGuid(request.partnerId, 'PartnerId'),
    principalKind: requireChoice(request.principalKind, ['User', 'Team'], 'PrincipalKind'),
    principalId: requireGuid(request.principalId, 'PrincipalId'),
    accessLevel: requireChoice(request.accessLevel, ['Read', 'Write'], 'AccessLevel'),
    requestKey: requireText(request.requestKey, 'RequestKey'),
  }
}

function resolveUpdateRequest(request: PartnerShareSettingUpdateRequest): ResolvedUpdateRequest {
  return {
    ...resolveCreateRequest(request),
    settingId: requireGuid(request.settingId, 'SettingId'),
  }
}

function normalizeResult(result: PartnerShareSettingOperationResult, fallbackSettingId?: string): PartnerShareSettingResult {
  if (!result.success) throw failureError(result.error)
  const rawSettingId = result.data.SettingId
  const settingId = typeof rawSettingId === 'string' && isDataverseGuid(rawSettingId)
    ? rawSettingId
    : fallbackSettingId
  if (!settingId || !isDataverseGuid(settingId)) throw new PartnerShareSettingApiError('保存結果に不正な共有設定IDが含まれています。', 'invalid-response')
  return {settingId, isReplay: result.data.IsReplay === true}
}

export function createPartnerShareSettingApi(invoker: PartnerShareSettingInvoker): PartnerShareSettingApi {
  return {
    create: async request => normalizeResult(await invoker.create(resolveCreateRequest(request))),
    update: async request => {
      const resolved = resolveUpdateRequest(request)
      return normalizeResult(await invoker.update(resolved), resolved.settingId)
    },
    revoke: async settingId => {
      const resolvedSettingId = requireGuid(settingId, 'SettingId')
      return normalizeResult(await invoker.revoke({settingId: resolvedSettingId}), resolvedSettingId)
    },
  }
}
