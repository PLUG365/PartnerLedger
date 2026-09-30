import type { IOperationResult } from '@microsoft/power-apps/data'
import type { ContractApprovalPolicy } from './contractApprovalPolicy'

export type ApprovalPolicyFlags = Omit<ContractApprovalPolicy, 'settingsVersion'>

export type ApprovalSettingsCreateRecord = {
  pl_name: string
  pl_settingsversion: number
  pl_policyjson: string
}

export type StandardApprovalSettingsWriteService = {
  getAll: (options?: Record<string, unknown>) => Promise<IOperationResult<{pl_settingsversion?: unknown}[]>>
  create: (record: ApprovalSettingsCreateRecord) => Promise<IOperationResult<unknown>>
}

export type ApprovalSettingsWriteApi = {
  saveNewVersion: (request: {policy: ApprovalPolicyFlags}) => Promise<{version: number}>
}

export class ApprovalSettingsWriteError extends Error {
  readonly code: 'conflict' | 'operation-failed'

  constructor(message: string, code: ApprovalSettingsWriteError['code'], cause?: unknown) {
    super(message)
    this.name = 'ApprovalSettingsWriteError'
    this.code = code
    if (cause !== undefined) this.cause = cause
  }
}

// サーバーの厳格な検査（ApprovalPolicyContract）と同じ項目・順番で書く。
export function approvalPolicyJson(policy: ApprovalPolicyFlags): string {
  return JSON.stringify({
    schemaVersion: 2,
    companyName: policy.companyName,
    tradingStatus: policy.tradingStatus,
    address: policy.address,
    phone: policy.phone,
    contractUpdate: policy.contractUpdate,
    mainOwner: policy.mainOwner,
  })
}

// 版番号（pl_settingsversion）の一意キーに当たったときのDataverseのエラー（0x80060892）。
function isDuplicateVersion(error: unknown): boolean {
  const text = error instanceof Error ? error.message : typeof error === 'string' ? error : JSON.stringify(error ?? '')
  return /0x80060892|already exists|DuplicateRecord/i.test(text)
}

const conflictMessage = 'ほかの管理者が先に変更しました。再読み込みしてから、もう一度変更してください。'
const failedMessage = '承認設定を保存できませんでした。権限を確認してください。'

// 状態を問わず全行の最大版を読む。版番号の一意キーは無効な行も対象にするため（2026-09-27、監査指摘）。
async function highestVersion(service: StandardApprovalSettingsWriteService): Promise<number> {
  let result: IOperationResult<{pl_settingsversion?: unknown}[]>
  try {
    result = await service.getAll({select: ['pl_settingsversion'], orderBy: ['pl_settingsversion desc'], top: 1})
  } catch (error) {
    throw new ApprovalSettingsWriteError(failedMessage, 'operation-failed', error)
  }
  if (!result.success || !Array.isArray(result.data)) throw new ApprovalSettingsWriteError(failedMessage, 'operation-failed', result.error)
  if (result.data.length === 0) return 0
  const version = result.data[0].pl_settingsversion
  if (typeof version !== 'number' || !Number.isInteger(version) || version < 0) throw new ApprovalSettingsWriteError(failedMessage, 'operation-failed')
  return version
}

/**
 * 承認設定を新しい版（全行の最大版＋1）の行として保存する。既存の行は書き換えない（2026-09-27）。
 * 同時に保存した場合は、版番号の一意キーで後から保存した方が失敗する。
 */
export function createApprovalSettingsWriteApi(service: StandardApprovalSettingsWriteService): ApprovalSettingsWriteApi {
  return {
    saveNewVersion: async ({policy}) => {
      const version = (await highestVersion(service)) + 1
      let result: IOperationResult<unknown>
      try {
        result = await service.create({pl_name: `承認設定 版${version}`, pl_settingsversion: version, pl_policyjson: approvalPolicyJson(policy)})
      } catch (error) {
        throw new ApprovalSettingsWriteError(isDuplicateVersion(error) ? conflictMessage : failedMessage, isDuplicateVersion(error) ? 'conflict' : 'operation-failed', error)
      }
      if (!result.success) {
        const duplicate = isDuplicateVersion(result.error)
        throw new ApprovalSettingsWriteError(duplicate ? conflictMessage : failedMessage, duplicate ? 'conflict' : 'operation-failed', result.error)
      }
      return {version}
    },
  }
}
