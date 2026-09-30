import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { ArrowDown, ArrowUp, ArrowUpDown, ListFilter, RotateCw, Search, X } from 'lucide-react'
import { filterOptions, type TableColumn, type TableViewState } from './tableView'

type HeaderProps<T> = {
  column: TableColumn<T>
  rows: T[]
  state: TableViewState
  onSort: (key: string) => void
  onFilter: (key: string, values: string[]) => void
  className?: string
}

function ariaSort<T>(column: TableColumn<T>, state: TableViewState) {
  if (!column.sortValue) return undefined
  if (state.sort?.key !== column.key) return 'none' as const
  return state.sort.direction === 'asc' ? 'ascending' as const : 'descending' as const
}

/** 並べ替え（見出しのクリック）と絞り込み（値の選択）を持つ列見出し。 */
export function ColumnHeader<T>({column, rows, state, onSort, onFilter, className}: HeaderProps<T>) {
  const sorted = state.sort?.key === column.key ? state.sort.direction : undefined
  const selected = state.filters[column.key] ?? []
  const SortIcon = sorted === 'asc' ? ArrowUp : sorted === 'desc' ? ArrowDown : ArrowUpDown
  return <th className={className} aria-sort={ariaSort(column, state)}>
    <div className="column-header">
      {column.sortValue ? <button className={'column-sort' + (sorted ? ' is-active' : '')} type="button" title={`${column.label}で並べ替え`} onClick={() => onSort(column.key)}>{column.label}<SortIcon className="column-icon" aria-hidden="true" strokeWidth={1.8}/></button> : <span>{column.label}</span>}
      {column.filterValue && <ColumnFilter column={column} rows={rows} selected={selected} onChange={values => onFilter(column.key, values)}/>}
    </div>
  </th>
}

function ColumnFilter<T>({column, rows, selected, onChange}: {column: TableColumn<T>; rows: T[]; selected: string[]; onChange: (values: string[]) => void}) {
  const [position, setPosition] = useState<CSSProperties>()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const open = Boolean(position)
  const options = filterOptions(rows, column)

  useEffect(() => {
    if (!open) return
    const close = (event: Event) => {
      const target = event.target as Node | null
      if (event.type === 'keydown' && (event as KeyboardEvent).key !== 'Escape') return
      if (event.type === 'mousedown' && (panelRef.current?.contains(target) || buttonRef.current?.contains(target))) return
      setPosition(undefined)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    window.addEventListener('resize', close)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', close)
      window.removeEventListener('resize', close)
    }
  }, [open])

  const toggle = () => {
    if (open) { setPosition(undefined); return }
    const rect = buttonRef.current?.getBoundingClientRect()
    if (!rect) return
    // 表の中はスクロール領域なので、切れないよう画面基準で位置を決める。
    setPosition({top: rect.bottom + 6, left: Math.max(8, Math.min(rect.left, window.innerWidth - 268))})
  }
  const toggleValue = (value: string) => onChange(selected.includes(value) ? selected.filter(item => item !== value) : [...selected, value])

  return <>
    <button ref={buttonRef} className={'column-filter' + (selected.length ? ' is-active' : '')} type="button" aria-label={`${column.label}で絞り込み${selected.length ? `（${selected.length}件選択中）` : ''}`} aria-expanded={open} aria-haspopup="dialog" title={`${column.label}で絞り込み`} onClick={toggle}><ListFilter className="column-icon" aria-hidden="true" strokeWidth={1.8}/></button>
    {position && <div ref={panelRef} className="column-filter-panel" style={position} role="dialog" aria-label={`${column.label}で絞り込み`}>
      <div className="column-filter-options">
        {options.map(option => <label key={option.value}><input type="checkbox" checked={selected.includes(option.value)} onChange={() => toggleValue(option.value)}/><span>{option.value}</span><small>{option.count}</small></label>)}
        {!options.length && <p className="empty-state">候補がありません。</p>}
      </div>
      <div className="column-filter-actions"><button className="secondary-button" type="button" disabled={!selected.length} onClick={() => onChange([])}>解除</button><button className="primary-button" type="button" onClick={() => setPosition(undefined)}>閉じる</button></div>
    </div>}
  </>
}

/** 絞り込み中であることと、その解除を一覧の見出しに出す。 */
export function FilterSummary({count, total, activeFilters, onClear}: {count: number; total: number; activeFilters: number; onClear: () => void}) {
  if (!activeFilters) return null
  return <span className="filter-summary" role="status">{count} / {total}件<button type="button" aria-label="絞り込みをすべて解除" title="絞り込みをすべて解除" onClick={onClear}><X aria-hidden="true" strokeWidth={1.8}/>解除</button></span>
}

export function RefreshButton({onClick, disabled}: {onClick: () => void; disabled?: boolean}) {
  return <button className="secondary-button refresh-button" type="button" aria-label="再読み込み" title="再読み込み" onClick={onClick} disabled={disabled}><RotateCw className="nav-icon" aria-hidden="true" strokeWidth={1.8}/><span className="refresh-label">再読み込み</span></button>
}

export function SearchField({label, placeholder, value, onChange}: {label: string; placeholder: string; value: string; onChange: (value: string) => void}) {
  return <label className="search-field"><Search className="field-icon" aria-hidden="true" strokeWidth={1.8}/><input aria-label={label} placeholder={placeholder} value={value} onChange={event => onChange(event.target.value)}/>{value && <button aria-label="検索をクリア" type="button" onClick={() => onChange('')}><X className="nav-icon" aria-hidden="true" strokeWidth={1.8}/></button>}</label>
}
