import { describe, expect, it, vi } from 'vitest'
import { blocksResubmission, createOcrJobAfterCapture, submitBusinessCardImages, type BusinessCardOcrJobLauncher } from './businessCardIntake'

const capture = {
  success: true as const,
  batchId: '11111111-1111-4111-8111-111111111111',
  captureId: '22222222-2222-4222-8222-222222222222',
  inputVersionId: '33333333-3333-4333-8333-333333333333',
  batchKey: 'R12-D-INTAKE-BATCH-01',
  captureKey: 'R12-D-INTAKE-01',
}

const job = {success: true as const, jobId: '44444444-4444-4444-8444-444444444444', jobKey: 'R12-D-INTAKE-01|v1|a1', attemptNumber: 1}

describe('business card intake to OCR boundary', () => {
  it('launches OCR once only after a successful capture', async () => {
    const launcher: BusinessCardOcrJobLauncher = vi.fn(async received => {
      expect(received.captureId).toBe(capture.captureId)
      return job
    })

    await expect(createOcrJobAfterCapture(capture, launcher)).resolves.toEqual(job)
    expect(launcher).toHaveBeenCalledTimes(1)
  })

  it('does not launch OCR after a capture failure', async () => {
    const launcher = vi.fn<BusinessCardOcrJobLauncher>()
    await expect(createOcrJobAfterCapture({success: false, stage: 'file-upload', kind: 'operation-failed', message: '保存失敗'}, launcher)).resolves.toBeUndefined()
    expect(launcher).not.toHaveBeenCalled()
  })

  it('converts a launcher exception to result-unknown without retrying', async () => {
    const launcher: BusinessCardOcrJobLauncher = vi.fn(async () => { throw new Error('network lost') })
    await expect(createOcrJobAfterCapture(capture, launcher)).resolves.toMatchObject({success: false, kind: 'result-unknown'})
    expect(launcher).toHaveBeenCalledTimes(1)
  })
})

describe('名刺画像の受付（1枚と複数枚の振り分け）', () => {
  const image = (name: string) => new File(['x'], name, {type: 'image/png'})
  const bulkSuccess = {success: true as const, batchKey: 'B', batchId: capture.batchId, items: []}

  it('1枚なら1枚用の受付を、同じ取込キーと画像名入りの表示名で呼ぶ', async () => {
    const singleApi = {submit: vi.fn(async () => capture)}
    const bulkApi = {submit: vi.fn(async () => bulkSuccess)}
    const result = await submitBusinessCardImages({batchKey: 'B', files: [{captureKey: 'C1', file: image('a.png')}]}, singleApi, bulkApi)
    expect(result).toEqual({mode: 'single', result: capture})
    expect(singleApi.submit).toHaveBeenCalledWith({batchKey: 'B', captureKey: 'C1', file: expect.any(File), displayName: '名刺取込 a.png'})
    expect(bulkApi.submit).not.toHaveBeenCalled()
  })

  it('2枚以上なら一括用の受付をまとめて1回呼ぶ', async () => {
    const singleApi = {submit: vi.fn(async () => capture)}
    const bulkApi = {submit: vi.fn(async () => bulkSuccess)}
    const files = [{captureKey: 'C1', file: image('a.png')}, {captureKey: 'C2', file: image('b.png')}]
    const result = await submitBusinessCardImages({batchKey: 'B', files}, singleApi, bulkApi)
    expect(result.mode).toBe('bulk')
    expect(bulkApi.submit).toHaveBeenCalledWith({batchKey: 'B', files})
    expect(singleApi.submit).not.toHaveBeenCalled()
  })

  it('通信の例外は結果不明として返し、同じキーでの再送を止める', async () => {
    const failing = {submit: vi.fn(async () => { throw new Error('network') })}
    const single = await submitBusinessCardImages({batchKey: 'B', files: [{captureKey: 'C1', file: image('a.png')}]}, failing, failing)
    const bulk = await submitBusinessCardImages({batchKey: 'B', files: [{captureKey: 'C1', file: image('a.png')}, {captureKey: 'C2', file: image('b.png')}]}, failing, failing)
    expect(single.result).toMatchObject({success: false, kind: 'result-unknown'})
    expect(bulk.result).toMatchObject({success: false, kind: 'result-unknown', items: []})
    expect(blocksResubmission(single)).toBe(true)
    expect(blocksResubmission(bulk)).toBe(true)
  })

  it('受付済み・入力版の途中失敗は再送を止め、それ以外の失敗は再送できる', () => {
    expect(blocksResubmission(undefined)).toBe(false)
    expect(blocksResubmission({mode: 'single', result: capture})).toBe(true)
    expect(blocksResubmission({mode: 'single', result: {success: false, stage: 'input-version-create', kind: 'operation-failed', message: 'x'}})).toBe(true)
    expect(blocksResubmission({mode: 'single', result: {success: false, stage: 'file-upload', kind: 'operation-failed', message: 'x'}})).toBe(false)
    expect(blocksResubmission({mode: 'bulk', result: {success: false, batchKey: 'B', stage: 'batch-create', kind: 'operation-failed', message: 'x', items: []}})).toBe(false)
  })
})
