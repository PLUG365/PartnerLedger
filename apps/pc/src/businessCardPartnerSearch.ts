import type { IOperationResult } from '@microsoft/power-apps/data'

export type CardPartnerCandidate = {id: string; name: string; mainOwnerName?: string}

export type CardPartnerSearchApi = {
  search: (text: string) => Promise<CardPartnerCandidate[]>
}

type PartnerSearchRecord = {
  pl_partnerid?: unknown
  pl_name?: unknown
  '_pl_mainownerlookup_value@OData.Community.Display.V1.FormattedValue'?: unknown
}

export type StandardCardPartnerSearchService = {
  getAll: (options?: Record<string, unknown>) => Promise<IOperationResult<PartnerSearchRecord[]>>
}

// 会社の種類の表記（前後どちらに付いても）を除いて比べる。NFKCで全角・半角と㈱などをそろえる。
const corporateMarkers = /(株式会社|有限会社|合同会社|合資会社|合名会社|一般社団法人|一般財団法人|\(株\)|\(有\))/g

export function companyCoreName(value: string): string {
  const normalized = value.normalize('NFKC').replace(/\s+/g, '')
  const core = normalized.replace(corporateMarkers, '')
  return core || normalized
}

export function isSameCompanyName(left: string, right: string): boolean {
  return companyCoreName(left).toLowerCase() === companyCoreName(right).toLowerCase()
}

const englishCorporateMarkers = /(Co\.,?\s*Ltd\.?|Co\.|Ltd\.|Inc\.|LLC|Corp\.|Corporation|K\.K\.)/gi
// ひらがな・カタカナ、漢字、半角カタカナ。
const japaneseCharacters = /[぀-ヿ㐀-鿿ｦ-ﾟ]/

/**
 * 検索に使う語。会社の種類の表記を除き、空白で区切った語のうち、漢字・かなを含む最も長い語を使う
 * （無ければ最も長い語）。空白の入り方が名刺と登録済みの名前で違っても見つかるようにするため
 * （2026-09-27、開発環境のE2Eで空白入りの社名が見つからなかった）。候補を選ぶのは人なので、広めに探す。
 */
export function companySearchTerm(value: string): string {
  const tokens = value.normalize('NFKC').replace(corporateMarkers, ' ').replace(englishCorporateMarkers, ' ').split(/\s+/).filter(Boolean)
  const preferred = tokens.filter(token => japaneseCharacters.test(token))
  const pool = preferred.length ? preferred : tokens
  return pool.reduce((longest, token) => (token.length > longest.length ? token : longest), '')
}

/**
 * Dataverseのcontainsは、SQLのLIKEと同じく [ % _ をワイルドカードとして扱う（2026-09-27、開発環境で確認）。
 * [x] と書くと文字として扱われる。OData文字列の引用符も逃がす。
 */
export function containsLiteral(value: string): string {
  return value.replace(/[[%_]/g, match => `[${match}]`).replace(/'/g, "''")
}

/**
 * 名刺の確認画面を開いたときの登録先の初期選択。見られる取引先が見つかれば既存への追加を選び、
 * 名前が同じ取引先がちょうど1社なら、それを選んでおく（2026-09-27、ユーザー決定）。
 */
export function initialRegistrationChoice(candidates: CardPartnerCandidate[], companyName: string): {mode: 'existing' | 'new'; partnerId: string} {
  if (candidates.length === 0) return {mode: 'new', partnerId: ''}
  const same = candidates.filter(candidate => isSameCompanyName(candidate.name, companyName))
  return {mode: 'existing', partnerId: same.length === 1 ? same[0].id : ''}
}

/**
 * 名刺の会社名で、利用者が見られる有効な取引先を探す（2026-09-27、要件PL-029）。
 * 結果はDataverseの権限で絞られるため、見られない会社は候補に出ない（要件PL-032）。自動でまとめはしない（PL-006）。
 */
export function createCardPartnerSearchApi(service: StandardCardPartnerSearchService): CardPartnerSearchApi {
  return {
    search: async text => {
      const term = companySearchTerm(text)
      if (!term) return []
      let result: IOperationResult<PartnerSearchRecord[]>
      try {
        result = await service.getAll({
          select: ['pl_partnerid', 'pl_name', '_pl_mainownerlookup_value'],
          filter: `statecode eq 0 and contains(pl_name,'${containsLiteral(term)}')`,
          orderBy: ['pl_name asc'],
          top: 20,
        })
      } catch (error) {
        throw new Error('取引先を検索できませんでした。', {cause: error})
      }
      if (!result.success || !Array.isArray(result.data)) throw new Error('取引先を検索できませんでした。')
      return result.data
        .filter(record => typeof record.pl_partnerid === 'string' && typeof record.pl_name === 'string')
        .map(record => {
          const owner = record['_pl_mainownerlookup_value@OData.Community.Display.V1.FormattedValue']
          return {id: record.pl_partnerid as string, name: record.pl_name as string, ...(typeof owner === 'string' && owner ? {mainOwnerName: owner} : {})}
        })
    },
  }
}
