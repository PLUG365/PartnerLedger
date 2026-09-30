import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_partners } from './generated/models/Pl_partnersModel'
import { Pl_partnersService } from './generated/services/Pl_partnersService'
import { createPartnerDirectUpdateApi, type PartnerDirectUpdateApi, type PartnerDirectUpdateService } from './partnerDirectUpdate'

const generatedStandardPartnerUpdateService: PartnerDirectUpdateService = {
  update: async (partnerId, fields) => {
    const result = await Pl_partnersService.update(
      partnerId,
      fields as Parameters<typeof Pl_partnersService.update>[1],
    ) as IOperationResult<Pl_partners>
    return {success: result.success, data: result.data, error: result.error}
  },
}

export const dataverseStandardPartnerUpdateApi: PartnerDirectUpdateApi = createPartnerDirectUpdateApi(
  generatedStandardPartnerUpdateService,
)
