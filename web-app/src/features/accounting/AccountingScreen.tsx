// Contabilidad → Contabilización de compras / de despachos (Lote 181, H1): el costo YA congelado de lo que entró (recibos confirmados) o salió (recolecciones y despachos) del inventario PROPIO, asentado por
// corridas CMP-##### / DSP-#####: generar (se ve antes lo que entraría) → aceptar → exportar con la plantilla de la compañía; cancelar libera las líneas. Teikem junta y exporta; no emite el asiento contable.
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, type DataColumn, DataTable, DateField, Modal, Panel, QBox, Spinner, toast } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import { downloadExportFile } from '../billing/exportFile'
import { TemplateEditor } from '../billing/ExportParts'
import { useExportTemplates } from '../billing/billingApi'
import {
  exportRun,
  runPageReader,
  useExportFilePreview,
  useRun,
  useRunMutations,
  useRunPreview,
  useRuns,
  accountingKeys,
  type AccountingLineDto,
  type AccountingRunListItemDto,
} from './accountingApi'
import { ACCOUNTING_KINDS, ACCOUNTING_STATUSES, canAccept, canCancel, canExport, defaultPeriod, newRunError, PREVIEW_LINES_SHOWN, type AccountingKind } from './accountingRules'
import { useQueryClient } from '@tanstack/react-query'
import '../billing/billing.css'
import './accounting.css'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

// ---------------------------------------------------------------- líneas

