import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_partners } from './generated/models/Pl_partnersModel'
import { Pl_partnersService } from './generated/services/Pl_partnersService'
import { createPartnerDirectoryApi, type PartnerDirectoryOperationResult, type PartnerDirectoryApi } from './partnerDirectory'

/**
 * 画面に必要な最小列だけを取得する。行の可視性はDataverseの通常Read権限に委ね、
 * UIのフィルターで隠す方式にはしない。
 */
export const partnerDirectorySelect = [
  'pl_partnerid',
  'pl_name',
  'pl_industry',
  'pl_address',
  'pl_phone',
  'pl_tradingstatuscode',
  'pl_aclversion',
  'pl_registeredat',
  '_pl_mainownerlookup_value',
  '_pl_registeredbylookup_value',
  '_ownerid_value',
  '_createdby_value',
  'statecode',
]

export const partnerDirectoryActiveFilter = 'statecode eq 0 and pl_tradingstatuscode ne 100000003'

function asOperationResult(result: IOperationResult<Pl_partners[]>): PartnerDirectoryOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

export const dataversePartnerDirectoryApi: PartnerDirectoryApi = createPartnerDirectoryApi({
  listPartners: async () => asOperationResult(await Pl_partnersService.getAll({
    select: partnerDirectorySelect,
    filter: partnerDirectoryActiveFilter,
    orderBy: ['pl_name asc'],
    maxPageSize: 100,
  })),
})
