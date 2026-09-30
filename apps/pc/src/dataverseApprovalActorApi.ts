import './dataverseRuntimeDataSourcesInfo'
import { SystemusersService } from './generated/services/SystemusersService'
import type { ApprovalActorQuery } from './approvalActor'

/** 本人のsystemuseridだけを読む。管理者ロールの確認には、利用者に既定で付くBasic UserのRole読取を使う。 */
export const dataverseApprovalActorQuery: ApprovalActorQuery = async filter => {
  const result = await SystemusersService.getAll({select: ['systemuserid'], filter, top: 2})
  return {success: result.success, data: result.data?.map(user => ({systemuserid: user.systemuserid}))}
}
