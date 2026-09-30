import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_contracttypes } from './generated/models/Pl_contracttypesModel'
import { Pl_contracttypesService } from './generated/services/Pl_contracttypesService'
import { withCreatedRow } from './createdRecordId'
import { createContractTypeManagementApi, type ContractTypeManagementApi } from './contractTypeManagement'

export const contractTypeManagementSelect = [
  'pl_contracttypeid',
  'pl_name',
  'pl_displayorder',
  'pl_lifecyclecode',
  'pl_selectable',
  'statecode',
  'statuscode',
]

const generatedContractTypeManagementApi: ContractTypeManagementApi = createContractTypeManagementApi({
  list: async () => {
    const result: IOperationResult<Pl_contracttypes[]> = await Pl_contracttypesService.getAll({
      select: contractTypeManagementSelect,
      orderBy: ['statecode asc', 'pl_displayorder asc', 'pl_name asc'],
      maxPageSize: 200,
    })
    return {success: result.success, data: result.data, error: result.error}
  },
  create: async record => {
    // 作成結果にIDが返らないとき（Power Appsモバイルアプリ）は、有効な同名の契約種類を読み直す。
    const result = await withCreatedRow(
      await Pl_contracttypesService.create(record as Parameters<typeof Pl_contracttypesService.create>[0]),
      'pl_contracttypeid',
      async () => typeof record.pl_name === 'string'
        ? Pl_contracttypesService.getAll({select: ['pl_contracttypeid'], filter: `statecode eq 0 and pl_name eq '${record.pl_name.replace(/'/g, "''")}'`, top: 2})
        : {success: true, data: []},
    )
    return {success: result.success, data: result.data, error: result.error}
  },
  update: async (id, fields) => {
    const result = await Pl_contracttypesService.update(id, fields as Parameters<typeof Pl_contracttypesService.update>[1])
    return {success: result.success, data: result.data, error: result.error}
  },
})

export const dataverseContractTypeManagementApi = generatedContractTypeManagementApi
