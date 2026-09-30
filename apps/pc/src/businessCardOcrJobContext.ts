import { isDataverseGuid } from './dataverseIds'
import { BUSINESS_CARD_CAPTURE_STATUS, BUSINESS_CARD_OCR_JOB_STATUS, type BusinessCardCaptureStatus, type BusinessCardOcrJobStatus } from './businessCardOcr'
import { type BusinessCardImageState, type BusinessCardInputState } from './businessCardReview'
import type { BusinessCardOcrJobCreateContext } from './businessCardOcrJob'

export type BusinessCardOcrJobContextOperationResult = {
  success: boolean
  data: unknown
  error?: unknown
}

export type BusinessCardOcrJobContextInvoker = {
  getCapture: (captureId: string) => Promise<BusinessCardOcrJobContextOperationResult>
  listInputVersions: (captureId: string) => Promise<BusinessCardOcrJobContextOperationResult>
  listOcrJobs: (inputVersionId: string) => Promise<BusinessCardOcrJobContextOperationResult>
}

export type BusinessCardOcrJobContextReadApi = {
  getCreateContext: (captureId: string) => Promise<BusinessCardOcrJobCreateContext>
}

export class BusinessCardOcrJobContextReadError extends Error {
  readonly code: 'invalid-input' | 'operation-failed' | 'invalid-response'

  constructor(message: string, code: 'invalid-input' | 'operation-failed' | 'invalid-response') {
    super(message)
    this.name = 'BusinessCardOcrJobContextReadError'
    this.code = code
  }
}

type RecordLike = Record<string, unknown>

function isRecord(value: unknown): value is RecordLike {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireRecord(value: unknown, label: string): RecordLike {
  if (!isRecord(value)) throw new BusinessCardOcrJobContextReadError(`${label}の応答形式が不正です。`, 'invalid-response')
  return value
}

function requireString(record: RecordLike, field: string): string {
  const value = record[field]
  if (typeof value !== 'string' || !value.trim()) throw new BusinessCardOcrJobContextReadError(`${field}が不正です。`, 'invalid-response')
  return value.trim()
}

function requireGuid(record: RecordLike, field: string): string {
  const value = requireString(record, field)
  if (!isDataverseGuid(value)) throw new BusinessCardOcrJobContextReadError(`${field}を正しく読み込めませんでした。`, 'invalid-response')
  return value
}

function requirePositiveInteger(record: RecordLike, field: string): number {
  const value = record[field]
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1) {
    throw new BusinessCardOcrJobContextReadError(`${field}が正の整数ではありません。`, 'invalid-response')
  }
  return value
}

function requireValue<T extends string>(record: RecordLike, field: string, values: readonly T[]): T {
  const value = requireString(record, field)
  if (!values.includes(value as T)) throw new BusinessCardOcrJobContextReadError(`${field}が不正です。`, 'invalid-response')
  return value as T
}

function active(record: RecordLike, label: string): boolean {
  const value = record.statecode
  if (value !== 0 && value !== '0' && value !== 1 && value !== '1') {
    throw new BusinessCardOcrJobContextReadError(`${label}のstatecodeが不正です。`, 'invalid-response')
  }
  return value === 0 || value === '0'
}

function optionalLease(record: RecordLike): string | null | undefined {
  const value = record.pl_leaseduntil
  if (value === undefined || value === null || value === '') return value === null ? null : undefined
  if (typeof value !== 'string' || Number.isNaN(Date.parse(value))) {
    throw new BusinessCardOcrJobContextReadError('pl_leaseduntilが不正です。', 'invalid-response')
  }
  return value
}

function ensureRelated(actual: string, expected: string, label: string): void {
  if (actual.toLowerCase() !== expected.toLowerCase()) {
    throw new BusinessCardOcrJobContextReadError(`${label}の関連付けが一致しません。`, 'invalid-response')
  }
}

function parseCapture(value: unknown): {id: string; captureKey: string; active: boolean; currentVersionNumber: number; status: BusinessCardCaptureStatus} {
  const record = requireRecord(value, 'pl_CardCapture')
  return {
    id: requireGuid(record, 'pl_cardcaptureid'),
    captureKey: requireString(record, 'pl_capturekey'),
    active: active(record, 'pl_CardCapture'),
    currentVersionNumber: requirePositiveInteger(record, 'pl_currentversionnumber'),
    status: requireValue(record, 'pl_capturestatuscode', Object.values(BUSINESS_CARD_CAPTURE_STATUS)),
  }
}

