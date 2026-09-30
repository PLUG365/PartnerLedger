import { describe, expect, it } from 'vitest'
import { activeFilterCount, applyTableView, emptyFilterLabel, filterOptions, nextSort, type TableColumn } from './tableView'

type Row = {id: string; name: string; status: string; owner?: string; order: number}
const rows: Row[] = [
  {id: 'a', name: '株式会社いろは', status: '取引中', owner: '田中', order: 1},
  {id: 'b', name: 'ABC商事', status: '未承認', order: 0},
  {id: 'c', name: '青空', status: '取引中', owner: '佐伯', order: 1},
  {id: 'd', name: '10番目の会社', status: '休止中', owner: '田中', order: 2},
  {id: 'e', name: '2番目の会社', status: '取引中', owner: '', order: 1},
]
const columns: TableColumn<Row>[] = [
  {key: 'name', label: '取引先', sortValue: row => row.name},
  {key: 'status', label: '取引状態', sortValue: row => row.order, filterValue: row => row.status},
  {key: 'owner', label: '主担当', sortValue: row => row.owner, filterValue: row => row.owner},
]

describe('一覧の並べ替え・絞り込み', () => {
  it('見出しのクリックで昇順→降順→解除と切り替わる', () => {
    const first = nextSort(undefined, 'name')
    expect(first).toEqual({key: 'name', direction: 'asc'})
    const second = nextSort(first, 'name')
    expect(second).toEqual({key: 'name', direction: 'desc'})
    expect(nextSort(second, 'name')).toBeUndefined()
    expect(nextSort(second, 'owner')).toEqual({key: 'owner', direction: 'asc'})
  })

  it('数値の並びを考慮して並べ、同じ値は元の順を保つ', () => {
    const byName = applyTableView(rows, columns, {sort: {key: 'name', direction: 'asc'}, filters: {}})
    expect(byName.map(row => row.id).slice(0, 2)).toEqual(['e', 'd'])
    const byStatus = applyTableView(rows, columns, {sort: {key: 'status', direction: 'asc'}, filters: {}})
    expect(byStatus.map(row => row.id)).toEqual(['b', 'a', 'c', 'e', 'd'])
    const byStatusDesc = applyTableView(rows, columns, {sort: {key: 'status', direction: 'desc'}, filters: {}})
    expect(byStatusDesc.map(row => row.id)).toEqual(['d', 'a', 'c', 'e', 'b'])
  })

  it('空の値は昇順でも降順でも末尾に置く', () => {
    expect(applyTableView(rows, columns, {sort: {key: 'owner', direction: 'asc'}, filters: {}}).map(row => row.id).slice(-2)).toEqual(['b', 'e'])
    expect(applyTableView(rows, columns, {sort: {key: 'owner', direction: 'desc'}, filters: {}}).map(row => row.id).slice(-2)).toEqual(['b', 'e'])
  })

  it('列ごとに選んだ値のどれかに一致する行だけを残し、列どうしは両方を満たす', () => {
    expect(applyTableView(rows, columns, {filters: {status: ['取引中', '休止中']}}).map(row => row.id)).toEqual(['a', 'c', 'd', 'e'])
    expect(applyTableView(rows, columns, {filters: {status: ['取引中'], owner: ['田中']}}).map(row => row.id)).toEqual(['a'])
    expect(applyTableView(rows, columns, {filters: {owner: [emptyFilterLabel]}}).map(row => row.id)).toEqual(['b', 'e'])
    expect(applyTableView(rows, columns, {filters: {status: []}})).toHaveLength(5)
  })

  it('絞り込みの候補を件数付きで返し、未設定は最後に置く', () => {
    expect(filterOptions(rows, columns[2])).toEqual([{value: '佐伯', count: 1}, {value: '田中', count: 2}, {value: emptyFilterLabel, count: 2}])
    expect(filterOptions(rows, columns[0])).toEqual([])
    expect(activeFilterCount({filters: {status: ['取引中'], owner: []}})).toBe(1)
  })
})
