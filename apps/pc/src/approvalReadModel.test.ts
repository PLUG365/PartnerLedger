import { describe, expect, it } from 'vitest'
import { createApprovalInboxModel, createApprovalReadApi, ApprovalReadError } from './approvalReadModel'

const requestId = '11111111-1111-4111-8111-111111111111'
const secondRequestId = '22222222-2222-4222-8222-222222222222'
const formattedPartnerLookupName = '_pl_partnerlookup_value@OData.Community.Display.V1.FormattedValue'
const formattedRequestingUserLookupName = '_pl_requestinguserlookup_value@OData.Community.Display.V1.FormattedValue'
const formattedApproverTeamLookupName = '_pl_approverteamlookup_value@OData.Community.Display.V1.FormattedValue'

function request(id = requestId) {
  return {
    pl_requestid: id,
    pl_name: '会社名変更申請',
    pl_requesttypecode: 'company-name',
    pl_requeststatuscode: '提出中',
    pl_requestedat: '2026-09-13T01:00:00Z',
    _pl_partnerlookup_value: '33333333-3333-4333-8333-333333333333',
    pl_partnerlookupname: '青空ソリューションズ株式会社',
    pl_requestinguserlookupname: '田中 健一',
    pl_approverteamlookupname: '承認チーム',
  }
}

function submission(id: string, versionNumber: number, submittedAt: string, requestLookup = requestId) {
  return {
    pl_submissionversionid: id,
    _pl_requestlookup_value: requestLookup,
    pl_name: `提出版 ${versionNumber}`,
    pl_submissionstatuscode: '提出済み',
    pl_versionnumber: versionNumber,
    pl_submittedat: submittedAt,
  }
}

describe('承認一覧の読み取り境界', () => {
  it('申請ごとに最新の提出版だけを選び、その版の変更内容を返す', () => {
    const model = createApprovalInboxModel([
      request(),
      {...request(secondRequestId), pl_requestedat: '2026-09-12T01:00:00Z'},
    ], [
      {...submission('44444444-4444-4444-8444-444444444444', 1, '2026-09-13T01:01:00Z'), pl_changesetjson: '{"version":1}'},
      {...submission('55555555-5555-4555-8555-555555555555', 2, '2026-09-13T01:02:00Z'), pl_changesetjson: '{"version":2}'},
      submission('66666666-6666-4666-8666-666666666666', 1, '2026-09-12T01:01:00Z', secondRequestId),
      submission('77777777-7777-4777-8777-777777777777', 3, '2026-09-13T01:03:00Z', '88888888-8888-4888-8888-888888888888'),
    ])

    expect(model).toHaveLength(2)
    expect(model[0].request.id).toBe(requestId)
    expect(model[0].latestSubmission?.id).toBe('55555555-5555-4555-8555-555555555555')
    expect(model[0].latestSubmission?.changeSetJson).toBe('{"version":2}')
    expect(model[1].latestSubmission?.requestId).toBe(secondRequestId)
    expect(model[1].latestSubmission?.changeSetJson).toBeUndefined()
  })

  it('差戻し後の未提出の修正版は、最新の提出版とは別に返す', () => {
    const model = createApprovalInboxModel([{...request(), pl_requeststatuscode: '差戻し'}], [
      {...submission('44444444-4444-4444-8444-444444444444', 1, '2026-09-13T01:01:00Z'), pl_submissionstatuscode: '差戻し', pl_changesetjson: '{"version":1}'},
      {pl_submissionversionid: '55555555-5555-4555-8555-555555555555', _pl_requestlookup_value: requestId, pl_name: '修正版', pl_submissionstatuscode: '下書き', pl_changesetjson: '{"draft":true}'},
    ])

    expect(model[0].latestSubmission?.id).toBe('44444444-4444-4444-8444-444444444444')
    expect(model[0].draftSubmission?.id).toBe('55555555-5555-4555-8555-555555555555')
    expect(model[0].draftSubmission?.changeSetJson).toBe('{"draft":true}')
  })

  it('申請ID・状態が欠けた応答をfail-closedで拒否する', () => {
    expect(() => createApprovalInboxModel([{...request(), pl_requestid: 'not-a-guid'}], [])).toThrowError(ApprovalReadError)
    expect(() => createApprovalInboxModel([{...request(), pl_requeststatuscode: ''}], [])).toThrowError(ApprovalReadError)
  })

  it('Lookup表示名は生成仮想名またはOData FormattedValueから解決する', () => {
    const row = {...request(), pl_partnerlookupname: undefined, pl_requestinguserlookupname: undefined, pl_approverteamlookupname: undefined,
      [formattedPartnerLookupName]: '注釈取引先', [formattedRequestingUserLookupName]: '注釈申請者', [formattedApproverTeamLookupName]: '注釈承認Team'}
    const parsed = createApprovalInboxModel([row], []).at(0)?.request
    expect(parsed?.partnerName).toBe('注釈取引先')
    expect(parsed?.requestingUserName).toBe('注釈申請者')
    expect(parsed?.approverTeamName).toBe('注釈承認Team')
  })

  it('非Activeの申請・提出版をライブ一覧へ混ぜない', () => {
    expect(() => createApprovalInboxModel([{...request(), statecode: 1}], [])).toThrowError(/非Active/)
    expect(() => createApprovalInboxModel([request()], [{...submission('44444444-4444-4444-8444-444444444444', 1, '2026-09-13T01:01:00Z'), statecode: 1}])).toThrowError(/非Active/)
  })

  it('取得失敗と不正な配列応答を成功扱いしない', async () => {
    const failed = createApprovalReadApi({
      listRequests: async () => ({success: false, error: new Error('network')}),
      listSubmissionVersions: async () => ({success: true, data: []}),
    })
    await expect(failed.listAccessibleApprovalRequests()).rejects.toMatchObject({name: 'ApprovalReadError', code: 'operation-failed'})

    const malformed = createApprovalReadApi({
      listRequests: async () => ({success: true, data: undefined}),
      listSubmissionVersions: async () => ({success: true, data: []}),
    })
    await expect(malformed.listAccessibleApprovalRequests()).rejects.toMatchObject({name: 'ApprovalReadError', code: 'invalid-response'})
  })

  it('提出版の読取失敗時は申請一覧を維持し、概要取得不可を明示する', async () => {
    const partial = createApprovalReadApi({
      listRequests: async () => ({success: true, data: [request()]}),
      listSubmissionVersions: async () => ({success: false, error: new Error('forbidden')}),
    })

    await expect(partial.listAccessibleApprovalRequests()).resolves.toMatchObject({
      submissionSummaryUnavailable: true,
      items: [{request: {id: requestId}, latestSubmission: undefined}],
    })
  })
})
