import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_partnersharesettings } from './generated/models/Pl_partnersharesettingsModel'
import { Pl_partnersharesettingsService } from './generated/services/Pl_partnersharesettingsService'
import { lookupByKey, withCreatedRow } from './createdRecordId'
import { createPartnerShareSettingApi, type PartnerShareAccessLevel, type PartnerSharePrincipalKind, type PartnerShareSettingApi, type PartnerShareSettingInvoker, type PartnerShareSettingOperationResult } from './partnerShareSettingApi'

export type StandardPartnerShareSettingService = {
  create: (record: Record<string, unknown>) => Promise<IOperationResult<Pick<Pl_partnersharesettings, 'pl_partnersharesettingid'>>>
  update: (id: string, changedFields: Record<string, unknown>) => Promise<IOperationResult<Pick<Pl_partnersharesettings, 'pl_partnersharesettingid'>>>
}

const principalKindOptions: Record<PartnerSharePrincipalKind, number> = {User: 100000000, Team: 100000001}
const accessLevelOptions: Record<PartnerShareAccessLevel, number> = {Read: 100000000, Write: 100000001}
const revokedSettingStatus = 100000001

function asOperationResult(
  result: IOperationResult<Pick<Pl_partnersharesettings, 'pl_partnersharesettingid'>>,
  settingId = '',
): PartnerShareSettingOperationResult {
  const returnedId = typeof result.data?.pl_partnersharesettingid === 'string'
    ? result.data.pl_partnersharesettingid
    : settingId
  return {
    success: result.success,
    data: {SettingId: returnedId, IsReplay: false},
    error: result.error,
  }
}

function buildCreateRecord(request: Parameters<PartnerShareSettingInvoker['create']>[0]): Record<string, unknown> {
  const principalBinding = request.principalKind === 'User'
    ? {'pl_UserPrincipalLookup@odata.bind': `/systemusers(${request.principalId})`}
    : {'pl_TeamPrincipalLookup@odata.bind': `/teams(${request.principalId})`}

  // pl_contenthash、pl_settingstatuscode、pl_isprotectedmanagementpathはサーバー導出。
  // 生成型上の必須列を埋めるためのダミー値をクライアントから送らない。
  return {
    'pl_PartnerLookup@odata.bind': `/pl_partners(${request.partnerId})`,
    pl_principalkindcode: principalKindOptions[request.principalKind],
    pl_accesslevelcode: accessLevelOptions[request.accessLevel],
    pl_requestkey: request.requestKey,
    ...principalBinding,
  }
}

function buildUpdateFields(request: Parameters<PartnerShareSettingInvoker['update']>[0]): Record<string, unknown> {
  return {
    pl_accesslevelcode: accessLevelOptions[request.accessLevel],
    pl_requestkey: request.requestKey,
  }
}

function createGeneratedServiceAdapter(): StandardPartnerShareSettingService {
  return {
    create: async record => withCreatedRow(
      await Pl_partnersharesettingsService.create(record as Parameters<typeof Pl_partnersharesettingsService.create>[0]),
      'pl_partnersharesettingid',
      lookupByKey(options => Pl_partnersharesettingsService.getAll(options), 'pl_partnersharesettingid', 'pl_requestkey', record.pl_requestkey),
    ),
    update: (id, fields) => Pl_partnersharesettingsService.update(
      id,
      fields as Parameters<typeof Pl_partnersharesettingsService.update>[1],
    ),
  }
}

export function createDataversePartnerShareSettingApi(
  service: StandardPartnerShareSettingService,
): PartnerShareSettingApi {
  const invoker: PartnerShareSettingInvoker = {
    create: async request => {
      try {
        return asOperationResult(await service.create(buildCreateRecord(request)))
      } catch (error) {
        return {success: false, data: {}, error}
      }
    },
    update: async request => {
      try {
        return asOperationResult(await service.update(request.settingId, buildUpdateFields(request)), request.settingId)
      } catch (error) {
        return {success: false, data: {}, error}
      }
    },
    revoke: async request => {
      try {
        return asOperationResult(await service.update(request.settingId, {pl_settingstatuscode: revokedSettingStatus}), request.settingId)
      } catch (error) {
        return {success: false, data: {}, error}
      }
    },
  }
  return createPartnerShareSettingApi(invoker)
}

export const dataversePartnerShareSettingApi = createDataversePartnerShareSettingApi(
  createGeneratedServiceAdapter(),
)
