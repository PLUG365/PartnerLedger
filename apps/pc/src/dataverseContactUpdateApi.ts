import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_contacts } from './generated/models/Pl_contactsModel'
import { Pl_contactsService } from './generated/services/Pl_contactsService'
import { createContactUpdateApi, type ContactUpdateApi, type ContactUpdateInvoker } from './contactUpdateApi'

export type StandardContactUpdateService = {
  update: (id: string, changedFields: Record<string, unknown>) => Promise<IOperationResult<Pl_contacts>>
}

export function createDataverseContactUpdateApi(service: StandardContactUpdateService): ContactUpdateApi {
  const invoker: ContactUpdateInvoker = async (contactId, fields) => {
    try {
      const result = await service.update(contactId, fields)
      return {success: result.success, error: result.error}
    } catch (error) {
      return {success: false, error}
    }
  }
  return createContactUpdateApi(invoker)
}

const generatedStandardContactUpdateService: StandardContactUpdateService = {
  update: (id, changedFields) => Pl_contactsService.update(
    id,
    changedFields as Parameters<typeof Pl_contactsService.update>[1],
  ),
}

export const dataverseContactUpdateApi = createDataverseContactUpdateApi(generatedStandardContactUpdateService)
