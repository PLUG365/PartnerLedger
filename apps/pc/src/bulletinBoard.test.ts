import { describe, expect, it, vi } from 'vitest'
import { createBulletinPostRequestKey, BulletinBoardApiError, normalizeBulletinPostRequest } from './bulletinBoard'
import { createDataverseBulletinBoardApi } from './dataverseBulletinBoardApi'

const partnerId = '22222222-2222-4222-8222-222222222222'
const contactId = '33333333-3333-4333-8333-333333333333'
const postId = '44444444-4444-4444-8444-444444444444'

function row(overrides: Record<string, unknown> = {}) {
  return {
    pl_bulletinpostid: postId,
    pl_body: '確認しました。',
    pl_contactkey: undefined,
    pl_registeredat: '2026-09-17T01:02:03.000Z',
    pl_requestkey: 'BulletinPost-1',
    createdbyname: 'Diego',
    createdon: '2026-09-17T01:02:03.000Z',
    _pl_partnerlookup_value: partnerId,
    statecode: 0,
    ...overrides,
  }
}

describe('bulletinBoard', () => {
  it('normalizes and generates a request key', () => {
    expect(normalizeBulletinPostRequest({partnerId, body: '  本文  '})).toMatchObject({partnerId, body: '本文'})
    expect(createBulletinPostRequestKey()).toMatch(/^BulletinPost-/)
  })

  it('fails closed for invalid scope or body', () => {
    expect(() => normalizeBulletinPostRequest({partnerId: 'bad', body: '本文'})).toThrow(BulletinBoardApiError)
    expect(() => normalizeBulletinPostRequest({partnerId, body: ' '})).toThrow(/本文/)
    expect(() => normalizeBulletinPostRequest({partnerId, body: '本文', contactId: 'bad'})).toThrow(/ContactId/)
  })

  it('lists only the requested partner/contact scope', async () => {
    const list = vi.fn().mockResolvedValue({success: true, data: [
      row(),
      row({pl_bulletinpostid: '55555555-5555-4555-8555-555555555555', pl_contactkey: contactId}),
      row({pl_bulletinpostid: '66666666-6666-4666-8666-666666666666', _pl_partnerlookup_value: '77777777-7777-4777-8777-777777777777'}),
    ]})
    const api = createDataverseBulletinBoardApi({list, create: vi.fn()})

    await expect(api.list({partnerId})).resolves.toHaveLength(1)
    await expect(api.list({partnerId, contactId})).resolves.toHaveLength(1)
    expect(list).toHaveBeenCalledWith(expect.objectContaining({filter: `statecode eq 0 and _pl_partnerlookup_value eq ${partnerId}`}))
  })

  it('新しい投稿から順に取得する（2026-09-28、ユーザー要望）', async () => {
    const list = vi.fn().mockResolvedValue({success: true, data: []})
    const api = createDataverseBulletinBoardApi({list, create: vi.fn()})

    await api.list({partnerId})

    expect(list).toHaveBeenCalledWith(expect.objectContaining({orderBy: ['pl_registeredat desc', 'createdon desc']}))
  })

  it('投稿者はcreatedby名が無くてもFormattedValueへフォールバックする', async () => {
    const list = vi.fn().mockResolvedValue({success: true, data: [row({
      createdbyname: undefined,
      ['_createdby_value@OData.Community.Display.V1.FormattedValue']: 'Formatted投稿者',
    })]})
    const api = createDataverseBulletinBoardApi({list, create: vi.fn()})

    await expect(api.list({partnerId})).resolves.toEqual([expect.objectContaining({author: 'Formatted投稿者'})])
  })

  it('accepts Dataverse GUIDs without RFC UUID version bits', async () => {
    const dataversePartnerId = '1efdf7ad-4bb0-f111-aaac-e4fb1eff79c7'
    const list = vi.fn().mockResolvedValue({success: true, data: [row({
      pl_bulletinpostid: '880cc130-a6b2-f111-aaab-e4fade04470e',
      _pl_partnerlookup_value: dataversePartnerId,
    })]})
    const api = createDataverseBulletinBoardApi({list, create: vi.fn()})

    await expect(api.list({partnerId: dataversePartnerId})).resolves.toHaveLength(1)
  })

  it('creates with standard lookup bind and can resolve a replay', async () => {
    const create = vi.fn().mockResolvedValue({success: true, data: row({pl_requestkey: 'new-key', pl_body: '新規投稿', pl_contactkey: contactId})})
    const api = createDataverseBulletinBoardApi({list: vi.fn(), create})
    await api.create({partnerId, contactId, body: '新規投稿', requestKey: 'new-key'})
    expect(create).toHaveBeenCalledWith(expect.objectContaining({
      pl_body: '新規投稿',
      pl_requestkey: 'new-key',
      pl_contactkey: contactId,
      'pl_PartnerLookup@odata.bind': `/pl_partners(${partnerId})`,
    }))

    const replayList = vi.fn().mockResolvedValue({success: true, data: [row({pl_requestkey: 'replay-key', pl_body: '同じ投稿'})]})
    const replayCreate = vi.fn().mockResolvedValue({success: false, error: new Error('duplicate')})
    const replayApi = createDataverseBulletinBoardApi({list: replayList, create: replayCreate})
    await expect(replayApi.create({partnerId, body: '同じ投稿', requestKey: 'replay-key'})).resolves.toMatchObject({requestKey: 'replay-key'})
  })

  it('does not treat a different body as a successful replay', async () => {
    const api = createDataverseBulletinBoardApi({
      list: vi.fn().mockResolvedValue({success: true, data: [row({pl_requestkey: 'same-key', pl_body: '既存'})]}),
      create: vi.fn().mockResolvedValue({success: false, error: new Error('duplicate')}),
    })
    await expect(api.create({partnerId, body: '別の内容', requestKey: 'same-key'})).rejects.toMatchObject({code: 'request-key-conflict'})
  })
})
