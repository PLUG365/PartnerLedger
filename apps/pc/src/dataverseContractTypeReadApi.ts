import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_contracttypes } from './generated/models/Pl_contracttypesModel'
import { Pl_contracttypesService } from './generated/services/Pl_contracttypesService'
import { createContractTypeReadApi, type ContractTypeReadApi } from './contractTypeReadModel'

export const contractTypeReadSelect = ['pl_contracttypeid', 'pl_name', 'pl_displayorder', 'pl_selectable', 'statecode']

export const dataverseContractTypeReadApi: ContractTypeReadApi = createContractTypeReadApi({
  listSelectableContractTypes: async () => {
    const result: IOperationResult<Pl_contracttypes[]> = await Pl_contracttypesService.getAll({
      select: contractTypeReadSelect,
      filter: 'statecode eq 0 and pl_selectable eq true',
      orderBy: ['pl_displayorder asc', 'pl_name asc'],
      maxPageSize: 200,
    })
    return {success: result.success, data: result.data, error: result.error}
  },
})
