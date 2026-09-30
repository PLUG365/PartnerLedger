import { isDataverseGuid } from './dataverseIds'

export type ContactRegistrationParent = {id: string; name: string}

export class ContactParentDirectoryError extends Error {
  readonly code: string

  constructor(message: string, code = 'invalid-response') {
    super(message)
    this.name = 'ContactParentDirectoryError'
    this.code = code
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

export function parseContactRegistrationParents(data: unknown): ContactRegistrationParent[] {
  if (!Array.isArray(data)) throw new ContactParentDirectoryError('担当者登録用の取引先一覧が不正です。')
  const parents = data.map((value, index) => {
    if (!isRecord(value)) throw new ContactParentDirectoryError(`${index + 1}件目の取引先が不正です。`)
    const id = typeof value.pl_partnerid === 'string' ? value.pl_partnerid.trim() : ''
    const name = typeof value.pl_name === 'string' ? value.pl_name.trim() : ''
    const state = value.statecode
    if (!isDataverseGuid(id) || !name) throw new ContactParentDirectoryError(`${index + 1}件目の取引先IDまたは名前が不正です。`)
    if (!(state === 0 || state === '0')) throw new ContactParentDirectoryError(`${index + 1}件目の取引先状態が不正です。`)
    return {id, name}
  })
  const ids = new Set(parents.map(parent => parent.id))
  if (ids.size !== parents.length) throw new ContactParentDirectoryError('担当者登録用の取引先一覧に重複IDがあります。')
  return parents
}

export type ContactParentDirectoryInvoker = {listParents: () => Promise<{success: boolean; data: unknown; error?: unknown}>}

export async function createContactParentDirectoryApi(invoker: ContactParentDirectoryInvoker): Promise<ContactRegistrationParent[]> {
  let result
  try {
    result = await invoker.listParents()
  } catch {
    throw new ContactParentDirectoryError('担当者登録用の取引先を取得できません。', 'transport-error')
  }
  if (!result.success) throw new ContactParentDirectoryError('担当者登録用の取引先を取得できません。', 'transport-error')
  return parseContactRegistrationParents(result.data)
}
