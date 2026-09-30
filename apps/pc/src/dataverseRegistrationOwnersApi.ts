import './dataverseRuntimeDataSourcesInfo'
import type { Systemusers } from './generated/models/SystemusersModel'
import { SystemusersService } from './generated/services/SystemusersService'

export type PartnerRegistrationOwner = {
  id: string
  name: string
  email?: string
}

export type PartnerRegistrationOwnerLoadState = 'idle' | 'loading' | 'ready' | 'error'

export class PartnerRegistrationOwnerError extends Error {
  constructor(message = '主担当候補を取得できませんでした。') {
    super(message)
    this.name = 'PartnerRegistrationOwnerError'
  }
}

export function toRegistrationOwner(user: Systemusers): PartnerRegistrationOwner | undefined {
  // 通常の利用者（アクセスモード0）だけ。Microsoftのサポート用アカウントなどは主担当にしない。
  if (!user.systemuserid || !user.fullname || user.applicationid || Number(user.accessmode) !== 0) return undefined
  return {id: user.systemuserid, name: user.fullname, email: user.internalemailaddress || undefined}
}

export async function listActivePartnerRegistrationOwners(): Promise<PartnerRegistrationOwner[]> {
  const result = await SystemusersService.getAll({
    select: ['systemuserid', 'fullname', 'internalemailaddress', 'isdisabled', 'applicationid', 'accessmode'],
    filter: 'isdisabled eq false and applicationid eq null and accessmode eq 0',
    orderBy: ['fullname asc'],
    maxPageSize: 200,
  })
  if (!result.success) throw new PartnerRegistrationOwnerError()
  return (result.data ?? []).map(toRegistrationOwner).filter((owner): owner is PartnerRegistrationOwner => Boolean(owner))
}
