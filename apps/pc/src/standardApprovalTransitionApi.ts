import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_requests } from './generated/models/Pl_requestsModel'
import type { Pl_submissionversions } from './generated/models/Pl_submissionversionsModel'
import { Pl_requestsService } from './generated/services/Pl_requestsService'
import { Pl_submissionversionsService } from './generated/services/Pl_submissionversionsService'
import { boundId, lookupByKey, withCreatedRow } from './createdRecordId'
import { isDataverseGuid } from './dataverseIds'

/**
 * 承認の公開入口は生成された標準CRUDだけにする。
 * このファイルで送る値は下書きの業務入力と提出状態だけであり、
 * 権威列の保護は必ずDataverseの同期Pluginでも再検査する。
 */
export type ApprovalRequestDraftInput = {
  name: string
  requestTypeCode: string
  partnerId: string
  contractId?: string
  idempotencyKey?: string
}

export type ApprovalSubmissionDraftInput = {
  requestId: string
  name: string
  changeSetJson: string
}

export type ApprovalRequestDraftUpdateInput = ApprovalRequestDraftInput & {
  requestId: string
}

export type ApprovalSubmissionDraftUpdateInput = ApprovalSubmissionDraftInput & {
  submissionVersionId: string
}

export type ApprovalSubmitInput = {
  requestId: string
}

export type ApprovalCancellationInput = {
  requestId: string
  /** 申請者の取下げでは省略できる。管理者の取消では必須（サーバーが判定する）。 */
  reason?: string
}

export type StandardApprovalWriteService = {
  createRequest: (record: Record<string, unknown>) => Promise<IOperationResult<Pl_requests>>
  createSubmissionVersion: (record: Record<string, unknown>) => Promise<IOperationResult<Pl_submissionversions>>
  updateRequest: (requestId: string, fields: Record<string, unknown>) => Promise<IOperationResult<Pl_requests>>
  updateSubmissionVersion: (submissionVersionId: string, fields: Record<string, unknown>) => Promise<IOperationResult<Pl_submissionversions>>
}

export type StandardApprovalWriteApi = {
  createRequest: (input: ApprovalRequestDraftInput) => Promise<{requestId: string}>
  createSubmissionDraft: (input: ApprovalSubmissionDraftInput) => Promise<{submissionVersionId: string}>
  updateRequestDraft: (input: ApprovalRequestDraftUpdateInput) => Promise<{requestId: string}>
  updateSubmissionDraft: (input: ApprovalSubmissionDraftUpdateInput) => Promise<{submissionVersionId: string}>
  submit: (input: ApprovalSubmitInput) => Promise<{requestId: string}>
  cancel?: (input: ApprovalCancellationInput) => Promise<{requestId: string}>
}

export class StandardApprovalWriteError extends Error {
  readonly code: string

  constructor(message: string, code: string) {
    super(message)
    this.name = 'StandardApprovalWriteError'
    this.code = code
  }
}

let idempotencySequence = 0

function errorText(error: unknown): string {
  if (!error || typeof error !== 'object') return typeof error === 'string' ? error : ''
  const candidate = error as {message?: unknown; response?: unknown; cause?: unknown}
  return [typeof candidate.message === 'string' ? candidate.message : '', errorText(candidate.response), errorText(candidate.cause)].join(' ')
}

// 提出を断った理由のうち、利用者が直せるもの（StandardApprovalSubmitPluginの「標準提出を拒否しました: <コード>」）。
const submitRejections: {pattern: RegExp; code: string; message: string}[] = [
  {pattern: /標準提出を拒否しました:\s*MainOwnerUnavailable\b/, code: 'main-owner-unavailable', message: '選んだ主担当は、通常の有効な利用者ではないため選べません（無効になったユーザーや、Microsoftのサポート用アカウントなど）。'},
  {pattern: /標準提出を拒否しました:\s*MainOwnerUnchanged\b/, code: 'main-owner-unchanged', message: '選んだ人は、すでにこの取引先の主担当です。'},
]

function requireText(value: string | undefined, fieldName: string): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new StandardApprovalWriteError(`${fieldName}を指定してください。`, 'required')
  }
  return value.trim()
}

