import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { Pl_cardcapturesService } from './generated/services/Pl_cardcapturesService'
import { Pl_contactsService } from './generated/services/Pl_contactsService'
import { Pl_partnersService } from './generated/services/Pl_partnersService'
import { dataverseStandardContactRegistrationApi } from './standardContactRegistrationApi'
import { dataverseStandardPartnerRegistrationApi } from './standardPartnerRegistrationApi'
import type { ContactRegistrationReplayRecord } from './contactRegistrationApi'
import { createBusinessCardFormalRegistrationApi, type BusinessCardFormalRegistrationInvoker, type FormalRegistrationReadResult, type PartnerFormalRegistrationRecord } from './businessCardFormalRegistration'

export const businessCardFormalPartnerSelect = [
  'pl_partnerid',
  'pl_name',
  'pl_industry',
  'pl_address',
  'pl_phone',
  'pl_registrationkey',
  '_pl_mainownerlookup_value',
]

export const businessCardFormalContactSelect = [
  'pl_contactid',
  'pl_name',
  '_pl_partnerlookup_value',
  'pl_statuscode',
  'pl_departmentrole',
  'pl_email',
  'pl_phone',
  'pl_registrationkey',
]

function escapeODataString(value: string): string {
  return value.replace(/'/g, "''")
}

function asPartnerReadResult(result: IOperationResult<unknown[]>): FormalRegistrationReadResult<PartnerFormalRegistrationRecord> {
  return {success: result.success, data: (result.data ?? []) as PartnerFormalRegistrationRecord[], error: result.error}
}

function asContactReadResult(result: IOperationResult<unknown[]>): FormalRegistrationReadResult<ContactRegistrationReplayRecord> {
  return {success: result.success, data: (result.data ?? []) as ContactRegistrationReplayRecord[], error: result.error}
}

const invoker: BusinessCardFormalRegistrationInvoker = {
  findPartnerByRegistrationKey: async registrationKey => asPartnerReadResult(await Pl_partnersService.getAll({
    select: businessCardFormalPartnerSelect,
    filter: `statecode eq 0 and pl_registrationkey eq '${escapeODataString(registrationKey)}'`,
    maxPageSize: 2,
  })),
  registerPartner: request => dataverseStandardPartnerRegistrationApi.registerPartner(request),
  findContactByRegistrationKey: async registrationKey => asContactReadResult(await Pl_contactsService.getAll({
    select: businessCardFormalContactSelect,
    filter: `statecode eq 0 and pl_registrationkey eq '${escapeODataString(registrationKey)}'`,
    maxPageSize: 2,
  })),
  registerContact: request => dataverseStandardContactRegistrationApi.registerContact(request),
  updateCaptureStatus: async (captureId, status) => {
    const result = await Pl_cardcapturesService.update(captureId, {pl_capturestatuscode: status})
    return {success: result.success, error: result.error}
  },
}

/**
 * 確認確定済みReviewから、標準Partner／Contact CreateとCapture Updateだけを束ねる。
 * Custom APIや任意のHTTP入口は使わず、要求キーの再照合で途中失敗・再送を安全側に扱う。
 */
export const dataverseBusinessCardFormalRegistrationApi = createBusinessCardFormalRegistrationApi(invoker)

