import { describe, expect, it } from 'vitest'
import { createStandardPartnerRegistrationApi } from './standardPartnerRegistrationApi'

const ownerId = 'a1b2c3d4-0000-4000-8000-000000000001'
const partnerId = '9e60c161-88af-f111-aaac-e4fb1eff79c7'

describe('標準pl_partner CreateのPC境界', () => {
  it('業務入力と要求キーだけを標準Createへ送り、応答IDを正規化する', async () => {
    const calls: Record<string, unknown>[] = []
    const api = createStandardPartnerRegistrationApi({
      create: async record => {
        calls.push(record)
        return {success: true, data: {pl_partnerid: partnerId}}
      },
    })

    await expect(api.registerPartner({
      name: ' テスト会社 ',
      industry: 'IT',
      address: '東京都',
      phone: '03-0000-0000',
      mainOwnerId: ownerId,
      idempotencyKey: 'standard-1',
    })).resolves.toEqual({partnerId, isReplay: false})

    expect(calls).toEqual([{
      pl_name: 'テスト会社',
      pl_industry: 'IT',
      pl_address: '東京都',
      pl_phone: '03-0000-0000',
      'pl_MainOwnerLookup@odata.bind': `/systemusers(${ownerId})`,
      pl_registrationkey: 'standard-1',
    }])
    expect(calls[0]).not.toHaveProperty('pl_registrationcontenthash')
    expect(calls[0]).not.toHaveProperty('pl_sharesetupstatuscode')
  })

  it('標準Createの通信失敗は未確定エラーとして扱う', async () => {
    const api = createStandardPartnerRegistrationApi({
      create: async () => ({success: false, data: {pl_partnerid: ''}, error: new Error('network')}),
    })

    await expect(api.registerPartner({name: 'テスト会社', mainOwnerId: ownerId, idempotencyKey: 'standard-2'}))
      .rejects.toMatchObject({code: 'transport-error'})
  })
})
