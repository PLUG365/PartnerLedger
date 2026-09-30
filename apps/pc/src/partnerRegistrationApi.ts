import { isDataverseGuid } from './dataverseIds'

/** 取引先登録の保存処理が返す結果。成功時は保存した行を data に持つ。 */
export type PartnerRegistrationOperationResult = { success: boolean; data: Record<string, unknown>; error?: unknown }

export type PartnerRegistrationRequest = {
  name: string
  industry?: string
  address?: string
  phone?: string
  mainOwnerId: string
  idempotencyKey?: string
}

export type PartnerRegistrationResult = {
  partnerId: string
  isReplay: boolean
}

type ResolvedPartnerRegistrationRequest = PartnerRegistrationRequest & {idempotencyKey: string}

export type PartnerRegistrationInvoker = {
  registerPartner: (request: ResolvedPartnerRegistrationRequest) => Promise<PartnerRegistrationOperationResult>
}

export type PartnerLedgerRegistrationApi = {
  registerPartner: (request: PartnerRegistrationRequest) => Promise<PartnerRegistrationResult>
}

export class PartnerRegistrationApiError extends Error {
  readonly code?: string

  constructor(message: string, code?: string) {
    super(message)
    this.name = 'PartnerRegistrationApiError'
    this.code = code
  }
}

let idempotencySequence = 0

export function createPartnerRegistrationIdempotencyKey(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `RegisterPartner-${uuid}`
  idempotencySequence += 1
  return `RegisterPartner-${Date.now().toString(36)}-${idempotencySequence.toString(36)}`
}

function requireText(value: string | undefined, fieldName: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new PartnerRegistrationApiError(`${fieldName}を指定してください。`, 'required')
  return value.trim()
}

function requireOptionalText(value: string | undefined, fieldName: string, maxLength: number): string | undefined {
  if (value === undefined || value.trim() === '') return undefined
  const normalized = value.trim()
  if (normalized.length > maxLength) throw new PartnerRegistrationApiError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  return normalized
}

function requireGuid(value: string, fieldName: string): string {
  const normalized = requireText(value, fieldName)
  if (!isDataverseGuid(normalized)) throw new PartnerRegistrationApiError(`${fieldName}が正しくありません。`, 'invalid-guid')
  return normalized
}

function resolveIdempotencyKey(value?: string): string {
  const normalized = value?.trim()
  return normalized || createPartnerRegistrationIdempotencyKey()
}

function errorText(error: unknown): string {
  if (!error || typeof error !== 'object') return typeof error === 'string' ? error : ''
  const candidate = error as {message?: unknown; response?: unknown; cause?: unknown}
  return [typeof candidate.message === 'string' ? candidate.message : '', errorText(candidate.response), errorText(candidate.cause)].join(' ')
}

function normalizeResponse(result: PartnerRegistrationOperationResult): PartnerRegistrationResult {
  // 登録時の初期共有で、サーバーが主担当を先に検査して拒否した（2026-09-28）。再試行では通らない。
  if (!result.success && errorText(result.error).includes('主担当に選んだユーザーには共有できません')) {
    throw new PartnerRegistrationApiError('選択した主担当を利用できないため、登録できません。', 'main-owner-unavailable')
  }
  if (!result.success) throw new PartnerRegistrationApiError('保存結果が未確定の可能性があります。入力を保持したまま同じ操作を再試行してください。', 'transport-error')

  const errorCode = typeof result.data.ErrorCode === 'string' && result.data.ErrorCode ? result.data.ErrorCode : 'operation-rejected'
  if (result.data.Success !== true) {
    const message = errorCode === 'duplicate-visible'
      ? '同じ会社名の取引先が見つかったため、登録を中止しました。'
      : errorCode === 'main-owner-unavailable'
        ? '選択した主担当を利用できないため、登録できません。'
        : `取引先登録がサーバーで拒否されました（${errorCode}）。`
    throw new PartnerRegistrationApiError(message, errorCode)
  }

  const partnerId = typeof result.data.PartnerId === 'string' ? result.data.PartnerId : ''
  if (!isDataverseGuid(partnerId) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/i.test(partnerId)) throw new PartnerRegistrationApiError('保存結果を確認できませんでした。取引先リストで確認してください。', 'invalid-response')
  return {
    partnerId,
    isReplay: result.data.IsReplay === true,
  }
}

export function createPartnerLedgerRegistrationApi(invoker: PartnerRegistrationInvoker): PartnerLedgerRegistrationApi {
  return {
    registerPartner: async request => {
      const name = requireText(request.name, '会社名')
      const mainOwnerId = requireGuid(request.mainOwnerId, '主担当')
      const industry = requireOptionalText(request.industry, '業種', 200)
      const address = requireOptionalText(request.address, '住所', 500)
      const phone = requireOptionalText(request.phone, '代表電話', 200)
      const idempotencyKey = resolveIdempotencyKey(request.idempotencyKey)
      if (idempotencyKey.length > 200) throw new PartnerRegistrationApiError('登録の識別子が長すぎます。', 'too-long')
      return normalizeResponse(await invoker.registerPartner({name, industry, address, phone, mainOwnerId, idempotencyKey}))
    },
  }
}
