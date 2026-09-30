import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_partners } from './generated/models/Pl_partnersModel'
import { Pl_partnersService } from './generated/services/Pl_partnersService'
import { lookupByKey, withCreatedRow } from './createdRecordId'
import { createPartnerLedgerRegistrationApi, type PartnerLedgerRegistrationApi, type PartnerRegistrationInvoker } from './partnerRegistrationApi'
import type { PartnerRegistrationRequest } from './partnerRegistrationApi'

/**
 * 標準 pl_partner Createのローカル接続境界。
 *
 * Custom APIの応答契約を再利用しているが、ここで実行するのは生成サービスの
 * createだけである。サーバー同期Pluginが入力境界と導出列を検査するため、画面の
 * 既定経路として使用する。生成型はApplicationRequiredのサーバー導出列も必須として
 * 表現するため、入力payloadはRecordで組み立て、権威列を送らないことを明示する。
 */
export type StandardPartnerCreateService = {
  create: (record: Record<string, unknown>) => Promise<IOperationResult<Pick<Pl_partners, 'pl_partnerid'>>>
}

function asOperationResult(result: IOperationResult<Pick<Pl_partners, 'pl_partnerid'>>, error?: unknown) {
  const partnerId = typeof result.data?.pl_partnerid === 'string' ? result.data.pl_partnerid : ''
  return {
    success: result.success,
    data: {Success: result.success, PartnerId: partnerId, IsReplay: false},
    error: error ?? result.error,
  }
}

function buildStandardCreateRecord(request: PartnerRegistrationRequest & {idempotencyKey: string}): Record<string, unknown> {
  return {
    pl_name: request.name,
    ...(request.industry ? {pl_industry: request.industry} : {}),
    ...(request.address ? {pl_address: request.address} : {}),
    ...(request.phone ? {pl_phone: request.phone} : {}),
    'pl_MainOwnerLookup@odata.bind': `/systemusers(${request.mainOwnerId})`,
    pl_registrationkey: request.idempotencyKey,
  }
}

export function createStandardPartnerRegistrationApi(
  service: StandardPartnerCreateService,
): PartnerLedgerRegistrationApi {
  const invoker: PartnerRegistrationInvoker = {
    registerPartner: async request => {
      try {
        return asOperationResult(await service.create(buildStandardCreateRecord(request)))
      } catch (error) {
        return {success: false, data: {}, error}
      }
    },
  }
  return createPartnerLedgerRegistrationApi(invoker)
}

const generatedStandardPartnerCreateService: StandardPartnerCreateService = {
  create: async record => withCreatedRow(
    await Pl_partnersService.create(record as Parameters<typeof Pl_partnersService.create>[0]),
    'pl_partnerid',
    lookupByKey(options => Pl_partnersService.getAll(options), 'pl_partnerid', 'pl_registrationkey', record.pl_registrationkey),
  ),
}

export const dataverseStandardPartnerRegistrationApi = createStandardPartnerRegistrationApi(
  generatedStandardPartnerCreateService,
)
