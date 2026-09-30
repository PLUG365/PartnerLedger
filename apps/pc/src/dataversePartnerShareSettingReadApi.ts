import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_partnersharesettings } from './generated/models/Pl_partnersharesettingsModel'
import { Pl_partnersharesettingsService } from './generated/services/Pl_partnersharesettingsService'
import { createPartnerShareSettingReadApi, type PartnerShareSettingReadApi, type PartnerShareSettingReadInvoker, type PartnerShareSettingReadOperationResult } from './partnerShareSettingReadModel'

export const partnerShareSettingSelect = [
  'pl_partnersharesettingid',
  '_pl_partnerlookup_value',
  'pl_principalkindcode',
  '_pl_userprincipallookup_value',
  '_pl_teamprincipallookup_value',
  'pl_accesslevelcode',
  'pl_settingstatuscode',
  'pl_requestkey',
  'pl_isprotectedmanagementpath',
  'pl_ismanagedprojection',
]

export type PartnerShareSettingReadService = Pick<typeof Pl_partnersharesettingsService, 'getAll'>

function asOperationResult(result: IOperationResult<Pl_partnersharesettings[]>): PartnerShareSettingReadOperationResult {
  return {success: result.success, data: result.data ?? [], error: result.error}
}

export function createDataversePartnerShareSettingReadApi(
  service: PartnerShareSettingReadService,
): PartnerShareSettingReadApi {
  const invoker: PartnerShareSettingReadInvoker = {
    list: async partnerId => asOperationResult(await service.getAll({
      select: partnerShareSettingSelect,
      filter: `_pl_partnerlookup_value eq ${partnerId} and statecode eq 0`,
      orderBy: ['createdon asc'],
      maxPageSize: 200,
    })),
  }
  return createPartnerShareSettingReadApi(invoker)
}

export const dataversePartnerShareSettingReadApi = createDataversePartnerShareSettingReadApi(
  Pl_partnersharesettingsService,
)
