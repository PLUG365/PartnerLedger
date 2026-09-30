import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_contacts } from './generated/models/Pl_contactsModel'
import { Pl_contactsService } from './generated/services/Pl_contactsService'
import { lookupByKey, withCreatedRow } from './createdRecordId'
import { createContactLedgerRegistrationApi, type ContactLedgerRegistrationApi, type ContactRegistrationReplayRecord } from './contactRegistrationApi'
import type { ContactRegistrationRequest } from './contactRegistrationApi'

export type StandardContactCreateService = {
  create: (record: Record<string, unknown>) => Promise<IOperationResult<Pick<Pl_contacts, 'pl_contactid'>>>
  findByRegistrationKey: (registrationKey: string) => Promise<IOperationResult<Pl_contacts[]>>
}

function asReplayRecords(result: IOperationResult<Pl_contacts[]>): IOperationResult<ContactRegistrationReplayRecord[]> {
  return {success: result.success, data: (result.data ?? []) as ContactRegistrationReplayRecord[], error: result.error}
}

function buildStandardCreateRecord(request: ReturnType<typeof normalizeRequestForService>): Record<string, unknown> {
  return {
    pl_name: request.name,
    'pl_PartnerLookup@odata.bind': `/pl_partners(${request.partnerId})`,
    pl_statuscode: request.statusCode,
    ...(request.departmentRole ? {pl_departmentrole: request.departmentRole} : {}),
    ...(request.email ? {pl_email: request.email} : {}),
    ...(request.phone ? {pl_phone: request.phone} : {}),
    pl_registrationkey: request.idempotencyKey,
  }
}

function normalizeRequestForService(request: ContactRegistrationRequest & {statusCode: 100000000 | 100000001 | 100000002; idempotencyKey: string}) {
  return request
}

export function createStandardContactRegistrationApi(service: StandardContactCreateService): ContactLedgerRegistrationApi {
  return createContactLedgerRegistrationApi(
    {
      findByRegistrationKey: async registrationKey => asReplayRecords(await service.findByRegistrationKey(registrationKey)),
    },
    async request => {
      try {
        const result = await service.create(buildStandardCreateRecord(normalizeRequestForService(request)))
        const contactId = typeof result.data?.pl_contactid === 'string' ? result.data.pl_contactid : undefined
        return {success: result.success, contactId, error: result.error}
      } catch (error) {
        return {success: false, error}
      }
    },
  )
}

const generatedStandardContactCreateService: StandardContactCreateService = {
  create: async record => withCreatedRow(
    await Pl_contactsService.create(record as Parameters<typeof Pl_contactsService.create>[0]),
    'pl_contactid',
    lookupByKey(options => Pl_contactsService.getAll(options), 'pl_contactid', 'pl_registrationkey', record.pl_registrationkey),
  ),
  findByRegistrationKey: registrationKey => Pl_contactsService.getAll({
    select: ['pl_contactid', 'pl_name', '_pl_partnerlookup_value', 'pl_statuscode', 'pl_departmentrole', 'pl_email', 'pl_phone'],
    filter: `pl_registrationkey eq '${registrationKey.replace(/'/g, "''")}'`,
    maxPageSize: 2,
  }),
}

export const dataverseStandardContactRegistrationApi = createStandardContactRegistrationApi(generatedStandardContactCreateService)
