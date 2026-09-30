import { describe, expect, it } from 'vitest'
import { ArchiveDirectoryError, createArchiveDirectoryApi, parseArchivedContacts, parseArchivedPartners } from './archiveDirectory'

const partnerId = '11111111-1111-4111-8111-111111111111'
const contactId = '22222222-2222-4222-8222-222222222222'
const parentId = '33333333-3333-4333-8333-333333333333'

describe('archiveDirectory', () => {
  it('終了取引先はActive行でもアーカイブ対象として解析する', () => {
    const items = parseArchivedPartners([{
      pl_partnerid: partnerId,
      pl_name: '終了会社',
      pl_tradingstatuscode: 100000003,
      pl_registeredat: '2026-09-19T00:00:00Z',
      statecode: 0,
    }])
    expect(items).toEqual([expect.objectContaining({id: partnerId, name: '終了会社', tradingStatus: '終了', state: 'Active'})])
  })

  it('退職担当者は親LookupとFormattedValueを保持する', () => {
    const items = parseArchivedContacts([{
      pl_contactid: contactId,
      pl_name: '退職担当者',
      _pl_partnerlookup_value: parentId,
      ['_pl_partnerlookup_value@OData.Community.Display.V1.FormattedValue']: '親会社',
      pl_statuscode: 100000002,
      statecode: 1,
    }])
    expect(items[0]).toEqual(expect.objectContaining({id: contactId, partnerId: parentId, partnerName: '親会社', status: '退職', state: 'Inactive'}))
  })

  it('終了取引先は主担当・登録者LookupのFormattedValueを表示名へ解決する', () => {
    const items = parseArchivedPartners([{
      pl_partnerid: partnerId,
      pl_name: '終了会社',
      pl_tradingstatuscode: 100000003,
      ['_pl_mainownerlookup_value@OData.Community.Display.V1.FormattedValue']: '主担当者',
      ['_pl_registeredbylookup_value@OData.Community.Display.V1.FormattedValue']: '登録者',
      ['_ownerid_value@OData.Community.Display.V1.FormattedValue']: '所有者',
      statecode: 1,
    }])
    expect(items[0]).toEqual(expect.objectContaining({mainOwnerName: '主担当者', ownerName: '所有者', registeredBy: '登録者'}))
  })

  it('通常状態の行や不正な行をアーカイブとして表示しない', () => {
    expect(() => parseArchivedPartners([{
      pl_partnerid: partnerId,
      pl_name: '取引中会社',
      pl_tradingstatuscode: 100000001,
      statecode: 0,
    }])).toThrowError(ArchiveDirectoryError)
    expect(() => parseArchivedContacts([{
      pl_contactid: contactId,
      pl_name: '担当者',
      _pl_partnerlookup_value: parentId,
      pl_statuscode: 100000000,
      statecode: 0,
    }])).toThrowError(/アーカイブ担当者/)
  })

  it('重複IDを全体エラーにし、通信失敗も成功扱いにしない', async () => {
    expect(() => parseArchivedPartners([
      {pl_partnerid: partnerId, pl_name: '終了会社', pl_tradingstatuscode: 100000003, statecode: 1},
      {pl_partnerid: partnerId, pl_name: '終了会社2', pl_tradingstatuscode: 100000003, statecode: 1},
    ])).toThrowError(/重複/)
    const api = createArchiveDirectoryApi({
      listArchivedPartners: async () => ({success: false, data: []}),
      listArchivedContacts: async () => { throw new Error('offline') },
    })
    await expect(api.listArchivedPartners()).rejects.toMatchObject({code: 'operation-rejected'})
    await expect(api.listArchivedContacts()).rejects.toMatchObject({code: 'transport-error'})
  })
})
