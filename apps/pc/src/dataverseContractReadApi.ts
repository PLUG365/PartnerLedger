import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_contracts } from './generated/models/Pl_contractsModel'
import { Pl_contractsService } from './generated/services/Pl_contractsService'
import { createContractReadApi, type ContractReadApi, type ContractReadOperationResult } from './contractReadModel'

/**
 * 契約詳細で必要な列だけを取得する。契約の可視性はDataverseの通常Read権限へ委ね、
 * 画面側で会社別の権限を再判定したり、モックへフォールバックしたりしない。
 */
export const contractReadSelect = [
  'pl_contractid',
  'pl_name',
  '_pl_partnerlookup_value',
  '_pl_contracttypelookup_value',
  'pl_contractstatuscode',
  'pl_enddate',
  'pl_noticedate',
  'pl_decisiondate',
  'pl_autorenew',
  'pl_link',
  'statecode',
]

function asOperationResult(result: IOperationResult<Pl_contracts[]>): ContractReadOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

export const dataverseContractReadApi: ContractReadApi = createContractReadApi({
  listContracts: async partnerId => asOperationResult(await Pl_contractsService.getAll({
    select: contractReadSelect,
    filter: `statecode eq 0 and _pl_partnerlookup_value eq ${partnerId}`,
    orderBy: ['pl_enddate asc', 'pl_name asc'],
    maxPageSize: 100,
  })),
})
