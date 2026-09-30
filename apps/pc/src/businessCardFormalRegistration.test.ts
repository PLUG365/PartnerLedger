import { describe, expect, it, vi } from 'vitest'
import { createBusinessCardFormalRegistrationApi, type BusinessCardFormalRegistrationInvoker } from './businessCardFormalRegistration'
import type { BusinessCardReviewItem } from './businessCardReview'

const reviewId = '11111111-1111-4111-8111-111111111111'
const captureId = '22222222-2222-4222-8222-222222222222'
const inputVersionId = '33333333-3333-4333-8333-333333333333'
const ocrJobId = '44444444-4444-4444-8444-444444444444'
const ownerId = '55555555-5555-4555-8555-555555555555'
const partnerId = '66666666-6666-4666-8666-666666666666'
const contactId = '77777777-7777-4777-8777-777777777777'
const partnerKey = `BusinessCardReview-${reviewId}-V1-Partner`
const contactKey = `BusinessCardReview-${reviewId}-V1-Contact`

function item(overrides: Partial<BusinessCardReviewItem> = {}): BusinessCardReviewItem {
  return {
    reviewId,
    reviewStatus: '確認確定',
    captureId,
    captureKey: 'DEMO-R12-H-C01',
    captureStatus: '確認待ち',
    inputVersionId,
    inputVersionNumber: 1,
    inputState: '固定済み',
    imageState: '画像到着確認済み',
    ocrJobId,
    ocrJobKey: 'DEMO-R12-H-C01-V01-A01',
    ocrAttemptNumber: 1,
    ocrJobStatus: '成功',
    ocrFields: {companyName: 'OCR会社', fullName: 'OCR担当'},
    correctedFields: {
      companyName: '確認済み会社',
      fullName: '確認済み担当',
      department: '営業部',
      jobTitle: '部長',
      email: 'confirmed@example.com',
      businessPhone: '03-0000-0000',
      mobilePhone: '090-0000-0000',
      fullAddress: '東京都千代田区',
    },
    ...overrides,
  }
}

function invoker(overrides: Partial<BusinessCardFormalRegistrationInvoker> = {}): BusinessCardFormalRegistrationInvoker {
  return {
    findPartnerByRegistrationKey: vi.fn(async () => ({success: true, data: []})),
    registerPartner: vi.fn(async () => ({partnerId, isReplay: false})),
    findContactByRegistrationKey: vi.fn(async () => ({success: true, data: []})),
    registerContact: vi.fn(async () => ({contactId, isReplay: false})),
    updateCaptureStatus: vi.fn(async () => ({success: true})),
    ...overrides,
  }
}

