import { isDataverseGuid } from './dataverseIds'
import {
  planBusinessCardOcrJobCreate,
  type BusinessCardOcrJobCreateContext,
  type BusinessCardOcrJobCreatePlan,
} from './businessCardOcrJob'

export type BusinessCardOcrJobCreateOperationResult<T> = {
  success: boolean
  data?: T
  error?: unknown
}

export type BusinessCardOcrJobCreateInvoker = {
  create: (record: Record<string, unknown>) => Promise<BusinessCardOcrJobCreateOperationResult<{pl_ocrjobid?: unknown}>>
}

export type BusinessCardOcrJobCreateRequest = {
  context: BusinessCardOcrJobCreateContext
  now: Date
  retryFailed?: boolean
}

export type BusinessCardOcrJobCreateFailureKind =
  | 'blocked'
  | 'operation-failed'
  | 'result-unknown'
  | 'alternate-key-conflict'

export type BusinessCardOcrJobCreateResult =
  | {
      success: true
      jobId: string
      jobKey: string
      attemptNumber: number
    }
  | {
      success: false
      kind: BusinessCardOcrJobCreateFailureKind
      message: string
      reason?: Extract<BusinessCardOcrJobCreatePlan, {kind: 'skip'}>['reason']
      jobKey?: string
      error?: unknown
    }

function isHttpError(error: unknown): error is {status?: unknown; message?: unknown} {
  return Boolean(error) && typeof error === 'object' && !Array.isArray(error)
}

function isAlternateKeyConflict(error: unknown): boolean {
  return isHttpError(error) && error.status === 412
}

function blocked(
  plan: Extract<BusinessCardOcrJobCreatePlan, {kind: 'skip'}>,
): BusinessCardOcrJobCreateResult {
  return {success: false, kind: 'blocked', reason: plan.reason, message: plan.message}
}

/**
 * Plans and executes one standard OcrJob Create. The caller must supply a
 * freshly read context; this function never retries a write or turns an
 * unknown response into success.
 */
export async function createBusinessCardOcrJob(
  request: BusinessCardOcrJobCreateRequest,
  invoker: BusinessCardOcrJobCreateInvoker,
): Promise<BusinessCardOcrJobCreateResult> {
  const plan = planBusinessCardOcrJobCreate(request.context, request.now, request.retryFailed ?? false)
  if (plan.kind === 'skip') return blocked(plan)

  let result: BusinessCardOcrJobCreateOperationResult<{pl_ocrjobid?: unknown}>
  try {
    result = await invoker.create(plan.item)
  } catch (error) {
    return {
      success: false,
      kind: 'result-unknown',
      jobKey: plan.item.pl_jobkey,
      message: 'OCRジョブの作成結果を確認できません。再送せず照合してください。',
      error,
    }
  }

  if (!result.success) {
    return {
      success: false,
      kind: isAlternateKeyConflict(result.error) ? 'alternate-key-conflict' : 'operation-failed',
      jobKey: plan.item.pl_jobkey,
      message: isAlternateKeyConflict(result.error)
        ? '同じOCRジョブキーが存在します。再送せず既存行を照合してください。'
        : 'OCRジョブを作成できませんでした。',
      error: result.error,
    }
  }

  const jobId = result.data?.pl_ocrjobid
  if (typeof jobId !== 'string' || !isDataverseGuid(jobId)) {
    return {
      success: false,
      kind: 'result-unknown',
      jobKey: plan.item.pl_jobkey,
      message: 'OCRジョブの作成結果を確認できません。再送せず照合してください。',
      error: result.error,
    }
  }

  return {
    success: true,
    jobId,
    jobKey: plan.item.pl_jobkey,
    attemptNumber: plan.item.pl_attemptnumber,
  }
}

export function createBusinessCardOcrJobApi(invoker: BusinessCardOcrJobCreateInvoker) {
  return {
    create: (request: BusinessCardOcrJobCreateRequest) => createBusinessCardOcrJob(request, invoker),
  }
}
