import { isDataverseGuid } from './dataverseIds'
import {validateBusinessCardCaptureRequest, type BusinessCardCaptureFailureKind, type BusinessCardCaptureInvoker} from './businessCardCapture'

export type BusinessCardBulkCaptureFile = {
  captureKey: string
  file: File
}

export type BusinessCardBulkCaptureRequest = {
  batchKey: string
  files: BusinessCardBulkCaptureFile[]
  displayName?: string
}

export type BusinessCardBulkCaptureItemResult = {
  fileName: string
  captureKey: string
  success: boolean
  stage?: 'capture-create' | 'file-upload' | 'input-version-create'
  kind?: Exclude<BusinessCardCaptureFailureKind, 'invalid-input'>
  batchId: string
  captureId?: string
  inputVersionId?: string
  message?: string
  error?: unknown
}

export type BusinessCardBulkCaptureResult =
  | {success: true; batchKey: string; batchId: string; items: BusinessCardBulkCaptureItemResult[]}
  | {success: false; batchKey: string; stage: 'validation' | 'batch-create'; kind: BusinessCardCaptureFailureKind; message: string; items: BusinessCardBulkCaptureItemResult[]; error?: unknown}

export const BUSINESS_CARD_BULK_MAX_FILES = 20

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireBatchKey(value: unknown): string {
  if (typeof value !== 'string' || !value.trim() || value.trim().length > 200) throw new Error('バッチキーが不正です。')
  return value.trim()
}

function operationKind(success: boolean): Exclude<BusinessCardCaptureFailureKind, 'invalid-input'> {
  return success ? 'result-unknown' : 'operation-failed'
}

function resultId(value: unknown, field: string): string | undefined {
  if (!isRecord(value)) return undefined
  const id = value[field]
  return typeof id === 'string' && isDataverseGuid(id) ? id : undefined
}

async function submitItem(
  batchId: string,
  item: BusinessCardBulkCaptureFile,
  invoker: BusinessCardCaptureInvoker,
): Promise<BusinessCardBulkCaptureItemResult> {
  const base = {fileName: item.file.name, captureKey: item.captureKey, batchId}
  let captureResult
  try {
    captureResult = await invoker.createCapture({
      'pl_CaptureBatchLookup@odata.bind': `/pl_capturebatchs(${batchId})`,
      pl_capturekey: item.captureKey,
      pl_name: item.file.name,
    })
  } catch (error) {
    return {...base, success: false, stage: 'capture-create', kind: 'result-unknown', message: '名刺取込の受付結果を確認できません。', error}
  }
  if (!captureResult.success) return {...base, success: false, stage: 'capture-create', kind: operationKind(false), message: '名刺取込を作成できませんでした。', error: captureResult.error}
  const captureId = resultId(captureResult.data, 'pl_cardcaptureid')
  if (!captureId) return {...base, success: false, stage: 'capture-create', kind: 'result-unknown', message: '名刺取込の結果を確認できません。', error: captureResult.error}

  let uploadResult
  try {
    uploadResult = await invoker.uploadImage(captureId, item.file, item.file.name)
  } catch (error) {
    return {...base, success: false, stage: 'file-upload', kind: 'result-unknown', captureId, message: '名刺画像の保存結果を確認できません。', error}
  }
  if (!uploadResult.success) return {...base, success: false, stage: 'file-upload', kind: operationKind(false), captureId, message: '名刺画像を保存できませんでした。', error: uploadResult.error}

  let inputVersionResult
  try {
    inputVersionResult = await invoker.createInputVersion({'pl_CardCaptureLookup@odata.bind': `/pl_cardcaptures(${captureId})`})
  } catch (error) {
    return {...base, success: false, stage: 'input-version-create', kind: 'result-unknown', captureId, message: '入力版の作成結果を確認できません。', error}
  }
  if (!inputVersionResult.success) return {...base, success: false, stage: 'input-version-create', kind: operationKind(false), captureId, message: '入力版を作成できませんでした。', error: inputVersionResult.error}
  const inputVersionId = resultId(inputVersionResult.data, 'pl_cardinputversionid')
  if (!inputVersionId) return {...base, success: false, stage: 'input-version-create', kind: 'result-unknown', captureId, message: '入力版の結果を確認できません。', error: inputVersionResult.error}
  return {...base, success: true, captureId, inputVersionId}
}

export function createBusinessCardBulkCaptureApi(invoker: BusinessCardCaptureInvoker) {
  return {
    submit: async (request: BusinessCardBulkCaptureRequest): Promise<BusinessCardBulkCaptureResult> => {
      let batchKey: string
      try {
        batchKey = requireBatchKey(request.batchKey)
        if (!Array.isArray(request.files) || request.files.length < 1 || request.files.length > BUSINESS_CARD_BULK_MAX_FILES) throw new Error(`画像は1〜${BUSINESS_CARD_BULK_MAX_FILES}件で指定してください。`)
        const captureKeys = new Set<string>()
        request.files.forEach(item => {
          if (captureKeys.has(item.captureKey)) throw new Error('画像の取込キーが重複しています。')
          captureKeys.add(item.captureKey)
          validateBusinessCardCaptureRequest({batchKey, captureKey: item.captureKey, file: item.file})
        })
      } catch (error) {
        return {success: false, batchKey: typeof request.batchKey === 'string' ? request.batchKey.trim() : '', stage: 'validation', kind: 'invalid-input', message: error instanceof Error ? error.message : '名刺画像を確認できません。', items: [], error}
      }

      let batchResult
      try {
        batchResult = await invoker.createBatch({pl_batchkey: batchKey, pl_name: request.displayName?.trim() || `名刺一括取込 ${batchKey}`})
      } catch (error) {
        return {success: false, batchKey, stage: 'batch-create', kind: 'result-unknown', message: '取込バッチの受付結果を確認できません。', items: [], error}
      }
      if (!batchResult.success) return {success: false, batchKey, stage: 'batch-create', kind: 'operation-failed', message: '取込バッチを作成できませんでした。', items: [], error: batchResult.error}
      const batchId = resultId(batchResult.data, 'pl_capturebatchid')
      if (!batchId) return {success: false, batchKey, stage: 'batch-create', kind: 'result-unknown', message: '取込バッチの結果を確認できません。', items: [], error: batchResult.error}

      const items: BusinessCardBulkCaptureItemResult[] = []
      for (const item of request.files) items.push(await submitItem(batchId, item, invoker))
      return {success: true, batchKey, batchId, items}
    },
  }
}

export type BusinessCardBulkCaptureApi = ReturnType<typeof createBusinessCardBulkCaptureApi>
