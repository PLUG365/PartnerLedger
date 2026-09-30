import './dataverseRuntimeDataSourcesInfo'
import { Pl_cardcapturesService } from './generated/services/Pl_cardcapturesService'
import { createBusinessCardDismissApi } from './businessCardDismissal'

/** 標準Updateで名刺取込を非アクティブにする。自分の行へのWrite権限がなければDataverseが拒否する。 */
export const dataverseBusinessCardDismissApi = createBusinessCardDismissApi({
  deactivate: async captureId => {
    const result = await Pl_cardcapturesService.update(captureId, {statecode: 1, statuscode: 2})
    return {success: result.success, error: result.error}
  },
})
