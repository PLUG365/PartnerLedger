import {describe, expect, it, vi} from 'vitest'
import {buildStandardContractUpdateRecord, ContractDirectUpdateError, createContractDirectUpdateApi} from './contractDirectUpdate'
import type {ContractReadItem} from './contractReadModel'

const contract: ContractReadItem = {
  id: '11111111-1111-4111-8111-111111111111',
  partnerId: '22222222-2222-4222-8222-222222222222',
  name: '基本契約',
  contractTypeId: '33333333-3333-4333-8333-333333333333',
  contractTypeName: '基本取引契約',
  status: '締結済み',
  endDate: '2027-03-31T00:00:00.000Z',
  noticeDate: '2027-01-31T00:00:00.000Z',
  decisionDate: '2027-01-15T00:00:00.000Z',
  autoRenew: true,
  link: 'https://contoso.example/old',
}

describe('contractDirectUpdate', () => {
  it('standard Update payload contains only seven business fields', () => {
    const result = buildStandardContractUpdateRecord({
      contract,
      name: '  変更後契約  ',
      decision: '終了',
      endDate: '2028-03-31',
      noticeDate: '2028-01-31',
      decisionDate: '2028-01-15',
      autoRenew: false,
      link: ' https://contoso.example/new ',
    })

    expect(result.contractId).toBe(contract.id)
    expect(result.fields).toEqual({
      pl_name: '変更後契約',
      pl_contractstatuscode: 100000001,
      pl_enddate: '2028-03-31T00:00:00.000Z',
      pl_noticedate: '2028-01-31T00:00:00.000Z',
      pl_decisiondate: '2028-01-15T00:00:00.000Z',
      pl_autorenew: false,
      pl_link: 'https://contoso.example/new',
    })
    expect(Object.keys(result.fields)).not.toContain('ownerid')
    expect(Object.keys(result.fields)).not.toContain('statecode')
  })

  it('clears optional dates and link explicitly without changing parent/type', () => {
    const result = buildStandardContractUpdateRecord({
      contract,
      name: '変更',
      decision: '更新',
      autoRenew: false,
    })
    expect(result.fields.pl_enddate).toBeNull()
    expect(result.fields.pl_noticedate).toBeNull()
    expect(result.fields.pl_decisiondate).toBeNull()
    expect(result.fields.pl_link).toBeNull()
    expect(result.fields).not.toHaveProperty('pl_PartnerLookup@odata.bind')
    expect(result.fields).not.toHaveProperty('pl_ContractTypeLookup@odata.bind')
  })

  it('fails closed for invalid input and unsuccessful standard update', async () => {
    expect(() => buildStandardContractUpdateRecord({...({contract} as const), name: '', decision: '更新', autoRenew: false})).toThrow(ContractDirectUpdateError)
    expect(() => buildStandardContractUpdateRecord({...({contract} as const), name: '変更', decision: '更新', autoRenew: false, endDate: '2027-02-30'})).toThrow(ContractDirectUpdateError)

    const update = vi.fn().mockResolvedValue({success: false, error: new Error('forbidden')})
    const api = createContractDirectUpdateApi({update})
    await expect(api.updateContract({contract, name: '変更', decision: '更新', autoRenew: false})).rejects.toMatchObject({code: 'operation-rejected'})
    expect(update).toHaveBeenCalledTimes(1)
  })
})
