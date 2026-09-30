import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { Pl_capturebatchsService } from './generated/services/Pl_capturebatchsService'
import { Pl_cardcapturesService } from './generated/services/Pl_cardcapturesService'
import { createBusinessCardQueueApi, type BusinessCardQueueApi, type BusinessCardQueueOperationResult } from './businessCardQueue'

export const businessCardQueueBatchSelect = [
  'pl_capturebatchid',
  'pl_batchkey',
  'pl_batchstatuscode',
  'statecode',
]

export const businessCardQueueCaptureSelect = [
  'pl_cardcaptureid',
  'pl_capturekey',
  'pl_capturestatuscode',
  '_pl_capturebatchlookup_value',
  'pl_currentversionnumber',
  'pl_receivedat',
  'pl_registeredat',
  '_createdby_value',
  'statecode',
]

function asOperationResult<T>(result: IOperationResult<T[]>): BusinessCardQueueOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

/**
 * 名刺処理待ちのオンラインReadだけを公開する。ファイル・OCR・状態更新は
 * このアダプターから呼び出さず、行の可視性はDataverseのRead権限へ委ねる。
 */
export const dataverseBusinessCardQueueApi: BusinessCardQueueApi = createBusinessCardQueueApi({
  listBatches: async () => asOperationResult(await Pl_capturebatchsService.getAll({
    select: businessCardQueueBatchSelect,
    filter: 'statecode eq 0',
    orderBy: ['pl_registeredat desc'],
    maxPageSize: 200,
  })),
  listCaptures: async () => asOperationResult(await Pl_cardcapturesService.getAll({
    select: businessCardQueueCaptureSelect,
    filter: 'statecode eq 0',
    orderBy: ['pl_registeredat desc', 'pl_receivedat desc'],
    maxPageSize: 200,
  })),
})
