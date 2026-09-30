import { describe, expect, it } from 'vitest'
import { searchDirectory } from './headerSearch'

const partners = [
  {id: 'p1', name: '青空ソリューションズ株式会社', industry: 'IT', tradingStatus: '取引中' as const, aclVersion: 1},
  {id: 'p2', name: 'ＡＢＣ商事', industry: '卸売', tradingStatus: '未承認' as const, aclVersion: 1},
]
const contacts = [
  {id: 'c1', name: '井上 真帆', partnerId: 'p1', partnerName: '青空ソリューションズ株式会社', departmentRole: '推進部', status: '在籍' as const},
]

describe('ヘッダーの検索', () => {
  it('取引先を先に、担当者を後に返し、担当者は所属先の名前でも見つかる', () => {
    const results = searchDirectory(partners, contacts, '青空')
    expect(results.map(result => `${result.kind}:${result.id}`)).toEqual(['partner:p1', 'contact:c1'])
    expect(results[1].detail).toBe('担当者 · 青空ソリューションズ株式会社')
  })

  it('全角半角と空白の違いを無視する', () => {
    expect(searchDirectory(partners, contacts, 'abc').map(result => result.id)).toEqual(['p2'])
    expect(searchDirectory(partners, contacts, '井上真帆').map(result => result.id)).toEqual(['c1'])
  })

  it('空の検索語では何も返さず、件数の上限を守る', () => {
    expect(searchDirectory(partners, contacts, '  ')).toEqual([])
    expect(searchDirectory(partners, contacts, '株式会社', 1)).toHaveLength(1)
  })
})
