import { isDataverseGuid } from './dataverseIds'
import type { BusinessCardOcrFields } from './businessCardOcrResult'
import type { BusinessCardReviewItem } from './businessCardReview'

const MAX_FIELD_LENGTH = 2000
const confirmationFieldKeys: readonly (keyof BusinessCardOcrFields)[] = [
  'addressCity', 'addressCountry', 'addressPostalCode', 'addressPostOfficeBox', 'addressState', 'addressStreet',
  'businessPhone', 'companyName', 'department', 'email', 'fax', 'firstName', 'fullAddress', 'fullName',
  'jobTitle', 'lastName', 'mobilePhone', 'website',
]

export type BusinessCardReviewConfirmationOperationResult = {
  success: boolean
  error?: unknown
}

export type BusinessCardReviewConfirmationInvoker = {
  update: (reviewId: string, fields: Record<string, unknown>) => Promise<BusinessCardReviewConfirmationOperationResult>
}

export type BusinessCardReviewConfirmationRequest = {
  item: BusinessCardReviewItem
  correctedFields: BusinessCardOcrFields
}

export type BusinessCardReviewConfirmationPlan =
  | {kind: 'update'; reviewId: string; fields: {pl_correctedfieldsjson: string; pl_reviewstatuscode: '確認確定'}}
  | {kind: 'skip'; reason: 'invalid-review' | 'state-changed' | 'invalid-fields' | 'already-confirmed'; message: string}

export type BusinessCardReviewConfirmationResult =
  | {success: true; reviewId: string}
  | {success: false; kind: 'blocked' | 'operation-failed' | 'result-unknown' | 'conflict'; message: string; reason?: Extract<BusinessCardReviewConfirmationPlan, {kind: 'skip'}>['reason']; error?: unknown}

export type BusinessCardReviewConfirmationApi = {
  confirm: (request: BusinessCardReviewConfirmationRequest) => Promise<BusinessCardReviewConfirmationResult>
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function normalizeFields(value: unknown): BusinessCardOcrFields | undefined {
  if (!isRecord(value)) return undefined
  const normalized: BusinessCardOcrFields = {}
  for (const key of Object.keys(value)) {
    if (!confirmationFieldKeys.includes(key as keyof BusinessCardOcrFields)) return undefined
    const raw = value[key]
    if (typeof raw !== 'string' || raw.trim().length > MAX_FIELD_LENGTH) return undefined
    const text = raw.trim()
    if (text) normalized[key as keyof BusinessCardOcrFields] = text
  }
  if (!normalized.companyName || !normalized.fullName) return undefined
  return normalized
}

export function planBusinessCardReviewConfirmation(request: BusinessCardReviewConfirmationRequest): BusinessCardReviewConfirmationPlan {
  const item = request.item
  if (!isDataverseGuid(item.reviewId) || !isDataverseGuid(item.inputVersionId) || item.captureStatus !== '確認待ち' || item.inputState !== '固定済み' || item.imageState !== '画像到着確認済み' || item.ocrJobStatus !== '成功') {
    return {kind: 'skip', reason: 'invalid-review', message: '確認版の関連とOCR成功状態を確認できません。'}
  }
  if (item.reviewStatus === '確認確定') return {kind: 'skip', reason: 'already-confirmed', message: '確認版はすでに確定しています。'}
  if (item.reviewStatus !== '確認編集中') return {kind: 'skip', reason: 'state-changed', message: '確認版の状態が変わったため、保存しません。'}
  const fields = normalizeFields(request.correctedFields)
  if (!fields) return {kind: 'skip', reason: 'invalid-fields', message: '会社名・氏名を含む確認値を入力してください。'}
  return {
    kind: 'update',
    reviewId: item.reviewId,
    fields: {
      pl_correctedfieldsjson: JSON.stringify(fields),
      pl_reviewstatuscode: '確認確定',
    },
  }
}

function isHttpError(error: unknown): error is {status?: unknown} {
  return Boolean(error) && typeof error === 'object' && !Array.isArray(error)
}

export async function confirmBusinessCardReview(request: BusinessCardReviewConfirmationRequest, invoker: BusinessCardReviewConfirmationInvoker): Promise<BusinessCardReviewConfirmationResult> {
  const plan = planBusinessCardReviewConfirmation(request)
  if (plan.kind === 'skip') return {success: false, kind: 'blocked', reason: plan.reason, message: plan.message}
  try {
    const result = await invoker.update(plan.reviewId, {
      ...plan.fields,
      pl_reviewedat: new Date().toISOString(),
    })
    if (!result.success) {
      return {success: false, kind: isHttpError(result.error) && result.error.status === 412 ? 'conflict' : 'operation-failed', message: isHttpError(result.error) && result.error.status === 412 ? '確認版が別の操作で更新されました。再読取してください。' : '確認版を保存できませんでした。', error: result.error}
    }
    return {success: true, reviewId: plan.reviewId}
  } catch (error) {
    return {success: false, kind: 'result-unknown', message: '確認版の保存結果を確認できません。再送せず再読取してください。', error}
  }
}

export function createBusinessCardReviewConfirmationApi(invoker: BusinessCardReviewConfirmationInvoker) {
  return {confirm: (request: BusinessCardReviewConfirmationRequest) => confirmBusinessCardReview(request, invoker)}
}
