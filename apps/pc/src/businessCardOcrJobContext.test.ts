import { describe, expect, it, vi } from 'vitest'
import { BUSINESS_CARD_CAPTURE_STATUS, BUSINESS_CARD_OCR_JOB_STATUS } from './businessCardOcr'
import { createBusinessCardOcrJobContextReadApi, type BusinessCardOcrJobContextInvoker } from './businessCardOcrJobContext'

const captureId = '11111111-1111-4111-8111-111111111111'
const inputVersionId = '22222222-2222-4222-8222-222222222222'

const capture = {
  pl_cardcaptureid: captureId,
  pl_capturekey: 'R12-D-CONTEXT-01',
  pl_capturestatuscode: BUSINESS_CARD_CAPTURE_STATUS.ready,
  pl_currentversionnumber: 1,
  statecode: 0,
}

const inputVersion = {
  pl_cardinputversionid: inputVersionId,
  _pl_cardcapturelookup_value: captureId,
  pl_versionnumber: 1,
  pl_inputstatecode: '固定済み',
  pl_imagestatecode: '画像到着確認済み',
  statecode: 0,
}

function invoker(overrides: Partial<BusinessCardOcrJobContextInvoker> = {}): BusinessCardOcrJobContextInvoker {
  return {
    getCapture: vi.fn(async () => ({success: true, data: capture})),
    listInputVersions: vi.fn(async () => ({success: true, data: [inputVersion]})),
    listOcrJobs: vi.fn(async () => ({success: true, data: []})),
    ...overrides,
  }
}

describe('business card OCR job read context', () => {
  it('builds a create context from the current version and latest attempt', async () => {
    const service = invoker({listOcrJobs: vi.fn(async () => ({success: true, data: [
      {pl_ocrjobid: '33333333-3333-4333-8333-333333333333', _pl_cardinputversionlookup_value: inputVersionId, pl_attemptnumber: 1, pl_jobstatuscode: BUSINESS_CARD_OCR_JOB_STATUS.failed},
      {pl_ocrjobid: '44444444-4444-4444-8444-444444444444', _pl_cardinputversionlookup_value: inputVersionId, pl_attemptnumber: 2, pl_jobstatuscode: BUSINESS_CARD_OCR_JOB_STATUS.processing, pl_leaseduntil: '2099-01-01T00:00:00Z'},
    ]}))})

    await expect(createBusinessCardOcrJobContextReadApi(service).getCreateContext(captureId)).resolves.toEqual({
      inputVersionId,
      captureKey: 'R12-D-CONTEXT-01',
      currentVersionNumber: 1,
      inputVersionActive: true,
      captureActive: true,
      inputState: '固定済み',
      imageState: '画像到着確認済み',
      captureStatus: '未登録',
      existingJob: {inputVersionNumber: 1, attemptNumber: 2, status: '処理中', leaseUntil: '2099-01-01T00:00:00Z'},
    })
  })

  it('returns no existing job when the current input version has none', async () => {
    await expect(createBusinessCardOcrJobContextReadApi(invoker()).getCreateContext(captureId)).resolves.toMatchObject({inputVersionId, existingJob: undefined})
  })

  it('fails closed for an invalid capture id, duplicate current version, or duplicate latest attempt', async () => {
    const api = createBusinessCardOcrJobContextReadApi(invoker())
    await expect(api.getCreateContext('not-a-guid')).rejects.toMatchObject({code: 'invalid-input'})

    const duplicateVersion = createBusinessCardOcrJobContextReadApi(invoker({listInputVersions: vi.fn(async () => ({success: true, data: [inputVersion, {...inputVersion, pl_cardinputversionid: '55555555-5555-4555-8555-555555555555'}]}))}))
    await expect(duplicateVersion.getCreateContext(captureId)).rejects.toMatchObject({code: 'invalid-response'})

    const duplicateAttempt = createBusinessCardOcrJobContextReadApi(invoker({listOcrJobs: vi.fn(async () => ({success: true, data: [
      {pl_ocrjobid: '33333333-3333-4333-8333-333333333333', _pl_cardinputversionlookup_value: inputVersionId, pl_attemptnumber: 1, pl_jobstatuscode: BUSINESS_CARD_OCR_JOB_STATUS.failed},
      {pl_ocrjobid: '44444444-4444-4444-8444-444444444444', _pl_cardinputversionlookup_value: inputVersionId, pl_attemptnumber: 1, pl_jobstatuscode: BUSINESS_CARD_OCR_JOB_STATUS.failed},
    ]}))}))
    await expect(duplicateAttempt.getCreateContext(captureId)).rejects.toMatchObject({code: 'invalid-response'})
  })

  it('does not convert a read failure into an empty context', async () => {
    const api = createBusinessCardOcrJobContextReadApi(invoker({listInputVersions: vi.fn(async () => ({success: false, data: undefined, error: new Error('read failed')}))}))
    await expect(api.getCreateContext(captureId)).rejects.toMatchObject({code: 'operation-failed'})
  })
})
