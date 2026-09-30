import { isDataverseGuid } from './dataverseIds'
import type { ApprovalRequestSummary } from './approvalReadModel'

/**
 * サーバーの取消判定（ApprovalCancellationContract.IsAllowedAdministratorRole）と同じロール名。
 * サーバーと同じく、ユーザーへ直接割り当てたロールだけを数える（Team経由は数えない）。
 */
export const approvalAdministratorRoleNames = ['PL システム管理', 'システム管理者', 'System Administrator'] as const

/** 承認画面を操作している本人。取消・取下げボタンの表示判定だけに使い、可否の最終判定はサーバーが行う。 */
export type ApprovalActor = {
  systemUserId: string
  isAdministrator: boolean
}

export type ApprovalCancellationAction =
  | {kind: 'withdraw'; reasonRequired: false}
  | {kind: 'administrator-withdraw'; reasonRequired: true}
  | {kind: 'administrator-cancel'; reasonRequired: true}

function sameGuid(left: string | undefined, right: string): boolean {
  return Boolean(left) && left!.toLowerCase() === right.toLowerCase()
}

/**
 * サーバーの判定表（PL-033／PL-034）と同じ条件で、出してよい取消系の操作を返す。
 * 本人が分からないときは何も出さない。
 */
export function approvalCancellationAction(
  request: Pick<ApprovalRequestSummary, 'status' | 'requestingUserId'>,
  actor: ApprovalActor | undefined,
): ApprovalCancellationAction | undefined {
  if (!actor) return undefined
  const isRequester = sameGuid(request.requestingUserId, actor.systemUserId)
  if (request.status === '下書き' || request.status === '差戻し') {
    if (isRequester) return {kind: 'withdraw', reasonRequired: false}
    if (actor.isAdministrator) return {kind: 'administrator-withdraw', reasonRequired: true}
    return undefined
  }
  if (request.status === '提出中' && actor.isAdministrator) return {kind: 'administrator-cancel', reasonRequired: true}
  return undefined
}

export function selfFilter(objectId: string): string {
  return `azureactivedirectoryobjectid eq ${objectId}`
}

export function administratorFilter(systemUserId: string): string {
  const names = approvalAdministratorRoleNames.map(name => `r/name eq '${name.replace(/'/g, "''")}'`).join(' or ')
  return `systemuserid eq ${systemUserId} and systemuserroles_association/any(r:${names})`
}

export type ApprovalActorQueryResult = {
  success: boolean
  data?: {systemuserid?: string}[]
}

export type ApprovalActorQuery = (filter: string) => Promise<ApprovalActorQueryResult>

/**
 * サインイン中のユーザーのDataverse IDと管理者かどうかを読む。
 * 本人を特定できなければundefined、管理者の確認に失敗したら管理者ではない扱いにする（ボタンを出さない側に倒す）。
 */
export async function loadApprovalActor(objectId: string | undefined, query: ApprovalActorQuery): Promise<ApprovalActor | undefined> {
  if (!objectId || !isDataverseGuid(objectId)) return undefined
  let self: ApprovalActorQueryResult
  try {
    self = await query(selfFilter(objectId))
  } catch {
    return undefined
  }
  const rows = self.success ? self.data ?? [] : []
  const systemUserId = rows.length === 1 ? rows[0].systemuserid : undefined
  if (!systemUserId || !isDataverseGuid(systemUserId)) return undefined

  let isAdministrator = false
  try {
    const admin = await query(administratorFilter(systemUserId))
    isAdministrator = admin.success && (admin.data ?? []).length === 1
  } catch {
    isAdministrator = false
  }
  return {systemUserId, isAdministrator}
}
