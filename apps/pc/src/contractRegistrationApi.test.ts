import { describe, expect, it, vi } from 'vitest'
import { ContractRegistrationApiError, createContractLedgerRegistrationApi, type ContractRegistrationReplayRecord } from './contractRegistrationApi'

const partnerId = '33333333-3333-4333-8333-333333333333'
const contractTypeId = '44444444-4444-4444-8444-444444444444'
const contractId = '55555555-5555-4555-8555-555555555555'
const key = 'CreateContract-test-1'

const request = {
  partnerId,
  contractTypeId,
  name: '基本取引契約（2026年度）',
  endDate: '2027-03-31',
  noticeDate: '2027-01-31',
  decisionDate: '2027-01-15',
  autoRenew: true,
  link: 'https://contoso.example/contracts/2026-basic',
  idempotencyKey: key,
}

const existing = {
  pl_contractid: contractId,
  pl_name: request.name,
  _pl_partnerlookup_value: partnerId,
  _pl_contracttypelookup_value: contractTypeId,
  pl_contractstatuscode: 100000000,
  pl_enddate: '2027-03-31T00:00:00.000Z',
  pl_noticedate: '2027-01-31T00:00:00.000Z',
  pl_decisiondate: '2027-01-15T00:00:00.000Z',
  pl_autorenew: true,
  pl_link: request.link,
  pl_registrationkey: key,
}

function createTestApi(records: ContractRegistrationReplayRecord[], createResult: {success: boolean; contractId?: string} = {success: true, contractId: contractId}) {
  const findByRegistrationKey = vi.fn(async (registrationKey: string): Promise<{success: boolean; data: ContractRegistrationReplayRecord[]}> => { void registrationKey; return {success: true, data: records} })
  const create = vi.fn(async () => createResult)
  return {api: createContractLedgerRegistrationApi({findByRegistrationKey}, create), findByRegistrationKey, create}
}

describe('契約標準CreateのPC側保存境界', () => {
  it('同じ要求キー・内容の再送は既存IDへ収束し、Createを呼ばない', async () => {
    const test = createTestApi([existing])
    await expect(test.api.registerContract(request)).resolves.toEqual({contractId, isReplay: true})
    expect(test.create).not.toHaveBeenCalled()
  })

  it('再送結果のBoolean型不正を成功や別内容へ丸めない', async () => {
    const test = createTestApi([{...existing, pl_autorenew: 'false'}])
    await expect(test.api.registerContract({...request, autoRenew: false})).rejects.toMatchObject<Partial<ContractRegistrationApiError>>({code: 'invalid-response'})
    expect(test.create).not.toHaveBeenCalled()
  })

  it('同じ要求キーの内容違いを拒否する', async () => {
    const test = createTestApi([existing])
    await expect(test.api.registerContract({...request, name: '別契約'})).rejects.toMatchObject<Partial<ContractRegistrationApiError>>({code: 'idempotency-conflict'})
    expect(test.create).not.toHaveBeenCalled()
  })

  it('Create成功を返さない場合でも再読取りで確定結果を拾う', async () => {
    const findByRegistrationKey = vi.fn()
      .mockResolvedValueOnce({success: true, data: []})
      .mockResolvedValueOnce({success: true, data: [existing]})
    const api = createContractLedgerRegistrationApi({findByRegistrationKey}, async () => ({success: false}))
    await expect(api.registerContract(request)).resolves.toEqual({contractId, isReplay: true})
    expect(findByRegistrationKey).toHaveBeenCalledTimes(2)
  })

  it('必須値・GUID・日付・長さを検査し、要求キーを自動生成できる', async () => {
    const test = createTestApi([])
    await expect(test.api.registerContract({...request, partnerId: 'bad'})).rejects.toMatchObject({code: 'invalid-guid'})
    await expect(test.api.registerContract({...request, endDate: '2027-02-30'})).rejects.toMatchObject({code: 'invalid-date'})
    await expect(test.api.registerContract({...request, name: ''})).rejects.toMatchObject({code: 'required'})
    await expect(test.api.registerContract({...request, idempotencyKey: undefined})).resolves.toEqual({contractId, isReplay: false})
    expect(test.findByRegistrationKey.mock.calls[0][0]).toMatch(/^CreateContract-/)
  })

  it('保存結果が未確定で再読取りにも無ければ失敗する', async () => {
    const test = createTestApi([], {success: false})
    await expect(test.api.registerContract(request)).rejects.toMatchObject({code: 'transport-error'})
    expect(test.findByRegistrationKey).toHaveBeenCalledTimes(2)
  })
})
