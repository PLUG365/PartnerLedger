import { isDataverseGuid } from './dataverseIds'
import {
  BUSINESS_CARD_CAPTURE_STATUS,
  BUSINESS_CARD_OCR_JOB_STATUS,
  type BusinessCardCaptureStatus,
  type BusinessCardOcrJobStatus,
} from './businessCardOcr'

const MAX_KEY_LENGTH = 200

export type BusinessCardOcrJobCreateContext = {
  inputVersionId: string
  captureKey: string
  currentVersionNumber: number
  inputVersionActive: boolean
  captureActive: boolean
  inputState: string
  imageState: string
  captureStatus: BusinessCardCaptureStatus
  existingJob?: {
    inputVersionNumber: number
    attemptNumber: number
    status: BusinessCardOcrJobStatus
    leaseUntil?: string | null
  }
}

export type BusinessCardOcrJobCreatePlan =
  | {
      kind: 'create'
      item: {
        'pl_CardInputVersionLookup@odata.bind': string
        pl_name: string
        pl_jobkey: string
        pl_attemptnumber: number
        pl_jobstatuscode: typeof BUSINESS_CARD_OCR_JOB_STATUS.queued
      }
      uniqueKey: {
        schemaName: 'pl_OcrJobJobKeyKey'
        attribute: 'pl_jobkey'
      }
    }
  | {
      kind: 'skip'
      reason:
        | 'invalid-context'
        | 'state-changed'
        | 'processing-in-progress'
        | 'processing-lease-expired-reconciliation'
        | 'result-unknown-reconciliation'
        | 'job-terminal'
        | 'explicit-retry-required'
        | 'job-already-queued'
        | 'job-key-too-long'
      message: string
    }

function validPositiveInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0
}

function validKey(value: unknown): value is string {
  return typeof value === 'string' && Boolean(value.trim()) && value.trim().length <= MAX_KEY_LENGTH
}

function hasActiveLease(value: string | null | undefined, now: Date): boolean {
  if (typeof value !== 'string') return false
  const leaseUntil = new Date(value)
  return Number.isFinite(leaseUntil.getTime()) && leaseUntil.getTime() > now.getTime()
}

function createJobKey(captureKey: string, version: number, attempt: number): string | null {
  const key = `${captureKey}|v${version}|a${attempt}`
  return key.length <= MAX_KEY_LENGTH ? key : null
}

function skip(
  reason: Extract<BusinessCardOcrJobCreatePlan, {kind: 'skip'}>['reason'],
  message: string,
): BusinessCardOcrJobCreatePlan {
  return {kind: 'skip', reason, message}
}

/**
 * Plans the one standard Create that starts an OCR job. The caller must have
 * read the current input version, Capture, and latest job immediately before
 * applying the plan. The Alternate Key is part of the contract: a normal
 * read-then-create sequence is not a concurrency guarantee by itself.
 */
export function planBusinessCardOcrJobCreate(
  context: BusinessCardOcrJobCreateContext,
  now: Date,
  retryFailed = false,
): BusinessCardOcrJobCreatePlan {
  if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
    return skip('invalid-context', 'OCRジョブ作成時刻を確認できません。')
  }
  if (!isDataverseGuid(context.inputVersionId)
    || !validKey(context.captureKey)
    || !validPositiveInteger(context.currentVersionNumber)
    || typeof context.inputVersionActive !== 'boolean'
    || typeof context.captureActive !== 'boolean'
    || typeof context.inputState !== 'string'
    || typeof context.imageState !== 'string'
    || !Object.values(BUSINESS_CARD_CAPTURE_STATUS).includes(context.captureStatus)
    || (context.existingJob !== undefined
      && (!validPositiveInteger(context.existingJob.inputVersionNumber)
        || !validPositiveInteger(context.existingJob.attemptNumber)
        || !Object.values(BUSINESS_CARD_OCR_JOB_STATUS).includes(context.existingJob.status)
        || (context.existingJob.leaseUntil !== undefined
          && context.existingJob.leaseUntil !== null
          && typeof context.existingJob.leaseUntil !== 'string')))) {
    return skip('invalid-context', 'OCRジョブの識別値または状態を確認できません。')
  }
  if (!context.inputVersionActive || !context.captureActive
    || context.inputState !== '固定済み'
    || context.imageState !== '画像到着確認済み'
    || context.captureStatus !== BUSINESS_CARD_CAPTURE_STATUS.ready) {
    return skip('state-changed', 'OCR開始前に入力版またはCaptureの状態が変わったため、作成しません。')
  }
  if (context.existingJob?.inputVersionNumber !== undefined
    && context.existingJob.inputVersionNumber !== context.currentVersionNumber) {
    return skip('state-changed', '現行入力版とOCRジョブの版が一致しません。')
  }

  const existing = context.existingJob
  if (existing?.status === BUSINESS_CARD_OCR_JOB_STATUS.processing) {
    return hasActiveLease(existing.leaseUntil, now)
      ? skip('processing-in-progress', '別のOCR処理が実行中です。')
      : skip('processing-lease-expired-reconciliation', '期限切れのOCR処理は照合するまで再作成しません。')
  }
  if (existing?.status === BUSINESS_CARD_OCR_JOB_STATUS.unknown) {
    return skip('result-unknown-reconciliation', '結果不明のOCRジョブを照合するまで再作成しません。')
  }
  if (existing?.status === BUSINESS_CARD_OCR_JOB_STATUS.queued) {
    return skip('job-already-queued', '待機中のOCRジョブを再作成しません。')
  }
  if (existing?.status === BUSINESS_CARD_OCR_JOB_STATUS.succeeded
    || existing?.status === BUSINESS_CARD_OCR_JOB_STATUS.cancelled) {
    return skip('job-terminal', '終端済みのOCRジョブを再利用しません。')
  }
  if (existing?.status === BUSINESS_CARD_OCR_JOB_STATUS.failed && !retryFailed) {
    return skip('explicit-retry-required', '失敗したOCRは明示的な再試行が必要です。')
  }

  const attemptNumber = existing?.status === BUSINESS_CARD_OCR_JOB_STATUS.failed
    ? existing.attemptNumber + 1
    : 1
  const jobKey = createJobKey(context.captureKey.trim(), context.currentVersionNumber, attemptNumber)
  if (!jobKey) return skip('job-key-too-long', 'OCRジョブキーが長すぎます。')

  return {
    kind: 'create',
    item: {
      'pl_CardInputVersionLookup@odata.bind': `/pl_cardinputversions(${context.inputVersionId})`,
      pl_name: `OCR-${jobKey}`,
      pl_jobkey: jobKey,
      pl_attemptnumber: attemptNumber,
      pl_jobstatuscode: BUSINESS_CARD_OCR_JOB_STATUS.queued,
    },
    uniqueKey: {
      schemaName: 'pl_OcrJobJobKeyKey',
      attribute: 'pl_jobkey',
    },
  }
}
