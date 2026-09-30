import { describe, expect, it, vi } from 'vitest'
import { applyThemePreference, readThemePreference, saveThemePreference } from './theme'

function memoryStorage(initial: Record<string, string> = {}) {
  const values = new Map(Object.entries(initial))
  return {
    getItem: vi.fn((key: string) => values.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => { values.set(key, value) }),
    removeItem: vi.fn((key: string) => { values.delete(key) }),
  }
}

describe('表示テーマの保存', () => {
  it('保存したライト・ダークを読み戻し、未保存や不正な値はシステム設定に従う', () => {
    expect(readThemePreference(memoryStorage({'partnerledger.theme': 'dark'}))).toBe('dark')
    expect(readThemePreference(memoryStorage({'partnerledger.theme': 'light'}))).toBe('light')
    expect(readThemePreference(memoryStorage({'partnerledger.theme': 'purple'}))).toBe('system')
    expect(readThemePreference(memoryStorage())).toBe('system')
    expect(readThemePreference(undefined)).toBe('system')
  })

  it('ストレージが使えなくても例外を出さず、システム設定に従う', () => {
    const blocked = {getItem: () => { throw new Error('blocked') }, setItem: () => { throw new Error('blocked') }, removeItem: () => { throw new Error('blocked') }}
    expect(readThemePreference(blocked)).toBe('system')
    expect(() => saveThemePreference('dark', blocked)).not.toThrow()
    expect(() => saveThemePreference('system', blocked)).not.toThrow()
  })

  it('システムに合わせるときは保存値を消す', () => {
    const storage = memoryStorage({'partnerledger.theme': 'dark'})
    saveThemePreference('system', storage)
    expect(storage.removeItem).toHaveBeenCalledWith('partnerledger.theme')
    saveThemePreference('light', storage)
    expect(storage.setItem).toHaveBeenCalledWith('partnerledger.theme', 'light')
  })

  it('ライト・ダークは data-theme を付け、システムは外す', () => {
    const root = {dataset: {} as Record<string, string>} as unknown as HTMLElement
    applyThemePreference('dark', root)
    expect(root.dataset.theme).toBe('dark')
    applyThemePreference('system', root)
    expect(root.dataset.theme).toBeUndefined()
  })
})
