import type { IOperationResult } from '@microsoft/power-apps/data'

type Row = Record<string, unknown>

export type CreatedRowLookup = () => Promise<IOperationResult<unknown[]>>

export type KeyQuery = {select: string[]; filter: string; top: number; orderBy?: string[]}

export type RowGetAll = (options: KeyQuery) => Promise<IOperationResult<unknown[]>>

export type CreatedRowOptions = {
  /** 読み直しの前に待つ時間（ミリ秒）。1つ目が1回目。 */
  delays?: number[]
  sleep?: (milliseconds: number) => Promise<void>
}

// 起動直後のPower Appsモバイルアプリでは、しばらく作成・読み取りとも結果が返らない（2026-09-27、開発環境で観察）。
// 少し待って読み直す。合計で約5秒。
const defaultDelays = [0, 700, 1500, 3000]
const defaultSleep = (milliseconds: number) => new Promise<void>(resolve => setTimeout(resolve, milliseconds))

/**
 * 作成が成功したのに作成結果（主キー）が返らないとき、アプリが付けた一意のキーで作った行を読み直して補う。
 * Power Appsのモバイルアプリ（iPhone）では、作成はサーバーで成功しても結果が返らないことがある（2026-09-27、開発環境で確認）。
 * 見つからなければ少し待って読み直す。ちょうど1行見つかったときだけ補い、複数行なら直ちに、見つからないまま
 * 読み直しを終えたら元の結果のまま返す（呼び出し側が従来どおり「結果不明」として扱う）。
 */
export async function withCreatedRow<T>(result: IOperationResult<T>, primaryKey: string, lookup: CreatedRowLookup, options: CreatedRowOptions = {}): Promise<IOperationResult<T>> {
  if (!result.success) return result
  const data = result.data !== null && typeof result.data === 'object' ? result.data as unknown as Row : undefined
  const existing = data?.[primaryKey]
  if (typeof existing === 'string' && existing) return result
  const sleep = options.sleep ?? defaultSleep
  for (const delay of options.delays ?? defaultDelays) {
    if (delay > 0) await sleep(delay)
    let found: IOperationResult<unknown[]>
    try {
      found = await lookup()
    } catch {
      continue
    }
    if (!found.success || !Array.isArray(found.data)) continue
    const rows = (found.data as Row[]).filter(row => row !== null && typeof row === 'object' && typeof row[primaryKey] === 'string' && row[primaryKey])
    if (rows.length === 1) return {...result, data: {...(data ?? {}), ...rows[0]} as unknown as T}
    if (rows.length > 1) break
  }
  return result
}

/** 一意のキー列で1行を探す問い合わせ。キーが無ければ問い合わせない。 */
export function lookupByKey(getAll: RowGetAll, primaryKey: string, keyField: string, keyValue: unknown): CreatedRowLookup {
  return async () => {
    if (typeof keyValue !== 'string' || !keyValue) return {success: true, data: []}
    return getAll({select: [primaryKey], filter: `${keyField} eq '${keyValue.replace(/'/g, "''")}'`, top: 2})
  }
}

/** `/pl_requests(<id>)` のようなodata.bindの値から行のIDを取り出す。 */
export function boundId(bind: unknown): string | undefined {
  const match = typeof bind === 'string' ? bind.match(/\(([0-9a-fA-F-]{36})\)$/) : null
  return match ? match[1] : undefined
}
