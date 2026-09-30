import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import { Pl_requestsService } from './generated/services/Pl_requestsService'
import { Pl_submissionversionsService } from './generated/services/Pl_submissionversionsService'
import { createApprovalReadApi, type ApprovalReadApi, type ApprovalReadOperationResult } from './approvalReadModel'
import type { Pl_requests } from './generated/models/Pl_requestsModel'
import type { Pl_submissionversions } from './generated/models/Pl_submissionversionsModel'

export const approvalRequestSelect = [
  'pl_requestid',
  'pl_name',
  'pl_requesttypecode',
  'pl_requeststatuscode',
  'pl_cancellationreason',
  'pl_requestedat',
  '_pl_partnerlookup_value',
  '_pl_requestinguserlookup_value',
  '_pl_approverteamlookup_value',
  'statecode',
]

export const approvalSubmissionVersionSelect = [
  'pl_submissionversionid',
  'pl_name',
  'pl_submissionstatuscode',
  'pl_versionnumber',
  'pl_fixedat',
  'pl_submittedat',
  'pl_changesetjson',
  '_pl_requestlookup_value',
  'statecode',
]

function asOperationResult<T>(result: IOperationResult<T[]>): ApprovalReadOperationResult {
  return {success: result.success, data: result.data, error: result.error}
}

/**
 * 申請と提出版の一覧は読み取りだけを公開する。提出は標準Request Update、
 * グループ承認はPower Automate Approvals、結果取込みと対象反映はサーバー側の
 * 標準行Updateで行うため、このRead APIから承認操作や結果書込みは呼び出さない。
 */
export const dataverseApprovalReadApi: ApprovalReadApi = createApprovalReadApi({
  listRequests: async () => asOperationResult<Pl_requests>(await Pl_requestsService.getAll({
    select: approvalRequestSelect,
    filter: 'statecode eq 0',
    orderBy: ['pl_requestedat desc'],
    maxPageSize: 100,
  })),
  listSubmissionVersions: async () => asOperationResult<Pl_submissionversions>(await Pl_submissionversionsService.getAll({
    select: approvalSubmissionVersionSelect,
    filter: 'statecode eq 0',
    orderBy: ['pl_versionnumber desc'],
    maxPageSize: 200,
  })),
})
