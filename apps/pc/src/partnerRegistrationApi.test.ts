import { describe, expect, it } from 'vitest'
import { createPartnerLedgerRegistrationApi, PartnerRegistrationApiError } from './partnerRegistrationApi'

const ownerId = 'a1b2c3d4-0000-4000-8000-000000000001'
const partnerId = '9e60c161-88af-f111-aaac-e4fb1eff79c7'

describe('新規取引先登録APIのPC境界', () => {
  it('入力6項目を登録APIへ送り、成功結果を正規化する', async () => {
    const calls: unknown[] = []
    const api = createPartnerLedgerRegistrationApi({
      registerPartner: async request => {
        calls.push(request)
        return {success: true, data: {Success: true, PartnerId: partnerId, IsReplay: false}}
      },
    })

    const result = await api.registerPartner({name: '  テスト会社  ', industry: 'IT', address: '東京都', phone: '03-0000-0000', mainOwnerId: ownerId, idempotencyKey: 'register-1'})

    expect(result).toEqual({partnerId, isReplay: false})
    expect(calls).toEqual([{name: 'テスト会社', industry: 'IT', address: '東京都', phone: '03-0000-0000', mainOwnerId: ownerId, idempotencyKey: 'register-1'}])
  })

  it('冪等キーを自動生成し、通信失敗は再試行可能な未確定エラーにする', async () => {
    let receivedKey = ''
    const api = createPartnerLedgerRegistrationApi({
      registerPartner: async request => { receivedKey = request.idempotencyKey; return {success: false, data: {}} },
    })

    await expect(api.registerPartner({name: 'テスト会社', mainOwnerId: ownerId})).rejects.toMatchObject({code: 'transport-error'})
    expect(receivedKey).toMatch(/^RegisterPartner-/)
  })

  it('主担当に共有できないとサーバーが拒否したときは、再試行を促さずに主担当を選び直させる', async () => {
    // 2026-09-28：登録時の初期共有で、サーバーが主担当を先に検査して拒否する（独立監査#2）。
    const api = createPartnerLedgerRegistrationApi({
      registerPartner: async () => ({success: false, data: {}, error: {status: 400, message: '主担当に選んだユーザーには共有できません（無効なユーザーや、Microsoftのサポート用アカウントなど）。主担当には通常の利用者を選んでください。'}}),
    })

    await expect(api.registerPartner({name: 'テスト会社', mainOwnerId: ownerId})).rejects.toMatchObject({
      code: 'main-owner-unavailable',
      message: '選択した主担当を利用できないため、登録できません。',
    })
  })

  it('サーバー拒否を、画面が分類できるエラーにする', async () => {
    const api = createPartnerLedgerRegistrationApi({
      registerPartner: async () => ({success: true, data: {Success: false, ErrorCode: 'duplicate-visible'}}),
    })

    await expect(api.registerPartner({name: 'テスト会社', mainOwnerId: ownerId})).rejects.toMatchObject({code: 'duplicate-visible', message: '同じ会社名の取引先が見つかったため、登録を中止しました。'})
  })

  it('クライアント権威項目を入力モデルへ持たず、GUIDと上限を検証する', async () => {
    const api = createPartnerLedgerRegistrationApi({registerPartner: async () => ({success: true, data: {Success: true, PartnerId: partnerId}})})
    await expect(api.registerPartner({name: '', mainOwnerId: ownerId})).rejects.toMatchObject({code: 'required'})
    await expect(api.registerPartner({name: 'テスト会社', mainOwnerId: 'not-guid'})).rejects.toMatchObject({code: 'invalid-guid'})
    await expect(api.registerPartner({name: 'テスト会社', mainOwnerId: ownerId, address: 'あ'.repeat(501)})).rejects.toMatchObject({code: 'too-long'})
  })

  it('不正な成功応答を受け入れない', async () => {
    const api = createPartnerLedgerRegistrationApi({registerPartner: async () => ({success: true, data: {Success: true, PartnerId: '00000000-0000-0000-0000-000000000000'}})})
    await expect(api.registerPartner({name: 'テスト会社', mainOwnerId: ownerId})).rejects.toBeInstanceOf(PartnerRegistrationApiError)
  })
})
