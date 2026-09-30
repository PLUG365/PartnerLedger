import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { BusinessCardReviewContent, ExistingPartnerPicker } from './DataverseBusinessCardReview'
import type { BusinessCardReviewItem } from './businessCardReview'

const item: BusinessCardReviewItem = {
  reviewId: '11111111-1111-4111-8111-111111111111',
  reviewName: 'OCR review',
  reviewStatus: '確認編集中',
  reviewedAt: '2026-09-18T03:00:00Z',
  captureId: '22222222-2222-4222-8222-222222222222',
  captureKey: 'R12-E-REVIEW-01',
  captureStatus: '確認待ち',
  inputVersionId: '33333333-3333-4333-8333-333333333333',
  inputVersionNumber: 1,
  inputState: '固定済み',
  imageState: '画像到着確認済み',
  ocrJobId: '44444444-4444-4444-8444-444444444444',
  ocrJobKey: 'R12-E-OCR-01',
  ocrAttemptNumber: 1,
  ocrJobStatus: '成功',
  ocrFields: {companyName: 'デモ商事', fullName: '山田 太郎', email: 'taro@example.invalid'},
  correctedFields: {fullName: '山田 太郎（確認済み）'},
}

describe('名刺の確認画面', () => {
  it('読み取り結果と確定した内容を表示し、確定操作が無ければ書込み操作を出さない', () => {
    const html = renderToStaticMarkup(<BusinessCardReviewContent state={{status: 'ready', item}} onRetry={() => {}} onClose={() => {}}/>)
    for (const text of ['名刺の内容を確認', '読み取り結果', '確定した内容', 'デモ商事', '山田 太郎（確認済み）']) expect(html).toContain(text)
    for (const text of ['>確定する<', '>登録する<', 'pl_ocrresultjson', 'Dataverse', '取込キー', item.captureKey, item.reviewId]) expect(html).not.toContain(text)
  })

  it('取得失敗時はエラーと再読み込みだけを表示する', () => {
    const html = renderToStaticMarkup(<BusinessCardReviewContent state={{status: 'error', message: '取得失敗'}} onRetry={() => {}} onClose={() => {}}/>)
    expect(html).toContain('取得失敗')
    expect(html).toContain('再読み込み')
    expect(html).not.toContain('読み取り結果')
    expect(html).not.toContain('登録する')
  })

  it('確認編集中は修正できる確定フォームを表示する', () => {
    const html = renderToStaticMarkup(<BusinessCardReviewContent state={{status: 'ready', item}} onRetry={() => {}} onClose={() => {}} onConfirm={async () => ({success: true, reviewId: item.reviewId})}/>)
    expect(html).toContain('内容を確認して確定')
    expect(html).toContain('>確定する<')
    expect(html).not.toContain('disabled=""')
    expect(html).not.toContain('取引先・担当者として登録')
  })

  it('確定済みの名刺だけ主担当を選んで取引先・担当者として登録できる', () => {
    const confirmedItem = {...item, reviewStatus: '確認確定' as const, correctedFields: {companyName: 'デモ商事', fullName: '山田 太郎（確認済み）'}}
    const html = renderToStaticMarkup(<BusinessCardReviewContent
      state={{status: 'ready', item: confirmedItem}}
      onRetry={() => {}}
      onClose={() => {}}
      formalRegistrationApi={{register: async () => ({success: true, partnerId: '55555555-5555-4555-8555-555555555555', contactId: '66666666-6666-4666-8666-666666666666', partnerReplayed: false, contactReplayed: false})}}
      registrationOwners={[{id: '77777777-7777-4777-8777-777777777777', name: '主担当候補'}]}
      registrationOwnerState="ready"
    />)

    for (const text of ['取引先・担当者として登録', '主担当', '主担当候補', '>登録する<', '>確定済み<']) expect(html).toContain(text)
    // 確定した値は入力欄に出ているので、同じ内容の一覧は重ねて出さない。
    expect(html).not.toContain('確定した内容')
    expect(html).not.toContain('正式登録')
  })

  it('同じ会社を探している間は、登録の操作を出さない', () => {
    const confirmedItem = {...item, reviewStatus: '確認確定' as const, correctedFields: {companyName: 'デモ商事', fullName: '山田 太郎'}}
    const html = renderToStaticMarkup(<BusinessCardReviewContent
      state={{status: 'ready', item: confirmedItem}}
      onRetry={() => {}}
      onClose={() => {}}
      formalRegistrationApi={{register: async () => ({success: true, partnerId: '55555555-5555-4555-8555-555555555555', contactId: '66666666-6666-4666-8666-666666666666', partnerReplayed: false, contactReplayed: false})}}
      partnerSearchApi={{search: async () => []}}
      registrationOwners={[{id: '77777777-7777-4777-8777-777777777777', name: '主担当候補'}]}
      registrationOwnerState="ready"
    />)

    expect(html).toContain('同じ会社の取引先を探しています')
    expect(html).not.toContain('>登録する<')
  })

  it('既存の取引先の候補から選んで、担当者として追加する欄を出す', () => {
    const html = renderToStaticMarkup(<ExistingPartnerPicker
      query="デモ商事"
      candidates={[{id: '11111111-1111-4111-8111-111111111111', name: 'デモ商事株式会社', mainOwnerName: '田中 健一'}]}
      selectedId="11111111-1111-4111-8111-111111111111"
      searching={false}
      locked={false}
      saving={false}
      onQueryChange={() => {}}
      onSearch={() => {}}
      onSelect={() => {}}
    />)

    for (const text of ['取引先を検索', 'デモ商事株式会社', '主担当：田中 健一', '>担当者を追加する<']) expect(html).toContain(text)
    expect(html).toMatch(/<input[^>]*type="radio"[^>]*checked=""/)
  })

  it('候補が無いときは、その旨と選べないボタンを出す', () => {
    const html = renderToStaticMarkup(<ExistingPartnerPicker query="存在しない" candidates={[]} selectedId="" searching={false} locked={false} saving={false} onQueryChange={() => {}} onSearch={() => {}} onSelect={() => {}}/>)

    expect(html).toContain('該当する取引先はありません。')
    expect(html).toMatch(/<button[^>]*disabled=""[^>]*>担当者を追加する</)
  })

  it('検索し直している間は、候補なしではなく検索中と出す', () => {
    const html = renderToStaticMarkup(<ExistingPartnerPicker query="デモ" candidates={[]} selectedId="" searching={true} locked={false} saving={false} onQueryChange={() => {}} onSearch={() => {}} onSelect={() => {}}/>)

    expect(html).toContain('検索しています…')
    expect(html).not.toContain('該当する取引先はありません。')
  })

  it('検索に失敗したときは、候補なしとは言わない', () => {
    const html = renderToStaticMarkup(<ExistingPartnerPicker query="デモ" candidates={[]} selectedId="" searching={false} searchFailed={true} locked={false} saving={false} onQueryChange={() => {}} onSearch={() => {}} onSelect={() => {}}/>)

    expect(html).not.toContain('該当する取引先はありません。')
  })
})
