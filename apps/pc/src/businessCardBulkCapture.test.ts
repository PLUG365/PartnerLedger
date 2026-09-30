import {describe, expect, it, vi} from 'vitest'
import {BUSINESS_CARD_BULK_MAX_FILES, createBusinessCardBulkCaptureApi, type BusinessCardBulkCaptureFile} from './businessCardBulkCapture'
import type {BusinessCardCaptureInvoker} from './businessCardCapture'

const batchId = '11111111-1111-4111-8111-111111111111'
const captureId1 = '22222222-2222-4222-8222-222222222222'
const captureId2 = '33333333-3333-4333-8333-333333333333'
const inputVersionId1 = '44444444-4444-4444-8444-444444444444'
const inputVersionId2 = '55555555-5555-4555-8555-555555555555'

function image(name: string, type = 'image/png'): File {
  return new File([new Uint8Array(20)], name, {type})
}

function files(...names: string[]): BusinessCardBulkCaptureFile[] {
  return names.map((name, index) => ({captureKey: `capture-${index + 1}`, file: image(name)}))
}

function invoker(overrides: Partial<BusinessCardCaptureInvoker> = {}): BusinessCardCaptureInvoker {
  let captureCount = 0
  let inputVersionCount = 0
  return {
    createBatch: vi.fn(async () => ({success: true, data: {pl_capturebatchid: batchId}})),
    createCapture: vi.fn(async () => { captureCount += 1; return {success: true, data: {pl_cardcaptureid: captureCount === 1 ? captureId1 : captureId2}} }),
    uploadImage: vi.fn(async () => ({success: true, data: undefined})),
    createInputVersion: vi.fn(async () => { inputVersionCount += 1; return {success: true, data: {pl_cardinputversionid: inputVersionCount === 1 ? inputVersionId1 : inputVersionId2}} }),
    ...overrides,
  }
}

describe('business card bulk capture boundary', () => {
  it('creates one batch and processes images sequentially', async () => {
    const service = invoker()
    const api = createBusinessCardBulkCaptureApi(service)
    const result = await api.submit({batchKey: 'batch-1', files: files('one.png', 'two.png')})

    expect(result).toMatchObject({success: true, batchKey: 'batch-1', batchId})
    if (!result.success) return
    expect(result.items).toEqual([
      {fileName: 'one.png', captureKey: 'capture-1', success: true, batchId, captureId: captureId1, inputVersionId: inputVersionId1},
      {fileName: 'two.png', captureKey: 'capture-2', success: true, batchId, captureId: captureId2, inputVersionId: inputVersionId2},
    ])
    expect(service.createBatch).toHaveBeenCalledTimes(1)
    expect(service.createCapture).toHaveBeenNthCalledWith(1, expect.objectContaining({pl_capturekey: 'capture-1'}))
    expect(service.createCapture).toHaveBeenNthCalledWith(2, expect.objectContaining({pl_capturekey: 'capture-2'}))
    expect(service.uploadImage).toHaveBeenNthCalledWith(1, captureId1, expect.any(File), 'one.png')
    expect(service.uploadImage).toHaveBeenNthCalledWith(2, captureId2, expect.any(File), 'two.png')
  })

  it('continues with later images after one operation failure', async () => {
    let captureCount = 0
    const service = invoker({
      createCapture: vi.fn(async () => { captureCount += 1; return captureCount === 1 ? {success: false, error: new Error('denied')} : {success: true, data: {pl_cardcaptureid: captureId2}} }),
      createInputVersion: vi.fn(async () => ({success: true, data: {pl_cardinputversionid: inputVersionId2}})),
    })
    const result = await createBusinessCardBulkCaptureApi(service).submit({batchKey: 'batch-2', files: files('failed.png', 'later.png')})

    expect(result.success).toBe(true)
    if (!result.success) return
    expect(result.items[0]).toMatchObject({success: false, kind: 'operation-failed', stage: 'capture-create'})
    expect(result.items[1]).toMatchObject({success: true, captureId: captureId2, inputVersionId: inputVersionId2})
    expect(service.createCapture).toHaveBeenCalledTimes(2)
  })

  it('keeps an upload result unknown without creating an input version', async () => {
    const service = invoker({uploadImage: vi.fn(async () => { throw new Error('network lost') })})
    const result = await createBusinessCardBulkCaptureApi(service).submit({batchKey: 'batch-3', files: files('unknown.png', 'later.png')})

    expect(result.success).toBe(true)
    if (!result.success) return
    expect(result.items[0]).toMatchObject({success: false, kind: 'result-unknown', stage: 'file-upload', captureId: captureId1})
    expect(result.items[1]).toMatchObject({success: false, kind: 'result-unknown', stage: 'file-upload', captureId: captureId2})
    expect(service.uploadImage).toHaveBeenCalledTimes(2)
    expect(service.createInputVersion).not.toHaveBeenCalled()
  })

  it('validates every file before creating the batch', async () => {
    const service = invoker()
    const result = await createBusinessCardBulkCaptureApi(service).submit({batchKey: 'batch-4', files: [...files('good.png'), {captureKey: 'capture-2', file: image('bad.txt', 'text/plain')}]})

    expect(result).toMatchObject({success: false, stage: 'validation', kind: 'invalid-input'})
    expect(service.createBatch).not.toHaveBeenCalled()
    expect(service.createCapture).not.toHaveBeenCalled()
  })

  it('does not create children when the batch result is unknown', async () => {
    const service = invoker({createBatch: vi.fn(async () => { throw new Error('timeout') })})
    const result = await createBusinessCardBulkCaptureApi(service).submit({batchKey: 'batch-5', files: files('one.png')})

    expect(result).toMatchObject({success: false, stage: 'batch-create', kind: 'result-unknown'})
    expect(service.createCapture).not.toHaveBeenCalled()
  })

  it('rejects an empty or oversized batch before any operation', async () => {
    const service = invoker()
    const api = createBusinessCardBulkCaptureApi(service)
    await expect(api.submit({batchKey: 'empty', files: []})).resolves.toMatchObject({success: false, kind: 'invalid-input'})
    await expect(api.submit({batchKey: 'too-many', files: Array.from({length: BUSINESS_CARD_BULK_MAX_FILES + 1}, (_, index) => ({captureKey: `capture-${index}`, file: image(`${index}.png`)}))})).resolves.toMatchObject({success: false, kind: 'invalid-input'})
    expect(service.createBatch).not.toHaveBeenCalled()
  })

  it('rejects duplicate capture keys before any operation', async () => {
    const service = invoker()
    const result = await createBusinessCardBulkCaptureApi(service).submit({batchKey: 'duplicate', files: [{captureKey: 'same', file: image('one.png')}, {captureKey: 'same', file: image('two.png')}]})

    expect(result).toMatchObject({success: false, kind: 'invalid-input'})
    expect(service.createBatch).not.toHaveBeenCalled()
  })
})
