import './dataverseRuntimeDataSourcesInfo'
import { Pl_settingsesService } from './generated/services/Pl_settingsesService'
import { createApprovalSettingsWriteApi, type ApprovalSettingsWriteApi, type StandardApprovalSettingsWriteService } from './approvalSettingsWrite'

const generatedService: StandardApprovalSettingsWriteService = {
  getAll: options => Pl_settingsesService.getAll(options as Parameters<typeof Pl_settingsesService.getAll>[0]),
  create: record => Pl_settingsesService.create(record as Parameters<typeof Pl_settingsesService.create>[0]),
}

export const dataverseApprovalSettingsWriteApi: ApprovalSettingsWriteApi = createApprovalSettingsWriteApi(generatedService)
