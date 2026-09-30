type AsyncOperationOverlayProps = {
  open: boolean
  title: string
  description?: string
}

/**
 * Dataverseなど応答待ちの操作を、画面を閉じずに待つための共通表示。
 * 呼び出し側がopenを操作中だけtrueにし、完了・失敗時にfalseへ戻す。
 */
export function AsyncOperationOverlay({open, title, description = '処理が完了するまで、この画面を閉じたり同じ操作を繰り返したりしないでください。'}: AsyncOperationOverlayProps) {
  if (!open) return null
  return <div className="operation-overlay" role="dialog" aria-modal="true" aria-labelledby="operation-overlay-title" aria-describedby="operation-overlay-description">
    <div className="operation-overlay-card" role="status" aria-live="polite" aria-busy="true">
      <span className="operation-spinner" aria-hidden="true" />
      <h2 id="operation-overlay-title">{title}</h2>
      <p id="operation-overlay-description">{description}</p>
    </div>
  </div>
}
