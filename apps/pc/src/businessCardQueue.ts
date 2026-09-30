import { isDataverseGuid } from './dataverseIds'

export type CaptureBatchStatus = '受付中' | '受付完了' | '処理中' | '完了' | '一部失敗' | '失敗'
export type BusinessCardCaptureStatus = '未登録' | '処理中' | '確認待ち' | '登録済み' | '失敗' | '結果不明'

export type BusinessCardQueueItem = {
  id: string
  captureKey: string
  captureStatus: BusinessCardCaptureStatus
  batchId: string
  batchKey: string
  batchStatus: CaptureBatchStatus
  registeredBy: string
  /** 受け付けたユーザーのsystemuserid。処理待ちから外す操作を本人の行だけに出すために使う。 */
  registeredById?: string
  registeredAt?: string
  receivedAt?: string
  currentVersionNumber?: number
}

export type BusinessCardQueueOperationResult = {
  success: boolean
  data: unknown
  error?: unknown
}

export type BusinessCardQueueInvoker = {
  listBatches: () => Promise<BusinessCardQueueOperationResult>
  listCaptures: () => Promise<BusinessCardQueueOperationResult>
}

export type BusinessCardQueueApi = {
  listQueue: () => Promise<BusinessCardQueueItem[]>
}

export type BusinessCardQueueLoadState =
  | {status: 'idle'}
  | {status: 'loading'}
  | {status: 'ready'; items: BusinessCardQueueItem[]}
  | {status: 'error'; message: string}

export class BusinessCardQueueError extends Error {
  readonly code: string

  constructor(message: string, code: string) {
    super(message)
    this.name = 'BusinessCardQueueError'
    this.code = code
  }
}

const batchStatuses: readonly CaptureBatchStatus[] = ['受付中', '受付完了', '処理中', '完了', '一部失敗', '失敗']
const captureStatuses: readonly BusinessCardCaptureStatus[] = ['未登録', '処理中', '確認待ち', '登録済み', '失敗', '結果不明']
const formattedCreatedByName = '_createdby_value@OData.Community.Display.V1.FormattedValue'

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

function requireString(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new BusinessCardQueueError(`${field}が不正です。`, 'invalid-response')
  return value.trim()
}

function optionalString(value: unknown, field: string): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string') throw new BusinessCardQueueError(`${field}が不正です。`, 'invalid-response')
  return value.trim() || undefined
}

function optionalDate(value: unknown, field: string): string | undefined {
  const normalized = optionalString(value, field)
  if (normalized === undefined) return undefined
  if (Number.isNaN(Date.parse(normalized))) throw new BusinessCardQueueError(`${field}が不正です。`, 'invalid-response')
  return normalized
}

function optionalVersion(value: unknown): number | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 0) {
    throw new BusinessCardQueueError('入力版番号が不正です。', 'invalid-response')
  }
  return value
}

function requireStatus<T extends string>(value: unknown, statuses: readonly T[], field: string): T {
  const status = requireString(value, field)
  if (!statuses.includes(status as T)) throw new BusinessCardQueueError(`${field}が不正です。`, 'invalid-response')
  return status as T
}

function requireActive(value: Record<string, unknown>, label: string, index: number) {
  if (value.statecode !== undefined && value.statecode !== 0 && value.statecode !== '0') {
    throw new BusinessCardQueueError(`${label}の${index + 1}件目が非Activeです。`, 'invalid-response')
  }
}

function parseBatch(value: unknown, index: number): {id: string; key: string; status: CaptureBatchStatus} {
  if (!isRecord(value)) throw new BusinessCardQueueError(`取込バッチの${index + 1}件目が不正です。`, 'invalid-response')
  requireActive(value, '取込バッチ', index)
  const id = requireString(value.pl_capturebatchid, '取込バッチId')
  if (!isDataverseGuid(id)) throw new BusinessCardQueueError(`取込バッチの${index + 1}件目のIdが不正です。`, 'invalid-response')
  return {
    id,
    key: requireString(value.pl_batchkey, 'バッチキー'),
    status: requireStatus(value.pl_batchstatuscode, batchStatuses, 'バッチ状態'),
  }
}

