import { describe, expect, it } from 'vitest'
import { BUSINESS_CARD_CAPTURE_STATUS, BUSINESS_CARD_OCR_JOB_STATUS } from './businessCardOcr'
import { planBusinessCardOcrJobCreate, type BusinessCardOcrJobCreateContext } from './businessCardOcrJob'

const base: BusinessCardOcrJobCreateContext = {
  inputVersionId: '11111111-1111-4111-8111-111111111111',
  captureKey: 'R12-D-CAPTURE-01',
  currentVersionNumber: 1,
  inputVersionActive: true,
  captureActive: true,
  inputState: '固定済み',
  imageState: '画像到着確認済み',
  captureStatus: BUSINESS_CARD_CAPTURE_STATUS.ready,
}

describe('business card OCR standard Create boundary', () => {
  it('builds only the queued job fields and binds the current input version', () => {
    expect(planBusinessCardOcrJobCreate(base, new Date('2026-09-18T04:00:00Z'))).toEqual({
      kind: 'create',
      item: {
        'pl_CardInputVersionLookup@odata.bind': '/pl_cardinputversions(11111111-1111-4111-8111-111111111111)',
        pl_name: 'OCR-R12-D-CAPTURE-01|v1|a1',
        pl_jobkey: 'R12-D-CAPTURE-01|v1|a1',
        pl_attemptnumber: 1,
        pl_jobstatuscode: '待機',
      },
      uniqueKey: {schemaName: 'pl_OcrJobJobKeyKey', attribute: 'pl_jobkey'},
    })
  })

  it('does not create when the current state or version is not safe', () => {
    expect(planBusinessCardOcrJobCreate({...base, captureStatus: BUSINESS_CARD_CAPTURE_STATUS.review}, new Date())).toMatchObject({kind: 'skip', reason: 'state-changed'})
    expect(planBusinessCardOcrJobCreate({...base, inputState: '下書き'}, new Date())).toMatchObject({kind: 'skip', reason: 'state-changed'})
    expect(planBusinessCardOcrJobCreate({...base, existingJob: {inputVersionNumber: 2, attemptNumber: 1, status: BUSINESS_CARD_OCR_JOB_STATUS.failed}}, new Date(), true)).toMatchObject({kind: 'skip', reason: 'state-changed'})
  })

  it('stops duplicate, active, expired, and unknown jobs before Create', () => {
    expect(planBusinessCardOcrJobCreate({...base, existingJob: {inputVersionNumber: 1, attemptNumber: 1, status: BUSINESS_CARD_OCR_JOB_STATUS.queued}}, new Date())).toMatchObject({kind: 'skip', reason: 'job-already-queued'})
    expect(planBusinessCardOcrJobCreate({...base, existingJob: {inputVersionNumber: 1, attemptNumber: 1, status: BUSINESS_CARD_OCR_JOB_STATUS.processing, leaseUntil: '2099-01-01T00:00:00Z'}}, new Date('2026-09-18T04:00:00Z'))).toMatchObject({kind: 'skip', reason: 'processing-in-progress'})
    expect(planBusinessCardOcrJobCreate({...base, existingJob: {inputVersionNumber: 1, attemptNumber: 1, status: BUSINESS_CARD_OCR_JOB_STATUS.processing, leaseUntil: '2026-01-01T00:00:00Z'}}, new Date('2026-09-18T04:00:00Z'))).toMatchObject({kind: 'skip', reason: 'processing-lease-expired-reconciliation'})
    expect(planBusinessCardOcrJobCreate({...base, existingJob: {inputVersionNumber: 1, attemptNumber: 1, status: BUSINESS_CARD_OCR_JOB_STATUS.unknown}}, new Date())).toMatchObject({kind: 'skip', reason: 'result-unknown-reconciliation'})
  })

  it('requires an explicit retry for failure and increments the attempt', () => {
    const failed = {...base, existingJob: {inputVersionNumber: 1, attemptNumber: 2, status: BUSINESS_CARD_OCR_JOB_STATUS.failed} as const}
    expect(planBusinessCardOcrJobCreate(failed, new Date())).toMatchObject({kind: 'skip', reason: 'explicit-retry-required'})
    expect(planBusinessCardOcrJobCreate(failed, new Date(), true)).toMatchObject({kind: 'create', item: {pl_jobkey: 'R12-D-CAPTURE-01|v1|a3', pl_attemptnumber: 3}})
  })

  it('rejects an oversized business key before any Dataverse call', () => {
    expect(planBusinessCardOcrJobCreate({...base, captureKey: 'x'.repeat(195)}, new Date())).toMatchObject({kind: 'skip', reason: 'job-key-too-long'})
  })
})
