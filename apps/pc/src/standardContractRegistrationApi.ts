import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_contracts } from './generated/models/Pl_contractsModel'
import { Pl_contractsService } from './generated/services/Pl_contractsService'
import { lookupByKey, withCreatedRow } from './createdRecordId'
import { createContractLedgerRegistrationApi, type ContractLedgerRegistrationApi, type ContractRegistrationReplayRecord } from './contractRegistrationApi'
import type { ContractRegistrationRequest } from './contractRegistrationApi'

export type StandardContractCreateService = {
  create: (record: Record<string, unknown>) => Promise<IOperationResult<Pick<Pl_contracts, 'pl_contractid'>>>
  findByRegistrationKey: (registrationKey: string) => Promise<IOperationResult<Pl_contracts[]>>
}

function asReplayRecords(result: IOperationResult<Pl_contracts[]>): IOperationResult<ContractRegistrationReplayRecord[]> {
  return {success: result.success, data: (result.data ?? []) as ContractRegistrationReplayRecord[], error: result.error}
}

export function buildStandardContractCreateRecord(request: ContractRegistrationRequest & {partnerId: string; contractTypeId: string; idempotencyKey: string; name: string; autoRenew: boolean}): Record<string, unknown> {
  return {
    pl_name: request.name,
    'pl_PartnerLookup@odata.bind': `/pl_partners(${request.partnerId})`,
    'pl_ContractTypeLookup@odata.bind': `/pl_contracttypes(${request.contractTypeId})`,
    ...(request.endDate ? {pl_enddate: `${request.endDate}T00:00:00.000Z`} : {}),
    ...(request.noticeDate ? {pl_noticedate: `${request.noticeDate}T00:00:00.000Z`} : {}),
    ...(request.decisionDate ? {pl_decisiondate: `${request.decisionDate}T00:00:00.000Z`} : {}),
    pl_autorenew: request.autoRenew,
    ...(request.link ? {pl_link: request.link} : {}),
    pl_registrationkey: request.idempotencyKey,
  }
}

export function createStandardContractRegistrationApi(service: StandardContractCreateService): ContractLedgerRegistrationApi {
  return createContractLedgerRegistrationApi(
    {
      findByRegistrationKey: async registrationKey => asReplayRecords(await service.findByRegistrationKey(registrationKey)),
    },
    async request => {
      try {
        const result = await service.create(buildStandardContractCreateRecord(request))
        const contractId = typeof result.data?.pl_contractid === 'string' ? result.data.pl_contractid : undefined
        return {success: result.success, contractId, error: result.error}
      } catch (error) {
        return {success: false, error}
      }
    },
  )
}

const generatedStandardContractCreateService: StandardContractCreateService = {
  create: async record => withCreatedRow(
    await Pl_contractsService.create(record as Parameters<typeof Pl_contractsService.create>[0]),
    'pl_contractid',
    lookupByKey(options => Pl_contractsService.getAll(options), 'pl_contractid', 'pl_registrationkey', record.pl_registrationkey),
  ),
  findByRegistrationKey: registrationKey => Pl_contractsService.getAll({
    select: ['pl_contractid', 'pl_name', '_pl_partnerlookup_value', '_pl_contracttypelookup_value', 'pl_contractstatuscode', 'pl_enddate', 'pl_noticedate', 'pl_decisiondate', 'pl_autorenew', 'pl_link', 'pl_registrationkey'],
    filter: `pl_registrationkey eq '${registrationKey.replace(/'/g, "''")}'`,
    maxPageSize: 2,
  }),
}

export const dataverseStandardContractRegistrationApi = createStandardContractRegistrationApi(generatedStandardContractCreateService)
