import { describe, expect, it, vi } from 'vitest'
import { BUSINESS_CARD_OCR_JOB_STATUS } from './businessCardOcr'
import { confirmBusinessCardReview, planBusinessCardReviewConfirmation, type BusinessCardReviewConfirmationInvoker } from './businessCardReviewConfirmation'
import type { BusinessCardReviewItem } from './businessCardReview'

const item: BusinessCardReviewItem = {
  reviewId: '11111111-1111-4111-8111-111111111111', reviewName: 'review', reviewStatus: '確認編集中', reviewedAt: undefined,
  captureId: '22222222-2222-4222-8222-222222222222', captureKey: 'R12-F-REVIEW-01', captureStatus: '確認待ち',
  inputVersionId: '33333333-3333-4333-8333-333333333333', inputVersionNumber: 1, inputState: '固定済み', imageState: '画像到着確認済み',
  ocrJobId: '44444444-4444-4444-8444-444444444444', ocrJobKey: 'R12-F-REVIEW-01|v1|a1', ocrAttemptNumber: 1,
  ocrJobStatus: BUSINESS_CARD_OCR_JOB_STATUS.succeeded, ocrFields: {companyName: 'デモ商事', fullName: '山田 太郎', email: 'taro@example.invalid'},
}

const fields = {companyName: 'デモ商事', fullName: '山田 太郎（確認済み）', email: 'taro@example.invalid'}

function invoker(overrides: Partial<BusinessCardReviewConfirmationInvoker> = {}): BusinessCardReviewConfirmationInvoker {
  return {update: vi.fn(async () => ({success: true})), ...overrides}
}

describe('名刺確認版の標準Update境界', () => {
  it('確認編集中の最新確認値だけを確認確定へUpdateする', async () => {
    const update = vi.fn(async (...args: [string, Record<string, unknown>]) => ({success: args.length === 2}))
    const service = invoker({update})
    await expect(confirmBusinessCardReview({item, correctedFields: fields}, service)).resolves.toEqual({success: true, reviewId: item.reviewId})
    expect(update).toHaveBeenCalledWith(item.reviewId, expect.objectContaining({pl_correctedfieldsjson: JSON.stringify(fields), pl_reviewstatuscode: '確認確定'}))
    expect(update.mock.calls[0][1].pl_reviewedat).toEqual(expect.any(String))
  })

  it('会社名または氏名がない場合はUpdateしない', () => {
    const plan = planBusinessCardReviewConfirmation({item, correctedFields: {companyName: 'デモ商事'}})
    expect(plan).toMatchObject({kind: 'skip', reason: 'invalid-fields'})
  })

  it('確認確定済み、状態不一致、未許可項目はfail-closedにする', () => {
    expect(planBusinessCardReviewConfirmation({item: {...item, reviewStatus: '確認確定'}, correctedFields: fields})).toMatchObject({kind: 'skip', reason: 'already-confirmed'})
    expect(planBusinessCardReviewConfirmation({item: {...item, captureStatus: '確認待ち', inputState: '下書き'} as unknown as BusinessCardReviewItem, correctedFields: fields})).toMatchObject({kind: 'skip', reason: 'invalid-review'})
    expect(planBusinessCardReviewConfirmation({item, correctedFields: {...fields, unknown: 'x'} as never})).toMatchObject({kind: 'skip', reason: 'invalid-fields'})
  })

  it('Update結果不明は再送せず結果不明にする', async () => {
    const service = invoker({update: vi.fn(async () => { throw new Error('network lost') })})
    await expect(confirmBusinessCardReview({item, correctedFields: fields}, service)).resolves.toMatchObject({success: false, kind: 'result-unknown'})
    expect(service.update).toHaveBeenCalledTimes(1)
  })

  it('通常失敗とHTTP 412競合を成功扱いしない', async () => {
    await expect(confirmBusinessCardReview({item, correctedFields: fields}, invoker({update: vi.fn(async () => ({success: false, error: {status: 403}}))}))).resolves.toMatchObject({success: false, kind: 'operation-failed'})
    await expect(confirmBusinessCardReview({item, correctedFields: fields}, invoker({update: vi.fn(async () => ({success: false, error: {status: 412}}))}))).resolves.toMatchObject({success: false, kind: 'conflict'})
  })
})
