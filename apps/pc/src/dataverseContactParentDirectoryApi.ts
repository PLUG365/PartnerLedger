import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_partners } from './generated/models/Pl_partnersModel'
import { Pl_partnersService } from './generated/services/Pl_partnersService'
import { createContactParentDirectoryApi, type ContactRegistrationParent } from './contactParentDirectory'

export const contactParentDirectorySelect = ['pl_partnerid', 'pl_name', 'statecode']

export const dataverseContactParentDirectoryApi = {
  listParents: async (): Promise<ContactRegistrationParent[]> => createContactParentDirectoryApi({
    listParents: async () => {
      const result: IOperationResult<Pl_partners[]> = await Pl_partnersService.getAll({
        select: contactParentDirectorySelect,
        filter: 'statecode eq 0',
        orderBy: ['pl_name asc'],
        maxPageSize: 200,
      })
      return {success: result.success, data: result.data, error: result.error}
    },
  }),
}
