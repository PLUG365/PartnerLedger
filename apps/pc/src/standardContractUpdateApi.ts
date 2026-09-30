import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_contracts } from './generated/models/Pl_contractsModel'
import { Pl_contractsService } from './generated/services/Pl_contractsService'
import { createContractDirectUpdateApi, type ContractDirectUpdateApi, type ContractDirectUpdateService } from './contractDirectUpdate'

const generatedStandardContractUpdateService: ContractDirectUpdateService = {
  update: async (contractId, fields) => {
    const result = await Pl_contractsService.update(
      contractId,
      fields as Parameters<typeof Pl_contractsService.update>[1],
    ) as IOperationResult<Pl_contracts>
    return {success: result.success, data: result.data, error: result.error}
  },
}

export const dataverseStandardContractUpdateApi: ContractDirectUpdateApi = createContractDirectUpdateApi(
  generatedStandardContractUpdateService,
)
