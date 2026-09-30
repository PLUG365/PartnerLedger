import { describe, expect, it } from 'vitest'
import { createPartnerShareSettingReadApi, parsePartnerShareSettingView, PartnerShareSettingReadError } from './partnerShareSettingReadModel'

const partnerId = '11111111-1111-4111-8111-111111111111'
const settingId = '22222222-2222-4222-8222-222222222222'
const userId = '33333333-3333-4333-8333-333333333333'

function raw(overrides: Record<string, unknown> = {}) {
  return {
    pl_partnersharesettingid: settingId,
    _pl_partnerlookup_value: partnerId,
    pl_principalkindcode: 100000000,
    _pl_userprincipallookup_value: userId,
    _pl_teamprincipallookup_value: '',
    pl_userprincipallookupname: 'Diego',
    pl_accesslevelcode: 100000001,
    pl_settingstatuscode: 100000000,
    pl_requestkey: 'read-1',
    pl_isprotectedmanagementpath: false,
    pl_ismanagedprojection: true,
    ...overrides,
  }
}

describe('共有設定の標準Read応答境界', () => {
  it('User／TeamとChoiceを業務表示モデルへ変換する', () => {
    expect(parsePartnerShareSettingView(raw(), partnerId)).toEqual({
      settingId,
      partnerId,
      principalKind: 'User',
      principalId: userId,
      principalName: 'Diego',
      accessLevel: 'Write',
      status: 'Active',
      requestKey: 'read-1',
      isProtectedManagementPath: false,
      isManagedProjection: true,
    })
    expect(parsePartnerShareSettingView(raw({
      pl_principalkindcode: 100000001,
      _pl_userprincipallookup_value: '',
      _pl_teamprincipallookup_value: userId,
      pl_teamprincipallookupname: '営業Team',
    }), partnerId).principalKind).toBe('Team')
  })

  it('DataverseのFormattedValueアノテーションから共有先名を解決する', () => {
    const user = raw({
      pl_userprincipallookupname: undefined,
      '_pl_userprincipallookup_value@OData.Community.Display.V1.FormattedValue': 'Siciliani Diego',
    })
    expect(parsePartnerShareSettingView(user, partnerId).principalName).toBe('Siciliani Diego')

    const team = raw({
      pl_principalkindcode: 100000001,
      _pl_userprincipallookup_value: '',
      _pl_teamprincipallookup_value: userId,
      pl_teamprincipallookupname: undefined,
      '_pl_teamprincipallookup_value@OData.Community.Display.V1.FormattedValue': 'DecisionFlow-Users',
    })
    expect(parsePartnerShareSettingView(team, partnerId).principalName).toBe('DecisionFlow-Users')
  })

  it('別会社・未知Choice・排他違反は空一覧へ丸めず拒否する', () => {
    expect(() => parsePartnerShareSettingView(raw({_pl_partnerlookup_value: '44444444-4444-4444-8444-444444444444'}), partnerId))
      .toThrowError(PartnerShareSettingReadError)
    expect(() => parsePartnerShareSettingView(raw({pl_accesslevelcode: 999}), partnerId))
      .toThrowError(PartnerShareSettingReadError)
    expect(() => parsePartnerShareSettingView(raw({_pl_teamprincipallookup_value: userId}), partnerId))
      .toThrowError(PartnerShareSettingReadError)
  })

  it('後付けのPL管理フラグが欠損した旧行は未照合として表示する', () => {
    const legacy = raw() as Record<string, unknown>
    delete legacy.pl_ismanagedprojection

    expect(parsePartnerShareSettingView(legacy, partnerId).isManagedProjection).toBe(false)
  })

  it('取得失敗と不正IDは呼出し側へ明示エラーを返す', async () => {
    const api = createPartnerShareSettingReadApi({
      list: async () => ({success: false, data: [], error: new Error('network')}),
    })
    await expect(api.list(partnerId)).rejects.toMatchObject({code: 'transport-error'})
    await expect(api.list('bad')).rejects.toMatchObject({code: 'invalid-guid'})
  })
})
