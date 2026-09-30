import { useId, useState, type FocusEvent, type KeyboardEvent } from 'react'
import { Building2, Monitor, Moon, Search, Sun, User, X } from 'lucide-react'
import { nameInitial } from './displayText'
import type { HeaderSearchResult } from './headerSearch'
import type { ThemePreference } from './theme'

export type HeaderUser = {fullName?: string; userPrincipalName?: string}

/** 取引先・担当者をまとめて探し、選ぶと詳細を開く。一覧を読み込み済みのデータだけで検索する。 */
export function HeaderSearch({search, onOpen, loading}: {search: (query: string) => HeaderSearchResult[]; onOpen: (result: HeaderSearchResult) => void; loading?: boolean}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [highlighted, setHighlighted] = useState(0)
  const listboxId = useId()
  const results = search(query)
  const choose = (result: HeaderSearchResult) => { onOpen(result); setQuery(''); setOpen(false) }
  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') { setOpen(false); return }
    if (!results.length) return
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      setOpen(true)
      setHighlighted(current => (current + (event.key === 'ArrowDown' ? 1 : -1) + results.length) % results.length)
    }
    if (event.key === 'Enter' && open) {
      event.preventDefault()
      choose(results[Math.min(highlighted, results.length - 1)])
    }
  }
  const showList = open && query.trim().length > 0
  return <div className="header-search">
    <Search className="header-search-icon" aria-hidden="true" strokeWidth={1.8}/>
    <input role="combobox" aria-label="取引先・担当者を検索" aria-expanded={showList} aria-controls={listboxId} aria-autocomplete="list" aria-activedescendant={showList && results.length ? `${listboxId}-${Math.min(highlighted, results.length - 1)}` : undefined} placeholder="取引先・担当者を検索" value={query} onChange={event => { setQuery(event.target.value); setHighlighted(0); setOpen(true) }} onFocus={() => setOpen(true)} onBlur={() => setOpen(false)} onKeyDown={handleKeyDown}/>
    {query && <button className="header-search-clear" type="button" aria-label="検索をクリア" onMouseDown={event => event.preventDefault()} onClick={() => setQuery('')}><X aria-hidden="true" strokeWidth={1.8}/></button>}
    {showList && <div id={listboxId} className="header-search-results" role="listbox" aria-label="検索結果">
      {results.length ? results.map((result, index) => <button id={`${listboxId}-${index}`} key={`${result.kind}-${result.id}`} type="button" role="option" aria-selected={index === highlighted} className={index === highlighted ? 'is-highlighted' : ''} onMouseDown={event => event.preventDefault()} onMouseEnter={() => setHighlighted(index)} onClick={() => choose(result)}>
        <span className="header-search-kind">{result.kind === 'partner' ? <Building2 aria-hidden="true" strokeWidth={1.8}/> : <User aria-hidden="true" strokeWidth={1.8}/>}</span>
        <span><b>{result.title}</b><small>{result.detail}</small></span>
      </button>) : <p className="header-search-empty" role="status">{loading ? '一覧を読み込んでいます…' : '該当する取引先・担当者がありません。'}</p>}
    </div>}
  </div>
}

const themeOptions: {value: ThemePreference; label: string; Icon: typeof Sun}[] = [
  {value: 'light', label: 'ライト', Icon: Sun},
  {value: 'dark', label: 'ダーク', Icon: Moon},
  {value: 'system', label: 'システムに合わせる', Icon: Monitor},
]

/** 表示テーマの切替。システム設定に合わせるのが既定。 */
export function ThemeSwitcher({value, onChange}: {value: ThemePreference; onChange: (value: ThemePreference) => void}) {
  return <div className="theme-switcher" role="group" aria-label="表示テーマ">
    {themeOptions.map(({value: option, label, Icon}) => <button key={option} type="button" aria-pressed={value === option} aria-label={label} title={label} onClick={() => onChange(option)}><Icon aria-hidden="true" strokeWidth={1.8}/></button>)}
  </div>
}

/** サインイン中の利用者。サインアウトはPower Appsの画面で行うため、ここには置かない。 */
export function UserMenu({user}: {user?: HeaderUser}) {
  const [open, setOpen] = useState(false)
  const name = user?.fullName?.trim() || user?.userPrincipalName || ''
  const closeWhenFocusLeaves = (event: FocusEvent<HTMLDivElement>) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false) }
  return <div className="user-menu" onBlur={closeWhenFocusLeaves} onKeyDown={event => { if (event.key === 'Escape') setOpen(false) }}>
    <button className="user-avatar" type="button" aria-haspopup="true" aria-expanded={open} aria-label={name ? `${name}（サインイン中）` : 'サインイン中の利用者'} title={name || undefined} onClick={() => setOpen(current => !current)}>{name ? nameInitial(name) : <User aria-hidden="true" strokeWidth={1.8}/>}</button>
    {open && <div className="user-menu-panel" role="dialog" aria-label="サインイン中の利用者">
      <span className="user-avatar user-avatar-large" aria-hidden="true">{name ? nameInitial(name) : <User strokeWidth={1.8}/>}</span>
      <div><b>{user?.fullName || '名前を取得できません'}</b>{user?.userPrincipalName && <small>{user.userPrincipalName}</small>}</div>
    </div>}
  </div>
}
