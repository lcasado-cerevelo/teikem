// COD (Lote 85): reconciliar lo cobrado con las hojas que regresan de la calle. Se suben las hojas escaneadas (PDF o imágenes): el navegador lee el código de barras de cada una (el número de la factura o
// de la orden) y solo los códigos viajan al servidor; también se pueden escribir a mano. Primero una VISTA PREVIA (no cambia nada) y luego «Cuadrar». Pide cod.reconcile; las corridas, cod.view.
import { useMemo, useRef, useState, type DragEvent } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, DataTable, Modal, Panel, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import { ACCEPTED_TYPES, fileKind, readCodesFromFile } from './barcodeReader'
import { useCodMutations, useCodRun, useCodRuns, type CodReconcileItemDto, type CodReconcileResultDto, type CodRunListItemDto } from './codApi'
import {
  checkedCount,
  codesError,
  countResults,
  reconcilableAmount,
  RECONCILE_MODES,
  resultTone,
  RESULTS,
  rowsFromManual,
  rowsFromScanned,
  runSource,
  toReconcileRequest,
  type CodeRow,
  type ReconcileMode,
} from './codRules'

interface FileStatus {
  key: number
  name: string
  state: 'reading' | 'done' | 'error'
  page: number
  pages: number
  codes: number
  emptyPages: number[]
  error: string | null
}

const TONE_CLASS: Record<string, 'deliv' | 'warn' | 'neutral' | 'fail'> = { ok: 'deliv', info: 'neutral', warn: 'warn', bad: 'fail' }

function ResultChip({ result }: { result: string | null | undefined }) {
  const t = useT()
  return <Chip tone={TONE_CLASS[resultTone(result)]}>{t(`cod.results.${result ?? 'NOT_FOUND'}`)}</Chip>
}

/** La tabla de resultados por código (vista previa, corrida cuadrada y detalle de una corrida). */
function ResultsTable({ items, label }: { items: CodReconcileItemDto[]; label: string }) {
  const t = useT()
  const f = useFormat()
  const columns = useMemo<DataColumn<CodReconcileItemDto>[]>(
    () => [
      { id: 'code', header: t('cod.col.code'), cell: (i) => <b>{i.code}</b>, exportValue: (i) => i.code ?? '', sortValue: (i) => i.code ?? '', card: 'title' },
      { id: 'result', header: t('cod.col.result'), cell: (i) => <ResultChip result={i.result} />, exportValue: (i) => t(`cod.results.${i.result}`), sortValue: (i) => i.result ?? '' },
      { id: 'kind', header: t('cod.col.kind'), cell: (i) => (i.orderNumber ? t(`cod.kinds.${i.kind ?? 'COD'}`) : '—'), exportValue: (i) => (i.orderNumber ? t(`cod.kinds.${i.kind ?? 'COD'}`) : ''), sortValue: (i) => i.kind ?? '' },
      { id: 'order', header: t('cod.col.order'), cell: (i) => i.orderNumber ?? '—', exportValue: (i) => i.orderNumber ?? '', sortValue: (i) => i.orderNumber ?? '' },
      { id: 'invoice', header: t('cod.col.invoice'), cell: (i) => i.invoiceNumber ?? '—', exportValue: (i) => i.invoiceNumber ?? '', sortValue: (i) => i.invoiceNumber ?? '' },
      { id: 'client', header: t('cod.col.client'), cell: (i) => i.clientName ?? '—', exportValue: (i) => i.clientName ?? '', sortValue: (i) => i.clientName ?? '' },
      { id: 'amount', header: t('cod.col.reconciled'), cell: (i) => ((i.reconciled ?? 0) > 0 ? f.money(i.reconciled ?? 0) : '—'), exportValue: (i) => i.reconciled ?? 0, sortValue: (i) => i.reconciled ?? 0, align: 'end' },
      { id: 'source', header: t('cod.col.source'), cell: (i) => i.source ?? '—', exportValue: (i) => i.source ?? '', sortValue: (i) => i.source ?? '' },
      { id: 'message', header: t('cod.col.detail'), cell: (i) => i.message, exportValue: (i) => i.message ?? '', sortValue: (i) => i.message ?? '' },
    ],
    [t, f],
  )
  return <DataTable label={label} columns={columns} rows={items} rowKey={(i) => `${i.code}-${i.source ?? ''}-${i.result}`} empty={t('cod.noCodes')} />
}

