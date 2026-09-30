import { describe, expect, it } from 'vitest'
import { ContractTypeReadError, createContractTypeReadApi, parseSelectableContractTypes } from './contractTypeReadModel'

const firstId = '11111111-1111-4111-8111-111111111111'
const secondId = '22222222-2222-4222-8222-222222222222'

describe('契約種類マスタReadの表示契約', () => {
  it('Activeかつ選択可能な行だけを表示順で返す', () => {
    expect(parseSelectableContractTypes([
      {pl_contracttypeid: secondId, pl_name: '業務委託契約', pl_displayorder: 20, pl_selectable: true, statecode: 0},
      {pl_contracttypeid: firstId, pl_name: '基本取引契約', pl_displayorder: 10, pl_selectable: true, statecode: 0},
    ])).toEqual([
      {id: firstId, name: '基本取引契約', displayOrder: 10},
      {id: secondId, name: '業務委託契約', displayOrder: 20},
    ])
  })

  it('サーバー応答の非Active・選択不可・重複をfail-closedで拒否する', () => {
    const valid = {pl_contracttypeid: firstId, pl_name: '基本取引契約', pl_selectable: true, statecode: 0}
    expect(() => parseSelectableContractTypes([{...valid, statecode: 1}])).toThrowError(ContractTypeReadError)
    expect(() => parseSelectableContractTypes([{...valid, pl_selectable: false}])).toThrowError(ContractTypeReadError)
    expect(() => parseSelectableContractTypes([valid, valid])).toThrowError(/重複/)
  })

  it('通信失敗をモックへ戻さず返す', async () => {
    const api = createContractTypeReadApi({listSelectableContractTypes: async () => ({success: false, data: []})})
    await expect(api.listSelectableContractTypes()).rejects.toMatchObject<Partial<ContractTypeReadError>>({code: 'transport-error'})
  })
})
