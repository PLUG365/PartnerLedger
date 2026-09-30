import { describe, expect, it } from 'vitest'
import { ContractReadError, createContractReadApi, parseContractRead, type ContractReadItem } from './contractReadModel'

const contractId = '33333333-3333-4333-8333-333333333333'
const partnerId = '44444444-4444-4444-8444-444444444444'
const contractTypeId = '55555555-5555-4555-8555-555555555555'
const formattedContractTypeLookupName = '_pl_contracttypelookup_value@OData.Community.Display.V1.FormattedValue'

const validRecord = {
  pl_contractid: contractId,
  pl_name: '基本取引契約（2026年度）',
  _pl_partnerlookup_value: partnerId,
  pl_partnerlookupname: '青空ソリューションズ株式会社',
  _pl_contracttypelookup_value: contractTypeId,
  [formattedContractTypeLookupName]: '基本取引契約',
  pl_contractstatuscode: 100000000,
  pl_enddate: '2027-03-31T00:00:00.000Z',
  pl_noticedate: '2027-01-31T00:00:00.000Z',
  pl_decisiondate: '2027-01-15T00:00:00.000Z',
  pl_autorenew: true,
  pl_link: 'https://contoso.example/contracts/2026-basic',
  statecode: 0,
}

describe('Dataverse契約Readの表示契約', () => {
  it('最小列を契約表示へ変換し、期限3種を分離する', () => {
    expect(parseContractRead([validRecord], partnerId)).toEqual([{
      id: contractId,
      name: '基本取引契約（2026年度）',
      partnerId,
      partnerName: '青空ソリューションズ株式会社',
      contractTypeId,
      contractTypeName: '基本取引契約',
      status: '締結済み',
      endDate: '2027-03-31T00:00:00.000Z',
      noticeDate: '2027-01-31T00:00:00.000Z',
      decisionDate: '2027-01-15T00:00:00.000Z',
      autoRenew: true,
      link: 'https://contoso.example/contracts/2026-basic',
    }])
  })

  it('表示名がアノテーションにある場合と未設定値を推測せず扱う', () => {
    const response = {
      pl_contractid: contractId,
      pl_name: '契約',
      _pl_partnerlookup_value: partnerId,
      _pl_contracttypelookup_value: contractTypeId,
      pl_contractstatuscode: null,
      pl_enddate: null,
      pl_autorenew: null,
      statecode: 0,
      [formattedContractTypeLookupName]: '業務委託契約',
    }
    expect(parseContractRead([response], partnerId)).toMatchObject([{
      contractTypeName: '業務委託契約',
      status: '未設定',
      endDate: undefined,
      autoRenew: undefined,
    }])
  })

  it('終了状態・検索対象外の親・ID重複をfail-closedで拒否する', () => {
    expect(() => parseContractRead([{...validRecord, statecode: 1}], partnerId)).toThrowError(/状態/)
    expect(() => parseContractRead([{...validRecord, _pl_partnerlookup_value: '66666666-6666-4666-8666-666666666666'}], partnerId)).toThrowError(/親取引先/)
    expect(() => parseContractRead([validRecord, validRecord], partnerId)).toThrowError(/重複/)
  })

  it('必須ID・名前・型不正を全体拒否する', () => {
    expect(() => parseContractRead([{...validRecord, pl_contractid: 'not-a-guid'}], partnerId)).toThrowError(/ContractId/)
    expect(() => parseContractRead([{...validRecord, pl_name: ''}], partnerId)).toThrowError(/契約名/)
    expect(() => parseContractRead([{...validRecord, pl_enddate: 123}], partnerId)).toThrowError(/契約終了日/)
    expect(() => parseContractRead([{...validRecord, pl_autorenew: 'true'}], partnerId)).toThrowError(/自動更新/)
  })

  it('通信失敗・不正の親IDはモックへフォールバックせず拒否する', async () => {
    const api = createContractReadApi({listContracts: async () => ({success: false, data: []})})
    await expect(api.listContracts(partnerId)).rejects.toMatchObject<Partial<ContractReadError>>({code: 'transport-error'})
    await expect(api.listContracts('not-a-guid')).rejects.toMatchObject<Partial<ContractReadError>>({code: 'invalid-input'})
  })

  it('契約項目を会社単位で合算する表示関数へ渡せる形を保持する', () => {
    const contracts: ContractReadItem[] = parseContractRead([
      validRecord,
      {...validRecord, pl_contractid: '77777777-7777-4777-8777-777777777777', pl_name: '業務委託契約', pl_contractstatuscode: 100000001, pl_autorenew: false},
    ], partnerId)
    expect(contracts.map(contract => [contract.name, contract.status, contract.autoRenew])).toEqual([
      ['基本取引契約（2026年度）', '締結済み', true],
      ['業務委託契約', '終了', false],
    ])
  })
})
