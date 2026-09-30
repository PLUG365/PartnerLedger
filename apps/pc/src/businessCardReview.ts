import { isDataverseGuid } from './dataverseIds'
import { BUSINESS_CARD_CAPTURE_STATUS, BUSINESS_CARD_OCR_JOB_STATUS, type BusinessCardCaptureStatus, type BusinessCardOcrJobStatus } from './businessCardOcr'
import type { BusinessCardOcrFields } from './businessCardOcrResult'

export type BusinessCardInputState = '下書き' | '送信要求' | '固定済み' | '旧版'
export type BusinessCardImageState = '端末保存済み' | '同期待ち' | '画像到着確認済み' | '画像失敗'
export type BusinessCardReviewStatus = '確認編集中' | '確認確定'

export type BusinessCardReviewItem = {
  reviewId: string
  reviewName?: string
  reviewStatus: BusinessCardReviewStatus
  reviewedAt?: string
  captureId: string
  captureKey: string
  captureStatus: '確認待ち'
  inputVersionId: string
  inputVersionNumber: number
  inputState: '固定済み'
  imageState: '画像到着確認済み'
  ocrJobId: string
  ocrJobKey: string
  ocrAttemptNumber: number
  ocrJobStatus: '成功'
  ocrFields: BusinessCardOcrFields
  correctedFields?: BusinessCardOcrFields
}

export type BusinessCardReviewOperationResult = {
  success: boolean
  data: unknown
  error?: unknown
}

export type BusinessCardReviewInvoker = {
  getCapture: (captureId: string) => Promise<BusinessCardReviewOperationResult>
  listInputVersions: (captureId: string) => Promise<BusinessCardReviewOperationResult>
  listReviews: (inputVersionId: string) => Promise<BusinessCardReviewOperationResult>
  listOcrJobs: (inputVersionId: string) => Promise<BusinessCardReviewOperationResult>
}

export type BusinessCardReviewReadApi = {
  getReviewForCapture: (captureId: string) => Promise<BusinessCardReviewItem | undefined>
}

export class BusinessCardReviewReadError extends Error {
  readonly code: 'invalid-input' | 'operation-failed' | 'invalid-response'

  constructor(message: string, code: 'invalid-input' | 'operation-failed' | 'invalid-response') {
    super(message)
    this.name = 'BusinessCardReviewReadError'
    this.code = code
  }
}

const inputStates: readonly BusinessCardInputState[] = ['下書き', '送信要求', '固定済み', '旧版']
const imageStates: readonly BusinessCardImageState[] = ['端末保存済み', '同期待ち', '画像到着確認済み', '画像失敗']
const reviewStatuses: readonly BusinessCardReviewStatus[] = ['確認編集中', '確認確定']
const captureStatuses: readonly BusinessCardCaptureStatus[] = Object.values(BUSINESS_CARD_CAPTURE_STATUS)
const ocrJobStatuses: readonly BusinessCardOcrJobStatus[] = Object.values(BUSINESS_CARD_OCR_JOB_STATUS)
const storedFieldKeys: readonly (keyof BusinessCardOcrFields)[] = [
  'addressCity',
  'addressCountry',
  'addressPostalCode',
  'addressPostOfficeBox',
  'addressState',
  'addressStreet',
  'businessPhone',
  'companyName',
  'department',
  'email',
  'fax',
  'firstName',
  'fullAddress',
  'fullName',
  'jobTitle',
  'lastName',
  'mobilePhone',
  'website',
]

type RecordLike = Record<string, unknown>

