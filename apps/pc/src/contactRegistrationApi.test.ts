import { describe, expect, it } from 'vitest'
import { createContactLedgerRegistrationApi, type ContactRegistrationReplayRecord } from './contactRegistrationApi'

const partnerId = '22222222-2222-4222-8222-222222222222'
const contactId = '33333333-3333-4333-8333-333333333333'

const request = {
  partnerId,
  name: '井上 真帆',
  statusCode: 100000001 as const,
  departmentRole: '営業部',
  email: 'm.inoue@example.com',
  phone: '03-1234-5678',
  idempotencyKey: 'contact-registration-1',
}

function existing(): ContactRegistrationReplayRecord {
  return {
    pl_contactid: contactId,
    pl_name: request.name,
    _pl_partnerlookup_value: partnerId,
    pl_statuscode: request.statusCode,
    pl_departmentrole: request.departmentRole,
    pl_email: request.email,
    pl_phone: request.phone,
  }
}

describe('担当者標準CreateのPC境界', () => {
  it('業務入力と要求キーだけを標準Createへ渡す境界へ接続できる', async () => {
    const created: unknown[] = []
    const api = createContactLedgerRegistrationApi(
      {findByRegistrationKey: async () => ({success: true, data: []})},
      async normalized => {
        created.push(normalized)
        return {success: true, contactId}
      },
    )

    await expect(api.registerContact(request)).resolves.toEqual({contactId, isReplay: false})
    expect(created).toEqual([{
      ...request,
      statusCode: 100000001,
    }])
  })

  it('状態未指定は在籍へ正規化する', async () => {
    const calls: unknown[] = []
    const api = createContactLedgerRegistrationApi(
      {findByRegistrationKey: async () => ({success: true, data: []})},
      async normalized => { calls.push(normalized); return {success: true, contactId} },
    )

    await api.registerContact({partnerId, name: '中村 光', idempotencyKey: 'contact-registration-2'})
    expect(calls).toMatchObject([{statusCode: 100000000}])
  })

  it('同じ要求キー・同じ内容は既存結果へ収束し、別内容は拒否する', async () => {
    let createCalls = 0
    const api = createContactLedgerRegistrationApi(
      {findByRegistrationKey: async () => ({success: true, data: [existing()]})},
      async () => { createCalls += 1; return {success: true, contactId} },
    )

    await expect(api.registerContact(request)).resolves.toEqual({contactId, isReplay: true})
    expect(createCalls).toBe(0)
    await expect(api.registerContact({...request, name: '別の氏名'})).rejects.toMatchObject({code: 'idempotency-conflict'})
  })

  it('Create応答が失われても再読取りできれば再送結果を返す', async () => {
    let reads = 0
    const api = createContactLedgerRegistrationApi(
      {findByRegistrationKey: async () => ({success: true, data: reads++ === 0 ? [] : [existing()]})},
      async () => ({success: false, error: new Error('response lost')}),
    )

    await expect(api.registerContact(request)).resolves.toEqual({contactId, isReplay: true})
  })

  it('候補読取り失敗は保存結果未確定として扱う', async () => {
    const api = createContactLedgerRegistrationApi(
      {findByRegistrationKey: async () => ({success: false, data: [], error: new Error('offline')})},
      async () => ({success: true, contactId}),
    )

    await expect(api.registerContact(request)).rejects.toMatchObject({code: 'transport-error'})
  })
})
