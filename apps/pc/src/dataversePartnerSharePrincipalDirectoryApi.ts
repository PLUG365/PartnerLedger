import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Systemusers } from './generated/models/SystemusersModel'
import type { Teams } from './generated/models/TeamsModel'
import { SystemusersService } from './generated/services/SystemusersService'
import { TeamsService } from './generated/services/TeamsService'
import { createPartnerSharePrincipalDirectoryApi, type PartnerSharePrincipalDirectoryApi } from './partnerSharePrincipalDirectory'

export type PartnerSharePrincipalDirectoryService = {
  listUsers: (options: Parameters<typeof SystemusersService.getAll>[0]) => Promise<IOperationResult<Systemusers[]>>
  listGroupTeams: (options: Parameters<typeof TeamsService.getAll>[0]) => Promise<IOperationResult<Teams[]>>
}

export const partnerSharePrincipalPageSize = 20

function escapeODataString(value: string) {
  return value.replace(/'/g, "''")
}

// 通常の利用者（アクセスモード0）だけ。Microsoftのサポート用アカウント（3）などには共有できない
// （2026-09-28、検証環境で拒否を確認）。
function isRegularUser(user: Systemusers) {
  return Number(user.accessmode) === 0
}

function asResult(users: Systemusers[], teams: Teams[]) {
  const data = [
      ...users
        .filter(user => Boolean(user.systemuserid && user.fullname) && user.applicationid == null && isRegularUser(user))
        .map(user => ({id: user.systemuserid!, name: user.fullname!, kind: 'User' as const, detail: user.internalemailaddress || undefined})),
      ...teams
        .filter(team => Boolean(team.teamid && team.name) && (team.teamtype === 2 || team.teamtype === 3))
        .map(team => ({id: team.teamid!, name: team.name!, kind: 'Team' as const, detail: team.teamtype === 2 ? 'Security グループ' : 'Microsoft 365 グループ'})),
  ].sort((left, right) => left.name.localeCompare(right.name, 'ja'))

  return {
    success: true,
    data: data.slice(0, partnerSharePrincipalPageSize),
  }
}

export function createDataversePartnerSharePrincipalDirectoryApi(
  service: PartnerSharePrincipalDirectoryService,
): PartnerSharePrincipalDirectoryApi {
  return createPartnerSharePrincipalDirectoryApi({
    list: async (query?: string) => {
      const normalizedQuery = query?.trim()
      const escapedQuery = normalizedQuery ? escapeODataString(normalizedQuery) : undefined
      const userFilter = [
        'isdisabled eq false and applicationid eq null and accessmode eq 0',
        escapedQuery ? `(contains(fullname,'${escapedQuery}') or contains(internalemailaddress,'${escapedQuery}'))` : undefined,
      ].filter(Boolean).join(' and ')
      const teamFilter = [
        '(teamtype eq 2 or teamtype eq 3)',
        escapedQuery ? `contains(name,'${escapedQuery}')` : undefined,
      ].filter(Boolean).join(' and ')
      const [users, teams] = await Promise.all([
        service.listUsers({
          select: ['systemuserid', 'fullname', 'internalemailaddress', 'isdisabled', 'applicationid', 'accessmode'],
          filter: userFilter,
          orderBy: ['fullname asc'],
          top: partnerSharePrincipalPageSize,
        }),
        service.listGroupTeams({
          select: ['teamid', 'name', 'teamtype', 'azureactivedirectoryobjectid'],
          filter: teamFilter,
          orderBy: ['name asc'],
          top: partnerSharePrincipalPageSize,
        }),
      ])
      if (!users.success || !teams.success) return {success: false, data: [], error: users.error ?? teams.error}
      return asResult(users.data ?? [], teams.data ?? [])
    },
  })
}

export const dataversePartnerSharePrincipalDirectoryApi = createDataversePartnerSharePrincipalDirectoryApi({
  listUsers: options => SystemusersService.getAll(options),
  listGroupTeams: options => TeamsService.getAll(options),
})
