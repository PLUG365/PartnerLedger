import { describe, expect, it } from 'vitest'
import { createStandardContactRegistrationApi } from './standardContactRegistrationApi'

const partnerId = '22222222-2222-4222-8222-222222222222'
const contactId = '33333333-3333-4333-8333-333333333333'

describe('生成pl_contactsServiceを使う担当者登録アダプター', () => {
  it('Lookup bind、Choice、要求キーを標準Createへ渡す', async () => {
    const calls: Record<string, unknown>[] = []
    const api = createStandardContactRegistrationApi({
      findByRegistrationKey: async () => ({success: true, data: []}),
      create: async record => {
        calls.push(record)
        return {success: true, data: {pl_contactid: contactId}}
      },
    })

    await expect(api.registerContact({
      partnerId,
      name: '井上 真帆',
      statusCode: 100000001,
      departmentRole: '営業部',
      email: 'm.inoue@example.com',
      phone: '03-1234-5678',
      idempotencyKey: 'standard-contact-1',
    })).resolves.toEqual({contactId, isReplay: false})

    expect(calls).toEqual([{
      pl_name: '井上 真帆',
      'pl_PartnerLookup@odata.bind': `/pl_partners(${partnerId})`,
      pl_statuscode: 100000001,
      pl_departmentrole: '営業部',
      pl_email: 'm.inoue@example.com',
      pl_phone: '03-1234-5678',
      pl_registrationkey: 'standard-contact-1',
    }])
    expect(calls[0]).not.toHaveProperty('pl_registeredat')
    expect(calls[0]).not.toHaveProperty('createdby')
  })
})