function optionalText(value: string | undefined, fieldName: string, maxLength: number): string | undefined {
  if (value === undefined || value === null) return undefined
  if (typeof value !== 'string') {
    throw new StandardApprovalWriteError(`${fieldName}が不正です。`, 'invalid-input')
  }
  if (value.trim() === '') return undefined
  const normalized = value.trim()
  if (normalized.length > maxLength) {
    throw new StandardApprovalWriteError(`${fieldName}は${maxLength}文字以内で指定してください。`, 'too-long')
  }
  return normalized
}

function requireGuid(value: string | undefined, fieldName: string): string {
  const normalized = requireText(value, fieldName)
  if (!isDataverseGuid(normalized)) {
    throw new StandardApprovalWriteError(`${fieldName}が正しくありません。`, 'invalid-guid')
  }
  return normalized
}

function createIdempotencyKey(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `CreateApprovalRequest-${uuid}`
  idempotencySequence += 1
  return `CreateApprovalRequest-${Date.now().toString(36)}-${idempotencySequence.toString(36)}`
}

function normalizeRequestContent(input: Pick<ApprovalRequestDraftInput, 'name' | 'requestTypeCode' | 'partnerId' | 'contractId'>) {
  const name = requireText(input.name, '申請名')
  if (name.length > 850) throw new StandardApprovalWriteError('申請名は850文字以内で指定してください。', 'too-long')
  const requestTypeCode = requireText(input.requestTypeCode, '申請種別')
  if (requestTypeCode.length > 200) throw new StandardApprovalWriteError('申請種別は200文字以内で指定してください。', 'too-long')
  const partnerId = requireGuid(input.partnerId, '取引先')
  const contractText = optionalText(input.contractId, '契約', 100)
  const contractId = contractText ? requireGuid(contractText, '契約') : undefined
  return {name, requestTypeCode, partnerId, contractId}
}

function normalizeRequestDraft(input: ApprovalRequestDraftInput) {
  const {name, requestTypeCode, partnerId, contractId} = normalizeRequestContent(input)
  const idempotencyKey = optionalText(input.idempotencyKey, '冪等キー', 200) ?? createIdempotencyKey()
  return {name, requestTypeCode, partnerId, contractId, idempotencyKey}
}

function normalizeSubmissionDraft(input: ApprovalSubmissionDraftInput) {
  const requestId = requireGuid(input.requestId, '申請')
  const name = requireText(input.name, '提出版名')
  if (name.length > 850) throw new StandardApprovalWriteError('提出版名は850文字以内で指定してください。', 'too-long')
  const changeSetJson = requireText(input.changeSetJson, '変更内容')
  if (changeSetJson.length > 1048576) {
    throw new StandardApprovalWriteError('変更内容は1,048,576文字以内で指定してください。', 'too-long')
  }
  return {requestId, name, changeSetJson}
}

function normalizeSubmit(input: ApprovalSubmitInput) {
  return {requestId: requireGuid(input.requestId, '申請')}
}

function normalizeCancellation(input: ApprovalCancellationInput) {
  const requestId = requireGuid(input.requestId, '申請')
  const reason = (input.reason ?? '').trim()
  if (reason.length > 2000) {
    throw new StandardApprovalWriteError('取消理由は2,000文字以内で指定してください。', 'too-long')
  }
  return {requestId, reason}
}

/**
 * 下書き申請の標準Create payload。
 * RequestingUser、ApproverTeam、PolicyVersion、RequestedAt、Status、Ownerは送らない。
 */
export function buildStandardApprovalRequestCreateRecord(input: ApprovalRequestDraftInput): Record<string, unknown> {
  const normalized = normalizeRequestDraft(input)
  return {
    pl_name: normalized.name,
    pl_requesttypecode: normalized.requestTypeCode,
    'pl_PartnerLookup@odata.bind': `/pl_partners(${normalized.partnerId})`,
    ...(normalized.contractId ? {'pl_ContractLookup@odata.bind': `/pl_contracts(${normalized.contractId})`} : {}),
    pl_requestkey: normalized.idempotencyKey,
  }
}

