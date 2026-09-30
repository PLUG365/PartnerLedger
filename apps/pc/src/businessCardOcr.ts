// 名刺の取込み・読み取りの状態（Dataverseの選択肢の表示値）。読み取りの開始・確保・結果の判定は
// サーバー側（フロー・Plugin）に移ったため、アプリ側の判定処理は2026-09-27に削除した。
export const BUSINESS_CARD_CAPTURE_STATUS = {
  ready: '未登録',
  processing: '処理中',
  review: '確認待ち',
  registered: '登録済み',
  failed: '失敗',
  unknown: '結果不明',
} as const

export const BUSINESS_CARD_OCR_JOB_STATUS = {
  queued: '待機',
  processing: '処理中',
  succeeded: '成功',
  failed: '失敗',
  unknown: '結果不明',
  cancelled: '取消',
} as const

export type BusinessCardCaptureStatus = typeof BUSINESS_CARD_CAPTURE_STATUS[keyof typeof BUSINESS_CARD_CAPTURE_STATUS]
export type BusinessCardOcrJobStatus = typeof BUSINESS_CARD_OCR_JOB_STATUS[keyof typeof BUSINESS_CARD_OCR_JOB_STATUS]
