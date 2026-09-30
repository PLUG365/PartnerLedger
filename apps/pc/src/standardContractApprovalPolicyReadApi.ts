import './dataverseRuntimeDataSourcesInfo'
import { Pl_settingsesService } from './generated/services/Pl_settingsesService'
import { createContractApprovalPolicyReadApi, type ContractApprovalPolicyReadApi, type StandardContractApprovalPolicyService } from './contractApprovalPolicy'

const generatedService: StandardContractApprovalPolicyService = {
  getAll: options => Pl_settingsesService.getAll(options as Parameters<typeof Pl_settingsesService.getAll>[0]),
}

export const dataverseContractApprovalPolicyReadApi: ContractApprovalPolicyReadApi = createContractApprovalPolicyReadApi(generatedService)
