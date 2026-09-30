import './dataverseRuntimeDataSourcesInfo'
import { Pl_partnersService } from './generated/services/Pl_partnersService'
import { createCardPartnerSearchApi, type CardPartnerSearchApi, type StandardCardPartnerSearchService } from './businessCardPartnerSearch'

const generatedService: StandardCardPartnerSearchService = {
  getAll: options => Pl_partnersService.getAll(options as Parameters<typeof Pl_partnersService.getAll>[0]) as ReturnType<StandardCardPartnerSearchService['getAll']>,
}

export const dataverseCardPartnerSearchApi: CardPartnerSearchApi = createCardPartnerSearchApi(generatedService)
