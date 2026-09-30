import {describe, expect, it, vi} from 'vitest'
import {PartnerDirectUpdateError, buildPartnerDirectUpdateFields, createPartnerDirectUpdateApi} from './partnerDirectUpdate'
import type {PartnerDirectoryItem} from './partnerDirectory'

const partner: PartnerDirectoryItem = {
  id: '11111111-1111-4111-8111-111111111111',
  name: '株式会社サンプル',
  industry: 'IT',
  address: '東京都',
  phone: '03-0000-0000',
  tradingStatus: '取引中',
  aclVersion: 1,
}

describe('partnerDirectUpdate', () => {
  it('maps only one supported partner field to the standard Update payload', () => {
    expect(buildPartnerDirectUpdateFields({partner, target: 'companyName', value: '  新会社  '})).toEqual({
      partnerId: partner.id,
      fields: {pl_name: '新会社'},
    })
    expect(buildPartnerDirectUpdateFields({partner, target: 'tradingStatus', value: '休止中'}).fields).toEqual({pl_tradingstatuscode: 100000002})
    expect(buildPartnerDirectUpdateFields({partner, target: 'address', value: ' '}).fields).toEqual({pl_address: null})
    expect(buildPartnerDirectUpdateFields({partner, target: 'phone', value: ' 03-1234-5678 '}).fields).toEqual({pl_phone: '03-1234-5678'})
  })

  it('主担当の直接の変更は、ユーザーへの参照だけを送る（承認が不要な設定のとき、2026-09-29）', () => {
    const owner = '44444444-4444-4444-8444-444444444444'
    expect(buildPartnerDirectUpdateFields({partner, target: 'mainOwner', value: owner}).fields)
      .toEqual({'pl_MainOwnerLookup@odata.bind': `/systemusers(${owner})`})
    expect(() => buildPartnerDirectUpdateFields({partner, target: 'mainOwner', value: 'x'})).toThrowError(/主担当/)
  })

  it('rejects invalid identity, required, status, and length inputs', () => {
    expect(() => buildPartnerDirectUpdateFields({...({partner, target: 'companyName', value: '変更'}), partner: {...partner, id: 'not-guid'}})).toThrowError(PartnerDirectUpdateError)
    expect(() => buildPartnerDirectUpdateFields({partner, target: 'companyName', value: ' '})).toThrowError(/会社名を指定/)
    expect(() => buildPartnerDirectUpdateFields({partner, target: 'tradingStatus', value: '不明'})).toThrowError(/取引状態が不正/)
    expect(() => buildPartnerDirectUpdateFields({partner, target: 'address', value: 'x'.repeat(201)})).toThrowError(/200文字以内/)
  })

  it('does not convert rejected or transport results into success', async () => {
    const rejected = createPartnerDirectUpdateApi({update: vi.fn(async () => ({success: false, data: null}))})
    await expect(rejected.updatePartner({partner, target: 'address', value: '変更'})).rejects.toMatchObject({code: 'operation-rejected'})
    const transport = createPartnerDirectUpdateApi({update: vi.fn(async () => { throw new Error('network') })})
    await expect(transport.updatePartner({partner, target: 'phone', value: '変更'})).rejects.toMatchObject({code: 'transport-error'})
  })
})
