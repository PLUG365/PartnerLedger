import { describe, expect, it, vi } from 'vitest'
import { BUSINESS_CARD_CAPTURE_STATUS } from './businessCardOcr'
import { createBusinessCardOcrJob, type BusinessCardOcrJobCreateInvoker, type BusinessCardOcrJobCreateRequest } from './businessCardOcrJobApi'

const baseRequest: BusinessCardOcrJobCreateRequest = {
  context: {
    inputVersionId: '11111111-1111-4111-8111-111111111111',
    captureKey: 'R12-D-API-01',
    currentVersionNumber: 1,
    inputVersionActive: true,
    captureActive: true,
    inputState: '固定済み',
    imageState: '画像到着確認済み',
    captureStatus: BUSINESS_CARD_CAPTURE_STATUS.ready,
  },
  now: new Date('2026-09-18T04:00:00Z'),
}

function invoker(overrides: Partial<BusinessCardOcrJobCreateInvoker> = {}): BusinessCardOcrJobCreateInvoker {
  return {
    create: vi.fn(async () => ({success: true, data: {pl_ocrjobid: '22222222-2222-4222-8222-222222222222'}})),
    ...overrides,
  }
}

describe('business card OCR job standard Create adapter', () => {
  it('sends the planned minimal payload once and returns the created id', async () => {
    const service = invoker()
    const result = await createBusinessCardOcrJob(baseRequest, service)

    expect(result).toEqual({success: true, jobId: '22222222-2222-4222-8222-222222222222', jobKey: 'R12-D-API-01|v1|a1', attemptNumber: 1})
    expect(service.create).toHaveBeenCalledTimes(1)
    expect(service.create).toHaveBeenCalledWith({
      'pl_CardInputVersionLookup@odata.bind': '/pl_cardinputversions(11111111-1111-4111-8111-111111111111)',
      pl_name: 'OCR-R12-D-API-01|v1|a1',
      pl_jobkey: 'R12-D-API-01|v1|a1',
      pl_attemptnumber: 1,
      pl_jobstatuscode: '待機',
    })
  })

  it('does not call Dataverse when the read context blocks Create', async () => {
    const service = invoker()
    const result = await createBusinessCardOcrJob({...baseRequest, context: {...baseRequest.context, captureStatus: '確認待ち'}}, service)

    expect(result).toMatchObject({success: false, kind: 'blocked', reason: 'state-changed'})
    expect(service.create).not.toHaveBeenCalled()
  })

  it('classifies HTTP 412 as an Alternate Key conflict without retrying', async () => {
    const service = invoker({create: vi.fn(async () => ({success: false, error: {status: 412, message: 'duplicate alternate key'}}))})
    const result = await createBusinessCardOcrJob(baseRequest, service)

    expect(result).toMatchObject({success: false, kind: 'alternate-key-conflict', jobKey: 'R12-D-API-01|v1|a1'})
    expect(service.create).toHaveBeenCalledTimes(1)
  })

  it('stops as result-unknown when the write throws or returns no id', async () => {
    const thrown = invoker({create: vi.fn(async () => { throw new Error('network lost') })})
    await expect(createBusinessCardOcrJob(baseRequest, thrown)).resolves.toMatchObject({success: false, kind: 'result-unknown'})

    const missingId = invoker({create: vi.fn(async () => ({success: true, data: {}}))})
    await expect(createBusinessCardOcrJob(baseRequest, missingId)).resolves.toMatchObject({success: false, kind: 'result-unknown'})
  })

  it('does not treat an ordinary Dataverse failure as an unknown success', async () => {
    const service = invoker({create: vi.fn(async () => ({success: false, error: {status: 403, message: 'forbidden'}}))})
    const result = await createBusinessCardOcrJob(baseRequest, service)

    expect(result).toMatchObject({success: false, kind: 'operation-failed'})
  })
})
