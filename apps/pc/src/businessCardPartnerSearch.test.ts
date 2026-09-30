import { describe, expect, it } from 'vitest'
import { companyCoreName, companySearchTerm, containsLiteral, createCardPartnerSearchApi, initialRegistrationChoice, isSameCompanyName } from './businessCardPartnerSearch'

describe('名刺の会社名で、見られる取引先を探す', () => {
  it('会社の種類の表記と空白を除いた部分で探す', () => {
    expect(companyCoreName('株式会社 青空ソリューションズ')).toBe('青空ソリューションズ')
    expect(companyCoreName('青空ソリューションズ（株）')).toBe('青空ソリューションズ')
    expect(companyCoreName('㈱青空')).toBe('青空')
    expect(companyCoreName('合同会社')).toBe('合同会社')
  })

  it('全角・半角と会社の種類の表記の違いを同じ会社として扱う', () => {
    expect(isSameCompanyName('ＡＢＣ株式会社', 'ABC 株式会社')).toBe(true)
    expect(isSameCompanyName('株式会社ABC', 'ABC株式会社')).toBe(true)
    expect(isSameCompanyName('ABC', 'ABCD')).toBe(false)
  })

  it('有効な取引先だけを、名前の一部一致で、引用符を逃がして問い合わせる', async () => {
    const calls: Record<string, unknown>[] = []
    const api = createCardPartnerSearchApi({getAll: async options => {
      calls.push(options ?? {})
      return {success: true, data: [
        {pl_partnerid: '11111111-1111-4111-8111-111111111111', pl_name: "O'Brien株式会社", '_pl_mainownerlookup_value@OData.Community.Display.V1.FormattedValue': '田中 健一'},
      ]} as never
    }})

    const found = await api.search("株式会社 O'Brien")

    expect(calls[0]).toMatchObject({filter: "statecode eq 0 and contains(pl_name,'O''Brien')", orderBy: ['pl_name asc'], top: 20})
    expect(found).toEqual([{id: '11111111-1111-4111-8111-111111111111', name: "O'Brien株式会社", mainOwnerName: '田中 健一'}])
  })

  it('空白で区切った語のうち、漢字・かなを含む最も長い語で探す（開発環境のE2Eで、空白入りの社名が見つからなかった）', () => {
    expect(companySearchTerm('[E2E] 0927 テスト商事 改')).toBe('テスト商事')
    expect(companySearchTerm('株式会社 青空 ソリューションズ')).toBe('ソリューションズ')
    expect(companySearchTerm('ABC Trading Co., Ltd.')).toBe('Trading')
    expect(companySearchTerm('  ')).toBe('')
  })

  it('Dataverseのcontainsがワイルドカードとして扱う文字を、文字として探せるように書き換える', () => {
    // 2026-09-27、開発環境で確認：contains はLIKEと同じく [ % _ をワイルドカードとして扱い、[x] で文字になる。
    expect(containsLiteral("A_B%C[D]O'Brien")).toBe("A[_]B[%]C[[]D]O''Brien")
  })

  it('空の検索語では問い合わせない', async () => {
    let called = false
    const api = createCardPartnerSearchApi({getAll: async () => { called = true; return {success: true, data: []} as never }})

    await expect(api.search('   ')).resolves.toEqual([])
    expect(called).toBe(false)
  })

  it('読めなかったときは候補なしと区別できるよう失敗にする', async () => {
    const api = createCardPartnerSearchApi({getAll: async () => ({success: false, data: []}) as never})
    await expect(api.search('青空')).rejects.toThrow('取引先を検索できませんでした。')
  })

  it('見られる取引先が見つかれば既存への追加を初期選択にし、同じ名前が1社だけなら選んでおく', () => {
    const same = {id: '11111111-1111-4111-8111-111111111111', name: '青空ソリューションズ株式会社'}
    const other = {id: '22222222-2222-4222-8222-222222222222', name: '青空ソリューションズ北海道'}
    expect(initialRegistrationChoice([same, other], '株式会社 青空ソリューションズ')).toEqual({mode: 'existing', partnerId: same.id})
    expect(initialRegistrationChoice([other], '株式会社 青空ソリューションズ')).toEqual({mode: 'existing', partnerId: ''})
    expect(initialRegistrationChoice([same, {...same, id: '33333333-3333-4333-8333-333333333333'}], '青空ソリューションズ')).toEqual({mode: 'existing', partnerId: ''})
  })

  it('見つからなければ新しい取引先として登録する', () => {
    expect(initialRegistrationChoice([], '青空')).toEqual({mode: 'new', partnerId: ''})
  })
})
