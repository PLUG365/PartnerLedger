import { describe, expect, it } from 'vitest'
import type { Pl_partnersharesettings } from './generated/models/Pl_partnersharesettingsModel'
import { createDataversePartnerShareSettingReadApi, type PartnerShareSettingReadService } from './dataversePartnerShareSettingReadApi'

const partnerId = '11111111-1111-4111-8111-111111111111'
const settingId = '22222222-2222-4222-8222-222222222222'
const userId = '33333333-3333-4333-8333-333333333333'

describe('共有設定の標準Dataverse Read接続', () => {
  it('会社IDと最小列のfilterを標準getAllへ渡す', async () => {
    const calls: unknown[] = []
    const service: PartnerShareSettingReadService = {
      getAll: async options => {
        calls.push(options)
        return {success: true, data: [{
          pl_partnersharesettingid: settingId,
          _pl_partnerlookup_value: partnerId,
          pl_principalkindcode: 100000000,
          _pl_userprincipallookup_value: userId,
          _pl_teamprincipallookup_value: '',
          pl_accesslevelcode: 100000000,
          pl_settingstatuscode: 100000000,
          pl_requestkey: 'read-1',
          pl_isprotectedmanagementpath: false,
          pl_ismanagedprojection: true,
        } as unknown as Pl_partnersharesettings]}
      },
    }
    const api = createDataversePartnerShareSettingReadApi(service)

    await expect(api.list(partnerId)).resolves.toMatchObject([{settingId, partnerId, principalId: userId, accessLevel: 'Read'}])
    expect(calls).toEqual([{
      select: [
        'pl_partnersharesettingid',
        '_pl_partnerlookup_value',
        'pl_principalkindcode',
        '_pl_userprincipallookup_value',
        '_pl_teamprincipallookup_value',
        'pl_accesslevelcode',
        'pl_settingstatuscode',
        'pl_requestkey',
        'pl_isprotectedmanagementpath',
        'pl_ismanagedprojection',
      ],
      filter: `_pl_partnerlookup_value eq ${partnerId} and statecode eq 0`,
      orderBy: ['createdon asc'],
      maxPageSize: 200,
    }])
  })
})