function parseInputVersion(value: unknown): {id: string; captureId: string; active: boolean; versionNumber: number; inputState: BusinessCardInputState; imageState: BusinessCardImageState} {
  const record = requireRecord(value, 'pl_CardInputVersion')
  return {
    id: requireGuid(record, 'pl_cardinputversionid'),
    captureId: requireGuid(record, '_pl_cardcapturelookup_value'),
    active: active(record, 'pl_CardInputVersion'),
    versionNumber: requirePositiveInteger(record, 'pl_versionnumber'),
    inputState: requireValue(record, 'pl_inputstatecode', ['下書き', '送信要求', '固定済み', '旧版']),
    imageState: requireValue(record, 'pl_imagestatecode', ['端末保存済み', '同期待ち', '画像到着確認済み', '画像失敗']),
  }
}

function parseJob(value: unknown): {id: string; inputVersionId: string; attemptNumber: number; status: BusinessCardOcrJobStatus; leaseUntil?: string | null} {
  const record = requireRecord(value, 'pl_OcrJob')
  return {
    id: requireGuid(record, 'pl_ocrjobid'),
    inputVersionId: requireGuid(record, '_pl_cardinputversionlookup_value'),
    attemptNumber: requirePositiveInteger(record, 'pl_attemptnumber'),
    status: requireValue(record, 'pl_jobstatuscode', Object.values(BUSINESS_CARD_OCR_JOB_STATUS)),
    leaseUntil: optionalLease(record),
  }
}

function requireSuccessful(result: BusinessCardOcrJobContextOperationResult, label: string): unknown {
  if (!result.success) throw new BusinessCardOcrJobContextReadError(`${label}を取得できませんでした。`, 'operation-failed')
  return result.data
}

async function invokeRead(invocation: () => Promise<BusinessCardOcrJobContextOperationResult>, label: string): Promise<unknown> {
  try {
    return requireSuccessful(await invocation(), label)
  } catch (error) {
    if (error instanceof BusinessCardOcrJobContextReadError) throw error
    throw new BusinessCardOcrJobContextReadError(`${label}を取得できませんでした。`, 'operation-failed')
  }
}

function selectCurrentInputVersion(rows: unknown, captureId: string, versionNumber: number): ReturnType<typeof parseInputVersion> {
  if (!Array.isArray(rows)) throw new BusinessCardOcrJobContextReadError('名刺入力版の配列応答がありません。', 'invalid-response')
  const versions = rows.map(parseInputVersion)
  const current = versions.filter(version => version.versionNumber === versionNumber)
  if (current.length !== 1) throw new BusinessCardOcrJobContextReadError('現行の名刺入力版を一意に取得できません。', 'invalid-response')
  ensureRelated(current[0].captureId, captureId, '名刺入力版')
  return current[0]
}

function selectLatestJob(rows: unknown, inputVersionId: string): ReturnType<typeof parseJob> | undefined {
  if (!Array.isArray(rows)) throw new BusinessCardOcrJobContextReadError('OCRジョブの配列応答がありません。', 'invalid-response')
  const jobs = rows.map(parseJob)
  for (const job of jobs) ensureRelated(job.inputVersionId, inputVersionId, 'OCRジョブ')
  if (!jobs.length) return undefined
  const latestAttempt = Math.max(...jobs.map(job => job.attemptNumber))
  const latest = jobs.filter(job => job.attemptNumber === latestAttempt)
  if (latest.length !== 1) throw new BusinessCardOcrJobContextReadError('最新のOCRジョブを一意に取得できません。', 'invalid-response')
  return latest[0]
}

export function createBusinessCardOcrJobContextReadApi(invoker: BusinessCardOcrJobContextInvoker): BusinessCardOcrJobContextReadApi {
  return {
    getCreateContext: async captureId => {
      if (!isDataverseGuid(captureId)) throw new BusinessCardOcrJobContextReadError('名刺取込Idが不正です。', 'invalid-input')
      const capture = parseCapture(await invokeRead(() => invoker.getCapture(captureId), '名刺取込'))
      ensureRelated(capture.id, captureId, '名刺取込')
      const inputVersion = selectCurrentInputVersion(
        await invokeRead(() => invoker.listInputVersions(capture.id), '名刺入力版'),
        capture.id,
        capture.currentVersionNumber,
      )
      const latestJob = selectLatestJob(
        await invokeRead(() => invoker.listOcrJobs(inputVersion.id), 'OCRジョブ'),
        inputVersion.id,
      )
      return {
        inputVersionId: inputVersion.id,
        captureKey: capture.captureKey,
        currentVersionNumber: capture.currentVersionNumber,
        inputVersionActive: inputVersion.active,
        captureActive: capture.active,
        inputState: inputVersion.inputState,
        imageState: inputVersion.imageState,
        captureStatus: capture.status,
        existingJob: latestJob === undefined
          ? undefined
          : {
              inputVersionNumber: inputVersion.versionNumber,
              attemptNumber: latestJob.attemptNumber,
              status: latestJob.status,
              leaseUntil: latestJob.leaseUntil,
            },
      }
    },
  }
}
