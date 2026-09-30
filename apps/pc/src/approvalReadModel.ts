import { isDataverseGuid } from './dataverseIds'

type RecordLike = Record<string, unknown>

export type ApprovalRequestSummary = {
  id: string
  name: string
  requestType: string
  status: string
  cancellationReason?: string
  requestedAt?: string
  partnerId?: string
  partnerName?: string
  requestingUserId?: string
  requestingUserName?: string
  approverTeamName?: string
}

export type ApprovalSubmissionSummary = {
  id: string
  requestId: string
  name?: string
  status: string
  versionNumber?: number
  fixedAt?: string
  submittedAt?: string
  changeSetJson?: string
}

export type ApprovalInboxItem = {
  request: ApprovalRequestSummary
  latestSubmission?: ApprovalSubmissionSummary
  /** 未提出の提出版。差戻し後に作った修正版もここに入り、提出するまで latestSubmission にはならない。 */
  draftSubmission?: ApprovalSubmissionSummary
  submissionSummaryUnavailable?: boolean
}

export type ApprovalReadLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; items: ApprovalInboxItem[]; submissionSummaryUnavailable?: boolean}
  | {status: 'error'; message: string}

export type ApprovalReadResult = {
  items: ApprovalInboxItem[]
  submissionSummaryUnavailable: boolean
}

export type ApprovalReadOperationResult = {
  success: boolean
  data?: unknown[]
  error?: unknown
}

export type ApprovalReadInvoker = {
  listRequests: () => Promise<ApprovalReadOperationResult>
  listSubmissionVersions: () => Promise<ApprovalReadOperationResult>
}

export type ApprovalReadApi = {
  listAccessibleApprovalRequests: () => Promise<ApprovalReadResult>
}

export class ApprovalReadError extends Error {
  readonly code: 'operation-failed' | 'invalid-response'

  constructor(message: string, code: 'operation-failed' | 'invalid-response') {
    super(message)
    this.name = 'ApprovalReadError'
    this.code = code
  }
}

const formattedPartnerLookupName = '_pl_partnerlookup_value@OData.Community.Display.V1.FormattedValue'
const formattedRequestingUserLookupName = '_pl_requestinguserlookup_value@OData.Community.Display.V1.FormattedValue'
const formattedApproverTeamLookupName = '_pl_approverteamlookup_value@OData.Community.Display.V1.FormattedValue'

function asRecord(value: unknown, fieldName: string): RecordLike {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new ApprovalReadError(`${fieldName}の応答形式が不正です。`, 'invalid-response')
  }
  return value as RecordLike
}

function requiredString(record: RecordLike, fieldName: string): string {
  const value = record[fieldName]
  if (typeof value !== 'string' || !value.trim()) {
    throw new ApprovalReadError(`${fieldName}が応答にありません。`, 'invalid-response')
  }
  return value.trim()
}

function optionalString(record: RecordLike, fieldName: string): string | undefined {
  const value = record[fieldName]
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') {
    throw new ApprovalReadError(`${fieldName}の応答形式が不正です。`, 'invalid-response')
  }
  return value.trim() || undefined
}

function requiredGuid(record: RecordLike, fieldName: string): string {
  const value = requiredString(record, fieldName)
  if (!isDataverseGuid(value)) {
    throw new ApprovalReadError(`${fieldName}を正しく読み込めませんでした。`, 'invalid-response')
  }
  return value
}

function optionalGuid(record: RecordLike, fieldName: string): string | undefined {
  const value = optionalString(record, fieldName)
  if (value === undefined) return undefined
  if (!isDataverseGuid(value)) {
    throw new ApprovalReadError(`${fieldName}を正しく読み込めませんでした。`, 'invalid-response')
  }
  return value
}

function optionalIsoDate(record: RecordLike, fieldName: string): string | undefined {
  const value = optionalString(record, fieldName)
  if (value === undefined) return undefined
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    throw new ApprovalReadError(`${fieldName}の日時形式が不正です。`, 'invalid-response')
  }
  return parsed.toISOString()
}

function optionalNonNegativeInteger(record: RecordLike, fieldName: string): number | undefined {
  const value = record[fieldName]
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 0) {
    throw new ApprovalReadError(`${fieldName}の数値形式が不正です。`, 'invalid-response')
  }
  return value
}

function ensureActive(record: RecordLike, fieldName: string): void {
  const value = record.statecode
  if (value !== undefined && value !== null && value !== '' && value !== 0 && value !== '0') {
    throw new ApprovalReadError(`${fieldName}が非Activeです。`, 'invalid-response')
  }
}

function resolveLabel(record: RecordLike, generatedName: string, formattedName: string): string | undefined {
  return optionalString(record, generatedName) ?? optionalString(record, formattedName)
}

