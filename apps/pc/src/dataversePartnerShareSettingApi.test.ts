import { describe, expect, it } from 'vitest'
import { createDataversePartnerShareSettingApi, type StandardPartnerShareSettingService } from './dataversePartnerShareSettingApi'

const partnerId = '11111111-1111-4111-8111-111111111111'
const principalId = '22222222-2222-4222-8222-222222222222'
const settingId = '33333333-3333-4333-8333-333333333333'

describe('共有設定の標準Dataverse CRUD接続', () => {
  it('Createは業務入力だけを送り、サーバー導出列をクライアントから送らない', async () => {
    const calls: unknown[] = []
    const service: StandardPartnerShareSettingService = {
      create: async record => {
        calls.push(['create', record])
        return {success: true, data: {pl_partnersharesettingid: settingId}}
      },
      update: async () => ({success: true, data: {pl_partnersharesettingid: settingId}}),
    }
    const api = createDataversePartnerShareSettingApi(service)

    await expect(api.create({partnerId, principalKind: 'Team', principalId, accessLevel: 'Write', requestKey: 'standard-share-1'}))
      .resolves.toEqual({settingId, isReplay: false})
    expect(calls).toEqual([['create', {
      'pl_PartnerLookup@odata.bind': `/pl_partners(${partnerId})`,
      pl_principalkindcode: 100000001,
      pl_accesslevelcode: 100000001,
      pl_requestkey: 'standard-share-1',
      'pl_TeamPrincipalLookup@odata.bind': `/teams(${principalId})`,
    }]])
  })

  it('Updateはアクセス種別と要求キー、取消は状態だけを送る', async () => {
    const calls: unknown[] = []
    const service: StandardPartnerShareSettingService = {
      create: async () => ({success: true, data: {pl_partnersharesettingid: settingId}}),
      update: async (id, fields) => {
        calls.push([id, fields])
        return {success: true, data: {pl_partnersharesettingid: settingId}}
      },
    }
    const api = createDataversePartnerShareSettingApi(service)

    await api.update({settingId, partnerId, principalKind: 'User', principalId, accessLevel: 'Read', requestKey: 'standard-share-2'})
    await api.revoke(settingId)

    expect(calls).toEqual([
      [settingId, {pl_accesslevelcode: 100000000, pl_requestkey: 'standard-share-2'}],
      [settingId, {pl_settingstatuscode: 100000001}],
    ])
  })
})