/**
 * 下書き申請の標準Update payload。保護列や提出状態は更新対象に含めない。
 */
export function buildStandardApprovalRequestUpdateRecord(input: ApprovalRequestDraftUpdateInput): {requestId: string; fields: Record<string, unknown>} {
  const requestId = requireGuid(input.requestId, '申請')
  const normalized = normalizeRequestContent(input)
  return {
    requestId,
    fields: {
      pl_name: normalized.name,
      pl_requesttypecode: normalized.requestTypeCode,
      'pl_PartnerLookup@odata.bind': `/pl_partners(${normalized.partnerId})`,
      ...(normalized.contractId ? {'pl_ContractLookup@odata.bind': `/pl_contracts(${normalized.contractId})`} : {}),
    },
  }
}

/**
 * 提出版下書きの標準Create payload。
 * 版番号・PolicyVersion・対象row version・提出者・提出時刻・状態はサーバー導出とする。
 */
export function buildStandardApprovalSubmissionCreateRecord(input: ApprovalSubmissionDraftInput): Record<string, unknown> {
  const normalized = normalizeSubmissionDraft(input)
  return {
    pl_name: normalized.name,
    pl_changesetjson: normalized.changeSetJson,
    'pl_RequestLookup@odata.bind': `/pl_requests(${normalized.requestId})`,
  }
}

/**
 * 下書き提出版の標準Update payload。版番号・提出・固定・状態列は更新対象に含めない。
 */
export function buildStandardApprovalSubmissionUpdateRecord(input: ApprovalSubmissionDraftUpdateInput): {submissionVersionId: string; fields: Record<string, unknown>} {
  const submissionVersionId = requireGuid(input.submissionVersionId, '提出版')
  const normalized = normalizeSubmissionDraft(input)
  return {
    submissionVersionId,
    fields: {
      pl_name: normalized.name,
      pl_changesetjson: normalized.changeSetJson,
    },
  }
}

/**
 * 提出は申請の状態更新1回だけを公開する。
 * 対応する版の固定、連携行作成、監査はサーバー側同期処理が同一トランザクションで行う。
 */
export function buildStandardApprovalSubmitFields(input: ApprovalSubmitInput): {requestId: string; fields: Record<string, unknown>} {
  const normalized = normalizeSubmit(input)
  return {
    requestId: normalized.requestId,
    fields: {pl_requeststatuscode: '提出中'},
  }
}

export function buildStandardApprovalCancellationFields(input: ApprovalCancellationInput): {requestId: string; fields: Record<string, unknown>} {
  const normalized = normalizeCancellation(input)
  return {
    requestId: normalized.requestId,
    fields: normalized.reason
      ? {pl_requeststatuscode: '取消', pl_cancellationreason: normalized.reason}
      : {pl_requeststatuscode: '取消'},
  }
}

function requireCreatedId<T extends {pl_requestid?: unknown} | {pl_submissionversionid?: unknown}>(
  result: IOperationResult<T>,
  idField: 'pl_requestid' | 'pl_submissionversionid',
): string {
  const value = (result.data as Record<string, unknown> | undefined)?.[idField]
  if (!result.success || typeof value !== 'string' || !isDataverseGuid(value)) {
    throw new StandardApprovalWriteError('保存結果のIDを確認できません。入力を保持したまま再試行してください。', 'invalid-response')
  }
  return value
}

async function requireSuccessfulUpdate<T>(
  update: () => Promise<IOperationResult<T>>,
  failureMessage: string,
): Promise<void> {
  let result: IOperationResult<T>
  try {
    result = await update()
  } catch {
    throw new StandardApprovalWriteError(failureMessage, 'transport-error')
  }
  if (!result.success) throw new StandardApprovalWriteError('下書きを保存できませんでした。状態と権限を確認してください。', 'operation-rejected')
}

