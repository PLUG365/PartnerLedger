import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { Pl_cardcapturesService } from './generated/services/Pl_cardcapturesService'
import { Pl_cardinputversionsService } from './generated/services/Pl_cardinputversionsService'
import { Pl_ocrjobsService } from './generated/services/Pl_ocrjobsService'
import { createBusinessCardOcrJobContextReadApi, type BusinessCardOcrJobContextInvoker, type BusinessCardOcrJobContextOperationResult } from './businessCardOcrJobContext'

export const businessCardOcrJobCaptureSelect = [
  'pl_cardcaptureid',
  'pl_capturekey',
  'pl_capturestatuscode',
  'pl_currentversionnumber',
  'statecode',
]

export const businessCardOcrJobInputVersionSelect = [
  'pl_cardinputversionid',
  '_pl_cardcapturelookup_value',
  'pl_versionnumber',
  'pl_inputstatecode',
  'pl_imagestatecode',
  'statecode',
]

export const businessCardOcrJobSelect = [
  'pl_ocrjobid',
  '_pl_cardinputversionlookup_value',
  'pl_attemptnumber',
  'pl_jobstatuscode',
  'pl_leaseduntil',
  'statecode',
]

function asOperationResult<T>(result: IOperationResult<T>): BusinessCardOcrJobContextOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

const generatedInvoker: BusinessCardOcrJobContextInvoker = {
  getCapture: async captureId => asOperationResult(await Pl_cardcapturesService.get(captureId, {select: businessCardOcrJobCaptureSelect})),
  listInputVersions: async captureId => asOperationResult(await Pl_cardinputversionsService.getAll({
    select: businessCardOcrJobInputVersionSelect,
    filter: `statecode eq 0 and _pl_cardcapturelookup_value eq ${captureId}`,
    orderBy: ['pl_versionnumber desc'],
    maxPageSize: 50,
  })),
  listOcrJobs: async inputVersionId => asOperationResult(await Pl_ocrjobsService.getAll({
    select: businessCardOcrJobSelect,
    filter: `statecode eq 0 and _pl_cardinputversionlookup_value eq ${inputVersionId}`,
    orderBy: ['pl_attemptnumber desc'],
    maxPageSize: 50,
  })),
}

/**
 * Create直前にCapture／現行入力版／最新OCRジョブを標準Readで再取得する。
 * CreateやUpdateはここから呼び出さず、UI／フロー結線前のRead境界として使う。
 */
export const dataverseBusinessCardOcrJobContextApi = createBusinessCardOcrJobContextReadApi(generatedInvoker)
