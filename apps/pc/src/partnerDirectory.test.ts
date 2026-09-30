import { describe, expect, it } from 'vitest'
import { createPartnerDirectoryApi, filterPartnerDirectory, parsePartnerDirectory, PartnerDirectoryError, type PartnerDirectoryItem } from './partnerDirectory'

const partnerId = '33333333-3333-4333-8333-333333333333'

const validRecord = {
  pl_partnerid: partnerId,
  pl_name: '青空ソリューションズ株式会社',
  pl_industry: 'ITサービス',
  pl_address: '東京都千代田区1-1-1',
  pl_phone: '03-1234-5678',
  pl_tradingstatuscode: 100000001,
  pl_aclversion: 3,
  pl_registeredat: '2026-09-12T00:00:00.000Z',
  pl_mainownerlookupname: '主担当者',
  pl_registeredbylookupname: '佐伯 菜月',
  owneridname: '田中 健一',
  createdbyname: '佐伯 菜月',
  statecode: 0,
}

describe('Dataverse取引先一覧の読み取り契約', () => {
  it('最小列を表示契約へ変換し、Choice名が無い場合はコードを解決する', () => {
    expect(parsePartnerDirectory([validRecord])).toEqual([{
      id: partnerId,
      name: '青空ソリューションズ株式会社',
      industry: 'ITサービス',
      address: '東京都千代田区1-1-1',
      phone: '03-1234-5678',
      tradingStatus: '取引中',
      mainOwnerName: '主担当者',
      ownerName: '田中 健一',
      registeredBy: '佐伯 菜月',
      registeredAt: '2026-09-12T00:00:00.000Z',
      aclVersion: 3,
    }])
  })

  it('UI検索は会社名・業種・住所・代表電話・状態・所有者を横断する', () => {
    const partners: PartnerDirectoryItem[] = parsePartnerDirectory([
      validRecord,
      {...validRecord, pl_partnerid: '44444444-4444-4444-8444-444444444444', pl_name: '北斗精機工業株式会社', pl_tradingstatuscodename: '休止中', owneridname: '中村 光'},
    ])
    expect(filterPartnerDirectory(partners, '北斗')).toHaveLength(1)
    expect(filterPartnerDirectory(partners, '休止中')[0].id).toBe('44444444-4444-4444-8444-444444444444')
    expect(filterPartnerDirectory(partners, '中村 光')[0].name).toBe('北斗精機工業株式会社')
    expect(filterPartnerDirectory(partners, '東京都千代田区')[0].name).toBe('青空ソリューションズ株式会社')
    expect(filterPartnerDirectory(partners, '03-1234-5678')[0].name).toBe('青空ソリューションズ株式会社')
  })

  it('追加項目が未設定でも読み取れる', () => {
    const withoutAdditionalFields = Object.fromEntries(Object.entries(validRecord).filter(([key]) => !['pl_industry', 'pl_address', 'pl_phone'].includes(key)))
    expect(parsePartnerDirectory([withoutAdditionalFields])[0]).toMatchObject({
      industry: undefined,
      address: undefined,
      phone: undefined,
    })
  })

  it('Lookup名がFormattedValueだけでも主担当・所有者・登録者を表示できる', () => {
    const formatted = Object.fromEntries(Object.entries(validRecord).filter(([key]) => !['pl_mainownerlookupname', 'pl_registeredbylookupname', 'owneridname', 'createdbyname'].includes(key)))
    expect(parsePartnerDirectory([{
      ...formatted,
      ['_pl_mainownerlookup_value@OData.Community.Display.V1.FormattedValue']: 'Formatted主担当',
      _pl_mainownerlookup_value: '55555555-5555-4555-8555-555555555555',
      ['_pl_registeredbylookup_value@OData.Community.Display.V1.FormattedValue']: 'Formatted登録者',
      ['_ownerid_value@OData.Community.Display.V1.FormattedValue']: 'Formatted所有者',
    }])[0]).toMatchObject({
      // 主担当の変更フォームで今の主担当と比べるため、IDも持つ（2026-09-29）。
      mainOwnerId: '55555555-5555-4555-8555-555555555555',
      mainOwnerName: 'Formatted主担当',
      ownerName: 'Formatted所有者',
      registeredBy: 'Formatted登録者',
    })
  })

  it('追加項目の不正な型を全体拒否する', () => {
    expect(() => parsePartnerDirectory([{...validRecord, pl_industry: 123}])).toThrowError(/業種/)
    expect(() => parsePartnerDirectory([{...validRecord, pl_address: 123}])).toThrowError(/住所/)
    expect(() => parsePartnerDirectory([{...validRecord, pl_phone: 123}])).toThrowError(/代表電話/)
  })

  it('通信失敗はモックへフォールバックせず拒否する', async () => {
    const api = createPartnerDirectoryApi({listPartners: async () => ({success: false, data: [], error: new Error('offline')})})
    await expect(api.listPartners()).rejects.toMatchObject<Partial<PartnerDirectoryError>>({code: 'transport-error'})
  })

  it('不正な行・非アクティブ行・重複IDを全体拒否する', () => {
    expect(() => parsePartnerDirectory([{...validRecord, pl_partnerid: 'not-a-guid'}])).toThrowError(/PartnerId/)
    expect(() => parsePartnerDirectory([{...validRecord, statecode: 1}])).toThrowError(/状態/)
    expect(() => parsePartnerDirectory([validRecord, validRecord])).toThrowError(/重複/)
    expect(() => parsePartnerDirectory([{...validRecord, pl_aclversion: 0}])).toThrowError(/取引先の情報/)
  })
})
