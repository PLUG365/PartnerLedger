import { isDataverseGuid } from './dataverseIds'

export type BulletinPost = {
  id: string
  author: string
  createdAt: string
  body: string
  partnerId: string
  contactId?: string
  requestKey: string
}

export type BulletinBoardScope = {
  partnerId: string
  contactId?: string
}

export type BulletinPostCreateRequest = BulletinBoardScope & {
  body: string
  requestKey?: string
}

let requestKeySequence = 0

export function createBulletinPostRequestKey(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `BulletinPost-${uuid}`
  requestKeySequence += 1
  return `BulletinPost-${Date.now().toString(36)}-${requestKeySequence.toString(36)}`
}

export class BulletinBoardApiError extends Error {
  readonly code?: string

  constructor(message: string, code?: string) {
    super(message)
    this.name = 'BulletinBoardApiError'
    this.code = code
  }
}

export function normalizeBulletinPostRequest(request: BulletinPostCreateRequest): Required<Pick<BulletinPostCreateRequest, 'partnerId' | 'body' | 'requestKey'>> & Pick<BulletinPostCreateRequest, 'contactId'> {
  const partnerId = requireGuid(request.partnerId, 'PartnerId')
  const body = requireText(request.body, '本文', 200)
  const requestKey = request.requestKey?.trim()
    ? requireText(request.requestKey, '要求キー', 200)
    : createBulletinPostRequestKey()
  const contactId = request.contactId === undefined || request.contactId.trim() === ''
    ? undefined
    : requireGuid(request.contactId, 'ContactId')
  return {partnerId, body, requestKey, contactId}
}

export function isSameBulletinPost(post: BulletinPost, request: Required<Pick<BulletinPostCreateRequest, 'partnerId' | 'body' | 'requestKey'>> & Pick<BulletinPostCreateRequest, 'contactId'>): boolean {
  return post.partnerId.toLowerCase() === request.partnerId.toLowerCase()
    && post.body === request.body
    && post.requestKey === request.requestKey
    && (post.contactId ?? '').toLowerCase() === (request.contactId ?? '').toLowerCase()
}

function requireText(value: string, fieldName: string, maxLength: number): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new BulletinBoardApiError(`${fieldName}を指定してください。`, 'required')
  }
  const normalized = value.trim()
  if (normalized.length > maxLength) {
    throw new BulletinBoardApiError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  }
  return normalized
}

function requireGuid(value: string, fieldName: string): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new BulletinBoardApiError(`${fieldName}を指定してください。`, 'required')
  }
  const normalized = value.trim()
  if (!isDataverseGuid(normalized)) {
    throw new BulletinBoardApiError(`${fieldName}が正しくありません。`, 'invalid-guid')
  }
  return normalized
}