export function createStandardApprovalWriteApi(service: StandardApprovalWriteService): StandardApprovalWriteApi {
  return {
    createRequest: async input => {
      const record = buildStandardApprovalRequestCreateRecord(input)
      let result: IOperationResult<Pl_requests>
      try {
        result = await service.createRequest(record)
      } catch {
        throw new StandardApprovalWriteError('申請の保存に失敗しました。入力を保持したまま再試行してください。', 'transport-error')
      }
      return {requestId: requireCreatedId(result, 'pl_requestid')}
    },
    createSubmissionDraft: async input => {
      const record = buildStandardApprovalSubmissionCreateRecord(input)
      let result: IOperationResult<Pl_submissionversions>
      try {
        result = await service.createSubmissionVersion(record)
      } catch {
        throw new StandardApprovalWriteError('提出版の保存に失敗しました。入力を保持したまま再試行してください。', 'transport-error')
      }
      return {submissionVersionId: requireCreatedId(result, 'pl_submissionversionid')}
    },
    updateRequestDraft: async input => {
      const {requestId, fields} = buildStandardApprovalRequestUpdateRecord(input)
      await requireSuccessfulUpdate(() => service.updateRequest(requestId, fields), '申請下書きの更新に失敗しました。入力を保持したまま再試行してください。')
      return {requestId}
    },
    updateSubmissionDraft: async input => {
      const {submissionVersionId, fields} = buildStandardApprovalSubmissionUpdateRecord(input)
      await requireSuccessfulUpdate(() => service.updateSubmissionVersion(submissionVersionId, fields), '提出版下書きの更新に失敗しました。入力を保持したまま再試行してください。')
      return {submissionVersionId}
    },
    submit: async input => {
      const {requestId, fields} = buildStandardApprovalSubmitFields(input)
      let result: IOperationResult<Pl_requests>
      try {
        result = await service.updateRequest(requestId, fields)
      } catch {
        throw new StandardApprovalWriteError('提出に失敗しました。入力を保持したまま権限と接続を確認してください。', 'transport-error')
      }
      if (!result.success) {
        const text = errorText(result.error)
        const known = submitRejections.find(rejection => rejection.pattern.test(text))
        if (known) throw new StandardApprovalWriteError(known.message, known.code)
        throw new StandardApprovalWriteError('提出できませんでした。申請状態と提出版を確認してください。', 'operation-rejected')
      }
      return {requestId}
    },
    cancel: async input => {
      const {requestId, fields} = buildStandardApprovalCancellationFields(input)
      await requireSuccessfulUpdate(
        () => service.updateRequest(requestId, fields),
        '取り消せませんでした。申請の状態を確認してください。',
      )
      return {requestId}
    },
  }
}

const generatedStandardApprovalWriteService: StandardApprovalWriteService = {
  createRequest: async record => withCreatedRow(
    await Pl_requestsService.create(record as Parameters<typeof Pl_requestsService.create>[0]),
    'pl_requestid',
    lookupByKey(options => Pl_requestsService.getAll(options), 'pl_requestid', 'pl_requestkey', record.pl_requestkey),
  ),
  // 提出版にはキーが無い。サーバーは申請ごとに下書きの提出版を1つだけ許すため、その1行を読み直す。
  createSubmissionVersion: async record => withCreatedRow(
    await Pl_submissionversionsService.create(record as Parameters<typeof Pl_submissionversionsService.create>[0]),
    'pl_submissionversionid',
    async () => {
      const requestId = boundId(record['pl_RequestLookup@odata.bind'])
      if (!requestId) return {success: true, data: []}
      return Pl_submissionversionsService.getAll({select: ['pl_submissionversionid'], filter: `_pl_requestlookup_value eq ${requestId} and pl_submissionstatuscode eq '下書き'`, top: 2})
    },
  ),
  updateRequest: (requestId, fields) => Pl_requestsService.update(requestId, fields as Parameters<typeof Pl_requestsService.update>[1]),
  updateSubmissionVersion: (submissionVersionId, fields) => Pl_submissionversionsService.update(submissionVersionId, fields as Parameters<typeof Pl_submissionversionsService.update>[1]),
}

export const dataverseStandardApprovalWriteApi = createStandardApprovalWriteApi(generatedStandardApprovalWriteService)
