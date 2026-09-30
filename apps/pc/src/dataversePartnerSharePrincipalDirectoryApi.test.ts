import { describe, expect, it } from 'vitest'
import type { Systemusers } from './generated/models/SystemusersModel'
import type { Teams } from './generated/models/TeamsModel'
import { createDataversePartnerSharePrincipalDirectoryApi, partnerSharePrincipalPageSize, type PartnerSharePrincipalDirectoryService } from './dataversePartnerSharePrincipalDirectoryApi'

const userId = '11111111-1111-4111-8111-111111111111'
const teamId = '22222222-2222-4222-8222-222222222222'

describe('共有先候補の標準Dataverse Read接続', () => {
  it('有効UserとSecurity／Office group Teamだけを別クエリで取得する', async () => {
    const calls: unknown[] = []
    const service: PartnerSharePrincipalDirectoryService = {
      listUsers: async options => {
        calls.push(['users', options])
        return {success: true, data: [
          {systemuserid: userId, fullname: 'Diego', internalemailaddress: 'diego@example.com', isdisabled: false, applicationid: null, accessmode: 0} as unknown as Systemusers,
          // Microsoftのサポート用アカウント（アクセスモード3）は共有できない（2026-09-28、検証環境で拒否を確認）。
          {systemuserid: '66666666-6666-4666-8666-666666666666', fullname: 'Support User', internalemailaddress: '', isdisabled: false, applicationid: null, accessmode: 3} as unknown as Systemusers,
          {systemuserid: '44444444-4444-4444-8444-444444444444', fullname: 'Connector App #', internalemailaddress: '', isdisabled: false, applicationid: '55555555-5555-4555-8555-555555555555'} as unknown as Systemusers,
        ]}
      },
      listGroupTeams: async options => {
        calls.push(['teams', options])
        return {success: true, data: [{teamid: teamId, name: '営業グループ', teamtype: 3, azureactivedirectoryobjectid: '33333333-3333-4333-8333-333333333333'} as Teams]}
      },
    }
    const api = createDataversePartnerSharePrincipalDirectoryApi(service)

    await expect(api.list()).resolves.toEqual([
      {id: userId, name: 'Diego', kind: 'User', detail: 'diego@example.com'},
      {id: teamId, name: '営業グループ', kind: 'Team', detail: 'Microsoft 365 グループ'},
    ])
    expect(calls).toEqual([
      ['users', {
        select: ['systemuserid', 'fullname', 'internalemailaddress', 'isdisabled', 'applicationid', 'accessmode'],
        filter: 'isdisabled eq false and applicationid eq null and accessmode eq 0',
        orderBy: ['fullname asc'],
        top: partnerSharePrincipalPageSize,
      }],
      ['teams', {
        select: ['teamid', 'name', 'teamtype', 'azureactivedirectoryobjectid'],
        filter: '(teamtype eq 2 or teamtype eq 3)',
        orderBy: ['name asc'],
        top: partnerSharePrincipalPageSize,
      }],
    ])
  })

  it('検索語をDataverse側へ渡し、OData文字列をエスケープする', async () => {
    const calls: unknown[] = []
    const service: PartnerSharePrincipalDirectoryService = {
      listUsers: async options => { calls.push(['users', options]); return {success: true, data: []} },
      listGroupTeams: async options => { calls.push(['teams', options]); return {success: true, data: []} },
    }

    await createDataversePartnerSharePrincipalDirectoryApi(service).list("O'Brien")

    expect(calls).toEqual([
      ['users', expect.objectContaining({
        filter: "isdisabled eq false and applicationid eq null and accessmode eq 0 and (contains(fullname,'O''Brien') or contains(internalemailaddress,'O''Brien'))",
        top: partnerSharePrincipalPageSize,
      })],
      ['teams', expect.objectContaining({
        filter: "(teamtype eq 2 or teamtype eq 3) and contains(name,'O''Brien')",
        top: partnerSharePrincipalPageSize,
      })],
    ])
  })

  it('検索は1文字からDataverse側へ渡して各Queryを20件以下にする', async () => {
    const calls: unknown[] = []
    const service: PartnerSharePrincipalDirectoryService = {
      listUsers: async options => { calls.push(['users', options]); return {success: true, data: []} },
      listGroupTeams: async options => { calls.push(['teams', options]); return {success: true, data: []} },
    }

    await createDataversePartnerSharePrincipalDirectoryApi(service).list('a')

    expect(calls).toEqual([
      ['users', expect.objectContaining({
        filter: "isdisabled eq false and applicationid eq null and accessmode eq 0 and (contains(fullname,'a') or contains(internalemailaddress,'a'))",
        top: 20,
      })],
      ['teams', expect.objectContaining({
        filter: "(teamtype eq 2 or teamtype eq 3) and contains(name,'a')",
        top: 20,
      })],
    ])
  })

  it('候補が多くても結合後の表示候補を上位20件に制限する', async () => {
    const service: PartnerSharePrincipalDirectoryService = {
      listUsers: async () => ({success: true, data: Array.from({length: 25}, (_, index) => ({
        systemuserid: `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
        fullname: `User ${String(index).padStart(2, '0')}`,
        internalemailaddress: `user${index}@example.com`,
        isdisabled: false,
        applicationid: null,
        accessmode: 0,
      }) as unknown as Systemusers)}),
      listGroupTeams: async () => ({success: true, data: []}),
    }

    const result = await createDataversePartnerSharePrincipalDirectoryApi(service).list()
    expect(result).toHaveLength(partnerSharePrincipalPageSize)
    expect(result[0]?.name).toBe('User 00')
    expect(result[19]?.name).toBe('User 19')
  })
})