/** Las líneas (con el costo congelado). Sin costo: la fila se marca y el costo queda en blanco. */
function LinesTable({ kind, lines, total }: { kind: AccountingKind; lines: readonly AccountingLineDto[]; total?: number }) {
  const t = useT()
  const f = useFormat()
  const shown = lines.slice(0, PREVIEW_LINES_SHOWN)
  const k = ACCOUNTING_KINDS[kind].key
  if (lines.length === 0) return <p className="help">{t('acct.noLines')}</p>
  return (
    <>
      <div className="acct-lines" role="region" aria-label={t('acct.lines')} tabIndex={0}>
        <table className="acct-table">
          <thead>
            <tr>
              <th>{t('acct.line.date')}</th>
              <th>{t(`acct.${k}.document`)}</th>
              <th>{t(`acct.${k}.reference`)}</th>
              <th>{t(`acct.${k}.party`)}</th>
              <th>{t('acct.line.product')}</th>
              <th className="num">{t('acct.line.qty')}</th>
              <th className="num">{t('acct.line.unitCost')}</th>
              <th className="num">{t('acct.line.totalCost')}</th>
            </tr>
          </thead>
          <tbody>
            {shown.map((l, i) => (
              <tr key={l.id || i} className={l.unitCost == null ? 'acct-nocost' : undefined}>
                <td>{f.date(l.docDate ?? '')}</td>
                <td>{l.documentNumber}</td>
                <td>{l.referenceNumber ?? '—'}</td>
                <td>{l.partyName ?? '—'}</td>
                <td>
                  <b>{l.sku}</b> {l.productName}
                  {l.lotNumber ? ` · ${t('acct.line.lot')} ${l.lotNumber}` : ''}
                </td>
                <td className="num">{f.number(l.quantity ?? 0)}</td>
                <td className="num">{l.unitCost == null ? t('acct.line.noCost') : f.number(l.unitCost, { maximumFractionDigits: 4 })}</td>
                <td className="num">{l.totalCost == null ? '—' : <b>{f.money(l.totalCost)}</b>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {lines.length > shown.length && <p className="acct-more">{t('acct.moreLines', { shown: shown.length, total: total ?? lines.length })}</p>}
    </>
  )
}

function Totals({ count, total, unpriced }: { count: number; total: number; unpriced: number }) {
  const t = useT()
  const f = useFormat()
  return (
    <>
      <dl className="acct-meta">
        <div><dt>{t('acct.col.lines')}</dt><dd>{f.number(count)}</dd></div>
        <div><dt>{t('acct.col.total')}</dt><dd><b>{f.money(total)}</b></dd></div>
      </dl>
      {unpriced > 0 && <p className="acct-warn" role="status">{t('acct.unpriced', { n: unpriced })}</p>}
    </>
  )
}

// ---------------------------------------------------------------- lista

function RunsTab({ kind, onOpen, onNew }: { kind: AccountingKind; onOpen: (id: string) => void; onNew: () => void }) {
  const t = useT()
  const f = useFormat()
  const cfg = ACCOUNTING_KINDS[kind]
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useRuns(kind, status, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<AccountingRunListItemDto>[]>(
    () => [
      { id: 'number', header: t('acct.col.number'), cell: (r) => <b>{r.number}</b>, exportValue: (r) => r.number ?? '', sortValue: (r) => r.number ?? '', card: 'title' },
      { id: 'period', header: t('acct.col.period'), cell: (r) => `${f.date(r.periodStart ?? '')} – ${f.date(r.periodEnd ?? '')}`, exportValue: (r) => `${r.periodStart} ${r.periodEnd}`, sortValue: (r) => r.periodStart ?? '' },
      { id: 'lines', header: t('acct.col.lines'), cell: (r) => r.lineCount, exportValue: (r) => r.lineCount ?? 0, sortValue: (r) => r.lineCount ?? 0, align: 'end' },
      { id: 'unpriced', header: t('acct.col.unpriced'), cell: (r) => (r.unpricedLineCount ? <Chip tone="warn">{r.unpricedLineCount}</Chip> : '—'), exportValue: (r) => r.unpricedLineCount ?? 0, sortValue: (r) => r.unpricedLineCount ?? 0, align: 'end' },
      { id: 'total', header: t('acct.col.total'), cell: (r) => <b>{f.money(r.totalAmount ?? 0)}</b>, exportValue: (r) => r.totalAmount ?? 0, sortValue: (r) => r.totalAmount ?? 0, align: 'end' },
      { id: 'status', header: t('acct.col.status'), cell: (r) => <Chip color={r.statusColor ?? undefined}>{r.status}</Chip>, exportValue: (r) => r.status ?? '', sortValue: (r) => r.status ?? '' },
      { id: 'generated', header: t('acct.col.generated'), cell: (r) => f.dateTime(r.generatedAtUtc ?? ''), exportValue: (r) => r.generatedAtUtc ?? '', sortValue: (r) => r.generatedAtUtc ?? '' },
    ],
    [t, f],
  )
  return (
    <Panel
      flush
      icon={<IconCart />}
      title={t(`acct.${cfg.key}.panel`)}
      badge={list.isPending ? undefined : list.data?.total}
      actions={
        <Can perm={cfg.manage}>
          <button type="button" className="btn flow" onClick={onNew}>{t('acct.new')}</button>
        </Can>
      }
    >
      <div className="pb">
        <div className="qrow">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('acct.search')} />
          <select aria-label={t('acct.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
            <option value="">{t('acct.allStatuses')}</option>
            {ACCOUNTING_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`acct.statuses.${s}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t(`acct.${cfg.key}.panel`)}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(r) => r.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(r) => onOpen(r.publicId!)}
            empty={t('acct.empty')}
            exportRows={() => fetchAllPages<AccountingRunListItemDto>(runPageReader(kind, status, search))}
          />
        )}
      </div>
    </Panel>
  )
}

// ---------------------------------------------------------------- nueva corrida

function NewRunModal({ kind, onClose, onCreated }: { kind: AccountingKind; onClose: () => void; onCreated: (id: string) => void }) {
  const t = useT()
  const k = ACCOUNTING_KINDS[kind].key
  const { generate } = useRunMutations(kind)
  const initial = useMemo(() => defaultPeriod(new Date()), [])
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const problem = newRunError(from, to)
  const preview = useRunPreview(kind, from, to, problem === null)
  const data = preview.data
  const lines = data?.lines ?? []
  const save = async () => {
    if (problem) return setError(t(`acct.errors.${problem}`))
    setError(null)
    try {
      const saved = await generate.mutateAsync({ periodStart: from, periodEnd: to, note: note.trim() || undefined })
      toast.success(t('acct.created', { number: saved.number ?? '' }))
      onCreated(saved.publicId!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="lg"
      title={t(`acct.${k}.newTitle`)}
      onClose={onClose}
      dismissible={!generate.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={generate.isPending}>{t('acct.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={generate.isPending || problem !== null || lines.length === 0}>{generate.isPending ? t('common.loading') : t('acct.generate')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t(`acct.${k}.newHelp`)}</p>
        <div className="acct-dates">
          <div className="f">
            <label htmlFor="acct-from">{t('acct.from')}</label>
            <DateField id="acct-from" value={from} onChange={(v) => setFrom(v)} />
          </div>
          <div className="f">
            <label htmlFor="acct-to">{t('acct.to')}</label>
            <DateField id="acct-to" value={to} onChange={(v) => setTo(v)} />
          </div>
        </div>
        <div className="f">
          <label htmlFor="acct-note">{t('acct.note')}</label>
          <input id="acct-note" type="text" value={note} maxLength={500} autoComplete="off" onChange={(e) => setNote(e.target.value)} />
        </div>
        {problem && <p className="ferr" role="alert">{t(`acct.errors.${problem}`)}</p>}
        {problem === null && preview.isPending && <Spinner block />}
        {preview.isError && <p className="ferr" role="alert">{applyProblemDetails(preview.error).title}</p>}
        {data && (
          <>
            <h3 className="exp-h">{t('acct.preview', { n: data.lineCount ?? 0 })}</h3>
            <Totals count={data.lineCount ?? 0} total={data.totalAmount ?? 0} unpriced={data.unpricedLineCount ?? 0} />
            {lines.length === 0 ? <p className="help">{t('acct.nothing')}</p> : <LinesTable kind={kind} lines={lines} total={data.lineCount ?? lines.length} />}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- ficha

function RunModal({ kind, id, onClose }: { kind: AccountingKind; id: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const cfg = ACCOUNTING_KINDS[kind]
  const rec = useRun(kind, id)
  const { accept, cancel } = useRunMutations(kind)
  const [dialog, setDialog] = useState<'cancel' | null>(null)
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const data = rec.data
  const status = data?.statusCode

  const act = async (p: Promise<unknown>, ok: string) => {
    setError(null)
    try {
      await p
      toast.success(ok)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t(`acct.${cfg.key}.modalTitle`, { number: data?.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {data && canCancel(status) && (
            <Can perm={cfg.manage}>
              <button type="button" className="btn danger" onClick={() => setDialog('cancel')}>{t('acct.cancel')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('acct.close')}</button>
          {data && canAccept(status) && (
            <Can perm={cfg.manage}>
              <button type="button" className="btn flow" disabled={accept.isPending} onClick={() => void act(accept.mutateAsync(id), t('acct.accepted'))}>{accept.isPending ? t('common.loading') : t('acct.accept')}</button>
            </Can>
          )}
          {data && canExport(status) && (
            <Can perm={cfg.export}>
              <button type="button" className="btn flow" onClick={() => setExporting(true)}>{t('acct.export')}</button>
            </Can>
          )}
        </>
      }
    >
      <div className="pb">
        {rec.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(rec.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="acct-meta">
              <div><dt>{t('acct.col.status')}</dt><dd><Chip color={data.statusColor ?? undefined}>{data.status}</Chip></dd></div>
              <div><dt>{t('acct.col.period')}</dt><dd>{f.date(data.periodStart ?? '')} – {f.date(data.periodEnd ?? '')}</dd></div>
              <div><dt>{t('acct.generatedAt')}</dt><dd>{f.dateTime(data.generatedAtUtc ?? '')}{data.generatedByName ? ` · ${data.generatedByName}` : ''}</dd></div>
              {data.acceptedAtUtc && <div><dt>{t('acct.acceptedAt')}</dt><dd>{f.dateTime(data.acceptedAtUtc)}{data.acceptedByName ? ` · ${data.acceptedByName}` : ''}</dd></div>}
              {data.exportedAtUtc && <div><dt>{t('acct.exportedAt')}</dt><dd>{f.dateTime(data.exportedAtUtc)}{data.exportedByName ? ` · ${data.exportedByName}` : ''}</dd></div>}
              {data.note && <div><dt>{t('acct.note')}</dt><dd>{data.note}</dd></div>}
            </dl>
            <Totals count={data.lineCount ?? 0} total={data.totalAmount ?? 0} unpriced={data.unpricedLineCount ?? 0} />
            <LinesTable kind={kind} lines={data.lines ?? []} />
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
      <ConfirmDialog
        open={dialog === 'cancel'}
        title={t('acct.cancelTitle', { number: data?.number ?? '' })}
        message={<p>{t('acct.cancelBody')}</p>}
        confirmLabel={t('acct.cancel')}
        tone="danger"
        onConfirm={async () => {
          await act(cancel.mutateAsync({ id }), t('acct.cancelled'))
          setDialog(null)
        }}
        onClose={() => setDialog(null)}
      />
      {exporting && data && <ExportDialog kind={kind} runId={id} number={data.number ?? ''} first={status === 'ACCEPTED'} onClose={() => setExporting(false)} />}
    </Modal>
  )
}

// ---------------------------------------------------------------- exportar

/** Exportar el archivo de la corrida (una fila por línea): elegir la plantilla, configurarla, ver el archivo y descargarlo (la primera descarga pasa la corrida de aceptada a exportada). */
function ExportDialog({ kind, runId, number, first, onClose }: { kind: AccountingKind; runId: string; number: string; first: boolean; onClose: () => void }) {
  const t = useT()
  const cfg = ACCOUNTING_KINDS[kind]
  const qc = useQueryClient()
  const templates = useExportTemplates(cfg.scope)
  const canConfigure = useCan(cfg.export)
  const list = templates.data ?? []
  const [chosen, setChosen] = useState<string | null>(null)
  const selected = chosen && list.some((x) => x.publicId === chosen) ? chosen : (list[0]?.publicId ?? null)
  const current = list.find((x) => x.publicId === selected) ?? null
  const [configuring, setConfiguring] = useState(false)
  const [viewing, setViewing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const file = useExportFilePreview(kind, runId, selected, viewing && !!selected)

  const download = async () => {
    setBusy(true)
    setError(null)
    try {
      downloadExportFile(await exportRun(kind, runId, selected))
      await qc.invalidateQueries({ queryKey: accountingKeys.all(kind) })   // exportar pasa una aceptada a exportada
      toast.success(t(first ? 'acct.exported' : 'acct.downloaded', { number }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('acct.exportTitle', { number })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('acct.close')}</button>
          <button type="button" className="btn flow" onClick={() => void download()} disabled={busy || !selected}>{busy ? t('common.loading') : t(first ? 'acct.downloadMark' : 'acct.download')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('acct.exportHelp')}</p>
        {first && <p className="help">{t('acct.markHelp')}</p>}
        {templates.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(templates.error).title}</p>
        ) : templates.isPending ? (
          <Spinner />
        ) : (
          <div className="exp-row">
            <div className="f">
              <label htmlFor="acct-template">{t('acct.template')}</label>
              <select id="acct-template" value={selected ?? ''} onChange={(e) => (setChosen(e.target.value), setViewing(false))}>
                {list.map((x) => (
                  <option key={x.publicId} value={x.publicId}>{x.name}</option>
                ))}
              </select>
            </div>
            {canConfigure && <button type="button" className="btn" onClick={() => setConfiguring(true)} disabled={!current}>{t('billing.exp.configure')}</button>}
            <button type="button" className="btn" onClick={() => setViewing((v) => !v)} disabled={!current}>{viewing ? t('acct.hideFile') : t('acct.viewFile')}</button>
          </div>
        )}
        {viewing && (
          file.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(file.error).title}</p>
          ) : !file.data ? (
            <Spinner block />
          ) : (
            <>
              <p className="help">{t('acct.fileHelp', { file: file.data.fileName ?? '', rows: file.data.rowCount ?? 0, template: file.data.templateName ?? '' })}</p>
              <pre className="exp-pre" tabIndex={0} aria-label={t('billing.exp.fileText')}>{file.data.text}</pre>
            </>
          )
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
      {configuring && current && (
        <TemplateEditor
          scope={cfg.scope}
          current={current}
          canDeactivate={list.length > 1}
          onClose={() => setConfiguring(false)}
          onSaved={(id) => {
            setChosen(id)
            setViewing(false)
            setConfiguring(false)
          }}
        />
      )}
    </Modal>
  )
}

// ---------------------------------------------------------------- pantalla

export default function AccountingScreen({ kind }: { kind: AccountingKind }) {
  const t = useT()
  const k = ACCOUNTING_KINDS[kind].key
  const [openId, setOpenId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  return (
    <div className="acct-screen">
      <div className="head">
        <div>
          <h1>{t(`acct.${k}.title`)}</h1>
          <p>{t(`acct.${k}.subtitle`)}</p>
        </div>
      </div>
      <RunsTab kind={kind} onOpen={setOpenId} onNew={() => setCreating(true)} />
      {creating && (
        <NewRunModal
          kind={kind}
          onClose={() => setCreating(false)}
          onCreated={(id) => {
            setCreating(false)
            setOpenId(id)
          }}
        />
      )}
      {openId !== null && <RunModal key={openId} kind={kind} id={openId} onClose={() => setOpenId(null)} />}
    </div>
  )
}
