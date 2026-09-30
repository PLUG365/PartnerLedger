import { describe, expect, it, vi } from 'vitest'
import { BusinessCardDismissError, canDismissBusinessCard, createBusinessCardDismissApi } from './businessCardDismissal'

const me = '11111111-1111-4111-8111-111111111111'
const other = '22222222-2222-4222-8222-222222222222'
const captureId = '33333333-3333-4333-8333-333333333333'

describe('処理待ちから名刺を外す', () => {
  it('外せるのは、自分が受け付けて読み取りに失敗した・結果を確認できない名刺だけ', () => {
    expect(canDismissBusinessCard({captureStatus: '失敗', registeredById: me}, me)).toBe(true)
    expect(canDismissBusinessCard({captureStatus: '結果不明', registeredById: me.toUpperCase()}, me)).toBe(true)
    for (const status of ['未登録', '処理中', '確認待ち', '登録済み'] as const) {
      expect(canDismissBusinessCard({captureStatus: status, registeredById: me}, me)).toBe(false)
    }
    expect(canDismissBusinessCard({captureStatus: '失敗', registeredById: other}, me)).toBe(false)
    expect(canDismissBusinessCard({captureStatus: '失敗', registeredById: undefined}, me)).toBe(false)
    expect(canDismissBusinessCard({captureStatus: '失敗', registeredById: me}, undefined)).toBe(false)
  })

  it('行を非アクティブにするだけで、削除しない', async () => {
    const deactivate = vi.fn(async () => ({success: true}))
    await createBusinessCardDismissApi({deactivate}).dismiss({id: captureId, captureStatus: '失敗'})
    expect(deactivate).toHaveBeenCalledWith(captureId)
  })

  it('対象外の状態や不正なIdでは呼び出さず、失敗は利用者向けの文言で返す', async () => {
    const deactivate = vi.fn(async () => ({success: true}))
    const api = createBusinessCardDismissApi({deactivate})
    await expect(api.dismiss({id: captureId, captureStatus: '確認待ち'})).rejects.toMatchObject({code: 'invalid-input'})
    await expect(api.dismiss({id: 'not-a-guid', captureStatus: '失敗'})).rejects.toBeInstanceOf(BusinessCardDismissError)
    expect(deactivate).not.toHaveBeenCalled()

    await expect(createBusinessCardDismissApi({deactivate: async () => ({success: false})}).dismiss({id: captureId, captureStatus: '結果不明'}))
      .rejects.toMatchObject({code: 'operation-failed', message: '一覧から外せませんでした。自分が受け付けた名刺だけ外せます。'})
    await expect(createBusinessCardDismissApi({deactivate: async () => { throw new Error('offline') }}).dismiss({id: captureId, captureStatus: '失敗'}))
      .rejects.toMatchObject({code: 'transport-error'})
  })
})
