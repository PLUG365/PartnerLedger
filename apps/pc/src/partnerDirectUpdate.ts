import { isDataverseGuid } from './dataverseIds'
import type { PartnerDirectoryItem } from './partnerDirectory'

export type PartnerEditTarget = 'companyName' | 'tradingStatus' | 'address' | 'phone' | 'mainOwner'

export type PartnerDirectUpdateInput = {
  partner: PartnerDirectoryItem
  target: PartnerEditTarget
  value: string
}

export type PartnerDirectUpdateService = {
  update: (partnerId: string, fields: Record<string, unknown>) => Promise<PartnerDirectUpdateOperationResult>
}

export type PartnerDirectUpdateOperationResult = {
  success: boolean
  data?: unknown
  error?: unknown
}

export type PartnerDirectUpdateApi = {
  updatePartner: (input: PartnerDirectUpdateInput) => Promise<{partnerId: string}>
}

export class PartnerDirectUpdateError extends Error {
  readonly code: 'required' | 'invalid-guid' | 'invalid-input' | 'too-long' | 'operation-rejected' | 'transport-error'

  constructor(message: string, code: PartnerDirectUpdateError['code']) {
    super(message)
    this.name = 'PartnerDirectUpdateError'
    this.code = code
  }
}

const tradingStatusCode: Record<string, number> = {
  '未承認': 100000000,
  '取引中': 100000001,
  '休止中': 100000002,
  '終了': 100000003,
}

function requiredPartnerId(partner: PartnerDirectoryItem): string {
  if (!isDataverseGuid(partner.id)) throw new PartnerDirectUpdateError('対象の取引先が正しくありません。', 'invalid-guid')
  return partner.id
}

function normalizedText(value: string, label: string, required: boolean): string | null {
  if (typeof value !== 'string') throw new PartnerDirectUpdateError(`${label}が不正です。`, 'invalid-input')
  const normalized = value.trim()
  if (required && !normalized) throw new PartnerDirectUpdateError(`${label}を指定してください。`, 'required')
  if (normalized.length > 200) throw new PartnerDirectUpdateError(`${label}は200文字以内で指定してください。`, 'too-long')
  return normalized || null
}

export function buildPartnerDirectUpdateFields(input: PartnerDirectUpdateInput): {partnerId: string; fields: Record<string, unknown>} {
  const partnerId = requiredPartnerId(input.partner)
  switch (input.target) {
    case 'companyName':
      return {partnerId, fields: {pl_name: normalizedText(input.value, '会社名', true)}}
    case 'tradingStatus': {
      const code = tradingStatusCode[input.value.trim()]
      if (code === undefined) throw new PartnerDirectUpdateError('取引状態が不正です。', 'invalid-input')
      return {partnerId, fields: {pl_tradingstatuscode: code}}
    }
    case 'address':
      return {partnerId, fields: {pl_address: normalizedText(input.value, '住所', false)}}
    case 'phone':
      return {partnerId, fields: {pl_phone: normalizedText(input.value, '代表電話', false)}}
    case 'mainOwner': {
      // 承認が不要な設定のときだけ使う。サーバー（PartnerStandardUpdate）が通常の利用者か・共有を確かめる。
      const owner = input.value.trim().toLowerCase()
      if (!isDataverseGuid(owner)) throw new PartnerDirectUpdateError('主担当を選択してください。', 'required')
      return {partnerId, fields: {'pl_MainOwnerLookup@odata.bind': `/systemusers(${owner})`}}
    }
    default:
      throw new PartnerDirectUpdateError('編集対象が不正です。', 'invalid-input')
  }
}

export function createPartnerDirectUpdateApi(service: PartnerDirectUpdateService): PartnerDirectUpdateApi {
  return {
    updatePartner: async input => {
      const {partnerId, fields} = buildPartnerDirectUpdateFields(input)
      let result: PartnerDirectUpdateOperationResult
      try {
        result = await service.update(partnerId, fields)
      } catch {
        throw new PartnerDirectUpdateError('取引先の直接更新に失敗しました。入力を保持したまま再試行してください。', 'transport-error')
      }
      if (!result.success) throw new PartnerDirectUpdateError('取引先を更新できませんでした。承認設定と権限を確認してください。', 'operation-rejected')
      return {partnerId}
    },
  }
}
