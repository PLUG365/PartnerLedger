import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { boundId, lookupByKey, withCreatedRow } from './createdRecordId'
import { Pl_capturebatchsService } from './generated/services/Pl_capturebatchsService'
import { Pl_cardcapturesService } from './generated/services/Pl_cardcapturesService'
import { Pl_cardinputversionsService } from './generated/services/Pl_cardinputversionsService'
import { createBusinessCardCaptureApi, type BusinessCardCaptureApi, type BusinessCardCaptureInvoker, type BusinessCardCaptureOperationResult } from './businessCardCapture'

function asOperationResult<T>(result: IOperationResult<T>): BusinessCardCaptureOperationResult<T> {
  return {success: result.success, data: result.data, error: result.error}
}

/**
 * 標準Create→File Uploadの接続だけを提供する。Appへ接続するのは、
 * R11-Cのサーバー側キー／認可境界を適用した後とする。
 */
export const dataverseBusinessCardCaptureInvoker: BusinessCardCaptureInvoker = {
  // 作成結果にIDが返らないとき（Power Appsモバイルアプリ）は、アプリが付けた取込キーで読み直す。
  createBatch: async record => asOperationResult(await withCreatedRow(
    await Pl_capturebatchsService.create(record as Parameters<typeof Pl_capturebatchsService.create>[0]),
    'pl_capturebatchid',
    lookupByKey(options => Pl_capturebatchsService.getAll(options), 'pl_capturebatchid', 'pl_batchkey', record.pl_batchkey),
  )),
  createCapture: async record => asOperationResult(await withCreatedRow(
    await Pl_cardcapturesService.create(record as Parameters<typeof Pl_cardcapturesService.create>[0]),
    'pl_cardcaptureid',
    lookupByKey(options => Pl_cardcapturesService.getAll(options), 'pl_cardcaptureid', 'pl_capturekey', record.pl_capturekey),
  )),
  uploadImage: async (captureId, file, fileDisplayName) => asOperationResult(await Pl_cardcapturesService.upload(
    captureId,
    'pl_imagefile',
    file,
    fileDisplayName,
  )),
  // 入力版にはキーが無い。版番号はサーバーが取込ごとに増やすため、取込の最新の入力版を読み直す。
  createInputVersion: async record => asOperationResult(await withCreatedRow(
    await Pl_cardinputversionsService.create(record as Parameters<typeof Pl_cardinputversionsService.create>[0]),
    'pl_cardinputversionid',
    async () => {
      const captureId = boundId(record['pl_CardCaptureLookup@odata.bind'])
      if (!captureId) return {success: true, data: []}
      return Pl_cardinputversionsService.getAll({select: ['pl_cardinputversionid'], filter: `_pl_cardcapturelookup_value eq ${captureId}`, orderBy: ['pl_versionnumber desc'], top: 1})
    },
  )),
}

export const dataverseBusinessCardCaptureApi: BusinessCardCaptureApi = createBusinessCardCaptureApi(dataverseBusinessCardCaptureInvoker)
