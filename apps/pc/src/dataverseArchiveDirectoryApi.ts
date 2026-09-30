import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_contacts } from './generated/models/Pl_contactsModel'
import type { Pl_partners } from './generated/models/Pl_partnersModel'
import { Pl_contactsService } from './generated/services/Pl_contactsService'
import { Pl_partnersService } from './generated/services/Pl_partnersService'
import { createArchiveDirectoryApi, type ArchiveDirectoryApi, type ArchiveDirectoryOperationResult } from './archiveDirectory'

export const archivePartnerSelect = [
  'pl_partnerid',
  'pl_name',
  'pl_tradingstatuscode',
  'pl_registeredat',
  '_pl_mainownerlookup_value',
  '_pl_registeredbylookup_value',
  '_ownerid_value',
  '_createdby_value',
  'statecode',
]

export const archiveContactSelect = [
  'pl_contactid',
  'pl_name',
  '_pl_partnerlookup_value',
  'pl_departmentrole',
  'pl_statuscode',
  'pl_registeredat',
  'statecode',
]

function asPartnerResult(result: IOperationResult<Pl_partners[]>): ArchiveDirectoryOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

function asContactResult(result: IOperationResult<Pl_contacts[]>): ArchiveDirectoryOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

const generatedArchiveDirectoryApi: ArchiveDirectoryApi = createArchiveDirectoryApi({
  listArchivedPartners: async () => asPartnerResult(await Pl_partnersService.getAll({
    select: archivePartnerSelect,
    filter: 'statecode eq 1 or pl_tradingstatuscode eq 100000003',
    orderBy: ['pl_name asc'],
    maxPageSize: 100,
  })),
  listArchivedContacts: async () => asContactResult(await Pl_contactsService.getAll({
    select: archiveContactSelect,
    filter: 'statecode eq 1 or pl_statuscode eq 100000002',
    orderBy: ['pl_name asc'],
    maxPageSize: 100,
  })),
})

export const dataverseArchiveDirectoryApi = generatedArchiveDirectoryApi
