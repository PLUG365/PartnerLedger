import { isDataverseGuid } from './dataverseIds'

export const BUSINESS_CARD_IMAGE_MAX_BYTES = 10 * 1024 * 1024

export type BusinessCardCaptureStage = 'validation' | 'batch-create' | 'capture-create' | 'file-upload' | 'input-version-create'
export type BusinessCardCaptureFailureKind = 'invalid-input' | 'operation-failed' | 'result-unknown'

export type BusinessCardCaptureOperationResult<T> = {
  success: boolean
  data?: T
  error?: unknown
}

export type BusinessCardCaptureRequest = {
  batchKey: string
  captureKey: string
  file: File
  displayName?: string
}

type BatchCreateResult = {pl_capturebatchid?: unknown}
type CaptureCreateResult = {pl_cardcaptureid?: unknown}
type InputVersionCreateResult = {pl_cardinputversionid?: unknown}

export type BusinessCardCaptureInvoker = {
  createBatch: (record: Record<string, unknown>) => Promise<BusinessCardCaptureOperationResult<BatchCreateResult>>
  createCapture: (record: Record<string, unknown>) => Promise<BusinessCardCaptureOperationResult<CaptureCreateResult>>
  uploadImage: (captureId: string, file: File, fileDisplayName: string) => Promise<BusinessCardCaptureOperationResult<void>>
  createInputVersion: (record: Record<string, unknown>) => Promise<BusinessCardCaptureOperationResult<InputVersionCreateResult>>
}

export type BusinessCardCaptureApi = {
  submit: (request: BusinessCardCaptureRequest) => Promise<BusinessCardCaptureResult>
}

export type BusinessCardCaptureResult =
  | {success: true; batchId: string; captureId: string; inputVersionId: string; batchKey: string; captureKey: string}
  | {
      success: false
      stage: BusinessCardCaptureStage
      kind: BusinessCardCaptureFailureKind
      batchId?: string
      captureId?: string
      message: string
      error?: unknown
    }

export class BusinessCardCaptureError extends Error {
  readonly code: BusinessCardCaptureFailureKind