export function parseApprovalRequestRecord(value: unknown): ApprovalRequestSummary {
  const record = asRecord(value, 'pl_Request')
  ensureActive(record, 'pl_Request')
  return {
    id: requiredGuid(record, 'pl_requestid'),
    name: optionalString(record, 'pl_name') ?? '申請名未設定',
    requestType: requiredString(record, 'pl_requesttypecode'),
    status: requiredString(record, 'pl_requeststatuscode'),
    cancellationReason: optionalString(record, 'pl_cancellationreason'),
    requestedAt: optionalIsoDate(record, 'pl_requestedat'),
    partnerId: optionalGuid(record, '_pl_partnerlookup_value'),
    partnerName: resolveLabel(record, 'pl_partnerlookupname', formattedPartnerLookupName),
    requestingUserId: optionalGuid(record, '_pl_requestinguserlookup_value'),
    requestingUserName: resolveLabel(record, 'pl_requestinguserlookupname', formattedRequestingUserLookupName),
    approverTeamName: resolveLabel(record, 'pl_approverteamlookupname', formattedApproverTeamLookupName),
  }
}

export function parseApprovalSubmissionVersionRecord(value: unknown): ApprovalSubmissionSummary {
  const record = asRecord(value, 'pl_SubmissionVersion')
  ensureActive(record, 'pl_SubmissionVersion')
  return {
    id: requiredGuid(record, 'pl_submissionversionid'),
    requestId: requiredGuid(record, '_pl_requestlookup_value'),
    name: optionalString(record, 'pl_name'),
    status: requiredString(record, 'pl_submissionstatuscode'),
    versionNumber: optionalNonNegativeInteger(record, 'pl_versionnumber'),
    fixedAt: optionalIsoDate(record, 'pl_fixedat'),
    submittedAt: optionalIsoDate(record, 'pl_submittedat'),
    changeSetJson: optionalString(record, 'pl_changesetjson'),
  }
}

function latestSubmission(current: ApprovalSubmissionSummary | undefined, candidate: ApprovalSubmissionSummary): ApprovalSubmissionSummary {
  if (!current) return candidate
  const currentVersion = current.versionNumber ?? -1
  const candidateVersion = candidate.versionNumber ?? -1
  if (candidateVersion !== currentVersion) return candidateVersion > currentVersion ? candidate : current
  const currentDate = current.submittedAt ?? current.fixedAt ?? ''
  const candidateDate = candidate.submittedAt ?? candidate.fixedAt ?? ''
  if (candidateDate !== currentDate) return candidateDate > currentDate ? candidate : current
  return candidate.id > current.id ? candidate : current
}

export function createApprovalInboxModel(requestRows: unknown[], submissionRows: unknown[]): ApprovalInboxItem[] {
  const requests = requestRows.map(parseApprovalRequestRecord)
  const requestIds = new Set<string>()
  for (const request of requests) {
    if (requestIds.has(request.id)) {
      throw new ApprovalReadError('申請一覧に重複したIDがあります。', 'invalid-response')
    }
    requestIds.add(request.id)
  }

  const latestByRequest = new Map<string, ApprovalSubmissionSummary>()
  const draftByRequest = new Map<string, ApprovalSubmissionSummary>()
  for (const row of submissionRows) {
    const submission = parseApprovalSubmissionVersionRecord(row)
    // A version without an accessible parent is not displayed. This prevents
    // a partial ACL response from becoming an orphaned detail in the UI.
    if (!requestIds.has(submission.requestId)) continue
    latestByRequest.set(submission.requestId, latestSubmission(latestByRequest.get(submission.requestId), submission))
    if (submission.status === '下書き') draftByRequest.set(submission.requestId, latestSubmission(draftByRequest.get(submission.requestId), submission))
  }

  return requests
    .map(request => ({request, latestSubmission: latestByRequest.get(request.id), draftSubmission: draftByRequest.get(request.id)}))
    .sort((left, right) => (right.request.requestedAt ?? '').localeCompare(left.request.requestedAt ?? '') || left.request.id.localeCompare(right.request.id))
}

function requireSuccessfulRows(result: ApprovalReadOperationResult, operationName: string): unknown[] {
  if (!result.success) throw new ApprovalReadError(`${operationName}の取得に失敗しました。`, 'operation-failed')
  if (!Array.isArray(result.data)) throw new ApprovalReadError(`${operationName}の応答形式が不正です。`, 'invalid-response')
  return result.data
}

export function createApprovalReadApi(invoker: ApprovalReadInvoker): ApprovalReadApi {
  return {
    listAccessibleApprovalRequests: async () => {
      const requestRows = requireSuccessfulRows(await invoker.listRequests(), '申請')
      const submissionResult = await invoker.listSubmissionVersions()
      if (!submissionResult.success) {
        return {
          items: createApprovalInboxModel(requestRows, []),
          submissionSummaryUnavailable: true,
        }
      }
      const submissionRows = requireSuccessfulRows(submissionResult, '提出版')
      return {
        items: createApprovalInboxModel(requestRows, submissionRows),
        submissionSummaryUnavailable: false,
      }
    },
  }
}
