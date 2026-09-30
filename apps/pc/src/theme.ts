export type ThemePreference = 'light' | 'dark' | 'system'

const storageKey = 'partnerledger.theme'

type ThemeStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

function defaultStorage(): ThemeStorage | undefined {
  try {
    return typeof window === 'undefined' ? undefined : window.localStorage
  } catch {
    // アプリはPower Appsの別オリジンiframeで動くため、ストレージ自体が使えないことがある。
    return undefined
  }
}

/** 保存された表示テーマ。読めない・値が不正なときはシステム設定に従う。 */
export function readThemePreference(storage: ThemeStorage | undefined = defaultStorage()): ThemePreference {
  try {
    const value = storage?.getItem(storageKey)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

export function saveThemePreference(preference: ThemePreference, storage: ThemeStorage | undefined = defaultStorage()) {
  try {
    if (preference === 'system') storage?.removeItem(storageKey)
    else storage?.setItem(storageKey, preference)
  } catch {
    // 保存できなくても、今の画面には反映済みなので何もしない。
  }
}

export function applyThemePreference(preference: ThemePreference, root: HTMLElement | undefined = typeof document === 'undefined' ? undefined : document.documentElement) {
  if (!root) return
  if (preference === 'system') delete root.dataset.theme
  else root.dataset.theme = preference
}
