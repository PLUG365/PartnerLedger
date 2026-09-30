import type { BusinessCardBulkCaptureApi, BusinessCardBulkCaptureFile, BusinessCardBulkCaptureResult } from './businessCardBulkCapture'
import type { BusinessCardCaptureApi, BusinessCardCaptureResult } from './businessCardCapture'
import type { BusinessCardOcrJobCreateResult } from './businessCardOcrJobApi'

type SuccessfulCapture = Extract<BusinessCardCaptureResult, {success: true}>

export type BusinessCardOcrJobLauncher = (capture: SuccessfulCapture) => Promise<BusinessCardOcrJobCreateResult>

/**
 * 受付が成功した場合だけ、同じ成功結果を使ってOCR起動境界を1回呼び出す。
 * 受付失敗では起動せず、起動結果不明は再送可能な成功に変換しない。
 */
export async function createOcrJobAfterCapture(
  capture: BusinessCardCaptureResult,
  launcher?: BusinessCardOcrJobLauncher,
): Promise<BusinessCardOcrJobCreateResult | undefined> {
  if (!capture.success || !launcher) return undefined
  try {
    return await launcher(capture)
  } catch (error) {
    return {
      success: false,
      kind: 'result-unknown',
      message: 'OCRジョブの読取りまたは作成結果を確認できません。再送せず照合してください。',
      error,
    }
  }
}

export type BusinessCardImageSelection = {batchKey: string; files: BusinessCardBulkCaptureFile[]}

export type BusinessCardIntakeResult =
  | {mode: 'single'; result: BusinessCardCaptureResult}
  | {mode: 'bulk'; result: BusinessCardBulkCaptureResult}

const unknownMessage = '受付できたか確認できませんでした。'

/**
 * 選んだ画像が1枚なら1枚用、2枚以上なら一括用の受付を使う。どちらも同じ取込バッチ・取込キーで作り、
 * 通信の例外は「結果不明」に変えて返す（同じキーでの再送を画面側で止めるため）。
 */
export async function submitBusinessCardImages(selection: BusinessCardImageSelection, singleApi: BusinessCardCaptureApi, bulkApi: BusinessCardBulkCaptureApi): Promise<BusinessCardIntakeResult> {
  if (selection.files.length === 1) {
    const [{file, captureKey}] = selection.files
    try {
      return {mode: 'single', result: await singleApi.submit({batchKey: selection.batchKey, captureKey, file, displayName: `名刺取込 ${file.name}`})}
    } catch (error) {
      return {mode: 'single', result: {success: false, stage: 'batch-create', kind: 'result-unknown', message: unknownMessage, error}}
    }
  }
  try {
    return {mode: 'bulk', result: await bulkApi.submit({batchKey: selection.batchKey, files: selection.files})}
  } catch (error) {
    return {mode: 'bulk', result: {success: false, batchKey: selection.batchKey, stage: 'batch-create', kind: 'result-unknown', message: unknownMessage, items: [], error}}
  }
}

/** 同じキーで送り直すと重複や不整合のおそれがある結果か。画像を選び直すまで受付を止める。 */
export function blocksResubmission(intake: BusinessCardIntakeResult | undefined): boolean {
  if (!intake) return false
  if (intake.result.success) return true
  if (intake.result.kind === 'result-unknown') return true
  return intake.mode === 'single' && intake.result.stage === 'input-version-create'
}
