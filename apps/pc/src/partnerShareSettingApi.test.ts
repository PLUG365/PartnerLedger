import { describe, expect, it } from 'vitest'
import { createPartnerShareSettingApi, PartnerShareSettingApiError } from './partnerShareSettingApi'

const partnerId = '11111111-1111-4111-8111-111111111111'
const principalId = '22222222-2222-4222-8222-222222222222'
const settingId = '33333333-3333-4333-8333-333333333333'

describe('共有設定のPC入力境界', () => {
  it('GUID・Choice・要求キーを検証し、標準Invokerの結果を返す', async () => {
    const calls: unknown[] = []
    const api = createPartnerShareSettingApi({
      create: async request => {
        calls.push(['create', request])
        return {success: true, data: {SettingId: settingId, IsReplay: false}}
      },
      update: async request => {
        calls.push(['update', request])
        return {success: true, data: {SettingId: settingId, IsReplay: false}}
      },
      revoke: async request => {
        calls.push(['revoke', request])
        return {success: true, data: {SettingId: settingId, IsReplay: false}}
      },
    })

    await expect(api.create({partnerId, principalKind: 'User', principalId, accessLevel: 'Read', requestKey: 'share-1'}))
      .resolves.toEqual({settingId, isReplay: false})
    await expect(api.update({settingId, partnerId, principalKind: 'User', principalId, accessLevel: 'Write', requestKey: 'share-2'}))
      .resolves.toEqual({settingId, isReplay: false})
    await expect(api.revoke(settingId)).resolves.toEqual({settingId, isReplay: false})

    expect(calls).toEqual([
      ['create', {partnerId, principalKind: 'User', principalId, accessLevel: 'Read', requestKey: 'share-1'}],
      ['update', {settingId, partnerId, principalKind: 'User', principalId, accessLevel: 'Write', requestKey: 'share-2'}],
      ['revoke', {settingId}],
    ])
  })

  it('不正な主体・アクセス・IDを標準Invokerへ送らない', async () => {
    const api = createPartnerShareSettingApi({
      create: async () => ({success: true, data: {SettingId: settingId}}),
      update: async () => ({success: true, data: {SettingId: settingId}}),
      revoke: async () => ({success: true, data: {SettingId: settingId}}),
    })

    await expect(api.create({partnerId: 'bad', principalKind: 'User', principalId, accessLevel: 'Read', requestKey: 'x'}))
      .rejects.toMatchObject({code: 'invalid-guid'})
    await expect(api.create({partnerId, principalKind: 'User', principalId, accessLevel: 'Delete' as 'Read', requestKey: 'x'}))
      .rejects.toMatchObject({code: 'invalid-choice'})
    await expect(api.revoke('bad')).rejects.toBeInstanceOf(PartnerShareSettingApiError)
  })

  it('標準Createの失敗は同じ要求キーでの再試行を促す', async () => {
    const api = createPartnerShareSettingApi({
      create: async () => ({success: false, data: {}, error: new Error('network')}),
      update: async () => ({success: true, data: {SettingId: settingId}}),
      revoke: async () => ({success: true, data: {SettingId: settingId}}),
    })

    await expect(api.create({partnerId, principalKind: 'Team', principalId, accessLevel: 'Write', requestKey: 'retry-1'}))
      .rejects.toMatchObject({code: 'transport-error'})
  })

  it('403は権限拒否として表示し、結果不明の再試行を促さない', async () => {
    const api = createPartnerShareSettingApi({
      create: async () => ({success: false, data: {}, error: {status: 403}}),
      update: async () => ({success: true, data: {SettingId: settingId}}),
      revoke: async () => ({success: true, data: {SettingId: settingId}}),
    })

    await expect(api.create({partnerId, principalKind: 'Team', principalId, accessLevel: 'Write', requestKey: 'deny-1'}))
      .rejects.toMatchObject({
        code: 'authorization-denied',
        message: '現在の権限では共有設定を変更できません。入力は保持されています。',
      })
  })

  it('共有先がサポート用アカウントで拒否されたときは、呼び出した人の権限不足と表示しない', async () => {
    // 2026-09-28、検証環境で再現した実際の応答（HTTP 400）。
    const api = createPartnerShareSettingApi({
      create: async () => ({success: false, data: {}, error: {status: 400, message: 'OrganizationServiceFault: The support user has insufficient privileges. OrgType :13 and PrincipalId: x'}}),
      update: async () => ({success: true, data: {SettingId: settingId}}),
      revoke: async () => ({success: true, data: {SettingId: settingId}}),
    })

    await expect(api.create({partnerId, principalKind: 'User', principalId, accessLevel: 'Read', requestKey: 'support-1'}))
      .rejects.toMatchObject({
        code: 'principal-not-shareable',
        message: 'この共有先（Microsoftのサポート用アカウントなど）には共有できません。別の人またはグループを選んでください。入力は保持されています。',
      })
  })

  it('SDKがHTTP状態を隠してもDataverseの権限拒否文を認識する', async () => {
    const api = createPartnerShareSettingApi({
      create: async () => ({success: false, data: {}, error: new Error('The user does not have sufficient privileges to update the record.')}),
      update: async () => ({success: true, data: {SettingId: settingId}}),
      revoke: async () => ({success: true, data: {SettingId: settingId}}),
    })

    await expect(api.create({partnerId, principalKind: 'Team', principalId, accessLevel: 'Write', requestKey: 'deny-message-1'}))
      .rejects.toMatchObject({code: 'authorization-denied'})
  })

  it('4xx（認証・認可以外）は既知の操作拒否として扱う', async () => {
    const api = createPartnerShareSettingApi({
      create: async () => ({success: false, data: {}, error: {status: 400}}),
      update: async () => ({success: true, data: {SettingId: settingId}}),
      revoke: async () => ({success: true, data: {SettingId: settingId}}),
    })

    await expect(api.create({partnerId, principalKind: 'Team', principalId, accessLevel: 'Write', requestKey: 'reject-1'}))
      .rejects.toMatchObject({code: 'operation-rejected'})
  })

  it('5xxとHTTP状態不明は結果不明として扱う', async () => {
    const api = createPartnerShareSettingApi({
      create: async () => ({success: false, data: {}, error: {status: 503}}),
      update: async () => ({success: false, data: {}, error: new Error('network')}),
      revoke: async () => ({success: true, data: {SettingId: settingId}}),
    })

    await expect(api.create({partnerId, principalKind: 'Team', principalId, accessLevel: 'Write', requestKey: 'unknown-1'}))
      .rejects.toMatchObject({code: 'transport-error'})
    await expect(api.update({settingId, partnerId, principalKind: 'Team', principalId, accessLevel: 'Write', requestKey: 'unknown-2'}))
      .rejects.toMatchObject({code: 'transport-error'})
  })
})
