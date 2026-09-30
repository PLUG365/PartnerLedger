import { describe, expect, it } from 'vitest'
import { BusinessCardQueueError, createBusinessCardQueueApi, parseBusinessCardQueue } from './businessCardQueue'

const batchId = '11111111-1111-4111-8111-111111111111'
const captureId = '22222222-2222-4222-8222-222222222222'

function batch(id = batchId) {
  return {pl_capturebatchid: id, pl_batchkey: 'batch-001', pl_batchstatuscode: '受付完了', statecode: 0}
}

function capture(id = captureId) {
  return {
    pl_cardcaptureid: id,
    pl_capturekey: 'capture-001',
    pl_capturestatuscode: '確認待ち',
    _pl_capturebatchlookup_value: batchId,
    pl_currentversionnumber: 1,
    pl_registeredat: '2026-09-18',
    pl_receivedat: '2026-09-18',
    createdbyname: '山田 太郎',
    statecode: 0,
  }
}

describe('名刺処理待ちの標準Read境界', () => {
  it('バッチと名刺取込を結合し、OCR前の入力値を返さず最小項目へ正規化する', () => {
    expect(parseBusinessCardQueue([batch()], [capture()])).toEqual([{
      id: captureId,
      captureKey: 'capture-001',
      captureStatus: '確認待ち',
      batchId,
      batchKey: 'batch-001',
      batchStatus: '受付完了',
      registeredBy: '山田 太郎',
      registeredAt: '2026-09-18',
      receivedAt: '2026-09-18',
      currentVersionNumber: 1,
    }])
  })

  it('空の配列は正常な空一覧として返す', () => expect(parseBusinessCardQueue([], [])).toEqual([]))

  it('不正な状態、非Active、孤児取込、重複IDをfail-closedで拒否する', () => {
    expect(() => parseBusinessCardQueue([batch()], [{...capture(), pl_capturestatuscode: '不明'}])).toThrowError(BusinessCardQueueError)
    expect(() => parseBusinessCardQueue([{...batch(), statecode: 1}], [capture()])).toThrowError(/非Active/)
    expect(() => parseBusinessCardQueue([], [capture()])).toThrowError(/バッチがありません/)
    expect(() => parseBusinessCardQueue([batch()], [capture(), capture('33333333-3333-4333-8333-333333333333')])).toThrowError(/重複した取込キー/)
  })

  it('取得失敗と不正配列応答を成功扱いしない', async () => {
    const failed = createBusinessCardQueueApi({
      listBatches: async () => ({success: false, data: []}),
      listCaptures: async () => ({success: true, data: []}),
    })
    await expect(failed.listQueue()).rejects.toMatchObject({name: 'BusinessCardQueueError', code: 'operation-failed'})

    const malformed = createBusinessCardQueueApi({
      listBatches: async () => ({success: true, data: undefined}),
      listCaptures: async () => ({success: true, data: []}),
    })
    await expect(malformed.listQueue()).rejects.toMatchObject({name: 'BusinessCardQueueError', code: 'invalid-response'})
  })
})