function isRecord(value: unknown): value is RecordLike {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireRecord(value: unknown, label: string): RecordLike {
  if (!isRecord(value)) throw new BusinessCardReviewReadError(`${label}の応答形式が不正です。`, 'invalid-response')
  return value
}

function requireString(record: RecordLike, field: string): string {
  const value = record[field]
  if (typeof value !== 'string' || !value.trim()) throw new BusinessCardReviewReadError(`${field}が不正です。`, 'invalid-response')
  return value.trim()
}

function optionalString(record: RecordLike, field: string): string | undefined {
  const value = record[field]
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new BusinessCardReviewReadError(`${field}が不正です。`, 'invalid-response')
  return value.trim() || undefined
}

function requireGuid(record: RecordLike, field: string): string {
  const value = requireString(record, field)
  if (!isDataverseGuid(value)) throw new BusinessCardReviewReadError(`${field}を正しく読み込めませんでした。`, 'invalid-response')
  return value
}

function requirePositiveInteger(record: RecordLike, field: string): number {
  const value = record[field]
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1) {
    throw new BusinessCardReviewReadError(`${field}が正の整数ではありません。`, 'invalid-response')
  }
  return value
}

function optionalDate(record: RecordLike, field: string): string | undefined {
  const value = optionalString(record, field)
  if (value === undefined) return undefined
  if (Number.isNaN(Date.parse(value))) throw new BusinessCardReviewReadError(`${field}の日時形式が不正です。`, 'invalid-response')
  return value
}

function requireActive(record: RecordLike, label: string): void {
  if (record.statecode !== undefined && record.statecode !== 0 && record.statecode !== '0') {
    throw new BusinessCardReviewReadError(`${label}が非Activeです。`, 'invalid-response')
  }
}

function requireValue<T extends string>(record: RecordLike, field: string, values: readonly T[]): T {
  const value = requireString(record, field)
  if (!values.includes(value as T)) throw new BusinessCardReviewReadError(`${field}が不正です。`, 'invalid-response')
  return value as T
}

function parseStoredFields(value: unknown, field: string, required: boolean): BusinessCardOcrFields | undefined {
  if (value === undefined || value === null || value === '') {
    if (required) throw new BusinessCardReviewReadError(`${field}がありません。`, 'invalid-response')
    return undefined
  }
  if (typeof value !== 'string' || !value.trim()) {
    throw new BusinessCardReviewReadError(`${field}の形式が不正です。`, 'invalid-response')
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(value)
  } catch {
    throw new BusinessCardReviewReadError(`${field}のJSONを解釈できません。`, 'invalid-response')
  }
  const record = requireRecord(parsed, field)
  const fields: BusinessCardOcrFields = {}
  let count = 0
  for (const key of Object.keys(record)) {
    if (!storedFieldKeys.includes(key as keyof BusinessCardOcrFields)) {
      throw new BusinessCardReviewReadError(`${field}に未許可の項目があります。`, 'invalid-response')
    }
    const raw = record[key]
    if (typeof raw !== 'string' || raw.trim().length > 2000) {
      throw new BusinessCardReviewReadError(`${field}の項目が不正です。`, 'invalid-response')
    }
    const normalized = raw.trim()
    if (!normalized) continue
    fields[key as keyof BusinessCardOcrFields] = normalized
    count += 1
  }
  if (!count) throw new BusinessCardReviewReadError(`${field}に保存可能な項目がありません。`, 'invalid-response')
  return fields
}

function parseCapture(value: unknown): {id: string; captureKey: string; status: BusinessCardCaptureStatus; currentVersionNumber: number} {
  const record = requireRecord(value, 'pl_CardCapture')
  requireActive(record, 'pl_CardCapture')
  return {
    id: requireGuid(record, 'pl_cardcaptureid'),
    captureKey: requireString(record, 'pl_capturekey'),
    status: requireValue(record, 'pl_capturestatuscode', captureStatuses),
    currentVersionNumber: requirePositiveInteger(record, 'pl_currentversionnumber'),
  }
}

function parseInputVersion(value: unknown): {id: string; captureId: string; versionNumber: number; inputState: BusinessCardInputState; imageState: BusinessCardImageState} {
  const record = requireRecord(value, 'pl_CardInputVersion')
  requireActive(record, 'pl_CardInputVersion')
  return {
    id: requireGuid(record, 'pl_cardinputversionid'),
    captureId: requireGuid(record, '_pl_cardcapturelookup_value'),
    versionNumber: requirePositiveInteger(record, 'pl_versionnumber'),
    inputState: requireValue(record, 'pl_inputstatecode', inputStates),
    imageState: requireValue(record, 'pl_imagestatecode', imageStates),
  }
}