describe('確認確定済み名刺の標準Partner／Contact正式登録境界', () => {
  it('確認値から標準登録入力を作り、Partner→Contact→Capture完了の順で1回ずつ実行する', async () => {
    const service = invoker()
    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), mainOwnerId: ownerId})

    expect(result).toEqual({success: true, partnerId, contactId, partnerReplayed: false, contactReplayed: false})
    expect(service.registerPartner).toHaveBeenCalledWith({
      name: '確認済み会社',
      address: '東京都千代田区',
      phone: '03-0000-0000',
      mainOwnerId: ownerId,
      idempotencyKey: partnerKey,
      industry: undefined,
    })
    expect(service.registerContact).toHaveBeenCalledWith({
      partnerId,
      name: '確認済み担当',
      statusCode: 100000000,
      departmentRole: '営業部 / 部長',
      email: 'confirmed@example.com',
      phone: '090-0000-0000',
      idempotencyKey: contactKey,
    })
    expect(service.updateCaptureStatus).toHaveBeenCalledWith(captureId, '登録済み')
  })

  it.each([
    ['Reviewが未確定', {reviewStatus: '確認編集中' as const}],
    ['Captureが登録済み', {captureStatus: '登録済み' as never}],
    ['OCRが成功でない', {ocrJobStatus: '成功' as const, inputState: '固定済み' as const, imageState: '画像到着確認済み' as const, correctedFields: undefined}],
  ])('%sなら書込みを行わない', async (_label, overrides) => {
    const service = invoker()
    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(overrides), mainOwnerId: ownerId})

    expect(result).toMatchObject({success: false, kind: 'blocked'})
    expect(service.registerPartner).not.toHaveBeenCalled()
    expect(service.registerContact).not.toHaveBeenCalled()
    expect(service.updateCaptureStatus).not.toHaveBeenCalled()
  })

  it('同じ要求キーで既存内容が一致する場合はCreateを増やさずCaptureだけ完了にする', async () => {
    const service = invoker({
      findPartnerByRegistrationKey: vi.fn(async () => ({success: true, data: [{
        pl_partnerid: partnerId,
        pl_registrationkey: partnerKey,
        pl_name: '確認済み会社',
        pl_address: '東京都千代田区',
        pl_phone: '03-0000-0000',
        _pl_mainownerlookup_value: ownerId,
      }]})),
      findContactByRegistrationKey: vi.fn(async () => ({success: true, data: [{
        pl_contactid: contactId,
        pl_registrationkey: contactKey,
        pl_name: '確認済み担当',
        _pl_partnerlookup_value: partnerId,
        pl_statuscode: 100000000,
        pl_departmentrole: '営業部 / 部長',
        pl_email: 'confirmed@example.com',
        pl_phone: '090-0000-0000',
      }]})),
    })

    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), mainOwnerId: ownerId})

    expect(result).toEqual({success: true, partnerId, contactId, partnerReplayed: true, contactReplayed: true})
    expect(service.registerPartner).not.toHaveBeenCalled()
    expect(service.registerContact).not.toHaveBeenCalled()
    expect(service.updateCaptureStatus).toHaveBeenCalledOnce()
  })

  it('要求キーの既存内容が異なる場合はContact・完了更新へ進まない', async () => {
    const service = invoker({
      findPartnerByRegistrationKey: vi.fn(async () => ({success: true, data: [{
        pl_partnerid: partnerId,
        pl_registrationkey: partnerKey,
        pl_name: '別会社',
        _pl_mainownerlookup_value: ownerId,
      }]})),
    })

    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), mainOwnerId: ownerId})

    expect(result).toMatchObject({success: false, kind: 'conflict'})
    expect(service.registerPartner).not.toHaveBeenCalled()
    expect(service.registerContact).not.toHaveBeenCalled()
    expect(service.updateCaptureStatus).not.toHaveBeenCalled()
  })

  it('Partnerが確定してもContactの結果不明時は部分結果として止め、Captureを登録済みにしない', async () => {
    const service = invoker({
      registerContact: vi.fn(async () => { throw new Error('network') }),
      findContactByRegistrationKey: vi.fn(async () => { throw new Error('read unavailable') }),
    })

    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), mainOwnerId: ownerId})

    expect(result).toMatchObject({success: false, kind: 'result-unknown', partnerId})
    expect(service.updateCaptureStatus).not.toHaveBeenCalled()
  })

  it('PartnerとContactが確定してもCapture更新失敗は部分成功として返す', async () => {
    const service = invoker({updateCaptureStatus: vi.fn(async () => ({success: false, error: new Error('denied')}))})

    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), mainOwnerId: ownerId})

    expect(result).toMatchObject({success: false, kind: 'partial', partnerId, contactId})
  })

  it('Reviewの確認値が無ければOCR値へフォールバックせず拒否する', async () => {
    const service = invoker()
    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item({correctedFields: undefined}), mainOwnerId: ownerId})

    expect(result).toMatchObject({success: false, kind: 'blocked'})
    expect(service.findPartnerByRegistrationKey).not.toHaveBeenCalled()
  })

  it('既存の取引先を選んだときは取引先を作らず、その取引先の担当者として登録する', async () => {
    // 2026-09-27ユーザー決定：見られる取引先に同じ会社があれば、担当者として追加できる（要件PL-029）。
    const existingPartnerId = '88888888-8888-4888-8888-888888888888'
    const service = invoker()
    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), existingPartnerId})

    expect(result).toEqual({success: true, partnerId: existingPartnerId, contactId, partnerReplayed: false, contactReplayed: false, addedToExistingPartner: true})
    expect(service.findPartnerByRegistrationKey).not.toHaveBeenCalled()
    expect(service.registerPartner).not.toHaveBeenCalled()
    expect(service.registerContact).toHaveBeenCalledWith(expect.objectContaining({partnerId: existingPartnerId, name: '確認済み担当', idempotencyKey: contactKey}))
    expect(service.updateCaptureStatus).toHaveBeenCalledWith(captureId, '登録済み')
  })

  it('既存の取引先のIDが不正なら何も書かない', async () => {
    const service = invoker()
    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), existingPartnerId: 'not-a-guid'})

    expect(result).toMatchObject({success: false, kind: 'blocked'})
    expect(service.registerContact).not.toHaveBeenCalled()
    expect(service.updateCaptureStatus).not.toHaveBeenCalled()
  })

  it('新しい取引先として登録するときは主担当が必要', async () => {
    const service = invoker()
    const result = await createBusinessCardFormalRegistrationApi(service).register({item: item(), mainOwnerId: ''})

    expect(result).toMatchObject({success: false, kind: 'blocked', message: '主担当を選択してください。'})
    expect(service.registerPartner).not.toHaveBeenCalled()
  })
})
