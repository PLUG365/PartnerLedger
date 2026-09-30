import { describe, expect, it } from 'vitest'
import { boundId, lookupByKey, withCreatedRow } from './createdRecordId'

const id = '11111111-1111-4111-8111-111111111111'
const noWait = {sleep: async () => {}}

describe('作成結果にIDが返らないときの読み直し（Power Appsモバイルアプリ対策、2026-09-27）', () => {
  it('作成結果にIDがあれば、読み直さない', async () => {
    let called = false
    const result = await withCreatedRow({success: true, data: {pl_partnerid: id}}, 'pl_partnerid', async () => { called = true; return {success: true, data: []} }, noWait)
    expect(result.data).toEqual({pl_partnerid: id})
    expect(called).toBe(false)
  })

  it('作成が成功したのにIDが無ければ、キーで読み直した1行のIDで補う', async () => {
    const result = await withCreatedRow({success: true, data: undefined}, 'pl_partnerid', async () => ({success: true, data: [{pl_partnerid: id}]}), noWait)
    expect(result).toEqual({success: true, data: {pl_partnerid: id}})
  })

  it('起動直後で読み直しに結果が返らないときは、少し待って読み直す', async () => {
    // 起動直後のモバイルアプリでは、読み取りも結果なし（data無し）で返ることがある。
    const answers = [{success: true, data: undefined}, {success: true, data: []}, {success: true, data: [{pl_partnerid: id}]}]
    const waits: number[] = []
    const result = await withCreatedRow({success: true, data: undefined}, 'pl_partnerid', async () => answers.shift() as never, {delays: [0, 10, 20, 30], sleep: async ms => { waits.push(ms) }})
    expect(result.data).toEqual({pl_partnerid: id})
    expect(waits).toEqual([10, 20])
  })

  it('読み直しを終えても見つからない・複数行・失敗なら、元の結果のまま返す（呼び出し側が結果不明として扱う）', async () => {
    const empty = await withCreatedRow({success: true, data: undefined}, 'pl_partnerid', async () => ({success: true, data: []}), noWait)
    expect(empty).toEqual({success: true, data: undefined})
    let manyCalls = 0
    const many = await withCreatedRow({success: true, data: undefined}, 'pl_partnerid', async () => { manyCalls += 1; return {success: true, data: [{pl_partnerid: id}, {pl_partnerid: '22222222-2222-4222-8222-222222222222'}]} }, noWait)
    expect(many.data).toBeUndefined()
    expect(manyCalls).toBe(1)
    const failed = await withCreatedRow({success: true, data: undefined}, 'pl_partnerid', async () => ({success: false, data: []}), noWait)
    expect(failed.data).toBeUndefined()
    const thrown = await withCreatedRow({success: true, data: undefined}, 'pl_partnerid', async () => { throw new Error('network') }, noWait)
    expect(thrown.data).toBeUndefined()
  })

  it('作成が失敗したときは読み直さない', async () => {
    let called = false
    const result = await withCreatedRow({success: false, data: undefined, error: new Error('denied')}, 'pl_partnerid', async () => { called = true; return {success: true, data: [{pl_partnerid: id}]} }, noWait)
    expect(result.success).toBe(false)
    expect(called).toBe(false)
  })

  it('キーの問い合わせは値の引用符を逃がし、キーが無ければ問い合わせない', async () => {
    const calls: Record<string, unknown>[] = []
    const getAll = async (options?: Record<string, unknown>) => { calls.push(options ?? {}); return {success: true, data: []} }
    await lookupByKey(getAll, 'pl_partnerid', 'pl_registrationkey', "O'Key")()
    expect(calls[0]).toEqual({select: ['pl_partnerid'], filter: "pl_registrationkey eq 'O''Key'", top: 2})
    const none = await lookupByKey(getAll, 'pl_partnerid', 'pl_registrationkey', undefined)()
    expect(none).toEqual({success: true, data: []})
    expect(calls).toHaveLength(1)
  })

  it('odata.bindの値から行のIDを取り出す', () => {
    expect(boundId(`/pl_requests(${id})`)).toBe(id)
    expect(boundId('not-a-bind')).toBeUndefined()
  })
})