function parseReview(value: unknown): {id: string; inputVersionId: string; name?: string; status: BusinessCardReviewStatus; reviewedAt?: string; correctedFields?: BusinessCardOcrFields} {
  const record = requireRecord(value, 'pl_BusinessCardReview')
  requireActive(record, 'pl_BusinessCardReview')
  return {
    id: requireGuid(record, 'pl_businesscardreviewid'),
    inputVersionId: requireGuid(record, '_pl_cardinputversionlookup_value'),
    name: optionalString(record, 'pl_name'),
    status: requireValue(record, 'pl_reviewstatuscode', reviewStatuses),
    reviewedAt: optionalDate(record, 'pl_reviewedat'),
    correctedFields: parseStoredFields(record.pl_correctedfieldsjson, 'pl_correctedfieldsjson', false),
  }
}

function parseOcrJob(value: unknown): {id: string; inputVersionId: string; jobKey: string; attemptNumber: number; status: BusinessCardOcrJobStatus; fields?: BusinessCardOcrFields} {
  const record = requireRecord(value, 'pl_OcrJob')
  requireActive(record, 'pl_OcrJob')
  const status = requireValue(record, 'pl_jobstatuscode', ocrJobStatuses)
  return {
    id: requireGuid(record, 'pl_ocrjobid'),
    inputVersionId: requireGuid(record, '_pl_cardinputversionlookup_value'),
    jobKey: requireString(record, 'pl_jobkey'),
    attemptNumber: requirePositiveInteger(record, 'pl_attemptnumber'),
    status,
    fields: status === BUSINESS_CARD_OCR_JOB_STATUS.succeeded
      ? parseStoredFields(record.pl_ocrresultjson, 'pl_ocrresultjson', true)
      : undefined,
  }
}

function requireSuccessfulData(result: BusinessCardReviewOperationResult, label: string): unknown {
  if (!result.success) throw new BusinessCardReviewReadError(`${label}を取得できませんでした。`, 'operation-failed')
  return result.data
}

async function invokeRead(invocation: () => Promise<BusinessCardReviewOperationResult>, label: string): Promise<unknown> {
  try {
    return requireSuccessfulData(await invocation(), label)
  } catch (error) {
    if (error instanceof BusinessCardReviewReadError) throw error
    throw new BusinessCardReviewReadError(`${label}を取得できませんでした。`, 'operation-failed')
  }
}

function ensureGuidMatches(actual: string, expected: string, label: string): void {
  if (actual.toLowerCase() !== expected.toLowerCase()) {
    throw new BusinessCardReviewReadError(`${label}の関連が不正です。`, 'invalid-response')
  }
}

function selectCurrentInputVersion(rows: unknown[], captureId: string, versionNumber: number): {id: string; captureId: string; versionNumber: number; inputState: BusinessCardInputState; imageState: BusinessCardImageState} {
  if (!Array.isArray(rows)) throw new BusinessCardReviewReadError('名刺入力版の配列応答がありません。', 'invalid-response')
  const versions = rows.map(parseInputVersion)
  const current = versions.filter(version => version.versionNumber === versionNumber)
  if (current.length !== 1) throw new BusinessCardReviewReadError('現行の名刺入力版を一意に取得できません。', 'invalid-response')
  ensureGuidMatches(current[0].captureId, captureId, '名刺入力版')
  return current[0]
}

