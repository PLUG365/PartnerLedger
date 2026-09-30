import { describe, expect, it } from 'vitest'
import { createBusinessCardReviewReadApi, BusinessCardReviewReadError } from './businessCardReview'

const captureId = '11111111-1111-4111-8111-111111111111'
const inputVersionId = '22222222-2222-4222-8222-222222222222'
const reviewId = '33333333-3333-4333-8333-333333333333'
const jobId = '44444444-4444-4444-8444-444444444444'

function capture(overrides: Record<string, unknown> = {}) {
  return {
    pl_cardcaptureid: captureId,
    pl_capturekey: 'capture-001',
    pl_capturestatuscode: '確認待ち',
    pl_currentversionnumber: 1,
    statecode: 0,
    ...overrides,
  }
}

function inputVersion(overrides: Record<string, unknown> = {}) {
  return {
    pl_cardinputversionid: inputVersionId,
    _pl_cardcapturelookup_value: captureId,
    pl_versionnumber: 1,
    pl_inputstatecode: '固定済み',
    pl_imagestatecode: '画像到着確認済み',
    statecode: 0,
    ...overrides,
  }
}

function review(overrides: Record<string, unknown> = {}) {
  return {
    pl_businesscardreviewid: reviewId,
    _pl_cardinputversionlookup_value: inputVersionId,
    pl_name: 'OCR-review-001',
    pl_reviewstatuscode: '確認編集中',
    pl_correctedfieldsjson: JSON.stringify({companyName: '青空ソリューションズ', fullName: '井上 真帆'}),
    statecode: 0,
    ...overrides,
  }
}

function ocrJob(overrides: Record<string, unknown> = {}) {
  return {
    pl_ocrjobid: jobId,
    _pl_cardinputversionlookup_value: inputVersionId,
    pl_jobkey: 'capture-001|v1|a1',
    pl_attemptnumber: 1,
    pl_jobstatuscode: '成功',
    pl_ocrresultjson: JSON.stringify({companyName: '青空ソリューションズ', fullName: '井上 真帆', email: 'inoue@example.com'}),
    statecode: 0,
    ...overrides,
  }
}

function createApi(overrides: Partial<Parameters<typeof createBusinessCardReviewReadApi>[0]> = {}) {
  return createBusinessCardReviewReadApi({
    getCapture: async () => ({success: true, data: capture()}),
    listInputVersions: async () => ({success: true, data: [inputVersion()]}),
    listReviews: async () => ({success: true, data: [review()]}),
    listOcrJobs: async () => ({success: true, data: [ocrJob()]}),
    ...overrides,
  })
}

describe('名刺確認版の標準Read境界', () => {
  it('Capture→現行入力版→Review→最新成功OCRを関連確認し、構造化項目だけ返す', async () => {
    await expect(createApi().getReviewForCapture(captureId)).resolves.toEqual({
      reviewId,
      reviewName: 'OCR-review-001',
      reviewStatus: '確認編集中',
      captureId,
      captureKey: 'capture-001',
      captureStatus: '確認待ち',
      inputVersionId,
      inputVersionNumber: 1,
      inputState: '固定済み',
      imageState: '画像到着確認済み',
      ocrJobId: jobId,
      ocrJobKey: 'capture-001|v1|a1',
      ocrAttemptNumber: 1,
      ocrJobStatus: '成功',
      ocrFields: {companyName: '青空ソリューションズ', fullName: '井上 真帆', email: 'inoue@example.com'},
      correctedFields: {companyName: '青空ソリューションズ', fullName: '井上 真帆'},
    })
  })

  it('Reviewが無い場合は空結果として返し、OCR結果を勝手に表示しない', async () => {
    let ocrRead = false
    const api = createApi({
      listReviews: async () => ({success: true, data: []}),
      listOcrJobs: async () => { ocrRead = true; return {success: true, data: [ocrJob()]} },
    })
    await expect(api.getReviewForCapture(captureId)).resolves.toBeUndefined()
    expect(ocrRead).toBe(false)
  })

  it('OCR JSONの任意項目が空文字でも、実値のある項目だけを返す', async () => {
    const api = createApi({
      listOcrJobs: async () => ({success: true, data: [ocrJob({
        pl_ocrresultjson: JSON.stringify({companyName: '青空ソリューションズ', fullName: '井上 真帆', addressCountry: '', jobTitle: ''}),
      })]}),
    })
    await expect(api.getReviewForCapture(captureId)).resolves.toMatchObject({
      ocrFields: {companyName: '青空ソリューションズ', fullName: '井上 真帆'},
    })
    const result = await api.getReviewForCapture(captureId)
    expect(result?.ocrFields).not.toHaveProperty('addressCountry')
    expect(result?.ocrFields).not.toHaveProperty('jobTitle')
  })

  it('Captureが確認待ちでない場合は関連Readを広げず空結果にする', async () => {
    let inputRead = false
    const api = createApi({
      getCapture: async () => ({success: true, data: capture({pl_capturestatuscode: '未登録'})}),
      listInputVersions: async () => { inputRead = true; return {success: true, data: [inputVersion()]} },
    })
    await expect(api.getReviewForCapture(captureId)).resolves.toBeUndefined()
    expect(inputRead).toBe(false)
  })

  it('非Active、関連不一致、重複Review、最新Attempt重複をfail-closedにする', async () => {
    await expect(createApi({getCapture: async () => ({success: true, data: capture({statecode: 1})})}).getReviewForCapture(captureId)).rejects.toMatchObject({code: 'invalid-response'})
    await expect(createApi({listInputVersions: async () => ({success: true, data: [inputVersion({_pl_cardcapturelookup_value: '55555555-5555-4555-8555-555555555555'})]})}).getReviewForCapture(captureId)).rejects.toThrow('関連')
    await expect(createApi({listReviews: async () => ({success: true, data: [review(), review({pl_businesscardreviewid: '66666666-6666-4666-8666-666666666666'})]})}).getReviewForCapture(captureId)).rejects.toThrow('重複')
    await expect(createApi({listOcrJobs: async () => ({success: true, data: [ocrJob(), ocrJob({pl_ocrjobid: '77777777-7777-4777-8777-777777777777'})]})}).getReviewForCapture(captureId)).rejects.toThrow('一意')
  })

  it('状態不整合、JSON不正、通信失敗、GUID不正を成功扱いしない', async () => {
    await expect(createApi({listInputVersions: async () => ({success: true, data: [inputVersion({pl_inputstatecode: '下書き'})]})}).getReviewForCapture(captureId)).rejects.toMatchObject({code: 'invalid-response'})
    await expect(createApi({listOcrJobs: async () => ({success: true, data: [ocrJob({pl_ocrresultjson: '{bad'})]})}).getReviewForCapture(captureId)).rejects.toMatchObject({code: 'invalid-response'})
    await expect(createApi({listOcrJobs: async () => ({success: true, data: [ocrJob({pl_jobstatuscode: '結果不明', pl_ocrresultjson: undefined})]})}).getReviewForCapture(captureId)).rejects.toThrow('成功結果')
    await expect(createApi({listReviews: async () => ({success: false, data: []})}).getReviewForCapture(captureId)).rejects.toMatchObject({code: 'operation-failed'})
    await expect(createApi().getReviewForCapture('not-a-guid')).rejects.toBeInstanceOf(BusinessCardReviewReadError)
  })
})
