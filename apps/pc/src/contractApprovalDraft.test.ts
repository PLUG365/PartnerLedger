import { describe, expect, it, vi } from 'vitest'
import { buildContractApprovalChangeSetJson, ContractApprovalDraftError, ContractApprovalDraftSaveError, createContractApprovalRequestKey, saveContractApprovalDraft } from './contractApprovalDraft'

const contract = {
  id: '11111111-1111-4111-8111-111111111111',
  name: '基本取引契約',
  partnerId: '22222222-2222-4222-8222-222222222222',
  status: '締結済み' as const,
  endDate: '2027-03-31T00:00:00.000Z',
  noticeDate: '2027-01-31T00:00:00.000Z',
  decisionDate: '2027-01-15T00:00:00.000Z',
  autoRenew: true,
  link: 'https://example.test/contracts/1',
}

describe('contract approval draft contract', () => {
  it('固定順序のcontract.update変更セットを作り、権威列を含めない', () => {
    const json = buildContractApprovalChangeSetJson({
      contract,
      name: '基本取引契約（更新）',
      decision: '更新',
      endDate: '2028-03-31',
      noticeDate: '',
      decisionDate: '2028-01-15',
      autoRenew: false,
      link: '',
    })

    expect(JSON.parse(json)).toEqual({
      schemaVersion: 1,
      target: {entity: 'pl_contract', id: contract.id},
      changes: [
        {attribute: 'pl_name', value: '基本取引契約（更新）'},
        {attribute: 'pl_contractstatuscode', value: 100000000},
        {attribute: 'pl_enddate', value: '2028-03-31T00:00:00.000Z'},
        {attribute: 'pl_noticedate', value: null},
        {attribute: 'pl_decisiondate', value: '2028-01-15T00:00:00.000Z'},
        {attribute: 'pl_autorenew', value: false},
        {attribute: 'pl_link', value: null},
      ],
    })
    expect(json).not.toContain('contractType')
    expect(json).not.toContain('signedDate')
    expect(json).not.toContain('owner')
  })

  it('終了判断は許可された終了Choiceへ変換する', () => {
    const parsed = JSON.parse(buildContractApprovalChangeSetJson({
      contract,
      name: '基本取引契約（終了）',
      decision: '終了',
      autoRenew: false,
    })) as {changes: Array<{attribute: string; value: unknown}>}
    expect(parsed.changes.find(change => change.attribute === 'pl_contractstatuscode')?.value).toBe(100000001)
  })

  it('GUID・日付・必須値をfail-closedで検査する', () => {
    expect(() => buildContractApprovalChangeSetJson({...({contract: {...contract, id: 'not-a-guid'}, name: '申請', decision: '更新', autoRenew: true})})).toThrow(ContractApprovalDraftError)
    expect(() => buildContractApprovalChangeSetJson({...({contract, name: '申請', decision: '更新', endDate: '2028-02-30', autoRenew: true})})).toThrow(/不正/)
    expect(() => buildContractApprovalChangeSetJson({...({contract, name: '', decision: '更新', autoRenew: true})})).toThrow(/契約名を指定/)
  })

  it('要求キーは再利用可能な接頭辞付き値を作る', () => {
    expect(createContractApprovalRequestKey()).toMatch(/^ContractUpdate-.+/)
  })

  it('Requestを先に1回作成し、作成済みRequestを渡した再試行では再Createしない', async () => {
    const requestId = '33333333-3333-4333-8333-333333333333'
    const submissionVersionId = '44444444-4444-4444-8444-444444444444'
    const createRequest = vi.fn().mockResolvedValue({requestId})
    const createSubmissionDraft = vi.fn().mockResolvedValue({submissionVersionId})
    const api = {createRequest, createSubmissionDraft}
    const input = {contract, name: '基本取引契約（更新）', decision: '更新' as const, autoRenew: false}

    await expect(saveContractApprovalDraft(api, input, {idempotencyKey: 'ContractUpdate-test'})).resolves.toEqual({requestId, submissionVersionId})
    await expect(saveContractApprovalDraft(api, input, {idempotencyKey: 'ContractUpdate-test', requestId})).resolves.toEqual({requestId, submissionVersionId})
    expect(createRequest).toHaveBeenCalledTimes(1)
    expect(createSubmissionDraft).toHaveBeenCalledTimes(2)
    expect(createSubmissionDraft.mock.calls[0][0]).toMatchObject({requestId, name: '基本取引契約（更新） 提出版'})
  })

  it('SubmissionVersionの結果不明はrequestId付きの停止エラーにする', async () => {
    const requestId = '55555555-5555-4555-8555-555555555555'
    const api = {
      createRequest: vi.fn().mockResolvedValue({requestId}),
      createSubmissionDraft: vi.fn().mockRejectedValue(new Error('通信失敗')),
    }
    await expect(saveContractApprovalDraft(api, {contract, name: '申請', decision: '終了', autoRenew: false}, {idempotencyKey: 'ContractUpdate-test'})).rejects.toMatchObject<Partial<ContractApprovalDraftSaveError>>({phase: 'submission', requestId})
  })
})
