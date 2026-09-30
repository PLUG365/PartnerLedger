import { describe, expect, it } from 'vitest'
import { ContactUpdateApiError, createContactUpdateApi, type ContactUpdateRequest } from './contactUpdateApi'

const contactId = '33333333-3333-4333-8333-333333333333'

describe('担当者標準UpdateのPC境界', () => {
  it('在籍・休職の更新は業務列だけを送る', async () => {
    const calls: {id: string; fields: Record<string, unknown>}[] = []
    const api = createContactUpdateApi(async (id, fields) => {
      calls.push({id, fields})
      return {success: true}
    })

    await expect(api.update({
      contactId,
      name: '  井上 真帆（更新）  ',
      status: '休職',
      departmentRole: ' 営業部 ',
      email: '',
      phone: ' 03-1234-5678 ',
    })).resolves.toEqual({contactId, retired: false})

    expect(calls).toEqual([{
      id: contactId,
      fields: {
        pl_name: '井上 真帆（更新）',
        pl_statuscode: 100000001,
        pl_departmentrole: '営業部',
        pl_email: null,
        pl_phone: '03-1234-5678',
      },
    }])
    expect(calls[0].fields).not.toHaveProperty('pl_partnerlookup')
    expect(calls[0].fields).not.toHaveProperty('ownerid')
    expect(calls[0].fields).not.toHaveProperty('pl_registrationkey')
  })

  it('退職は在籍状態とDataverseアーカイブ状態を同時に送る', async () => {
    const calls: Record<string, unknown>[] = []
    const api = createContactUpdateApi(async (_id, fields) => {
      calls.push(fields)
      return {success: true}
    })

    await expect(api.update({
      contactId,
      name: '退職予定者',
      status: '退職',
      departmentRole: undefined,
      email: undefined,
      phone: undefined,
    })).resolves.toEqual({contactId, retired: true})

    expect(calls).toEqual([{
      pl_name: '退職予定者',
      pl_statuscode: 100000002,
      pl_departmentrole: null,
      pl_email: null,
      pl_phone: null,
      statecode: 1,
      statuscode: 2,
    }])
  })

  it.each([
    {contactId: 'not-a-guid', name: '氏名', status: '在籍' as const},
    {contactId, name: '  ', status: '在籍' as const},
    {contactId, name: '氏名', status: '未設定' as ContactUpdateRequest['status']},
  ])('不正なUpdate入力を送信前に拒否する', async request => {
    const api = createContactUpdateApi(async () => ({success: true}))
    await expect(api.update(request)).rejects.toBeInstanceOf(ContactUpdateApiError)
  })

  it('サーバー失敗時は保存成功として扱わない', async () => {
    const api = createContactUpdateApi(async () => ({success: false, error: new Error('server detail')}))
    await expect(api.update({contactId, name: '氏名', status: '在籍'})).rejects.toMatchObject({
      name: 'ContactUpdateApiError',
      code: 'transport-error',
    })
  })
})
