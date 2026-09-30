import { describe, expect, it } from 'vitest'
import { createPartnerSharePrincipalDirectoryApi, PartnerSharePrincipalDirectoryError } from './partnerSharePrincipalDirectory'

const userId = '11111111-1111-4111-8111-111111111111'
const teamId = '22222222-2222-4222-8222-222222222222'

describe('共有先候補の応答境界', () => {
  it('User／Teamを名前順に並べ、IDと補足を保持する', async () => {
    const api = createPartnerSharePrincipalDirectoryApi({
      list: async () => ({success: true, data: [
        {id: teamId, name: '営業グループ', kind: 'Team', detail: 'Office グループ'},
        {id: userId, name: 'Diego', kind: 'User', detail: 'diego@example.com'},
      ]}),
    })
    await expect(api.list()).resolves.toEqual([
      {id: userId, name: 'Diego', kind: 'User', detail: 'diego@example.com'},
      {id: teamId, name: '営業グループ', kind: 'Team', detail: 'Office グループ'},
    ])
  })

  it('通信失敗・不正GUID・重複候補を空一覧へ丸めない', async () => {
    await expect(createPartnerSharePrincipalDirectoryApi({list: async () => ({success: false, data: []})}).list())
      .rejects.toMatchObject({code: 'transport-error'})
    await expect(createPartnerSharePrincipalDirectoryApi({list: async () => ({success: true, data: [{id: 'bad', name: 'x', kind: 'User'}]})}).list())
      .rejects.toBeInstanceOf(PartnerSharePrincipalDirectoryError)
    await expect(createPartnerSharePrincipalDirectoryApi({list: async () => ({success: true, data: [
      {id: userId, name: 'A', kind: 'User'},
      {id: userId.toUpperCase(), name: 'B', kind: 'User'},
    ]})}).list())
      .rejects.toMatchObject({code: 'invalid-response'})
  })
})