function selectSingleReview(rows: unknown[], inputVersionId: string): {id: string; inputVersionId: string; name?: string; status: BusinessCardReviewStatus; reviewedAt?: string; correctedFields?: BusinessCardOcrFields} | undefined {
  if (!Array.isArray(rows)) throw new BusinessCardReviewReadError('名刺確認版の配列応答がありません。', 'invalid-response')
  const reviews = rows.map(parseReview)
  for (const review of reviews) ensureGuidMatches(review.inputVersionId, inputVersionId, '名刺確認版')
  if (reviews.length > 1) throw new BusinessCardReviewReadError('名刺確認版が重複しています。', 'invalid-response')
  return reviews[0]
}

function selectLatestOcrJob(rows: unknown[], inputVersionId: string): {id: string; inputVersionId: string; jobKey: string; attemptNumber: number; status: BusinessCardOcrJobStatus; fields?: BusinessCardOcrFields} {
  if (!Array.isArray(rows)) throw new BusinessCardReviewReadError('OCR処理結果の配列応答がありません。', 'invalid-response')
  const jobs = rows.map(parseOcrJob)
  for (const job of jobs) ensureGuidMatches(job.inputVersionId, inputVersionId, 'OCR処理結果')
  if (!jobs.length) throw new BusinessCardReviewReadError('OCR処理結果がありません。', 'invalid-response')
  const latestAttempt = Math.max(...jobs.map(job => job.attemptNumber))
  const latest = jobs.filter(job => job.attemptNumber === latestAttempt)
  if (latest.length !== 1) throw new BusinessCardReviewReadError('最新のOCR処理結果を一意に取得できません。', 'invalid-response')
  return latest[0]
}

export function createBusinessCardReviewReadApi(invoker: BusinessCardReviewInvoker): BusinessCardReviewReadApi {
  return {
    getReviewForCapture: async captureId => {
      if (!isDataverseGuid(captureId)) throw new BusinessCardReviewReadError('名刺取込Idが不正です。', 'invalid-input')
      const capture = parseCapture(await invokeRead(() => invoker.getCapture(captureId), '名刺取込'))
      ensureGuidMatches(capture.id, captureId, '名刺取込')
      if (capture.status !== BUSINESS_CARD_CAPTURE_STATUS.review) return undefined

      const inputVersion = selectCurrentInputVersion(
        await invokeRead(() => invoker.listInputVersions(capture.id), '名刺入力版') as unknown[],
        capture.id,
        capture.currentVersionNumber,
      )
      if (inputVersion.inputState !== '固定済み' || inputVersion.imageState !== '画像到着確認済み') {
        throw new BusinessCardReviewReadError('現行の名刺入力版がOCR確認可能な状態ではありません。', 'invalid-response')
      }

      const review = selectSingleReview(
        await invokeRead(() => invoker.listReviews(inputVersion.id), '名刺確認版') as unknown[],
        inputVersion.id,
      )
      if (!review) return undefined

      const ocrJob = selectLatestOcrJob(
        await invokeRead(() => invoker.listOcrJobs(inputVersion.id), 'OCR処理結果') as unknown[],
        inputVersion.id,
      )
      if (ocrJob.status !== BUSINESS_CARD_OCR_JOB_STATUS.succeeded || !ocrJob.fields) {
        throw new BusinessCardReviewReadError('確認版に対応するOCR成功結果を確認できません。', 'invalid-response')
      }

      return {
        reviewId: review.id,
        reviewName: review.name,
        reviewStatus: review.status,
        reviewedAt: review.reviewedAt,
        captureId: capture.id,
        captureKey: capture.captureKey,
        captureStatus: BUSINESS_CARD_CAPTURE_STATUS.review,
        inputVersionId: inputVersion.id,
        inputVersionNumber: inputVersion.versionNumber,
        inputState: inputVersion.inputState,
        imageState: inputVersion.imageState,
        ocrJobId: ocrJob.id,
        ocrJobKey: ocrJob.jobKey,
        ocrAttemptNumber: ocrJob.attemptNumber,
        ocrJobStatus: BUSINESS_CARD_OCR_JOB_STATUS.succeeded,
        ocrFields: ocrJob.fields,
        correctedFields: review.correctedFields,
      }
    },
  }
}