function Summary({ result }: { result: CodReconcileResultDto }) {
  const t = useT()
  const f = useFormat()
  const counts = countResults(result.items ?? [])
  return (
    <div className="cod-summary" role="status">
      <p>
        <b>{t('cod.summary.total', { n: result.total ?? 0 })}</b> · {t('cod.summary.amount', { amount: f.money(reconcilableAmount(result.items ?? [])) })}
        {checkedCount(result.items ?? []) > 0 && <> · {t('cod.summary.checked', { n: checkedCount(result.items ?? []) })}</>}
      </p>
      {(result.warnings ?? []).length > 0 && (
        <ul className="cod-warnings" role="alert" aria-label={t('cod.warnings.title')}>
          {(result.warnings ?? []).map((wk) => (
            <li key={wk}>{t(`cod.warnings.${wk}`)}</li>
          ))}
        </ul>
      )}
      <div className="cod-chips">
        {RESULTS.filter((r) => counts[r]).map((r) => (
          <span key={r}>
            <ResultChip result={r} /> {counts[r]}
          </span>
        ))}
      </div>
    </div>
  )
}

// ---------------------------------------------------------------- reconciliar

export function ReconcileTab() {
  const t = useT()
  const { reconcile } = useCodMutations()
  const [rows, setRows] = useState<CodeRow[]>([])
  const [files, setFiles] = useState<FileStatus[]>([])
  const [clientId, setClientId] = useState<string | null>(null)
  const [mode, setMode] = useState<ReconcileMode>('AUTO')
  const [manual, setManual] = useState('')
  const [preview, setPreview] = useState<CodReconcileResultDto | null>(null)
  const [done, setDone] = useState<CodReconcileResultDto | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [reading, setReading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const nextKey = useRef(1)
  const fileInput = useRef<HTMLInputElement>(null)

  const changed = () => {
    setPreview(null)
    setError(null)
  }

  const addFiles = async (list: FileList | File[]) => {
    const picked = Array.from(list)
    if (picked.length === 0) return
    changed()
    setReading(true)
    for (const file of picked) {
      const key = nextKey.current++
      const patch = (p: Partial<FileStatus>) => setFiles((x) => x.map((s) => (s.key === key ? { ...s, ...p } : s)))
      setFiles((x) => [...x, { key, name: file.name, state: 'reading', page: 0, pages: 0, codes: 0, emptyPages: [], error: null }])
      if (fileKind(file) === null) {
        patch({ state: 'error', error: 'type' })
        continue
      }
      const r = await readCodesFromFile(file, (page, pages) => patch({ page, pages }))
      if (r.error) patch({ state: 'error', error: r.error })
      else {
        const first = nextKey.current
        nextKey.current += r.codes.length
        setRows((x) => [...x, ...rowsFromScanned(r.codes, first)])
        patch({ state: 'done', pages: r.pages, codes: r.codes.length, emptyPages: r.emptyPages })
      }
    }
    setReading(false)
  }

  const addManual = () => {
    const added = rowsFromManual(manual, nextKey.current)
    if (added.length === 0) return
    nextKey.current += added.length
    changed()
    setRows((x) => [...x, ...added])
    setManual('')
  }

  const removeRow = (key: number) => {
    changed()
    setRows((x) => x.filter((r) => r.key !== key))
  }

  const reset = () => {
    setRows([])
    setFiles([])
    setPreview(null)
    setDone(null)
    setError(null)
    setManual('')
  }

  const run = async (dryRun: boolean) => {
    const problem = codesError(rows)
    if (problem) return setError(t(`cod.errors.${problem}`))
    setError(null)
    try {
      const res = await reconcile.mutateAsync(toReconcileRequest(rows, clientId, runSource(rows), dryRun, mode))
      if (dryRun) setPreview(res)
      else {
        setDone(res)
        toast.success(t('cod.reconciledToast', { n: res.reconciled ?? 0, c: res.checked ?? 0 }))
      }
    } catch (e) {
      setError(applyProblemDetails(e).title)
      throw e
    }
  }

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault()
    setDragging(false)
    void addFiles(e.dataTransfer.files)
  }

  const rowColumns = useMemo<DataColumn<CodeRow>[]>(
    () => [
      { id: 'code', header: t('cod.col.code'), cell: (r) => <b>{r.code}</b>, sortValue: (r) => r.code, card: 'title' },
      { id: 'source', header: t('cod.col.source'), cell: (r) => r.source, sortValue: (r) => r.source },
      {
        id: 'remove',
        header: '',
        cell: (r) => (
          <button type="button" className="btn" aria-label={t('cod.removeCode', { code: r.code })} onClick={() => removeRow(r.key)}>
            {t('cod.remove')}
          </button>
        ),
        align: 'end',
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t],
  )

  if (done) {
    return (
      <Panel icon={<IconCart />} title={t('cod.done.title')}>
        <div className="pb">
          <Summary result={done} />
          <ResultsTable items={done.items ?? []} label={t('cod.done.title')} />
          <div className="cod-actions">
            <button type="button" className="btn flow" onClick={reset}>{t('cod.newRun')}</button>
          </div>
        </div>
      </Panel>
    )
  }

  const canApply = preview !== null && ((preview.reconciled ?? 0) > 0 || (preview.checked ?? 0) > 0)
  // Lote 126: las facturas se procesan con el cliente elegido (el número de factura se repite entre clientes)
  const needsClient = preview !== null && clientId === null && (preview.items ?? []).some((i) => i.kind === 'INVOICE' && i.orderNumber)
  return (
    <Panel icon={<IconCart />} title={t('cod.reconcile.title')}>
      <div className="pb">
        <p className="help">{t('cod.reconcile.help')}</p>
        <fieldset className="cod-mode">
          <legend>{t('cod.mode.label')}</legend>
          {RECONCILE_MODES.map((m) => (
            <label key={m} className="cod-mode-opt">
              <input type="radio" name="cod-mode" value={m} checked={mode === m} onChange={() => (changed(), setMode(m))} />
              <span>{t(`cod.mode.${m}`)}</span>
            </label>
          ))}
          <p className="help">{t(`cod.mode.help.${mode}`)}</p>
        </fieldset>
        <div className="f">
          <label htmlFor="cod-client">{t('cod.reconcile.client')}</label>
          <ClientPicker id="cod-client" value={clientId} onChange={(v) => (changed(), setClientId(v))} />
          <p className="help">{t('cod.reconcile.clientHelp')}</p>
        </div>

        <div
          className={`cod-drop${dragging ? ' on' : ''}`}
          onDragOver={(e) => (e.preventDefault(), setDragging(true))}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          data-testid="cod-drop"
        >
          <p>{t('cod.drop.text')}</p>
          <input
            ref={fileInput}
            type="file"
            multiple
            accept={ACCEPTED_TYPES}
            className="cod-file"
            aria-label={t('cod.drop.input')}
            onChange={(e) => {
              void addFiles(e.target.files ?? [])
              e.target.value = ''
            }}
          />
          <button type="button" className="btn" onClick={() => fileInput.current?.click()} disabled={reading}>{t('cod.drop.choose')}</button>
          <p className="help">{t('cod.drop.formats')}</p>
        </div>

        {files.length > 0 && (
          <ul className="cod-files" aria-label={t('cod.files')}>
            {files.map((s) => (
              <li key={s.key}>
                <b>{s.name}</b>{' '}
                {s.state === 'reading' ? (
                  <>
                    <Spinner /> {s.pages > 0 ? t('cod.file.reading', { page: s.page, pages: s.pages }) : t('cod.file.opening')}
                  </>
                ) : s.state === 'error' ? (
                  <span className="ferr">{t(`cod.file.error.${s.error}`)}</span>
                ) : (
                  <span>
                    {t('cod.file.done', { codes: s.codes, pages: s.pages })}
                    {s.emptyPages.length > 0 && <> · <span className="cod-warn">{t('cod.file.empty', { pages: s.emptyPages.join(', ') })}</span></>}
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}

        <div className="f">
          <label htmlFor="cod-manual">{t('cod.manual.label')}</label>
          <textarea id="cod-manual" rows={3} value={manual} onChange={(e) => setManual(e.target.value)} placeholder={t('cod.manual.placeholder')} />
          <button type="button" className="btn" onClick={addManual} disabled={manual.trim() === ''}>{t('cod.manual.add')}</button>
        </div>

        <h3 className="exp-h">{t('cod.codes.title', { n: rows.length })}</h3>
        <DataTable label={t('cod.codes.title', { n: rows.length })} columns={rowColumns} rows={rows} rowKey={(r) => String(r.key)} empty={t('cod.noCodes')} />

        {error && <p className="ferr" role="alert">{error}</p>}
        <div className="cod-actions">
          <button type="button" className="btn" disabled={rows.length === 0 || reading || reconcile.isPending} onClick={() => void run(true).catch(() => undefined)}>{t('cod.previewBtn')}</button>
          <button type="button" className="btn flow" disabled={!canApply || reconcile.isPending} onClick={() => (needsClient ? setError(t('cod.errors.clientRequired')) : setConfirming(true))}>{t('cod.apply')}</button>
          {rows.length > 0 && <button type="button" className="btn" onClick={reset}>{t('cod.clear')}</button>}
        </div>

        {preview && (
          <>
            <h3 className="exp-h">{t('cod.previewTitle')}</h3>
            <p className="help">{t('cod.previewHelp')}</p>
            {needsClient && <p className="cod-warn">{t('cod.errors.clientRequired')}</p>}
            <Summary result={preview} />
            <ResultsTable items={preview.items ?? []} label={t('cod.previewTitle')} />
          </>
        )}
      </div>
      <ConfirmDialog
        open={confirming}
        title={t('cod.confirm.title')}
        message={<p>{t('cod.confirm.body', { n: preview?.reconciled ?? 0, c: preview?.checked ?? 0 })}</p>}
        confirmLabel={t('cod.apply')}
        onConfirm={() => run(false)}
        onClose={() => setConfirming(false)}
      />
    </Panel>
  )
}

// ---------------------------------------------------------------- corridas

export function RunsTab() {
  const t = useT()
  const f = useFormat()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [open, setOpen] = useState<string | null>(null)
  const list = useCodRuns((page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<CodRunListItemDto>[]>(
    () => [
      { id: 'when', header: t('cod.col.when'), cell: (r) => f.dateTime(r.createdAtUtc ?? ''), exportValue: (r) => r.createdAtUtc ?? '', sortValue: (r) => r.createdAtUtc ?? '', card: 'title' },
      { id: 'source', header: t('cod.col.source'), cell: (r) => r.source ?? '—', exportValue: (r) => r.source ?? '', sortValue: (r) => r.source ?? '' },
      { id: 'total', header: t('cod.col.codes'), cell: (r) => r.total, exportValue: (r) => r.total ?? 0, sortValue: (r) => r.total ?? 0, align: 'end' },
      { id: 'reconciled', header: t('cod.col.reconciledCount'), cell: (r) => r.reconciled, exportValue: (r) => r.reconciled ?? 0, sortValue: (r) => r.reconciled ?? 0, align: 'end' },
      { id: 'issues', header: t('cod.col.issues'), cell: (r) => ((r.issues ?? 0) > 0 ? <Chip tone="warn">{r.issues}</Chip> : 0), exportValue: (r) => r.issues ?? 0, sortValue: (r) => r.issues ?? 0, align: 'end' },
      { id: 'by', header: t('cod.col.by'), cell: (r) => r.createdByName ?? '—', exportValue: (r) => r.createdByName ?? '', sortValue: (r) => r.createdByName ?? '' },
    ],
    [t, f],
  )
  return (
    <>
      <Panel flush icon={<IconCart />} title={t('cod.runs.panel')} badge={list.isPending ? undefined : list.data?.total}>
        <div className="pb">
          {list.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('cod.runs.panel')}
              columns={columns}
              rows={list.data?.items ?? []}
              rowKey={(r) => r.publicId ?? ''}
              page={page}
              pageSize={pageSize}
              total={list.data?.total ?? 0}
              onPage={setPage}
              onPageSize={(s) => (setPageSize(s), setPage(1))}
              onRowClick={(r) => setOpen(r.publicId!)}
              empty={t('cod.runs.empty')}
            />
          )}
        </div>
      </Panel>
      {open && <RunModal id={open} onClose={() => setOpen(null)} />}
    </>
  )
}

function RunModal({ id, onClose }: { id: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const run = useCodRun(id)
  const data = run.data
  return (
    <Modal open size="lg" title={t('cod.run.title', { date: data ? f.dateTime(data.createdAtUtc ?? '') : '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('cod.close')}</button>}>
      <div className="pb">
        {run.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(run.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <p className="help">
              {[data.source, data.createdByName].filter(Boolean).join(' · ')}
            </p>
            <Summary result={{ dryRun: false, total: data.total, reconciled: data.reconciled, issues: data.issues, items: data.items, checked: data.checked }} />
            <ResultsTable items={data.items ?? []} label={t('cod.runs.panel')} />
          </>
        )}
      </div>
    </Modal>
  )
}
