import serverPolicySource from '../../../plugins/PartnerLedger.Plugins/ApprovalPolicyContract.cs?raw'
import {describe, expect, it} from 'vitest'
import {ApprovalSettingsWriteError, approvalPolicyJson, createApprovalSettingsWriteApi, type ApprovalSettingsCreateRecord, type StandardApprovalSettingsWriteService} from './approvalSettingsWrite'
import {defaultContractApprovalPolicy} from './contractApprovalPolicy'

const flags = {companyName: true, tradingStatus: false, address: true, phone: false, contractUpdate: true, mainOwner: false}

function service(versions: number[], create: StandardApprovalSettingsWriteService['create'] = async () => ({success: true, data: {}}) as never) {
  const reads: Record<string, unknown>[] = []
  const api: StandardApprovalSettingsWriteService = {
    getAll: async options => { reads.push(options ?? {}); return {success: true, data: [...versions].sort((a, b) => b - a).slice(0, 1).map(pl_settingsversion => ({pl_settingsversion}))} as never },
    create,
  }
  return {api, reads}
}

describe('approvalSettingsWrite', () => {
  it('numbers the new version from the highest version of all rows, inactive ones included', async () => {
    // 監査指摘（2026-09-27）：一意キーは無効な行も対象なので、採番も状態を問わず全行の最大＋1にする。
    const created: ApprovalSettingsCreateRecord[] = []
    const {api, reads} = service([1, 3, 2], async record => { created.push(record); return {success: true, data: {}} as never })

    await expect(createApprovalSettingsWriteApi(api).saveNewVersion({policy: flags})).resolves.toEqual({version: 4})

    expect(reads[0]).toMatchObject({select: ['pl_settingsversion'], orderBy: ['pl_settingsversion desc'], top: 1})
    expect(reads[0]).not.toHaveProperty('filter')
    expect(created).toEqual([{
      pl_name: '承認設定 版4',
      pl_settingsversion: 4,
      // 版2（2026-09-29）：主担当の変更（mainOwner）を最後に足す。サーバーの厳格な検査と同じ項目。
      pl_policyjson: '{"schemaVersion":2,"companyName":true,"tradingStatus":false,"address":true,"phone":false,"contractUpdate":true,"mainOwner":false}',
    }])
  })

  it('starts from version 1 when there is no row (the default policy is in use)', async () => {
    const created: ApprovalSettingsCreateRecord[] = []
    const {api} = service([], async record => { created.push(record); return {success: true, data: {}} as never })

    await expect(createApprovalSettingsWriteApi(api).saveNewVersion({policy: flags})).resolves.toEqual({version: 1})
    expect(created[0].pl_settingsversion).toBe(1)
  })

  it('reports a concurrent save of the same version as a conflict', async () => {
    const {api} = service([1], async () => ({success: false, error: new Error('A record that has the attribute values Settings Version already exists. (0x80060892)')}) as never)
    const error = await createApprovalSettingsWriteApi(api).saveNewVersion({policy: flags}).catch(caught => caught)

    expect(error).toBeInstanceOf(ApprovalSettingsWriteError)
    expect(error).toMatchObject({code: 'conflict', message: 'ほかの管理者が先に変更しました。再読み込みしてから、もう一度変更してください。'})
  })

  it('reports other failures without claiming success', async () => {
    const rejected = service([1], async () => ({success: false, error: new Error('Principal user is missing prvCreatepl_Settings privilege')}) as never)
    await expect(createApprovalSettingsWriteApi(rejected.api).saveNewVersion({policy: flags})).rejects.toMatchObject({code: 'operation-failed', message: '承認設定を保存できませんでした。権限を確認してください。'})

    const thrown = service([1], async () => { throw new Error('network') })
    await expect(createApprovalSettingsWriteApi(thrown.api).saveNewVersion({policy: flags})).rejects.toMatchObject({code: 'operation-failed'})
  })

  it('does not write when the current highest version cannot be read', async () => {
    let called = false
    const api: StandardApprovalSettingsWriteService = {
      getAll: async () => ({success: false, data: []}) as never,
      create: async () => { called = true; return {success: true, data: {}} as never },
    }

    await expect(createApprovalSettingsWriteApi(api).saveNewVersion({policy: flags})).rejects.toMatchObject({code: 'operation-failed'})
    expect(called).toBe(false)
  })

  it('uses the same default policy as the server', () => {
    // 監査指摘：サーバー（ApprovalPolicyContract.DefaultPolicyJson）とアプリの既定値の一致を検査する。
    const match = serverPolicySource.match(/DefaultPolicyJson\s*=\s*"((?:[^"\\]|\\.)*)"/)
    expect(match).not.toBeNull()
    const serverJson = match![1].replace(/\\"/g, '"')
    expect(serverJson).toBe(approvalPolicyJson(defaultContractApprovalPolicy))
  })
})
