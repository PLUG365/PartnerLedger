import { useState } from 'react'

export type SortDirection = 'asc' | 'desc'

export type TableColumn<T> = {
  key: string
  label: string
  /** 並べ替えに使う値。無い列は並べ替えできない。 */
  sortValue?: (row: T) => string | number | undefined
  /** 絞り込みに使う値。無い列は絞り込めない。空の値は「未設定」として扱う。 */
  filterValue?: (row: T) => string | undefined
}

export type TableSort = {key: string; direction: SortDirection}
export type TableViewState = {sort?: TableSort; filters: Record<string, string[]>}
export type FilterOption = {value: string; count: number}

export const emptyFilterLabel = '未設定'

function filterValueOf<T>(column: TableColumn<T>, row: T) {
  const value = column.filterValue?.(row)?.trim()
  return value ? value : emptyFilterLabel
}

function compareValues(left: string | number | undefined, right: string | number | undefined) {
  if (typeof left === 'number' && typeof right === 'number') return left - right
  return String(left).localeCompare(String(right), 'ja', {numeric: true, sensitivity: 'base'})
}

function isEmpty(value: string | number | undefined) {
  return value === undefined || value === ''
}

/** 絞り込み（列ごとに選んだ値のどれか）→並べ替えの順に適用する。空の値は並び順によらず末尾に置く。 */
export function applyTableView<T>(rows: T[], columns: TableColumn<T>[], state: TableViewState): T[] {
  const activeFilters = columns
    .map(column => ({column, values: state.filters[column.key] ?? []}))
    .filter(({column, values}) => column.filterValue && values.length > 0)
  const filtered = activeFilters.length
    ? rows.filter(row => activeFilters.every(({column, values}) => values.includes(filterValueOf(column, row))))
    : [...rows]
  const sortColumn = state.sort ? columns.find(column => column.key === state.sort?.key && column.sortValue) : undefined
  if (!sortColumn?.sortValue || !state.sort) return filtered
  const direction = state.sort.direction === 'asc' ? 1 : -1
  const sortValue = sortColumn.sortValue
  return filtered
    .map((row, index) => ({row, index, value: sortValue(row)}))
    .sort((left, right) => {
      const leftEmpty = isEmpty(left.value)
      const rightEmpty = isEmpty(right.value)
      if (leftEmpty || rightEmpty) return leftEmpty === rightEmpty ? left.index - right.index : leftEmpty ? 1 : -1
      return compareValues(left.value, right.value) * direction || left.index - right.index
    })
    .map(entry => entry.row)
}

/** 絞り込みの候補。値ごとの件数付きで、「未設定」は最後に並べる。 */
export function filterOptions<T>(rows: T[], column: TableColumn<T>): FilterOption[] {
  if (!column.filterValue) return []
  const counts = new Map<string, number>()
  for (const row of rows) {
    const value = filterValueOf(column, row)
    counts.set(value, (counts.get(value) ?? 0) + 1)
  }
  return [...counts.entries()]
    .map(([value, count]) => ({value, count}))
    .sort((left, right) => left.value === emptyFilterLabel ? 1 : right.value === emptyFilterLabel ? -1 : left.value.localeCompare(right.value, 'ja', {numeric: true}))
}

/** 見出しのクリックで 昇順→降順→解除 と切り替える。 */
export function nextSort(current: TableSort | undefined, key: string): TableSort | undefined {
  if (current?.key !== key) return {key, direction: 'asc'}
  return current.direction === 'asc' ? {key, direction: 'desc'} : undefined
}

export function activeFilterCount(state: TableViewState) {
  return Object.values(state.filters).filter(values => values.length > 0).length
}

export function useTableView(initial: TableViewState = {filters: {}}) {
  const [state, setState] = useState<TableViewState>(initial)
  return {
    state,
    toggleSort: (key: string) => setState(current => ({...current, sort: nextSort(current.sort, key)})),
    setFilter: (key: string, values: string[]) => setState(current => ({...current, filters: {...current.filters, [key]: values}})),
    clearFilters: () => setState(current => ({...current, filters: {}})),
  }
}
