import type { ApprovalInboxItem } from './approvalReadModel'
import { StandardApprovalWriteError, type StandardApprovalWriteApi } from './standardApprovalTransitionApi'
export const approvalRequestTypeLabels: Record<string, string> = {
  'partner.company-name': '会社名の変更',
  'partner.trading-status': '取引状態の変更',
  'partner.address': '住所の変更',
  'partner.phone': '代表電話の変更',
  'partner.main-owner': '主担当の変更',
  'contract.update': '契約の更新・終了判断',
}

export type ChangeValue = string | number | boolean | null
type ChangeOperation = {attribute: string; value: ChangeValue}
export type ParsedChangeSet = {schemaVersion: number; target: {entity: string; id: string}; changes: ChangeOperation[]}

const tradingStatusLabels: Record<number, string> = {100000000: '未承認', 100000001: '取引中', 100000002: '休止中', 100000003: '終了'}
const contractStatusLabels: Record<number, string> = {100000000: '更新（締結済み）', 100000001: '終了'}
export const dateAttributes = new Set(['pl_enddate', 'pl_noticedate', 'pl_decisiondate'])
export const choiceLabels: Record<string, Record<number, string>> = {pl_tradingstatuscode: tradingStatusLabels, pl_contractstatuscode: contractStatusLabels}

export function parseChangeSet(json?: string): ParsedChangeSet | undefined {
  if (!json) return undefined
  try {
    const value = JSON.parse(json) as Partial<ParsedChangeSet>
    if (!value || typeof value.schemaVersion !== 'number' || !value.target || typeof value.target.entity !== 'string' || typeof value.target.id !== 'string' || !Array.isArray(value.changes)) return undefined
    const changes = value.changes.filter((change): change is ChangeOperation => Boolean(change) && typeof change.attribute === 'string')
    return {schemaVersion: value.schemaVersion, target: value.target, changes}
  } catch {
    return undefined
  }
}

export function changeAttributeLabel(entity: string, attribute: string): string {
  if (attribute === 'pl_name') return entity === 'pl_contract' ? '契約名' : '会社名'
  const labels: Record<string, string> = {
    pl_tradingstatuscode: '取引状態',
    pl_address: '住所',
    pl_phone: '代表電話',
    pl_mainownerlookup: '主担当',
    pl_contractstatuscode: '判断',
    pl_enddate: '契約終了日',
    pl_noticedate: '解約通知期限',
    pl_decisiondate: '更新判断期限',
    pl_autorenew: '自動更新',
    pl_link: '契約書リンク',
  }
  return labels[attribute] ?? attribute
}

/** 変更内容の値を業務の表記にする。主担当はユーザーのIDなので、名前の一覧（userNames）で名前にする（2026-09-29）。 */
export function formatChangeValue(attribute: string, value: ChangeValue, userNames: Record<string, string> = {}): string {
  if (value === null || value === '') return '（空欄）'
  if (attribute === 'pl_mainownerlookup' && typeof value === 'string') return userNames[value.toLowerCase()] ?? '（ユーザー）'
  if (typeof value === 'boolean') return value ? 'あり' : 'なし'
  if (typeof value === 'number' && choiceLabels[attribute]) return choiceLabels[attribute][value] ?? String(value)
  if (typeof value === 'string' && dateAttributes.has(attribute)) return value.slice(0, 10)
  return String(value)
}

export function approvalStatusClass(status: string) {
  if (status === '承認済み' || status === '反映済み') return 'active'
  if (status === '却下' || status === '取消' || status === '反映失敗') return 'ended'
  return 'unconfirmed'
}

/** 申請に保存された理由の見出し。取消は取消の理由、反映失敗は反映できなかった理由。 */
export function statusReasonLabel(status: string) {
  return status === '反映失敗' ? '反映できなかった理由' : '取消の理由'
}

export function formatApprovalDate(value?: string) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('ja-JP', {dateStyle: 'short', timeStyle: 'short'}).format(new Date(value))
}

export function writeErrorMessage(error: unknown, fallback: string) {
  return error instanceof StandardApprovalWriteError ? error.message : fallback
}

export type ApprovalFilter = 'action' | 'pending' | 'done' | 'all'
export const filterLabels: Record<ApprovalFilter, string> = {action: '対応が必要', pending: '承認待ち', done: '完了', all: 'すべて'}
const actionStatuses = new Set(['下書き', '差戻し'])
const pendingStatuses = new Set(['提出中', '承認済み', '反映待ち'])

export function statusGroup(status: string): Exclude<ApprovalFilter, 'all'> {
  if (actionStatuses.has(status)) return 'action'
  if (pendingStatuses.has(status)) return 'pending'
  return 'done'
}

const groupOrder: Record<Exclude<ApprovalFilter, 'all'>, number> = {action: 0, pending: 1, done: 2}

export function sortApprovalItems(items: ApprovalInboxItem[]): ApprovalInboxItem[] {
  return [...items].sort((left, right) => {
    const byGroup = groupOrder[statusGroup(left.request.status)] - groupOrder[statusGroup(right.request.status)]
    if (byGroup !== 0) return byGroup
    return (right.request.requestedAt ?? '').localeCompare(left.request.requestedAt ?? '')
  })
}

/**
 * 差戻し後の修正内容を提出版の下書きとして保存する。未提出の修正版が既にあれば
 * それを更新し、無ければ作成する。下書きが2つになるとサーバーは提出を受け付けない。
 */
export async function saveResubmissionDraft(api: StandardApprovalWriteApi, item: ApprovalInboxItem, changeSetJson: string): Promise<void> {
  const draft = item.draftSubmission
  const name = draft?.name ?? `${item.request.name} 修正`
  if (draft) await api.updateSubmissionDraft({requestId: item.request.id, submissionVersionId: draft.id, name, changeSetJson})
  else await api.createSubmissionDraft({requestId: item.request.id, name, changeSetJson})
}
