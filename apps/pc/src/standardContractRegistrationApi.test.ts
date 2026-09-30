import { describe, expect, it, vi } from 'vitest'
import { createStandardContractRegistrationApi, buildStandardContractCreateRecord } from './standardContractRegistrationApi'

const request = {
  partnerId: '33333333-3333-4333-8333-333333333333',
  contractTypeId: '44444444-4444-4444-8444-444444444444',
  name: '基本取引契約（2026年度）',
  endDate: '2027-03-31',
  noticeDate: '2027-01-31',
  decisionDate: '2027-01-15',
  autoRenew: false,
  link: 'https://contoso.example/contracts/2026-basic',
  idempotencyKey: 'CreateContract-test-2',
}

describe('契約の標準Dataverse Serviceアダプター', () => {
  it('許可列だけを標準Createへ渡し、状態・signedDateをクライアントから渡さない', () => {
    expect(buildStandardContractCreateRecord(request)).toEqual({
      pl_name: request.name,
      'pl_PartnerLookup@odata.bind': `/pl_partners(${request.partnerId})`,
      'pl_ContractTypeLookup@odata.bind': `/pl_contracttypes(${request.contractTypeId})`,
      pl_enddate: '2027-03-31T00:00:00.000Z',
      pl_noticedate: '2027-01-31T00:00:00.000Z',
      pl_decisiondate: '2027-01-15T00:00:00.000Z',
      pl_autorenew: false,
      pl_link: request.link,
      pl_registrationkey: request.idempotencyKey,
    })
    expect(buildStandardContractCreateRecord(request)).not.toHaveProperty('pl_contractstatuscode')
    expect(buildStandardContractCreateRecord(request)).not.toHaveProperty('signedDate')
  })

  it('作成IDを返す', async () => {
    const create = vi.fn(async () => ({success: true, data: {pl_contractid: '55555555-5555-4555-8555-555555555555'}}))
    const api = createStandardContractRegistrationApi({
      create,
      findByRegistrationKey: async () => ({success: true, data: []}),
    })
    await expect(api.registerContract(request)).resolves.toEqual({contractId: '55555555-5555-4555-8555-555555555555', isReplay: false})
    expect(create).toHaveBeenCalledTimes(1)
  })
})
