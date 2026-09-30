import {describe, expect, it, vi} from 'vitest'
import {
  buildStandardApprovalCancellationFields,
  buildStandardApprovalRequestCreateRecord,
  buildStandardApprovalRequestUpdateRecord,
  buildStandardApprovalSubmissionCreateRecord,
  buildStandardApprovalSubmissionUpdateRecord,
  buildStandardApprovalSubmitFields,
  createStandardApprovalWriteApi,
  StandardApprovalWriteError,
} from './standardApprovalTransitionApi'

const partnerId = '11111111-1111-4111-8111-111111111111'
const contractId = '22222222-2222-4222-8222-222222222222'
const requestId = '33333333-3333-4333-8333-333333333333'
const submissionVersionId = '44444444-4444-4444-8444-444444444444'

describe('standardApprovalTransitionApi', () => {
  it('申請下書きは標準Createへ業務入力と冪等キーだけを渡す', () => {
    expect(buildStandardApprovalRequestCreateRecord({
      name: '会社名変更申請',
      requestTypeCode: 'partner.company-name',
      partnerId,
      contractId,
      idempotencyKey: 'request-1',
    })).toEqual({
      pl_name: '会社名変更申請',
      pl_requesttypecode: 'partner.company-name',
      'pl_PartnerLookup@odata.bind': `/pl_partners(${partnerId})`,
      'pl_ContractLookup@odata.bind': `/pl_contracts(${contractId})`,
      pl_requestkey: 'request-1',
    })
  })

  it('申請Create payloadへ権威列を混ぜない', () => {
    const payload = buildStandardApprovalRequestCreateRecord({name: '申請', requestTypeCode: 'partner.phone', partnerId, idempotencyKey: 'request-2'})
    for (const forbidden of ['pl_requeststatuscode', 'pl_approvalpolicyversion', 'pl_requestedat', 'pl_RequestingUserLookup@odata.bind', 'pl_ApproverTeamLookup@odata.bind', 'ownerid']) {
      expect(payload).not.toHaveProperty(forbidden)
    }
  })

  it('申請下書きUpdateは許可された業務列だけを送る', () => {
    expect(buildStandardApprovalRequestUpdateRecord({requestId, name: '変更後の申請', requestTypeCode: 'partner.phone', partnerId})).toEqual({
      requestId,
      fields: {
        pl_name: '変更後の申請',
        pl_requesttypecode: 'partner.phone',
        'pl_PartnerLookup@odata.bind': `/pl_partners(${partnerId})`,
      },
    })
  })

  it('任意の契約Lookupを送らずに申請を作れる', () => {
    const payload = buildStandardApprovalRequestCreateRecord({name: '申請', requestTypeCode: 'partner.phone', partnerId, contractId: '  '})
    expect(payload).not.toHaveProperty('pl_ContractLookup@odata.bind')
  })

  it('任意入力へ実行時の非文字列が来ても安全に拒否する', () => {
    expect(() => buildStandardApprovalRequestCreateRecord({name: '申請', requestTypeCode: 'partner.phone', partnerId, contractId: 123 as unknown as string})).toThrowError(/契約/)
    expect(() => buildStandardApprovalRequestCreateRecord({name: '申請', requestTypeCode: 'partner.phone', partnerId, idempotencyKey: 123 as unknown as string})).toThrowError(/冪等キー/)
  })

  it('入力をtrimし、GUIDでない対象を拒否する', () => {
    expect(buildStandardApprovalRequestCreateRecord({name: '  申請  ', requestTypeCode: ' partner.phone ', partnerId, idempotencyKey: ' key '})).toMatchObject({
      pl_name: '申請',
      pl_requesttypecode: 'partner.phone',
      pl_requestkey: 'key',
    })
    expect(() => buildStandardApprovalRequestCreateRecord({name: '申請', requestTypeCode: 'partner.phone', partnerId: 'not-a-guid'})).toThrow(StandardApprovalWriteError)
  })

  it('冪等キーは省略時に標準Create用として生成する', () => {
    const payload = buildStandardApprovalRequestCreateRecord({name: '申請', requestTypeCode: 'partner.phone', partnerId})
    expect(payload.pl_requestkey).toMatch(/^CreateApprovalRequest-/)
  })

  it('提出版下書きは申請Lookup・名称・変更内容だけを作成する', () => {
    expect(buildStandardApprovalSubmissionCreateRecord({requestId, name: '提出版 v1', changeSetJson: '{"schemaVersion":1}'})).toEqual({
      pl_name: '提出版 v1',
      pl_changesetjson: '{"schemaVersion":1}',
      'pl_RequestLookup@odata.bind': `/pl_requests(${requestId})`,
    })
  })

  it('提出版Create payloadへ版・提出・状態列を混ぜない', () => {
    const payload = buildStandardApprovalSubmissionCreateRecord({requestId, name: '提出版 v1', changeSetJson: '{}'})
    for (const forbidden of ['pl_submissionstatuscode', 'pl_versionnumber', 'pl_policyversion', 'pl_rowversiontoken', 'pl_submittedat', 'pl_SubmittedByLookup@odata.bind', 'pl_fixedat', 'statecode']) {
      expect(payload).not.toHaveProperty(forbidden)
    }
  })

  it('提出版下書きUpdateは名称と変更内容だけを送る', () => {
    expect(buildStandardApprovalSubmissionUpdateRecord({submissionVersionId, requestId, name: '提出版 v2', changeSetJson: '{"version":2}'})).toEqual({
      submissionVersionId,
      fields: {pl_name: '提出版 v2', pl_changesetjson: '{"version":2}'},
    })
  })

  it('空の変更内容とサイズ超過を拒否する', () => {
    expect(() => buildStandardApprovalSubmissionCreateRecord({requestId, name: '提出版', changeSetJson: ' '})).toThrow(/変更内容/)
    expect(() => buildStandardApprovalSubmissionCreateRecord({requestId, name: '提出版', changeSetJson: 'x'.repeat(1048577)})).toThrow(/1,048,576/)
  })

  it('提出の公開Updateは申請状態だけを提出中へ進める', () => {
    expect(buildStandardApprovalSubmitFields({requestId})).toEqual({requestId, fields: {pl_requeststatuscode: '提出中'}})
  })

  it('提出Updateへ結果・承認者・固定値を追加できない', () => {
    const result = buildStandardApprovalSubmitFields({requestId})
    expect(Object.keys(result.fields)).toEqual(['pl_requeststatuscode'])
  })

  it('標準ServiceのCreate／Updateだけを呼び、IDを返す', async () => {
    const createRequest = vi.fn().mockResolvedValue({success: true, data: {pl_requestid: requestId}})
    const createSubmissionVersion = vi.fn().mockResolvedValue({success: true, data: {pl_submissionversionid: submissionVersionId}})
    const updateRequest = vi.fn().mockResolvedValue({success: true, data: {pl_requestid: requestId}})
    const updateSubmissionVersion = vi.fn().mockResolvedValue({success: true, data: {pl_submissionversionid: submissionVersionId}})
    const api = createStandardApprovalWriteApi({createRequest, createSubmissionVersion, updateRequest, updateSubmissionVersion})

    await expect(api.createRequest({name: '申請', requestTypeCode: 'partner.phone', partnerId, idempotencyKey: 'request-3'})).resolves.toEqual({requestId})
    await expect(api.createSubmissionDraft({requestId, name: '提出版', changeSetJson: '{}'})).resolves.toEqual({submissionVersionId})
    await expect(api.updateRequestDraft({requestId, name: '申請更新', requestTypeCode: 'partner.phone', partnerId})).resolves.toEqual({requestId})
    await expect(api.updateSubmissionDraft({submissionVersionId, requestId, name: '提出版更新', changeSetJson: '{"updated":true}'})).resolves.toEqual({submissionVersionId})
    await expect(api.submit({requestId})).resolves.toEqual({requestId})
    expect(createRequest).toHaveBeenCalledWith(expect.objectContaining({pl_requestkey: 'request-3'}))
    expect(createSubmissionVersion).toHaveBeenCalledWith(expect.objectContaining({'pl_RequestLookup@odata.bind': `/pl_requests(${requestId})`}))
    expect(updateRequest).toHaveBeenCalledWith(requestId, expect.objectContaining({pl_name: '申請更新'}))
    expect(updateSubmissionVersion).toHaveBeenCalledWith(submissionVersionId, {pl_name: '提出版更新', pl_changesetjson: '{"updated":true}'})
    expect(updateRequest).toHaveBeenCalledWith(requestId, {pl_requeststatuscode: '提出中'})
  })

  it('作成成功でもIDが欠ける場合は成功扱いにしない', async () => {
    const api = createStandardApprovalWriteApi({
      createRequest: vi.fn().mockResolvedValue({success: true, data: {}}),
      createSubmissionVersion: vi.fn(),
      updateRequest: vi.fn(),
      updateSubmissionVersion: vi.fn(),
    })
    await expect(api.createRequest({name: '申請', requestTypeCode: 'partner.phone', partnerId})).rejects.toMatchObject({code: 'invalid-response'})
  })

  it('標準Updateの拒否と通信失敗をUI成功に変換しない', async () => {
    const rejected = createStandardApprovalWriteApi({
      createRequest: vi.fn(),
      createSubmissionVersion: vi.fn(),
      updateRequest: vi.fn().mockResolvedValue({success: false, data: {}}),
      updateSubmissionVersion: vi.fn(),
    })
    await expect(rejected.submit({requestId})).rejects.toMatchObject({code: 'operation-rejected'})

    const failed = createStandardApprovalWriteApi({
      createRequest: vi.fn(),
      createSubmissionVersion: vi.fn(),
      updateRequest: vi.fn().mockRejectedValue(new Error('network')),
      updateSubmissionVersion: vi.fn(),
    })
    await expect(failed.submit({requestId})).rejects.toMatchObject({code: 'transport-error'})
  })

  it('主担当の変更の提出がサーバーで断られたら、理由の分かるエラーにする', async () => {
    // サーバーは「標準提出を拒否しました: <理由のコード>」で断る（StandardApprovalSubmitPlugin）。
    const rejectedWith = (message: string) => createStandardApprovalWriteApi({
      createRequest: vi.fn(),
      createSubmissionVersion: vi.fn(),
      updateRequest: vi.fn().mockResolvedValue({success: false, data: {}, error: new Error(message)}),
      updateSubmissionVersion: vi.fn(),
    })
    const unavailable = rejectedWith('標準提出を拒否しました: MainOwnerUnavailable')
    await expect(unavailable.submit({requestId})).rejects.toMatchObject({code: 'main-owner-unavailable', message: expect.stringContaining('通常の有効な利用者ではない')})
    const unchanged = rejectedWith('標準提出を拒否しました: MainOwnerUnchanged')
    await expect(unchanged.submit({requestId})).rejects.toMatchObject({code: 'main-owner-unchanged', message: expect.stringContaining('すでにこの取引先の主担当')})
    const other = rejectedWith('標準提出を拒否しました: ChangeSetInvalid')
    await expect(other.submit({requestId})).rejects.toMatchObject({code: 'operation-rejected'})
  })

  it('取消・取下げは状態と理由だけを送り、理由が空なら理由列を送らない', () => {
    expect(buildStandardApprovalCancellationFields({requestId, reason: ' 承認者が休職中のため '})).toEqual({
      requestId,
      fields: {pl_requeststatuscode: '取消', pl_cancellationreason: '承認者が休職中のため'},
    })
    expect(buildStandardApprovalCancellationFields({requestId, reason: '  '})).toEqual({requestId, fields: {pl_requeststatuscode: '取消'}})
    expect(buildStandardApprovalCancellationFields({requestId})).toEqual({requestId, fields: {pl_requeststatuscode: '取消'}})
    expect(() => buildStandardApprovalCancellationFields({requestId, reason: 'あ'.repeat(2001)})).toThrow(StandardApprovalWriteError)
    expect(() => buildStandardApprovalCancellationFields({requestId: 'not-a-guid'})).toThrow(StandardApprovalWriteError)
  })
})
