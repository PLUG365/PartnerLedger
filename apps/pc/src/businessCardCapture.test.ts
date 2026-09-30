import {describe, expect, it, vi} from 'vitest'
import {
  BUSINESS_CARD_IMAGE_MAX_BYTES,
  submitBusinessCardCapture,
  validateBusinessCardCaptureRequest,
  type BusinessCardCaptureInvoker,
} from './businessCardCapture'

const batchId = '11111111-1111-4111-8111-111111111111'
const captureId = '22222222-2222-4222-8222-222222222222'
const inputVersionId = '33333333-3333-4333-8333-333333333333'

function image(name = 'card.png', size = 1024, type = 'image/png'): File {
  return new File([new Uint8Array(size)], name, {type})
}

function invoker(overrides: Partial<BusinessCardCaptureInvoker> = {}): BusinessCardCaptureInvoker {
  return {
    createBatch: vi.fn(async () => ({success: true, data: {pl_capturebatchid: batchId}})),
    createCapture: vi.fn(async () => ({success: true, data: {pl_cardcaptureid: captureId}})),
    uploadImage: vi.fn(async () => ({success: true, data: undefined})),
    createInputVersion: vi.fn(async () => ({success: true, data: {pl_cardinputversionid: inputVersionId}})),
    ...overrides,
  }
}

describe('business card capture boundary', () => {
  it('rejects invalid files before any Dataverse operation', () => {
    expect(() => validateBusinessCardCaptureRequest({batchKey: 'batch-1', captureKey: 'capture-1', file: image('x.txt', 10, 'text/plain')})).toThrow('画像ファイル')
    expect(() => validateBusinessCardCaptureRequest({batchKey: 'batch-1', captureKey: 'capture-1', file: image('empty.png', 0)})).toThrow('空')
    expect(() => validateBusinessCardCaptureRequest({batchKey: 'batch-1', captureKey: 'capture-1', file: image('large.png', BUSINESS_CARD_IMAGE_MAX_BYTES + 1)})).toThrow('10MiB')
  })

  it('creates one batch, one capture, then uploads exactly once', async () => {
    const service = invoker()
    const result = await submitBusinessCardCapture({batchKey: 'batch-1', captureKey: 'capture-1', file: image()}, service)

    expect(result).toEqual({success: true, batchId, captureId, inputVersionId, batchKey: 'batch-1', captureKey: 'capture-1'})
    expect(service.createBatch).toHaveBeenCalledWith({pl_batchkey: 'batch-1', pl_name: '名刺取込 batch-1'})
    expect(service.createCapture).toHaveBeenCalledWith(expect.objectContaining({
      'pl_CaptureBatchLookup@odata.bind': `/pl_capturebatchs(${batchId})`,
      pl_capturekey: 'capture-1',
      pl_name: 'card.png',
    }))
    expect(service.uploadImage).toHaveBeenCalledWith(captureId, expect.any(File), 'card.png')
    expect(service.createInputVersion).toHaveBeenCalledWith({
      'pl_CardCaptureLookup@odata.bind': `/pl_cardcaptures(${captureId})`,
    })
    expect(service.createBatch).toHaveBeenCalledTimes(1)
    expect(service.createCapture).toHaveBeenCalledTimes(1)
    expect(service.uploadImage).toHaveBeenCalledTimes(1)
    expect(service.createInputVersion).toHaveBeenCalledTimes(1)
  })

  it('stops before capture when batch creation fails', async () => {
    const service = invoker({createBatch: vi.fn(async () => ({success: false, error: new Error('denied')}))})
    const result = await submitBusinessCardCapture({batchKey: 'batch-1', captureKey: 'capture-1', file: image()}, service)

    expect(result).toMatchObject({success: false, stage: 'batch-create', kind: 'operation-failed'})
    expect(service.createCapture).not.toHaveBeenCalled()
    expect(service.uploadImage).not.toHaveBeenCalled()
  })

  it('does not upload when capture creation has an unknown result', async () => {
    const service = invoker({createCapture: vi.fn(async () => ({success: true, data: undefined}))})
    const result = await submitBusinessCardCapture({batchKey: 'batch-1', captureKey: 'capture-1', file: image()}, service)

    expect(result).toMatchObject({success: false, stage: 'capture-create', kind: 'result-unknown', batchId})
    expect(service.uploadImage).not.toHaveBeenCalled()
  })

  it('does not retry or delete after an upload result is unknown', async () => {
    const uploadImage = vi.fn(async () => { throw new Error('network lost') })
    const service = invoker({uploadImage})
    const result = await submitBusinessCardCapture({batchKey: 'batch-1', captureKey: 'capture-1', file: image()}, service)

    expect(result).toMatchObject({success: false, stage: 'file-upload', kind: 'result-unknown', batchId, captureId})
    expect(uploadImage).toHaveBeenCalledTimes(1)
  })

  it('keeps the caller-provided keys unchanged on success', async () => {
    const service = invoker()
    const result = await submitBusinessCardCapture({batchKey: 'stable-batch', captureKey: 'stable-capture', file: image()}, service)

    expect(result).toMatchObject({success: true, batchKey: 'stable-batch', captureKey: 'stable-capture'})
    expect(service.createBatch).toHaveBeenCalledWith(expect.objectContaining({pl_batchkey: 'stable-batch'}))
    expect(service.createCapture).toHaveBeenCalledWith(expect.objectContaining({pl_capturekey: 'stable-capture'}))
  })

  it('does not create an input version when file upload fails', async () => {
    const service = invoker({uploadImage: vi.fn(async () => ({success: false, error: new Error('upload failed')}))})
    const result = await submitBusinessCardCapture({batchKey: 'batch-1', captureKey: 'capture-1', file: image()}, service)

    expect(result).toMatchObject({success: false, stage: 'file-upload', kind: 'operation-failed', batchId, captureId})
    expect(service.createInputVersion).not.toHaveBeenCalled()
  })

  it('does not retry input version creation after an unknown result', async () => {
    const createInputVersion = vi.fn(async () => { throw new Error('network lost') })
    const service = invoker({createInputVersion})
    const result = await submitBusinessCardCapture({batchKey: 'batch-1', captureKey: 'capture-1', file: image()}, service)

    expect(result).toMatchObject({success: false, stage: 'input-version-create', kind: 'result-unknown', batchId, captureId})
    expect(createInputVersion).toHaveBeenCalledTimes(1)
  })
})
