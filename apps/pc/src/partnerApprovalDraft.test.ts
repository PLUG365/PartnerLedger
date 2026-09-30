import {describe, expect, it, vi} from 'vitest'
import {PartnerApprovalDraftError, PartnerApprovalDraftSaveError, buildPartnerApprovalChangeSetJson, savePartnerApprovalDraft, submitFailureMessage} from './partnerApprovalDraft'
import type {PartnerDirectoryItem} from './partnerDirectory'
import {StandardApprovalWriteError, type StandardApprovalWriteApi} from './standardApprovalTransitionApi'

const partner: PartnerDirectoryItem = {
  id: '11111111-1111-4111-8111-111111111111',
  name: '株式会社サンプル',
  tradingStatus: '取引中',
  aclVersion: 1,
}

const api = (overrides: Partial<StandardApprovalWriteApi> = {}): StandardApprovalWriteApi => ({
  createRequest: vi.fn(async () => ({requestId: '22222222-2222-4222-8222-222222222222'})),
  createSubmissionDraft: vi.fn(async () => ({submissionVersionId: '33333333-3333-4333-8333-333333333333'})),
  updateRequestDraft: vi.fn(),
  updateSubmissionDraft: vi.fn(),
  submit: vi.fn(),
  ...overrides,
})

describe('partnerApprovalDraft', () => {
  it('creates a one-field standard approval change set for each supported partner field', () => {
    expect(JSON.parse(buildPartnerApprovalChangeSetJson({partner, target: 'companyName', value: '  新会社  '}))).toEqual({
      schemaVersion: 1,
      target: {entity: 'pl_partner', id: partner.id},
      changes: [{attribute: 'pl_name', value: '新会社'}],
    })
    expect(JSON.parse(buildPartnerApprovalChangeSetJson({partner, target: 'tradingStatus', value: '終了'})).changes).toEqual([{attribute: 'pl_tradingstatuscode', value: 100000003}])
    expect(JSON.parse(buildPartnerApprovalChangeSetJson({partner, target: 'address', value: ' '})).changes).toEqual([{attribute: 'pl_address', value: null}])
    expect(JSON.parse(buildPartnerApprovalChangeSetJson({partner, target: 'phone', value: '03-0000-0000'})).changes).toEqual([{attribute: 'pl_phone', value: '03-0000-0000'}])
  })

  it('主担当の変更は、新しい主担当のIDだけを変更内容にし、主担当の申請として作る（2026-09-29）', async () => {
    const owner = '44444444-4444-4444-8444-444444444444'
    expect(JSON.parse(buildPartnerApprovalChangeSetJson({partner, target: 'mainOwner', value: ` ${owner.toUpperCase()} `})).changes)
      .toEqual([{attribute: 'pl_mainownerlookup', value: owner}])
    expect(() => buildPartnerApprovalChangeSetJson({partner, target: 'mainOwner', value: 'not-a-user'})).toThrowError(/主担当/)
    expect(() => buildPartnerApprovalChangeSetJson({partner, target: 'mainOwner', value: ' '})).toThrowError(/主担当/)
    const approvalApi = api()
    await savePartnerApprovalDraft(approvalApi, {partner, target: 'mainOwner', value: owner}, {idempotencyKey: 'main-owner-1'})
    expect(approvalApi.createRequest).toHaveBeenCalledWith(expect.objectContaining({name: '株式会社サンプル 主担当変更', requestTypeCode: 'partner.main-owner'}))
  })

  it('saves Request then SubmissionVersion without submitting or authority fields', async () => {
    const approvalApi = api()
    const result = await savePartnerApprovalDraft(approvalApi, {partner, target: 'address', value: '東京都'}, {idempotencyKey: 'partner-address-1'})
    expect(result).toEqual({requestId: '22222222-2222-4222-8222-222222222222', submissionVersionId: '33333333-3333-4333-8333-333333333333'})
    expect(approvalApi.createRequest).toHaveBeenCalledWith(expect.objectContaining({requestTypeCode: 'partner.address', partnerId: partner.id, idempotencyKey: 'partner-address-1'}))
    expect(approvalApi.createSubmissionDraft).toHaveBeenCalledWith(expect.objectContaining({requestId: result.requestId, changeSetJson: expect.not.stringContaining('ownerid')}))
    expect(approvalApi.submit).not.toHaveBeenCalled()
  })

  it('returns request phase and submission phase separately so uncertain writes are not retried', async () => {
    const requestFailure = api({createRequest: vi.fn(async () => { throw new Error('request failed') })})
    await expect(savePartnerApprovalDraft(requestFailure, {partner, target: 'phone', value: '03'}, {idempotencyKey: 'request-fail'})).rejects.toMatchObject({phase: 'request'})

    const submissionFailure = api({createSubmissionDraft: vi.fn(async () => { throw new Error('submission failed') })})
    const error = await savePartnerApprovalDraft(submissionFailure, {partner, target: 'phone', value: '03'}, {idempotencyKey: 'submission-fail'}).catch(value => value instanceof PartnerApprovalDraftSaveError ? value : undefined)
    expect(error).toBeInstanceOf(PartnerApprovalDraftSaveError)
    expect((error as PartnerApprovalDraftSaveError).phase).toBe('submission')
    expect((error as PartnerApprovalDraftSaveError).requestId).toBe('22222222-2222-4222-8222-222222222222')
  })

  it('rejects invalid ids, required names, statuses, and long values', () => {
    expect(() => buildPartnerApprovalChangeSetJson({partner: {...partner, id: 'bad'}, target: 'phone', value: '03'})).toThrowError(PartnerApprovalDraftError)
    expect(() => buildPartnerApprovalChangeSetJson({partner, target: 'companyName', value: ' '})).toThrowError(/会社名を指定/)
    expect(() => buildPartnerApprovalChangeSetJson({partner, target: 'tradingStatus', value: '不明'})).toThrowError(/取引状態が不正/)
    expect(() => buildPartnerApprovalChangeSetJson({partner, target: 'address', value: 'x'.repeat(201)})).toThrowError(/200文字以内/)
  })
})

describe('申請を保存した後の提出の失敗（2026-09-30）', () => {
  it('主担当の変更が断られた理由を出し、取り下げて出し直すよう案内する', () => {
    const message = submitFailureMessage(new StandardApprovalWriteError('選んだ主担当は、通常の有効な利用者ではないため選べません。', 'main-owner-unavailable'))
    expect(message).toContain('選んだ主担当は、通常の有効な利用者ではないため選べません。')
    expect(message).toContain('取り下げ')
    expect(submitFailureMessage(new StandardApprovalWriteError('選んだ人は、すでにこの取引先の主担当です。', 'main-owner-unchanged'))).toContain('すでにこの取引先の主担当')
  })

  it('理由の分からない失敗は、これまでどおり承認申請の画面から提出するよう案内する', () => {
    expect(submitFailureMessage(new StandardApprovalWriteError('提出できませんでした。', 'operation-rejected'))).toBe('申請を保存しましたが、提出できませんでした。「承認申請」画面から提出してください。')
    expect(submitFailureMessage(new Error('network'))).toBe('申請を保存しましたが、提出できませんでした。「承認申請」画面から提出してください。')
  })
})
