import { describe, expect, it } from 'vitest'
import { createContactParentDirectoryApi, parseContactRegistrationParents } from './contactParentDirectory'

const partnerId = '22222222-2222-4222-8222-222222222222'

describe('担当者登録用の取引先候補境界', () => {
  it('ActiveでRead可能な候補だけをID・名称へ変換する', () => {
    expect(parseContactRegistrationParents([{pl_partnerid: partnerId, pl_name: '青空ソリューションズ株式会社', statecode: 0}]))
      .toEqual([{id: partnerId, name: '青空ソリューションズ株式会社'}])
  })

  it('非Active・ID不正・重複はfail-closedで拒否する', () => {
    expect(() => parseContactRegistrationParents([{pl_partnerid: partnerId, pl_name: '終了会社', statecode: 1}])).toThrow(/状態/)
    expect(() => parseContactRegistrationParents([{pl_partnerid: 'invalid', pl_name: '不正', statecode: 0}])).toThrow(/ID/)
    expect(() => parseContactRegistrationParents([
      {pl_partnerid: partnerId, pl_name: 'A', statecode: 0},
      {pl_partnerid: partnerId, pl_name: 'B', statecode: 0},
    ])).toThrow(/重複/)
  })

  it('通信失敗はモック候補へフォールバックしない', async () => {
    await expect(createContactParentDirectoryApi({listParents: async () => ({success: false, data: []})}))
      .rejects.toMatchObject({code: 'transport-error'})
  })
})
