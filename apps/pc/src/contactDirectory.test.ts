import { describe, expect, it } from 'vitest'
import { createContactDirectoryApi, filterContactDirectory, parseContactDirectory, ContactDirectoryError, type ContactDirectoryItem } from './contactDirectory'

const contactId = '33333333-3333-4333-8333-333333333333'
const partnerId = '44444444-4444-4444-8444-444444444444'
const formattedPartnerLookupName = '_pl_partnerlookup_value@OData.Community.Display.V1.FormattedValue'

const validRecord = {
  pl_contactid: contactId,
  pl_name: '井上 真帆',
  _pl_partnerlookup_value: partnerId,
  pl_partnerlookupname: '青空ソリューションズ株式会社',
  pl_departmentrole: 'パートナー推進部 部長',
  pl_email: 'm.inoue@example.com',
  pl_phone: '03-1234-5678',
  pl_statuscode: 100000000,
  pl_registeredat: '2026-09-12T00:00:00.000Z',
  statecode: 0,
}

describe('Dataverse担当者一覧の読み取り契約', () => {
  it('最小列を表示契約へ変換し、Choice名が無い場合はコードを解決する', () => {
    expect(parseContactDirectory([validRecord])).toEqual([{
      id: contactId,
      name: '井上 真帆',
      partnerId,
      partnerName: '青空ソリューションズ株式会社',
      departmentRole: 'パートナー推進部 部長',
      email: 'm.inoue@example.com',
      phone: '03-1234-5678',
      status: '在籍',
      registeredAt: '2026-09-12T00:00:00.000Z',
    }])
  })

  it('DataverseのLookup表示アノテーションと未設定のChoiceを安全に表示へ変換する', () => {
    const response = {
      pl_contactid: contactId,
      pl_name: '井上 真帆',
      _pl_partnerlookup_value: partnerId,
      [formattedPartnerLookupName]: '青空ソリューションズ株式会社',
      pl_statuscode: null,
      statecode: 0,
    }

    expect(parseContactDirectory([response])).toMatchObject([{
      partnerId,
      partnerName: '青空ソリューションズ株式会社',
      status: '未設定',
    }])
  })

  it('UI検索は担当者・親取引先・部署・連絡先・在籍状態を横断する', () => {
    const contacts: ContactDirectoryItem[] = parseContactDirectory([
      validRecord,
      {...validRecord, pl_contactid: '55555555-5555-4555-8555-555555555555', pl_name: '中村 光', pl_partnerlookupname: '北斗精機工業株式会社', pl_departmentrole: '営業部', pl_statuscodename: '休職'},
    ])
    expect(filterContactDirectory(contacts, '北斗')).toHaveLength(1)
    expect(filterContactDirectory(contacts, '営業部')[0].id).toBe('55555555-5555-4555-8555-555555555555')
    expect(filterContactDirectory(contacts, '休職')[0].name).toBe('中村 光')
    expect(filterContactDirectory(contacts, 'm.inoue@example.com')[0].name).toBe('井上 真帆')
  })

  it('追加項目が未設定でも読み取れる', () => {
    const withoutAdditionalFields = Object.fromEntries(Object.entries(validRecord).filter(([key]) => !['pl_departmentrole', 'pl_email', 'pl_phone'].includes(key)))
    expect(parseContactDirectory([withoutAdditionalFields])[0]).toMatchObject({
      departmentRole: undefined,
      email: undefined,
      phone: undefined,
    })
  })

  it('追加項目の不正な型を全体拒否する', () => {
    expect(() => parseContactDirectory([{...validRecord, pl_departmentrole: 123}])).toThrowError(/部署・役職/)
    expect(() => parseContactDirectory([{...validRecord, pl_email: 123}])).toThrowError(/メールアドレス/)
    expect(() => parseContactDirectory([{...validRecord, pl_phone: 123}])).toThrowError(/電話番号/)
  })

  it('通信失敗はモックへフォールバックせず拒否する', async () => {
    const api = createContactDirectoryApi({listContacts: async () => ({success: false, data: [], error: new Error('offline')})})
    await expect(api.listContacts()).rejects.toMatchObject<Partial<ContactDirectoryError>>({code: 'transport-error'})
  })

  it('不正な行・親取引先なし・非アクティブ行・重複IDを全体拒否する', () => {
    expect(() => parseContactDirectory([{...validRecord, pl_contactid: 'not-a-guid'}])).toThrowError(/ContactId/)
    expect(() => parseContactDirectory([{...validRecord, _pl_partnerlookup_value: undefined}])).toThrowError(/親取引先Id/)
    expect(parseContactDirectory([{...validRecord, pl_partnerlookupname: undefined}])[0].partnerName).toBe('取引先名未取得')
    expect(() => parseContactDirectory([{...validRecord, pl_partnerlookupname: undefined, [formattedPartnerLookupName]: 123}])).toThrowError(/親取引先名/)
    expect(() => parseContactDirectory([{...validRecord, pl_statuscode: 'invalid'}])).toThrowError(/在籍状態/)
    expect(() => parseContactDirectory([{...validRecord, statecode: 1}])).toThrowError(/状態/)
    expect(() => parseContactDirectory([validRecord, validRecord])).toThrowError(/重複/)
  })
})
