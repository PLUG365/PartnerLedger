import type { IOperationResult } from '@microsoft/power-apps/data'
import './dataverseRuntimeDataSourcesInfo'
import type { Pl_bulletinposts } from './generated/models/Pl_bulletinpostsModel'
import { Pl_bulletinpostsService } from './generated/services/Pl_bulletinpostsService'
import { isDataverseGuid } from './dataverseIds'
import { BulletinBoardApiError, isSameBulletinPost, normalizeBulletinPostRequest, type BulletinBoardScope, type BulletinPost, type BulletinPostCreateRequest } from './bulletinBoard'

const formattedCreatedByName = '_createdby_value@OData.Community.Display.V1.FormattedValue'

export const bulletinPostSelect = [
  'pl_bulletinpostid',
  'pl_body',
  'pl_contactkey',
  'pl_name',
  'pl_registeredat',
  'pl_requestkey',
  '_createdby_value',
  'createdon',
  '_pl_partnerlookup_value',
  'statecode',
]

export type StandardBulletinBoardService = {
  list: (options: Record<string, unknown>) => Promise<IOperationResult<Pl_bulletinposts[]>>
  create: (record: Record<string, unknown>) => Promise<IOperationResult<Pl_bulletinposts>>
}

export type BulletinBoardApi = {
  list: (scope: BulletinBoardScope) => Promise<BulletinPost[]>
  create: (request: BulletinPostCreateRequest) => Promise<BulletinPost>
}

function recordGuid(value: unknown, fieldName: string): string {
  if (typeof value !== 'string' || !isDataverseGuid(value)) {
    throw new BulletinBoardApiError(`掲示板投稿の${fieldName}が不正です。`, 'invalid-response')
  }
  return value
}

function parseRecord(value: Pl_bulletinposts): BulletinPost {
  const id = recordGuid(value.pl_bulletinpostid, 'ID')
  const partnerId = recordGuid(value._pl_partnerlookup_value, '親取引先ID')
  const body = typeof value.pl_body === 'string' && value.pl_body.trim() ? value.pl_body.trim() : undefined
  const requestKey = typeof value.pl_requestkey === 'string' && value.pl_requestkey.trim() ? value.pl_requestkey.trim() : undefined
  const createdAt = typeof value.pl_registeredat === 'string' && value.pl_registeredat
    ? value.pl_registeredat
    : typeof value.createdon === 'string' && value.createdon
      ? value.createdon
      : undefined
  if (!body || !requestKey || !createdAt) throw new BulletinBoardApiError('掲示板投稿の必須項目が応答にありません。', 'invalid-response')
  const contactId = typeof value.pl_contactkey === 'string' && value.pl_contactkey.trim()
    ? recordGuid(value.pl_contactkey.trim(), '担当者ID')
    : undefined
  const raw = value as unknown as Record<string, unknown>
  return {
    id,
    author: typeof value.createdbyname === 'string' && value.createdbyname.trim()
      ? value.createdbyname.trim()
      : typeof raw[formattedCreatedByName] === 'string' && String(raw[formattedCreatedByName]).trim()
        ? String(raw[formattedCreatedByName]).trim()
        : '登録者未取得',
    createdAt,
    body,
    partnerId,
    contactId,
    requestKey,
  }
}

function ensureSuccess<T>(result: IOperationResult<T>, operation: string): T {
  if (!result.success || result.data === undefined || result.data === null) {
    throw new BulletinBoardApiError(`${operation}に失敗しました。通信状態を確認して、もう一度お試しください。`, 'transport-error')
  }
  return result.data
}

function sameRequestKeyFilter(requestKey: string): string {
  return `pl_requestkey eq '${requestKey.replace(/'/g, "''")}'`
}

export function createDataverseBulletinBoardApi(service: StandardBulletinBoardService): BulletinBoardApi {
  const findByRequestKey = async (requestKey: string): Promise<BulletinPost[]> => {
    const result = await service.list({
      select: bulletinPostSelect,
      filter: sameRequestKeyFilter(requestKey),
      maxPageSize: 2,
    })
    return ensureSuccess(result, '掲示板投稿の確認').map(parseRecord)
  }

  return {
    list: async scope => {
      const normalizedPartnerId = recordGuid(scope.partnerId, 'PartnerId')
      const normalizedContactId = scope.contactId?.trim() ? recordGuid(scope.contactId, 'ContactId').toLowerCase() : undefined
      const result = await service.list({
        select: bulletinPostSelect,
        filter: `statecode eq 0 and _pl_partnerlookup_value eq ${normalizedPartnerId}`,
        // 新しい投稿から順に（2026-09-28、ユーザー要望。入力欄は一覧の上）。
        orderBy: ['pl_registeredat desc', 'createdon desc'],
        maxPageSize: 100,
      })
      return ensureSuccess(result, '掲示板一覧の取得')
        .map(parseRecord)
        .filter(post => post.partnerId.toLowerCase() === normalizedPartnerId.toLowerCase()
          && (post.contactId ?? '').toLowerCase() === (normalizedContactId ?? ''))
    },
    create: async request => {
      const normalized = normalizeBulletinPostRequest({
        ...request,
        requestKey: request.requestKey ?? '',
      })
      const record: Record<string, unknown> = {
        pl_body: normalized.body,
        pl_requestkey: normalized.requestKey,
        'pl_PartnerLookup@odata.bind': `/pl_partners(${normalized.partnerId})`,
        ...(normalized.contactId ? {pl_contactkey: normalized.contactId} : {}),
      }
      let result: IOperationResult<Pl_bulletinposts> | undefined
      try {
        result = await service.create(record)
      } catch {
        result = undefined
      }
      if (result?.success && result.data) {
        const created = parseRecord(result.data)
        if (!isSameBulletinPost(created, normalized)) {
          throw new BulletinBoardApiError('掲示板投稿の保存結果が要求内容と一致しません。', 'invalid-response')
        }
        return created
      }

      // Alternate Key が競合した再送や応答欠落は、同じ要求キーを読み直して
      // 同一内容だけ成功扱いにする。異なる内容はサーバー拒否として残す。
      const existing = await findByRequestKey(normalized.requestKey)
      if (existing.length === 1 && isSameBulletinPost(existing[0], normalized)) return existing[0]
      if (existing.length > 0) throw new BulletinBoardApiError('前回の送信と内容が異なります。再読み込みしてから、もう一度お試しください。', 'request-key-conflict')
      throw new BulletinBoardApiError('投稿できたか確認できませんでした。入力内容はそのままなので、もう一度お試しください。', 'transport-error')
    },
  }
}

const generatedStandardBulletinBoardService: StandardBulletinBoardService = {
  list: options => Pl_bulletinpostsService.getAll(options as Parameters<typeof Pl_bulletinpostsService.getAll>[0]),
  create: record => Pl_bulletinpostsService.create(record as Parameters<typeof Pl_bulletinpostsService.create>[0]),
}

export const dataverseBulletinBoardApi = createDataverseBulletinBoardApi(generatedStandardBulletinBoardService)
