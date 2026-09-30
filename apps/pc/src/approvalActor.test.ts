import { describe, expect, it, vi } from 'vitest'
import { administratorFilter, approvalAdministratorRoleNames, approvalCancellationAction, loadApprovalActor, selfFilter, type ApprovalActor } from './approvalActor'

const me = '11111111-1111-4111-8111-111111111111'
const someoneElse = '22222222-2222-4222-8222-222222222222'
const objectId = '33333333-3333-4333-8333-333333333333'
const user: ApprovalActor = {systemUserId: me, isAdministrator: false}
const admin: ApprovalActor = {systemUserId: me, isAdministrator: true}

describe('取消・取下げボタンの表示判定', () => {
  it('申請者本人は下書き・差戻しを理由なしで取り下げられ、提出中には何も出ない', () => {
    expect(approvalCancellationAction({status: '下書き', requestingUserId: me}, user)).toEqual({kind: 'withdraw', reasonRequired: false})
    expect(approvalCancellationAction({status: '差戻し', requestingUserId: me.toUpperCase()}, user)).toEqual({kind: 'withdraw', reasonRequired: false})
    expect(approvalCancellationAction({status: '提出中', requestingUserId: me}, user)).toBeUndefined()
  })

  it('他人の申請には、管理者でなければ何も出さない', () => {
    for (const status of ['下書き', '差戻し', '提出中', '承認済み']) {
      expect(approvalCancellationAction({status, requestingUserId: someoneElse}, user)).toBeUndefined()
    }
  })

  it('管理者は提出中を取り消せ、申請者不在の下書き・差戻しは理由付きで取り下げられる', () => {
    expect(approvalCancellationAction({status: '提出中', requestingUserId: someoneElse}, admin)).toEqual({kind: 'administrator-cancel', reasonRequired: true})
    expect(approvalCancellationAction({status: '提出中', requestingUserId: me}, admin)).toEqual({kind: 'administrator-cancel', reasonRequired: true})
    expect(approvalCancellationAction({status: '下書き', requestingUserId: someoneElse}, admin)).toEqual({kind: 'administrator-withdraw', reasonRequired: true})
    expect(approvalCancellationAction({status: '差戻し', requestingUserId: me}, admin)).toEqual({kind: 'withdraw', reasonRequired: false})
  })

  it('判断済み・反映系・取消済みには誰にも出さない。本人が分からなければ何も出さない', () => {
    for (const status of ['承認済み', '却下', '反映待ち', '反映失敗', '反映済み', '取消']) {
      expect(approvalCancellationAction({status, requestingUserId: me}, admin)).toBeUndefined()
    }
    expect(approvalCancellationAction({status: '下書き', requestingUserId: me}, undefined)).toBeUndefined()
    expect(approvalCancellationAction({status: '下書き', requestingUserId: undefined}, user)).toBeUndefined()
  })
})

describe('操作している本人の読込', () => {
  it('管理者ロールの名前はサーバーの照合と同じ3つに固定する', () => {
    expect([...approvalAdministratorRoleNames]).toEqual(['PL システム管理', 'システム管理者', 'System Administrator'])
    expect(administratorFilter(me)).toBe(`systemuserid eq ${me} and systemuserroles_association/any(r:r/name eq 'PL システム管理' or r/name eq 'システム管理者' or r/name eq 'System Administrator')`)
    expect(selfFilter(objectId)).toBe(`azureactivedirectoryobjectid eq ${objectId}`)
  })

  it('本人のIDと、直接割り当てた管理者ロールの有無を読む', async () => {
    const query = vi.fn(async (filter: string) => filter.startsWith('azureactivedirectoryobjectid')
      ? {success: true, data: [{systemuserid: me}]}
      : {success: true, data: [{systemuserid: me}]})
    await expect(loadApprovalActor(objectId, query)).resolves.toEqual({systemUserId: me, isAdministrator: true})
    expect(query).toHaveBeenNthCalledWith(1, selfFilter(objectId))
    expect(query).toHaveBeenNthCalledWith(2, administratorFilter(me))
  })

  it('管理者ロールが無い、または確認に失敗したら管理者ではない扱いにする', async () => {
    const none = vi.fn(async (filter: string) => filter.startsWith('azureactivedirectoryobjectid') ? {success: true, data: [{systemuserid: me}]} : {success: true, data: []})
    await expect(loadApprovalActor(objectId, none)).resolves.toEqual({systemUserId: me, isAdministrator: false})
    const failed = vi.fn(async (filter: string) => filter.startsWith('azureactivedirectoryobjectid') ? {success: true, data: [{systemuserid: me}]} : {success: false})
    await expect(loadApprovalActor(objectId, failed)).resolves.toEqual({systemUserId: me, isAdministrator: false})
    const thrown = vi.fn(async (filter: string) => {
      if (filter.startsWith('azureactivedirectoryobjectid')) return {success: true, data: [{systemuserid: me}]}
      throw new Error('missing prvReadRole')
    })
    await expect(loadApprovalActor(objectId, thrown)).resolves.toEqual({systemUserId: me, isAdministrator: false})
  })

  it('本人を1人に特定できなければ何も返さず、ID以外の値でクエリを組み立てない', async () => {
    const query = vi.fn(async () => ({success: true, data: [] as {systemuserid?: string}[]}))
    await expect(loadApprovalActor(objectId, query)).resolves.toBeUndefined()
    await expect(loadApprovalActor(objectId, async () => ({success: true, data: [{systemuserid: me}, {systemuserid: someoneElse}]}))).resolves.toBeUndefined()
    await expect(loadApprovalActor(objectId, async () => { throw new Error('offline') })).resolves.toBeUndefined()
    const injected = vi.fn(async () => ({success: true, data: [{systemuserid: me}]}))
    await expect(loadApprovalActor("x' or true", injected)).resolves.toBeUndefined()
    await expect(loadApprovalActor(undefined, injected)).resolves.toBeUndefined()
    expect(injected).not.toHaveBeenCalled()
  })
})
