import type { IOperationResult } from '@microsoft/power-apps/data'
import type { Pl_settingses } from './generated/models/Pl_settingsesModel'
import { isDataverseGuid } from './dataverseIds'

export type ContractApprovalPolicy = {
  companyName: boolean
  tradingStatus: boolean
  address: boolean
  phone: boolean
  contractUpdate: boolean
  /** 主担当の変更に承認が必要か（版2で追加、2026-09-29）。版1の設定では承認が必要として読む。 */
  mainOwner: boolean
  settingsVersion: number
}

export type ContractApprovalPolicyRecord = {
  pl_settingsid?: unknown
  pl_policyjson?: unknown
  pl_settingsversion?: unknown
  statecode?: unknown
}

export type ContractApprovalPolicyReadApi = {
  getActivePolicy: () => Promise<ContractApprovalPolicy>
}

export type ContractApprovalPolicyLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; policy: ContractApprovalPolicy}
  | {status: 'error'; message: string}

export class ContractApprovalPolicyReadError extends Error {
  readonly code: 'invalid-response' | 'transport-error'

  constructor(message: string, code: ContractApprovalPolicyReadError['code']) {
    super(message)
    this.name = 'ContractApprovalPolicyReadError'
    this.code = code
  }
}

// 版1の項目。版2は最後にmainOwnerを足す（サーバーのApprovalPolicyContractと同じ）。
const policyKeys = ['schemaVersion', 'companyName', 'tradingStatus', 'address', 'phone', 'contractUpdate'] as const
const policyKeysV2 = [...policyKeys, 'mainOwner'] as const

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && isDataverseGuid(value)
}

function parsePolicyJson(value: unknown): Omit<ContractApprovalPolicy, 'settingsVersion'> {
  if (typeof value !== 'string' || !value.trim()) throw new ContractApprovalPolicyReadError('承認設定のJSONが空です。', 'invalid-response')
  let parsed: unknown
  try {
    parsed = JSON.parse(value)
  } catch {
    throw new ContractApprovalPolicyReadError('承認設定のJSONを解釈できません。', 'invalid-response')
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new ContractApprovalPolicyReadError('承認設定の構造が不正です。', 'invalid-response')
  }
  const object = parsed as Record<string, unknown>
  const expectedKeys: readonly string[] = object.schemaVersion === 2 ? policyKeysV2 : policyKeys
  const keys = Object.keys(object).sort()
  if (keys.length !== expectedKeys.length || keys.some((key, index) => key !== [...expectedKeys].sort()[index])) {
    throw new ContractApprovalPolicyReadError('承認設定の項目が不正です。', 'invalid-response')
  }
  if ((object.schemaVersion !== 1 && object.schemaVersion !== 2) || expectedKeys.slice(1).some(key => typeof object[key] !== 'boolean')) {
    throw new ContractApprovalPolicyReadError('承認設定の値が不正です。', 'invalid-response')
  }
  return {
    companyName: object.companyName as boolean,
    tradingStatus: object.tradingStatus as boolean,
    address: object.address as boolean,
    phone: object.phone as boolean,
    contractUpdate: object.contractUpdate as boolean,
    mainOwner: object.schemaVersion === 2 ? object.mainOwner as boolean : true,
  }
}

/**
 * 有効な承認設定の行が無いときの既定値（版0）。サーバーのApprovalPolicyContract.DefaultPolicyJsonと同じ内容にする。
 * インポート直後に行を作る手順を無くすため（2026-09-27）。
 */
export const defaultContractApprovalPolicy: ContractApprovalPolicy = {
  companyName: true,
  tradingStatus: true,
  address: false,
  phone: false,
  contractUpdate: true,
  mainOwner: true,
  settingsVersion: 0,
}

export function parseActiveContractApprovalPolicy(records: unknown): ContractApprovalPolicy {
  if (!Array.isArray(records)) {
    throw new ContractApprovalPolicyReadError('承認設定の応答が不正です。', 'invalid-response')
  }
  if (records.length === 0) return {...defaultContractApprovalPolicy}
  const normalized = records.map(record => {
    const value = record as ContractApprovalPolicyRecord
    if (!isGuid(value.pl_settingsid) || value.statecode !== 0 || !Number.isInteger(value.pl_settingsversion) || Number(value.pl_settingsversion) <= 0) {
      throw new ContractApprovalPolicyReadError('承認設定の応答が不正です。', 'invalid-response')
    }
    return {id: value.pl_settingsid, version: value.pl_settingsversion as number, flags: parsePolicyJson(value.pl_policyjson)}
  })
  const maxVersion = Math.max(...normalized.map(value => value.version))
  const current = normalized.filter(value => value.version === maxVersion)
  if (current.length !== 1) throw new ContractApprovalPolicyReadError('承認設定の現行版を一意に解決できません。', 'invalid-response')
  return {...current[0].flags, settingsVersion: maxVersion}
}

export type StandardContractApprovalPolicyService = {
  getAll: (options?: Record<string, unknown>) => Promise<IOperationResult<Pl_settingses[]>>
}

export function createContractApprovalPolicyReadApi(service: StandardContractApprovalPolicyService): ContractApprovalPolicyReadApi {
  return {
    getActivePolicy: async () => {
      let result: IOperationResult<Pl_settingses[]>
      try {
        result = await service.getAll({
          select: ['pl_settingsid', 'pl_policyjson', 'pl_settingsversion', 'statecode'],
          filter: 'statecode eq 0',
          orderBy: ['pl_settingsversion desc'],
          maxPageSize: 101,
        })
      } catch {
        throw new ContractApprovalPolicyReadError('承認設定を取得できませんでした。', 'transport-error')
      }
      if (!result.success) throw new ContractApprovalPolicyReadError('承認設定を取得できませんでした。', 'transport-error')
      return parseActiveContractApprovalPolicy(result.data ?? [])
    },
  }
}
