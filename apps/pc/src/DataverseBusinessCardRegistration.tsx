import { useState, type ChangeEvent, type FormEvent } from 'react'
import { Image as ImageIcon, X } from 'lucide-react'
import { BUSINESS_CARD_IMAGE_MAX_BYTES, type BusinessCardCaptureApi, type BusinessCardCaptureResult } from './businessCardCapture'
import { BUSINESS_CARD_BULK_MAX_FILES, type BusinessCardBulkCaptureApi, type BusinessCardBulkCaptureFile, type BusinessCardBulkCaptureItemResult } from './businessCardBulkCapture'
import type { BusinessCardOcrJobCreateResult } from './businessCardOcrJobApi'
import { blocksResubmission, createOcrJobAfterCapture, submitBusinessCardImages, type BusinessCardIntakeResult, type BusinessCardOcrJobLauncher } from './businessCardIntake'

type SuccessfulCapture = Extract<BusinessCardCaptureResult, {success: true}>
type ResultRow = {captureKey: string; fileName: string; accepted: boolean; detail?: string; capture?: SuccessfulCapture}

function createKey(prefix: string): string {
  const id = typeof globalThis.crypto?.randomUUID === 'function' ? globalThis.crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`
  return `${prefix}-${id}`
}

function fileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.ceil(bytes / 1024)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function bulkItemCapture(batchKey: string, item: BusinessCardBulkCaptureItemResult): SuccessfulCapture | undefined {
  if (!item.success || !item.captureId || !item.inputVersionId) return undefined
  return {success: true, batchKey, captureKey: item.captureKey, batchId: item.batchId, captureId: item.captureId, inputVersionId: item.inputVersionId}
}

/** 1枚でも複数枚でも同じ形で結果を並べる。全体が失敗した場合は空で、エラー表示に任せる。 */
function resultRows(intake: BusinessCardIntakeResult, files: BusinessCardBulkCaptureFile[]): ResultRow[] {
  if (intake.mode === 'single') {
    const file = files[0]
    return intake.result.success ? [{captureKey: intake.result.captureKey, fileName: file?.file.name ?? '', accepted: true, capture: intake.result}] : []
  }
  if (!intake.result.success) return []
  const batchKey = intake.result.batchKey
  return intake.result.items.map(item => ({
    captureKey: item.captureKey,
    fileName: item.fileName,
    accepted: item.success,
    detail: item.success ? undefined : item.kind === 'result-unknown' ? '受付できたか確認できませんでした' : '受け付けられませんでした',
    capture: bulkItemCapture(batchKey, item),
  }))
}

function failureHint(intake: BusinessCardIntakeResult) {
  if (intake.result.success) return ''
  if (intake.result.kind === 'result-unknown') return '処理待ちに表示されているか確認してから、必要なら画像を選び直してやり直してください。'
  if (intake.result.kind === 'invalid-input') return '画像を選び直してください。'
  return 'もう一度お試しください。'
}

/**
 * 名刺画像の受付。1枚なら1枚用、2枚以上なら一括用の受付を使い、受け付けた名刺ごとに読み取りを始める。
 */
export function DataverseBusinessCardRegistration({api, bulkApi, onCreateOcrJob, onOpenQueue}: {api: BusinessCardCaptureApi; bulkApi: BusinessCardBulkCaptureApi; onCreateOcrJob?: BusinessCardOcrJobLauncher; onOpenQueue: () => void}) {
  const [files, setFiles] = useState<BusinessCardBulkCaptureFile[]>([])
  const [batchKey, setBatchKey] = useState(() => createKey('PL-PC-BATCH'))
  const [intake, setIntake] = useState<BusinessCardIntakeResult>()
  const [ocrResults, setOcrResults] = useState<Record<string, BusinessCardOcrJobCreateResult | 'starting'>>({})
  const [submitting, setSubmitting] = useState(false)

  const reset = (next: BusinessCardBulkCaptureFile[]) => {
    setFiles(next)
    setBatchKey(createKey('PL-PC-BATCH'))
    setIntake(undefined)
    setOcrResults({})
  }
  const selectFiles = (event: ChangeEvent<HTMLInputElement>) => {
    reset(Array.from(event.currentTarget.files ?? []).map(file => ({file, captureKey: createKey('PL-PC-CAP')})))
    event.currentTarget.value = ''
  }
  const removeFile = (captureKey: string) => reset(files.filter(file => file.captureKey !== captureKey))

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!files.length || submitting || blocksResubmission(intake)) return
    setSubmitting(true)
    setIntake(undefined)
    setOcrResults({})
    try {
      const next = await submitBusinessCardImages({batchKey, files}, api, bulkApi)
      setIntake(next)
      if (!onCreateOcrJob) return
      for (const row of resultRows(next, files)) {
        if (!row.capture) continue
        setOcrResults(current => ({...current, [row.captureKey]: 'starting'}))
        const ocr = await createOcrJobAfterCapture(row.capture, onCreateOcrJob)
        if (ocr) setOcrResults(current => ({...current, [row.captureKey]: ocr}))
      }
    } finally {
      setSubmitting(false)
    }
  }

  const tooMany = files.length > BUSINESS_CARD_BULK_MAX_FILES
  const canSubmit = files.length > 0 && !tooMany && !submitting && !blocksResubmission(intake)
  const accepted = intake?.result.success === true
  const rows = intake ? resultRows(intake, files) : []
  const rejected = rows.filter(row => !row.accepted).length
  const ocrLabel = (captureKey: string) => {
    const ocr = ocrResults[captureKey]
    if (!ocr) return undefined
    if (ocr === 'starting') return '読み取りを開始しています…'
    return ocr.success ? '読み取り中' : ocr.message || '読み取りを開始できませんでした。'
  }

  return <section className="bulk-intake-layout card-intake-layout">
    <form className="panel bulk-selection-panel" aria-label="名刺画像の受付" onSubmit={submit}>
      <div className="image-intake-heading"><span className="image-intake-icon"><ImageIcon aria-hidden="true" strokeWidth={1.8}/></span><div><h2>名刺画像を選択</h2><p>1枚でも、まとめて{BUSINESS_CARD_BULK_MAX_FILES}枚までも受け付けます（1枚あたり{fileSize(BUSINESS_CARD_IMAGE_MAX_BYTES)}まで）。読み取った内容は、ホームの処理待ちから確認して登録します。</p></div></div>
      <label className="primary-button image-file-picker bulk-file-picker" aria-disabled={submitting}><ImageIcon aria-hidden="true" strokeWidth={1.8}/><span>{files.length ? '画像を選び直す' : '画像を選択'}</span><input aria-label="名刺画像を選択" type="file" accept="image/*" multiple disabled={submitting} onChange={selectFiles}/></label>
      <div className={'bulk-file-list ' + (!files.length ? 'empty' : '')}>{files.length ? <ol>{files.map(selected => <li key={selected.captureKey}><span><b>{selected.file.name}</b><small>{fileSize(selected.file.size)}</small></span><button type="button" aria-label={`${selected.file.name}を選択から外す`} disabled={submitting || accepted} onClick={() => removeFile(selected.captureKey)}><X className="nav-icon" aria-hidden="true" strokeWidth={1.8}/></button></li>)}</ol> : <p>PNG、JPEGなどの画像を選択してください。複数枚まとめて選べます。</p>}</div>
      {tooMany && <p className="warning" role="alert">{BUSINESS_CARD_BULK_MAX_FILES}枚までにしてください（{files.length}枚選択中）。</p>}
      <div className="bulk-selection-footer"><span>{files.length}枚を選択</span><button className="primary-button" type="submit" disabled={!canSubmit}>{submitting ? '受付中…' : accepted ? '受付済み' : files.length > 1 ? `${files.length}枚を受け付ける` : '受け付ける'}</button></div>
      {intake && !intake.result.success && <div className="single-capture-result failure" role="alert"><b>{intake.result.message}</b><p>{failureHint(intake)}</p></div>}
      {accepted && <div className={'single-capture-result ' + (rejected ? 'partial' : 'success')} role="status"><b>{rejected ? `${rows.length - rejected}枚を受け付けました。${rejected}枚は受け付けられませんでした。` : rows.length > 1 ? `${rows.length}枚を受け付けました。` : '受け付けました。'}</b><p>{rejected ? '受け付けられなかった画像は、選び直してもう一度受け付けてください。' : ''}読み取りが終わると、ホームの処理待ちに表示されます。</p><div className="bulk-result-list"><ol>{rows.map(row => <li key={row.captureKey}><span><b>{row.fileName}</b><small className={row.accepted ? '' : 'is-error'}>{row.accepted ? '受付済み' : row.detail}</small>{ocrLabel(row.captureKey) && <small>{ocrLabel(row.captureKey)}</small>}</span></li>)}</ol></div><button className="secondary-button" type="button" onClick={onOpenQueue}>処理待ちを確認</button></div>}
    </form>
  </section>
}
