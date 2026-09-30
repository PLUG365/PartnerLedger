import { describe, expect, it, vi } from 'vitest'
import { buildContractTypeCreateRecord, ContractTypeManagementError, createContractTypeManagementApi, parseContractTypeAdminItems } from './contractTypeManagement'

const firstId = '11111111-1111-4111-8111-111111111111'
const secondId = '22222222-2222-4222-8222-222222222222'

describe('契約種類マスタ管理の標準CRUD契約', () => {
  it('Active／非Activeを状態付きで並べ、重複IDは拒否する', () => {
    expect(parseContractTypeAdminItems([
      {pl_contracttypeid: secondId, pl_name: '廃止済み契約', pl_selectable: false, statecode: 1, statuscode: 2},
      {pl_contracttypeid: firstId, pl_name: '基本取引契約', pl_displayorder: 10, pl_selectable: true, statecode: 0, statuscode: 1},
    ])).toEqual([
      {id: firstId, name: '基本取引契約', displayOrder: 10, lifecycleCode: undefined, selectable: true, state: 'Active'},
      {id: secondId, name: '廃止済み契約', displayOrder: undefined, lifecycleCode: undefined, selectable: false, state: 'Inactive'},
    ])
    expect(() => parseContractTypeAdminItems([
      {pl_contracttypeid: firstId, pl_name: 'A', pl_selectable: true, statecode: 0, statuscode: 1},
      {pl_contracttypeid: firstId, pl_name: 'B', pl_selectable: true, statecode: 0, statuscode: 1},
    ])).toThrow(/重複/)
  })

  it('不正な状態組合せ・選択可否・名前をfail-closedで拒否する', () => {
    const valid = {pl_contracttypeid: firstId, pl_name: '基本取引契約', pl_selectable: true, statecode: 0, statuscode: 1}
    expect(() => parseContractTypeAdminItems([{...valid, statecode: 1}])).toThrow(ContractTypeManagementError)
    expect(() => parseContractTypeAdminItems([{...valid, statuscode: 2}])).toThrow(ContractTypeManagementError)
    expect(() => parseContractTypeAdminItems([{...valid, pl_selectable: 'true'}])).toThrow(ContractTypeManagementError)
    expect(() => buildContractTypeCreateRecord({name: ' '})).toThrow(/契約種類名/)
    expect(() => buildContractTypeCreateRecord({name: 'x'.repeat(201)})).toThrow(/200文字以内/)
    expect(() => buildContractTypeCreateRecord({name: '契約', displayOrder: -1})).toThrow(/0以上/)
  })

  it('Create payloadは業務列だけで、権威列を含めない', () => {
    expect(buildContractTypeCreateRecord({name: '秘密保持契約', displayOrder: 30, lifecycleCode: 'v1'})).toEqual({
      pl_name: '秘密保持契約',
      pl_displayorder: 30,
      pl_lifecyclecode: 'v1',
      pl_selectable: true,
    })
  })

  it('追加・名称変更・廃止は標準CRUDの最小payloadへ変換する', async () => {
    const create = vi.fn(async () => ({success: true, data: {pl_contracttypeid: secondId}}))
    const update = vi.fn(async () => ({success: true, data: {}}))
    const api = createContractTypeManagementApi({
      list: async () => ({success: true, data: []}),
      create,
      update,
    })
    await expect(api.create({name: '業務委託契約', displayOrder: 20})).resolves.toEqual({id: secondId})
    await api.rename(firstId, '基本契約')
    await api.retire(firstId)
    expect(create).toHaveBeenCalledWith({pl_name: '業務委託契約', pl_displayorder: 20, pl_selectable: true})
    expect(update).toHaveBeenNthCalledWith(1, firstId, {pl_name: '基本契約'})
    expect(update).toHaveBeenNthCalledWith(2, firstId, {statecode: 1, statuscode: 2, pl_selectable: false})
  })

  it('権限拒否・通信失敗・不正Create結果を成功扱いにしない', async () => {
    const rejected = createContractTypeManagementApi({
      list: async () => ({success: false, data: []}),
      create: async () => ({success: false, data: {}}),
      update: async () => ({success: false, data: {}}),
    })
    await expect(rejected.list()).rejects.toMatchObject({code: 'operation-rejected'})
    await expect(rejected.create({name: '契約'})).rejects.toMatchObject({code: 'operation-rejected'})
    await expect(rejected.rename(firstId, '契約')).rejects.toMatchObject({code: 'operation-rejected'})
    await expect(rejected.retire(firstId)).rejects.toMatchObject({code: 'operation-rejected'})

    const broken = createContractTypeManagementApi({
      list: async () => { throw new Error('network') },
      create: async () => ({success: true, data: {}}),
      update: async () => ({success: true, data: {}}),
    })
    await expect(broken.list()).rejects.toMatchObject({code: 'transport-error'})
    await expect(broken.create({name: '契約'})).rejects.toMatchObject({code: 'invalid-response'})
  })
})