function parseCapture(value: unknown, index: number): BusinessCardQueueItem {
  if (!isRecord(value)) throw new BusinessCardQueueError(`名刺取込の${index + 1}件目が不正です。`, 'invalid-response')
  requireActive(value, '名刺取込', index)
  const id = requireString(value.pl_cardcaptureid, '名刺取込Id')
  const batchId = requireString(value._pl_capturebatchlookup_value, '取込バッチLookup')
  if (!isDataverseGuid(id) || !isDataverseGuid(batchId)) {
    throw new BusinessCardQueueError(`名刺取込の${index + 1}件目のIdが不正です。`, 'invalid-response')
  }
  const createdBy = optionalString(value.createdbyname, '登録者') ?? optionalString(value[formattedCreatedByName], '登録者') ?? '登録者未取得'
  const createdById = optionalString(value._createdby_value, '登録者Id')
  return {
    id,
    captureKey: requireString(value.pl_capturekey, '取込キー'),
    captureStatus: requireStatus(value.pl_capturestatuscode, captureStatuses, '名刺取込状態'),
    batchId,
    batchKey: '',
    batchStatus: '受付中',
    registeredBy: createdBy,
    registeredById: createdById && isDataverseGuid(createdById) ? createdById : undefined,
    registeredAt: optionalDate(value.pl_registeredat, '登録日'),
    receivedAt: optionalDate(value.pl_receivedat, '受信日'),
    currentVersionNumber: optionalVersion(value.pl_currentversionnumber),
  }
}

export function parseBusinessCardQueue(batchData: unknown, captureData: unknown): BusinessCardQueueItem[] {
  if (!Array.isArray(batchData) || !Array.isArray(captureData)) {
    throw new BusinessCardQueueError('名刺処理待ち一覧の配列応答がありません。', 'invalid-response')
  }

  const batches = batchData.map(parseBatch)
  const batchesById = new Map<string, {id: string; key: string; status: CaptureBatchStatus}>()
  for (const batch of batches) {
    const normalizedId = batch.id.toLowerCase()
    if (batchesById.has(normalizedId)) throw new BusinessCardQueueError('取込バッチ一覧に重複したIdがあります。', 'invalid-response')
    batchesById.set(normalizedId, batch)
  }

  const items = captureData.map(parseCapture)
  const ids = new Set<string>()
  const keys = new Set<string>()
  for (const item of items) {
    if (ids.has(item.id.toLowerCase())) throw new BusinessCardQueueError('名刺取込一覧に重複したIdがあります。', 'invalid-response')
    if (keys.has(item.captureKey)) throw new BusinessCardQueueError('名刺取込一覧に重複した取込キーがあります。', 'invalid-response')
    ids.add(item.id.toLowerCase())
    keys.add(item.captureKey)
    const batch = batchesById.get(item.batchId.toLowerCase())
    if (!batch) throw new BusinessCardQueueError('名刺取込に対応する取込バッチがありません。', 'invalid-response')
    item.batchKey = batch.key
    item.batchStatus = batch.status
  }
  return items
}

export function createBusinessCardQueueApi(invoker: BusinessCardQueueInvoker): BusinessCardQueueApi {
  return {
    listQueue: async () => {
      let batches: BusinessCardQueueOperationResult
      let captures: BusinessCardQueueOperationResult
      try {
        ;[batches, captures] = await Promise.all([invoker.listBatches(), invoker.listCaptures()])
      } catch {
        throw new BusinessCardQueueError('名刺処理待ち一覧を取得できませんでした。', 'operation-failed')
      }
      if (!batches.success || !captures.success) throw new BusinessCardQueueError('名刺処理待ち一覧を取得できませんでした。', 'operation-failed')
      return parseBusinessCardQueue(batches.data, captures.data)
    },
  }
}
