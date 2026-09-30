import {describe, expect, it} from 'vitest'
import {ContractApprovalPolicyReadError, createContractApprovalPolicyReadApi, defaultContractApprovalPolicy, parseActiveContractApprovalPolicy} from './contractApprovalPolicy'

const id = '11111111-1111-4111-8111-111111111111'
const policy = (contractUpdate: boolean) => JSON.stringify({schemaVersion: 1, companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate})

describe('contractApprovalPolicy', () => {
  it('selects the unique highest active settings version', () => {
    expect(parseActiveContractApprovalPolicy([
      {pl_settingsid: id, statecode: 0, pl_settingsversion: 1, pl_policyjson: policy(true)},
      {pl_settingsid: '22222222-2222-4222-8222-222222222222', statecode: 0, pl_settingsversion: 2, pl_policyjson: policy(false)},
    ])).toEqual({companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: false, mainOwner: true, settingsVersion: 2})
  })

  it('accepts platform-generated Dataverse ids whose version nibble is not RFC 4122', () => {
    expect(parseActiveContractApprovalPolicy([
      {pl_settingsid: '3f290d89-27b1-f111-aaac-e4fb1eff79c7', statecode: 0, pl_settingsversion: 1, pl_policyjson: policy(true)},
    ])).toEqual({companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: true, settingsVersion: 1})
  })

  it('fails closed for duplicate current version and unknown policy keys', () => {
    expect(() => parseActiveContractApprovalPolicy([
      {pl_settingsid: id, statecode: 0, pl_settingsversion: 1, pl_policyjson: policy(true)},
      {pl_settingsid: '22222222-2222-4222-8222-222222222222', statecode: 0, pl_settingsversion: 1, pl_policyjson: policy(false)},
    ])).toThrow(ContractApprovalPolicyReadError)
    expect(() => parseActiveContractApprovalPolicy([
      {pl_settingsid: id, statecode: 0, pl_settingsversion: 1, pl_policyjson: `${policy(true).slice(0, -1)},"extra":false}`},
    ])).toThrow(ContractApprovalPolicyReadError)
  })

  it('uses the default policy as version 0 when no active settings row exists', async () => {
    // 2026-09-27：インポート直後は行が無い。サーバー（ApprovalPolicyContract.DefaultPolicyJson）と同じ既定値で動く。
    const expected = {companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: true, settingsVersion: 0}
    expect(parseActiveContractApprovalPolicy([])).toEqual(expected)
    expect(defaultContractApprovalPolicy).toEqual(expected)
    const empty = createContractApprovalPolicyReadApi({getAll: async () => ({success: true, data: []}) as never})
    await expect(empty.getActivePolicy()).resolves.toEqual(expected)
  })

  it('版2の設定から主担当の変更の承認要否を読む（2026-09-29）', () => {
    const v2 = (mainOwner: unknown) => JSON.stringify({schemaVersion: 2, companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner})
    expect(parseActiveContractApprovalPolicy([{pl_settingsid: id, statecode: 0, pl_settingsversion: 6, pl_policyjson: v2(false)}]))
      .toEqual({companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: false, settingsVersion: 6})
    expect(() => parseActiveContractApprovalPolicy([{pl_settingsid: id, statecode: 0, pl_settingsversion: 6, pl_policyjson: v2('no')}])).toThrow(ContractApprovalPolicyReadError)
    // 版2で主担当が無い・版1で主担当がある・知らない版は、どれも読まない。
    const v2Missing = JSON.stringify({schemaVersion: 2, companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true})
    const v1WithMainOwner = JSON.stringify({schemaVersion: 1, companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: true})
    const v3 = JSON.stringify({schemaVersion: 3, companyName: true, tradingStatus: true, address: false, phone: false, contractUpdate: true, mainOwner: true})
    for (const json of [v2Missing, v1WithMainOwner, v3]) {
      expect(() => parseActiveContractApprovalPolicy([{pl_settingsid: id, statecode: 0, pl_settingsversion: 6, pl_policyjson: json}])).toThrow(ContractApprovalPolicyReadError)
    }
  })

  it('does not convert transport or unsuccessful result into a policy', async () => {
    const transport = createContractApprovalPolicyReadApi({getAll: async () => { throw new Error('network') }})
    await expect(transport.getActivePolicy()).rejects.toMatchObject({code: 'transport-error'})
    const rejected = createContractApprovalPolicyReadApi({getAll: async () => ({success: false, data: []}) as never})
    await expect(rejected.getActivePolicy()).rejects.toMatchObject({code: 'transport-error'})
  })
})