  constructor(message: string, code: BusinessCardCaptureFailureKind) {
    super(message)
    this.name = 'BusinessCardCaptureError'
    this.code = code
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireKey(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim() || value.trim().length > 200) {
    throw new BusinessCardCaptureError(`${label}が不正です。`, 'invalid-input')
  }
  return value.trim()
}

function requireFile(file: unknown): asserts file is File {
  if (!isRecord(file)) throw new BusinessCardCaptureError('名刺画像を選択してください。', 'invalid-input')
  if (typeof file.type !== 'string' || !file.type.toLowerCase().startsWith('image/')) {
    throw new BusinessCardCaptureError('名刺画像は画像ファイルを選択してください。', 'invalid-input')
  }
  if (typeof file.size !== 'number' || !Number.isFinite(file.size) || file.size <= 0) {
    throw new BusinessCardCaptureError('名刺画像が空です。', 'invalid-input')
  }
  if (file.size > BUSINESS_CARD_IMAGE_MAX_BYTES) {
    throw new BusinessCardCaptureError('名刺画像は10MiB以下にしてください。', 'invalid-input')
  }
}

function requireResultId<T extends Record<string, unknown>>(
  result: BusinessCardCaptureOperationResult<T>,
  field: keyof T,
  label: string,
): string {
  const id: unknown = result.data?.[field]
  if (!result.success || typeof id !== 'string' || !isDataverseGuid(id)) throw new BusinessCardCaptureError(`${label}の結果を確認できません。`, 'result-unknown')
  return id
}

function classifyOperationFailure<T>(result: BusinessCardCaptureOperationResult<T>): BusinessCardCaptureFailureKind {
  return result.success ? 'result-unknown' : 'operation-failed'
}

function failure(
  stage: BusinessCardCaptureStage,
  kind: BusinessCardCaptureFailureKind,
  message: string,
  error?: unknown,
  ids: {batchId?: string; captureId?: string} = {},
): BusinessCardCaptureResult {
  return {success: false, stage, kind, message, error, ...ids}
}

export function validateBusinessCardCaptureRequest(request: BusinessCardCaptureRequest): void {
  requireKey(request.batchKey, 'バッチキー')
  requireKey(request.captureKey, '取込キー')
  requireFile(request.file)
}

export function createBusinessCardCaptureApi(invoker: BusinessCardCaptureInvoker): BusinessCardCaptureApi {
  return {
    submit: request => submitBusinessCardCapture(request, invoker),
  }
}

/**
 * 標準Create→File Uploadの順序だけを担保する。結果不明時に再送・削除を行わず、
 * 呼出元へ受付キーと作成済みIDを返して人が再読取・照合できるようにする。
 */
export async function submitBusinessCardCapture(
  request: BusinessCardCaptureRequest,
  invoker: BusinessCardCaptureInvoker,
): Promise<BusinessCardCaptureResult> {
  try {
    validateBusinessCardCaptureRequest(request)
  } catch (error) {
    const captureError = error instanceof BusinessCardCaptureError ? error : new BusinessCardCaptureError('名刺画像を確認できません。', 'invalid-input')
    return failure('validation', captureError.code, captureError.message, captureError)
  }

  const batchName = request.displayName?.trim() || `名刺取込 ${request.batchKey}`
  let batchResult: BusinessCardCaptureOperationResult<BatchCreateResult>
  try {
    batchResult = await invoker.createBatch({
      pl_batchkey: request.batchKey,
      pl_name: batchName,
    })
  } catch (error) {
    return failure('batch-create', 'result-unknown', '取込バッチの受付結果を確認できません。', error)
  }
  if (!batchResult.success) return failure('batch-create', classifyOperationFailure(batchResult), '取込バッチを作成できませんでした。', batchResult.error)

  let batchId: string
  try {
    batchId = requireResultId(batchResult, 'pl_capturebatchid', '取込バッチ')
  } catch (error) {
    const captureError = error instanceof BusinessCardCaptureError ? error : new BusinessCardCaptureError('取込バッチの結果を確認できません。', 'result-unknown')
    return failure('batch-create', captureError.code, captureError.message, captureError)
  }

  let captureResult: BusinessCardCaptureOperationResult<CaptureCreateResult>
  try {
    captureResult = await invoker.createCapture({
      'pl_CaptureBatchLookup@odata.bind': `/pl_capturebatchs(${batchId})`,
      pl_capturekey: request.captureKey,
      pl_name: request.file.name,
    })
  } catch (error) {
    return failure('capture-create', 'result-unknown', '名刺取込の受付結果を確認できません。', error, {batchId})
  }
  if (!captureResult.success) return failure('capture-create', classifyOperationFailure(captureResult), '名刺取込を作成できませんでした。', captureResult.error, {batchId})

  let captureId: string
  try {
    captureId = requireResultId(captureResult, 'pl_cardcaptureid', '名刺取込')
  } catch (error) {
    const captureError = error instanceof BusinessCardCaptureError ? error : new BusinessCardCaptureError('名刺取込の結果を確認できません。', 'result-unknown')
    return failure('capture-create', captureError.code, captureError.message, captureError, {batchId})
  }

  let uploadResult: BusinessCardCaptureOperationResult<void>
  try {
    uploadResult = await invoker.uploadImage(captureId, request.file, request.file.name)
  } catch (error) {
    return failure('file-upload', 'result-unknown', '名刺画像の保存結果を確認できません。', error, {batchId, captureId})
  }
  if (!uploadResult.success) return failure('file-upload', classifyOperationFailure(uploadResult), '名刺画像を保存できませんでした。', uploadResult.error, {batchId, captureId})

  let inputVersionResult: BusinessCardCaptureOperationResult<InputVersionCreateResult>
  try {
    inputVersionResult = await invoker.createInputVersion({
      'pl_CardCaptureLookup@odata.bind': `/pl_cardcaptures(${captureId})`,
    })
  } catch (error) {
    return failure('input-version-create', 'result-unknown', '入力版の作成結果を確認できません。', error, {batchId, captureId})
  }
  if (!inputVersionResult.success) {
    return failure('input-version-create', classifyOperationFailure(inputVersionResult), '入力版を作成できませんでした。', inputVersionResult.error, {batchId, captureId})
  }

  let inputVersionId: string
  try {
    inputVersionId = requireResultId(inputVersionResult, 'pl_cardinputversionid', '入力版')
  } catch (error) {
    const captureError = error instanceof BusinessCardCaptureError ? error : new BusinessCardCaptureError('入力版の結果を確認できません。', 'result-unknown')
    return failure('input-version-create', captureError.code, captureError.message, captureError, {batchId, captureId})
  }

  return {success: true, batchId, captureId, inputVersionId, batchKey: request.batchKey, captureKey: request.captureKey}
}
