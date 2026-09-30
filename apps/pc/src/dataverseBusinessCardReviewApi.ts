import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { Pl_businesscardreviewsService } from './generated/services/Pl_businesscardreviewsService'
import { Pl_cardcapturesService } from './generated/services/Pl_cardcapturesService'
import { Pl_cardinputversionsService } from './generated/services/Pl_cardinputversionsService'
import { Pl_ocrjobsService } from './generated/services/Pl_ocrjobsService'
import { createBusinessCardReviewReadApi, type BusinessCardReviewInvoker, type BusinessCardReviewOperationResult, type BusinessCardReviewReadApi } from './businessCardReview'

export const businessCardReviewCaptureSelect = [
  'pl_cardcaptureid',
  'pl_capturekey',
  'pl_capturestatuscode',
  'pl_currentversionnumber',
  'statecode',
]

export const businessCardReviewInputVersionSelect = [
  'pl_cardinputversionid',
  '_pl_cardcapturelookup_value',
  'pl_versionnumber',
  'pl_inputstatecode',
  'pl_imagestatecode',
  'statecode',
]

export const businessCardReviewSelect = [
  'pl_businesscardreviewid',
  '_pl_cardinputversionlookup_value',
  'pl_name',
  'pl_reviewstatuscode',
  'pl_reviewedat',
  'pl_correctedfieldsjson',
  'statecode',
]

export const businessCardOcrJobSelect = [
  'pl_ocrjobid',
  '_pl_cardinputversionlookup_value',
  'pl_jobkey',
  'pl_attemptnumber',
  'pl_jobstatuscode',
  'pl_ocrresultjson',
  'statecode',
]

function asOperationResult<T>(result: IOperationResult<T>): BusinessCardReviewOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

const invoker: BusinessCardReviewInvoker = {
  getCapture: async captureId => asOperationResult(await Pl_cardcapturesService.get(captureId, {select: businessCardReviewCaptureSelect})),
  listInputVersions: async captureId => asOperationResult(await Pl_cardinputversionsService.getAll({
    select: businessCardReviewInputVersionSelect,
    filter: `statecode eq 0 and _pl_cardcapturelookup_value eq ${captureId}`,
    orderBy: ['pl_versionnumber desc'],
    maxPageSize: 50,
  })),
  listReviews: async inputVersionId => asOperationResult(await Pl_businesscardreviewsService.getAll({
    select: businessCardReviewSelect,
    filter: `statecode eq 0 and _pl_cardinputversionlookup_value eq ${inputVersionId}`,
    orderBy: ['pl_reviewedat desc'],
    maxPageSize: 20,
  })),
  listOcrJobs: async inputVersionId => asOperationResult(await Pl_ocrjobsService.getAll({
    select: businessCardOcrJobSelect,
    filter: `statecode eq 0 and _pl_cardinputversionlookup_value eq ${inputVersionId}`,
    orderBy: ['pl_attemptnumber desc'],
    maxPageSize: 50,
  })),
}

/**
 * OCR確認版の標準Readだけを提供する。Capture／入力版／Review／OCR結果の
 * 関連をサーバー応答から再確認し、画面へ渡すのは構造化された最小項目だけにする。
 * Create／Update、画像Download、OCR再実行、正式登録はこのアダプターから呼び出さない。
 */
export const dataverseBusinessCardReviewApi: BusinessCardReviewReadApi = createBusinessCardReviewReadApi(invoker)
