import { isDataverseGuid } from './dataverseIds'

export type ContactUpdateStatus = '在籍' | '休職' | '退職'

/**
 * 担当者の標準Updateに渡す画面入力。
 * 「未設定」は一覧の表示上の状態であり、保存時には許可しない。
 */
export type ContactUpdateRequest = {
  contactId: string
  name: string
  status: ContactUpdateStatus | '未設定'
  departmentRole?: string
  email?: string
  phone?: string
}

export type ContactUpdateOperationResult = {
  success: boolean
  error?: unknown
}

export type ContactUpdateInvoker = (
  contactId: string,
  fields: Record<string, unknown>,
) => Promise<ContactUpdateOperationResult>

export type ContactUpdateResult = {
  contactId: string
  retired: boolean
}

export type ContactUpdateApi = {
  update: (request: ContactUpdateRequest) => Promise<ContactUpdateResult>
}

export class ContactUpdateApiError extends Error {
  readonly code?: string

  constructor(message: string, code?: string) {
    super(message)
    this.name = 'ContactUpdateApiError'
    this.code = code
  }
}

const statusCodes: Record<ContactUpdateStatus, number> = {
  在籍: 100000000,
  休職: 100000001,
  退職: 100000002,
}

function requireText(value: string | undefined, fieldName: string): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new ContactUpdateApiError(`${fieldName}を指定してください。`, 'required')
  }
  return value.trim()
}

function optionalText(value: string | undefined, fieldName: string, maxLength: number): string | null {
  if (value === undefined || value === null) return null
  if (typeof value !== 'string') {
    throw new ContactUpdateApiError(`${fieldName}が不正です。`, 'invalid-input')
  }
  const normalized = value.trim()
  if (!normalized) return null
  if (normalized.length > maxLength) {
    throw new ContactUpdateApiError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  }
  return normalized
}

function requireGuid(value: string, fieldName: string): string {
  const normalized = requireText(value, fieldName)
  if (!isDataverseGuid(normalized)) {
    throw new ContactUpdateApiError(`${fieldName}が正しくありません。`, 'invalid-guid')
  }
  return normalized
}

function normalizeRequest(request: ContactUpdateRequest) {
  const contactId = requireGuid(request.contactId, '担当者')
  const name = requireText(request.name, '氏名')
  if (name.length > 850) {
    throw new ContactUpdateApiError('氏名は850文字以内で指定してください。', 'too-long')
  }

  const status = request.status
  if (!(status in statusCodes)) {
    throw new ContactUpdateApiError('在籍状態を選択してください。', 'invalid-status')
  }

  return {
    contactId,
    name,
    status: status as ContactUpdateStatus,
    departmentRole: optionalText(request.departmentRole, '部署・役職', 200),
    email: optionalText(request.email, 'メールアドレス', 200),
    phone: optionalText(request.phone, '電話番号', 200),
  }
}

export function buildContactUpdateFields(request: ContactUpdateRequest): Record<string, unknown> {
  const normalized = normalizeRequest(request)
  const fields: Record<string, unknown> = {
    pl_name: normalized.name,
    pl_statuscode: statusCodes[normalized.status],
    pl_departmentrole: normalized.departmentRole,
    pl_email: normalized.email,
    pl_phone: normalized.phone,
  }

  if (normalized.status === '退職') {
    // Custom status and the platform state must move together. The server Plugin
    // rejects partial or mismatched combinations even if a caller bypasses this UI.
    fields.statecode = 1
    fields.statuscode = 2
  }

  return fields
}

export function createContactUpdateApi(invoker: ContactUpdateInvoker): ContactUpdateApi {
  return {
    update: async request => {
      const normalized = normalizeRequest(request)
      const fields = buildContactUpdateFields(request)
      let result: ContactUpdateOperationResult
      try {
        result = await invoker(normalized.contactId, fields)
      } catch {
        throw new ContactUpdateApiError(
          '担当者の保存に失敗しました。入力を保持したまま、権限と接続を確認して再試行してください。',
          'transport-error',
        )
      }

      if (!result.success) {
        throw new ContactUpdateApiError(
          '担当者の保存に失敗しました。入力を保持したまま、権限と接続を確認して再試行してください。',
          'transport-error',
        )
      }

      return {contactId: normalized.contactId, retired: normalized.status === '退職'}
    },
  }
}
