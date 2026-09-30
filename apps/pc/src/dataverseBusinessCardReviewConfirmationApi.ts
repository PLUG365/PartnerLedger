import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { Pl_businesscardreviewsService } from './generated/services/Pl_businesscardreviewsService'
import { createBusinessCardReviewConfirmationApi, type BusinessCardReviewConfirmationInvoker } from './businessCardReviewConfirmation'

const invoker: BusinessCardReviewConfirmationInvoker = {
  update: async (reviewId, fields) => {
    const result = await Pl_businesscardreviewsService.update(
      reviewId,
      fields as Parameters<typeof Pl_businesscardreviewsService.update>[1],
    ) as IOperationResult<unknown>
    return {success: result.success, error: result.error}
  },
}

export const dataverseBusinessCardReviewConfirmationApi = createBusinessCardReviewConfirmationApi(invoker)
