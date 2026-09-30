import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { lookupByKey, withCreatedRow } from './createdRecordId'
import { Pl_ocrjobsService } from './generated/services/Pl_ocrjobsService'
import { createBusinessCardOcrJobApi, type BusinessCardOcrJobCreateInvoker, type BusinessCardOcrJobCreateOperationResult } from './businessCardOcrJobApi'

function asOperationResult(result: IOperationResult<{pl_ocrjobid?: unknown}>): BusinessCardOcrJobCreateOperationResult<{pl_ocrjobid?: unknown}> {
  return {success: result.success, data: result.data, error: result.error}
}

/**
 * 生成されたDataverse Serviceによる標準Create接続だけを提供する。
 * UIから呼び出され、呼出元の再取得・状態契約を通った1回のCreateだけを受け付ける。
 */
const generatedInvoker: BusinessCardOcrJobCreateInvoker = {
  create: async record => asOperationResult(await withCreatedRow(
    await Pl_ocrjobsService.create(record as Parameters<typeof Pl_ocrjobsService.create>[0]),
    'pl_ocrjobid',
    lookupByKey(options => Pl_ocrjobsService.getAll(options), 'pl_ocrjobid', 'pl_jobkey', record.pl_jobkey),
  )),
}

export const dataverseBusinessCardOcrJobApi = createBusinessCardOcrJobApi(generatedInvoker)
