import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AsyncOperationOverlay } from './AsyncOperationOverlay'

describe('AsyncOperationOverlay', () => {
  it('閉じているときは画面を占有しない', () => {
    expect(renderToStaticMarkup(<AsyncOperationOverlay open={false} title="保存中" />)).toBe('')
  })

  it('処理中は再操作を抑止する待機表示とアクセシビリティ属性を出す', () => {
    const html = renderToStaticMarkup(<AsyncOperationOverlay open title="共有設定を保存しています" description="Dataverseへ反映しています。" />)
    expect(html).toContain('role="dialog"')
    expect(html).toContain('aria-modal="true"')
    expect(html).toContain('aria-busy="true"')
    expect(html).toContain('共有設定を保存しています')
    expect(html).toContain('Dataverseへ反映しています。')
  })
})
