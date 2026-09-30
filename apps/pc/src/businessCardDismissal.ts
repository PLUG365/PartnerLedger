import { isDataverseGuid } from './dataverseIds'
import type { BusinessCardQueueItem } from './businessCardQueue'

/** 先へ進めない（確認画面も正式登録も扱えない）名刺取込の状態。 */
const dismissibleStatuses: readonly BusinessCardQueueItem['captureStatus'][] = ['失敗', '結果不明']

/**
 * 処理待ちから外せる行か。読み取りに失敗した・結果を確認できない行のうち、自分が受け付けたものだけ。
 * 可否の最終判定はDataverseの権限（自分の行へのWrite）が行う。
 */
export function canDismissBusinessCard(item: Pick<BusinessCardQueueItem, 'captureStatus' | 'registeredById'>, currentUserId: string | undefined): boolean {
  return dismissibleStatuses.includes(item.captureStatus)
    && Boolean(currentUserId && item.registeredById)
    && item.registeredById!.toLowerCase() === currentUserId!.toLowerCase()
}

export type BusinessCardDismissInvoker = {
  deactivate: (captureId: string) => Promise<{success: boolean; error?: unknown}>
}

export type BusinessCardDismissApi = {
  dismiss: (item: Pick<BusinessCardQueueItem, 'id' | 'captureStatus'>) => Promise<void>
}

export class BusinessCardDismissError extends Error {
  readonly code: 'invalid-input' | 'operation-failed' | 'transport-error'

  constructor(message: string, code: BusinessCardDismissError['code']) {
    super(message)
    this.name = 'BusinessCardDismissError'
    this.code = code
  }
}

/**
 * 名刺取込の行を非アクティブにして処理待ちから外す。行・画像・入力版は削除しない。
 */
export function createBusinessCardDismissApi(invoker: BusinessCardDismissInvoker): BusinessCardDismissApi {
  return {
    dismiss: async item => {
      if (!isDataverseGuid(item.id) || !dismissibleStatuses.includes(item.captureStatus)) {
        throw new BusinessCardDismissError('この名刺は一覧から外せません。', 'invalid-input')
      }
      let result: {success: boolean; error?: unknown}
      try {
        result = await invoker.deactivate(item.id)
      } catch {
        throw new BusinessCardDismissError('一覧から外せませんでした。接続を確認してください。', 'transport-error')
      }
      if (!result.success) throw new BusinessCardDismissError('一覧から外せませんでした。自分が受け付けた名刺だけ外せます。', 'operation-failed')
    },
  }
}
