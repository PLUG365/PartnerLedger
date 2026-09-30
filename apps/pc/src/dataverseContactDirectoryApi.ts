import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_contacts } from './generated/models/Pl_contactsModel'
import { Pl_contactsService } from './generated/services/Pl_contactsService'
import { createContactDirectoryApi, type ContactDirectoryApi, type ContactDirectoryOperationResult } from './contactDirectory'

/**
 * 画面に必要な最小列だけを取得する。行の可視性は Dataverse の通常 Read 権限に
 * 委ね、クライアント側で取引先ごとの可視性を再判定しない。
 *
 * Lookup / Choice の表示名は生成Modelの仮想名または OData の表示アノテーションとして
 * 返り得る。パーサーが両方を扱い、表示名が無い場合に追加読取りや書込みは行わない。
 */
export const contactDirectorySelect = [
  'pl_contactid',
  'pl_name',
  '_pl_partnerlookup_value',
  'pl_departmentrole',
  'pl_email',
  'pl_phone',
  'pl_statuscode',
  'pl_registeredat',
  'statecode',
]

export const contactDirectoryActiveFilter = 'statecode eq 0 and pl_statuscode ne 100000002'

function asOperationResult(result: IOperationResult<Pl_contacts[]>): ContactDirectoryOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

export const dataverseContactDirectoryApi: ContactDirectoryApi = createContactDirectoryApi({
  listContacts: async () => asOperationResult(await Pl_contactsService.getAll({
    select: contactDirectorySelect,
    filter: contactDirectoryActiveFilter,
    orderBy: ['pl_name asc'],
    maxPageSize: 100,
  })),
})
