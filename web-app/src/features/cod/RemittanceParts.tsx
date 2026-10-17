// COD (Lote 87): remesas al cliente. La lista, la comisión por omisión de la compañía, «Nueva remesa» (cliente y período: se ven los cobros cuadrados que entrarían y la comisión), y la ficha con el flujo
// cuadrar → aprobar (reautenticación) → remitir, cancelar y exportar con plantilla. Ver pide cod.view; todo lo demás, cod.remit. Teikem calcula y exporta; no emite el pago.
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, DataTable, Modal, Panel, QBox, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import { downloadExportFile } from '../billing/exportFile'
import {
  exportRemittance,
  remittancePageReader,
  useCandidates,
  useCodSettings,
  useRemittance,
  useRemittanceMutations,
  useRemittances,
  useRemittanceTemplates,
  type CodRemittanceDto,
  type CodRemittanceLineDto,
  type CodRemittanceListItemDto,
} from './codApi'
import {
  canApprove,
  canCancel,
  canExport,
  canRemit,
  canReview,
  commissionSourceKey,
  defaultPctError,
  defaultPeriod,
  newRemittanceError,
  parsePct,
  REMITTANCE_STATUSES,
} from './remittanceRules'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

function pctText(pct: number | null | undefined): string {
  return pct === null || pct === undefined ? '—' : `${pct}%`
}

// ---------------------------------------------------------------- lista

export function RemittancesTab({ onOpen, onNew }: { onOpen: (id: string) => void; onNew: () => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useRemittances(status, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<CodRemittanceListItemDto>[]>(
    () => [
      { id: 'number', header: t('cod.rem.col.number'), cell: (r) => <b>{r.number}</b>, exportValue: (r) => r.number ?? '', sortValue: (r) => r.number ?? '', card: 'title' },
      { id: 'client', header: t('cod.col.client'), cell: (r) => r.clientName, exportValue: (r) => r.clientName ?? '', sortValue: (r) => r.clientName ?? '' },
      { id: 'period', header: t('cod.rem.col.period'), cell: (r) => `${f.date(r.periodStart ?? '')} – ${f.date(r.periodEnd ?? '')}`, exportValue: (r) => `${r.periodStart} ${r.periodEnd}`, sortValue: (r) => r.periodStart ?? '' },
      { id: 'lines', header: t('cod.rem.col.lines'), cell: (r) => r.lineCount, exportValue: (r) => r.lineCount ?? 0, sortValue: (r) => r.lineCount ?? 0, align: 'end' },
      { id: 'gross', header: t('cod.rem.col.gross'), cell: (r) => f.money(r.grossCollected ?? 0), exportValue: (r) => r.grossCollected ?? 0, sortValue: (r) => r.grossCollected ?? 0, align: 'end' },
      { id: 'commission', header: t('cod.rem.col.commission'), cell: (r) => f.money(r.commissionAmount ?? 0), exportValue: (r) => r.commissionAmount ?? 0, sortValue: (r) => r.commissionAmount ?? 0, align: 'end' },
      { id: 'net', header: t('cod.rem.col.net'), cell: (r) => <b>{f.money(r.netRemitted ?? 0)}</b>, exportValue: (r) => r.netRemitted ?? 0, sortValue: (r) => r.netRemitted ?? 0, align: 'end' },
      {
        id: 'status',
        header: t('cod.col.status'),
        cell: (r) => <Chip color={r.statusColor ?? undefined}>{r.statusLabel}</Chip>,
        exportValue: (r) => r.statusLabel ?? '',
        sortValue: (r) => r.statusLabel ?? '',
      },
    ],
    [t, f],
  )
  return (
    <>
      <Panel
        flush
        icon={<IconCart />}
        title={t('cod.rem.panel')}
        badge={list.isPending ? undefined : list.data?.total}
        actions={
          <Can perm="cod.remit">
            <button type="button" className="btn flow" onClick={onNew}>{t('cod.rem.new')}</button>
          </Can>
        }
      >
        <div className="pb">
          <div className="qrow tax-filters">
            <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('cod.rem.search')} />
            <select aria-label={t('cod.rem.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
              <option value="">{t('cod.orders.allStatuses')}</option>
              {REMITTANCE_STATUSES.map((s) => (
                <option key={s} value={s}>{t(`cod.rem.statuses.${s}`)}</option>
              ))}
            </select>
          </div>
          {list.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('cod.rem.panel')}
              columns={columns}
              rows={list.data?.items ?? []}
              rowKey={(r) => r.publicId ?? ''}
              page={page}
              pageSize={pageSize}
              total={list.data?.total ?? 0}
              onPage={setPage}
              onPageSize={(s) => (setPageSize(s), setPage(1))}
              onRowClick={(r) => onOpen(r.publicId!)}
              empty={t('cod.rem.empty')}
              exportRows={() => fetchAllPages<CodRemittanceListItemDto>(remittancePageReader(status, search))}
            />
          )}
        </div>
      </Panel>
      <DefaultCommission />
    </>
  )
}

/** La comisión por omisión de la compañía: la usa una remesa cuando el contrato del cliente no trae la suya. La cambia quien puede remitir. */
function DefaultCommission() {
  const t = useT()
  const settings = useCodSettings()
  const { updateSettings } = useRemittanceMutations()
  const canEdit = useCan('cod.remit')
  const [text, setText] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  if (!settings.data) return null
  const shown = text ?? String(settings.data.defaultCommissionPct ?? '')
  const save = async () => {
    if (defaultPctError(shown)) return setError(t('cod.rem.defaultPctError'))
    setError(null)
    try {
      await updateSettings.mutateAsync(parsePct(shown).value ?? 0)
      setText(null)
      toast.success(t('cod.rem.defaultPctSaved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <div className="ret-window">
      <div className="f">
        <label htmlFor="cod-default-pct">{t('cod.rem.defaultPct')}</label>
        <input id="cod-default-pct" inputMode="decimal" value={shown} disabled={!canEdit} onChange={(e) => setText(e.target.value)} />
      </div>
      {canEdit && <button type="button" className="btn" onClick={() => void save()} disabled={updateSettings.isPending}>{t('cod.rem.defaultPctSave')}</button>}
      <p className="help">{t('cod.rem.defaultPctHelp')}</p>
      {error && <p className="ferr" role="alert">{error}</p>}
    </div>
  )
}

// ---------------------------------------------------------------- líneas

function useLineColumns(): DataColumn<CodRemittanceLineDto>[] {
  const t = useT()
  const f = useFormat()
  return useMemo(
    () => [
      { id: 'order', header: t('cod.col.order'), cell: (l) => <b>{l.orderNumber}</b>, exportValue: (l) => l.orderNumber ?? '', sortValue: (l) => l.orderNumber ?? '', card: 'title' as const },
      { id: 'invoice', header: t('cod.col.invoice'), cell: (l) => l.invoiceNumber ?? '—', exportValue: (l) => l.invoiceNumber ?? '', sortValue: (l) => l.invoiceNumber ?? '' },
      { id: 'when', header: t('cod.col.when'), cell: (l) => f.date(l.collectedAtUtc ?? ''), sortValue: (l) => l.collectedAtUtc ?? '' },
      { id: 'method', header: t('cod.col.method'), cell: (l) => [l.methodLabel, l.checkNumber].filter(Boolean).join(' · '), sortValue: (l) => l.methodLabel ?? '' },
      { id: 'amount', header: t('cod.rem.col.gross'), cell: (l) => f.money(l.amountCollected ?? 0), exportValue: (l) => l.amountCollected ?? 0, sortValue: (l) => l.amountCollected ?? 0, align: 'end' as const },
      { id: 'commission', header: t('cod.rem.col.commission'), cell: (l) => f.money(l.commissionAmount ?? 0), exportValue: (l) => l.commissionAmount ?? 0, sortValue: (l) => l.commissionAmount ?? 0, align: 'end' as const },
      { id: 'net', header: t('cod.rem.col.net'), cell: (l) => f.money(l.netAmount ?? 0), exportValue: (l) => l.netAmount ?? 0, sortValue: (l) => l.netAmount ?? 0, align: 'end' as const },
    ],
    [t, f],
  )
}

function Totals({ gross, commission, net, pct }: { gross: number; commission: number; net: number; pct: number | null | undefined }) {
  const t = useT()
  const f = useFormat()
  return (
    <dl className="cod-meta cod-totals">
      <div><dt>{t('cod.rem.col.gross')}</dt><dd>{f.money(gross)}</dd></div>
      <div><dt>{t('cod.rem.commissionWith', { pct: pctText(pct) })}</dt><dd>{f.money(commission)}</dd></div>
      <div><dt>{t('cod.rem.col.net')}</dt><dd><b>{f.money(net)}</b></dd></div>
    </dl>
  )
}

// ---------------------------------------------------------------- nueva remesa

export function NewRemittanceModal({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const t = useT()
  const { create } = useRemittanceMutations()
  const initial = useMemo(() => defaultPeriod(new Date()), [])
  const [clientId, setClientId] = useState<string | null>(null)
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const [pct, setPct] = useState('')
  const [error, setError] = useState<string | null>(null)
  const problem = newRemittanceError(clientId, from, to, pct)
  const candidates = useCandidates(clientId, from, to, parsePct(pct).value, problem === null)
  const columns = useLineColumns()
  const data = candidates.data
  const lines = data?.lines ?? []
  const save = async () => {
    if (problem) return setError(t(`cod.rem.errors.${problem}`))
    setError(null)
    try {
      const saved = await create.mutateAsync({ clientPublicId: clientId!, periodStart: from, periodEnd: to, commissionPct: parsePct(pct).value ?? undefined })
      toast.success(t('cod.rem.created', { number: saved.number ?? '' }))
      onCreated(saved.publicId!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="lg"
      title={t('cod.rem.newTitle')}
      onClose={onClose}
      dismissible={!create.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={create.isPending}>{t('cod.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={create.isPending || problem !== null || lines.length === 0}>{create.isPending ? t('common.loading') : t('cod.rem.create')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('cod.rem.newHelp')}</p>
        <div className="f">
          <label htmlFor="rem-client">{t('cod.col.client')}</label>
          <ClientPicker id="rem-client" value={clientId} onChange={(v) => (setError(null), setClientId(v))} />
        </div>
        <div className="cod-dates">
          <div className="f">
            <label htmlFor="rem-from">{t('cod.rem.from')}</label>
            <input id="rem-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="f">
            <label htmlFor="rem-to">{t('cod.rem.to')}</label>
            <input id="rem-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <div className="f">
            <label htmlFor="rem-pct">{t('cod.rem.pct')}</label>
            <input id="rem-pct" inputMode="decimal" value={pct} placeholder={t('cod.rem.pctPlaceholder')} onChange={(e) => setPct(e.target.value)} />
          </div>
        </div>
        {problem && clientId && <p className="ferr" role="alert">{t(`cod.rem.errors.${problem}`)}</p>}
        {problem === null && candidates.isPending && <Spinner block />}
        {candidates.isError && <p className="ferr" role="alert">{applyProblemDetails(candidates.error).title}</p>}
        {data && (
          <>
            <h3 className="exp-h">{t('cod.rem.candidates', { n: lines.length })}</h3>
            <p className="help">{t(`cod.rem.source.${commissionSourceKey(data.commissionSource)}`, { pct: pctText(data.commissionPct) })}</p>
            <Totals gross={data.grossCollected ?? 0} commission={data.commissionAmount ?? 0} net={data.netRemitted ?? 0} pct={data.commissionPct} />
            <DataTable label={t('cod.rem.candidates', { n: lines.length })} columns={columns} rows={lines} rowKey={(l) => String(l.collectionId)} empty={t('cod.rem.noCandidates')} />
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- ficha

export function RemittanceModal({ id, onClose }: { id: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const rem = useRemittance(id)
  const { review, approve, cancel } = useRemittanceMutations()
  const [dialog, setDialog] = useState<'remit' | 'cancel' | null>(null)
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const columns = useLineColumns()
  const data = rem.data

  const run = async (p: Promise<unknown>, ok: string) => {
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
      title={t('cod.rem.title', { number: data?.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {data && canCancel(data.status) && (
            <Can perm="cod.remit">
              <button type="button" className="btn danger" onClick={() => setDialog('cancel')}>{t('cod.rem.cancel')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('cod.close')}</button>
          {data && canExport(data.status) && (
            <Can perm="cod.remit">
              <button type="button" className="btn" onClick={() => setExporting(true)}>{t('cod.rem.export')}</button>
            </Can>
          )}
          {data && canReview(data.status) && (
            <Can perm="cod.remit">
              <button type="button" className="btn flow" disabled={review.isPending} onClick={() => void run(review.mutateAsync(id), t('cod.rem.reviewed'))}>{t('cod.rem.review')}</button>
            </Can>
          )}
          {data && canApprove(data.status) && (
            <Can perm="cod.remit">
              <button type="button" className="btn flow" disabled={approve.isPending} onClick={() => void run(approve.mutateAsync(id), t('cod.rem.approved'))}>{t('cod.rem.approve')}</button>
            </Can>
          )}
          {data && canRemit(data.status) && (
            <Can perm="cod.remit">
              <button type="button" className="btn flow" onClick={() => setDialog('remit')}>{t('cod.rem.remit')}</button>
            </Can>
          )}
        </>
      }
    >
      <div className="pb">
        {rem.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(rem.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="cod-meta">
              <div><dt>{t('cod.col.status')}</dt><dd><Chip color={data.statusColor ?? undefined}>{data.statusLabel}</Chip></dd></div>
              <div><dt>{t('cod.col.client')}</dt><dd>{data.clientName}</dd></div>
              <div><dt>{t('cod.rem.col.period')}</dt><dd>{f.date(data.periodStart ?? '')} – {f.date(data.periodEnd ?? '')}</dd></div>
              <div><dt>{t('cod.rem.currency')}</dt><dd>{data.currency}</dd></div>
              {data.approvedAtUtc && <div><dt>{t('cod.rem.approvedAt')}</dt><dd>{f.dateTime(data.approvedAtUtc)}{data.approvedByName ? ` · ${data.approvedByName}` : ''}</dd></div>}
              {data.remittedAtUtc && <div><dt>{t('cod.rem.remittedAt')}</dt><dd>{f.dateTime(data.remittedAtUtc)}{data.exportRef ? ` · ${data.exportRef}` : ''}</dd></div>}
            </dl>
            <Totals gross={data.grossCollected ?? 0} commission={data.commissionAmount ?? 0} net={data.netRemitted ?? 0} pct={data.commissionPct} />
            <p className="help">{t(`cod.rem.hint.${data.status}`)}</p>
            <DataTable label={t('cod.rem.lines')} columns={columns} rows={data.lines ?? []} rowKey={(l) => String(l.id)} empty={t('cod.rem.noCandidates')} />
            {error && <p className="ferr" role="alert">{error}</p>}
          </>
        )}
      </div>
      {dialog === 'remit' && data && <RemitDialog remittance={data} onClose={() => setDialog(null)} />}
      <ConfirmDialog
        open={dialog === 'cancel'}
        title={t('cod.rem.cancelTitle', { number: data?.number ?? '' })}
        message={<p>{t('cod.rem.cancelBody')}</p>}
        confirmLabel={t('cod.rem.cancel')}
        tone="danger"
        onConfirm={async () => {
          await cancel.mutateAsync(id)
          toast.success(t('cod.rem.cancelled', { number: data?.number ?? '' }))
          onClose()
        }}
        onClose={() => setDialog(null)}
      />
      {exporting && data && <ExportDialog remittance={data} onClose={() => setExporting(false)} />}
    </Modal>
  )
}

function RemitDialog({ remittance, onClose }: { remittance: CodRemittanceDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { remit } = useRemittanceMutations()
  const [ref, setRef] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <Modal
      open
      title={t('cod.rem.remitTitle', { number: remittance.number ?? '' })}
      onClose={onClose}
      dismissible={!remit.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={remit.isPending}>{t('cod.close')}</button>
          <button
            type="button"
            className="btn flow"
            disabled={remit.isPending}
            onClick={async () => {
              if (ref.trim().length > 80) return setError(t('cod.rem.errors.ref'))
              setError(null)
              try {
                await remit.mutateAsync({ id: remittance.publicId!, exportRef: ref.trim() || undefined })
                toast.success(t('cod.rem.remitted', { number: remittance.number ?? '' }))
                onClose()
              } catch (e) {
                setError(applyProblemDetails(e).title)
              }
            }}
          >
            {t('cod.rem.remit')}
          </button>
        </>
      }
    >
      <div className="pb">
        <p>{t('cod.rem.remitBody', { net: f.money(remittance.netRemitted ?? 0), client: remittance.clientName ?? '' })}</p>
        <div className="f">
          <label htmlFor="rem-ref">{t('cod.rem.ref')}</label>
          <input id="rem-ref" value={ref} maxLength={80} autoComplete="off" onChange={(e) => setRef(e.target.value)} />
          <p className="help">{t('cod.rem.refHelp')}</p>
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

/** Exportar el archivo de la remesa (una fila por cobro). Con permisos de facturación se puede elegir la plantilla; sin ellos se usa la estándar. */
function ExportDialog({ remittance, onClose }: { remittance: CodRemittanceDto; onClose: () => void }) {
  const t = useT()
  const canTemplates = useCan('billing.view')
  const templates = useRemittanceTemplates(canTemplates)
  const [templateId, setTemplateId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const list = templates.data ?? []
  return (
    <Modal
      open
      title={t('cod.rem.exportTitle', { number: remittance.number ?? '' })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('cod.close')}</button>
          <button
            type="button"
            className="btn flow"
            disabled={busy}
            onClick={async () => {
              setBusy(true)
              setError(null)
              try {
                downloadExportFile(await exportRemittance(remittance.publicId!, templateId || null))
                toast.success(t('cod.rem.exported'))
                onClose()
              } catch (e) {
                setError(applyProblemDetails(e).title)
              } finally {
                setBusy(false)
              }
            }}
          >
            {busy ? t('common.loading') : t('cod.rem.download')}
          </button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('cod.rem.exportHelp')}</p>
        {list.length > 0 ? (
          <div className="f">
            <label htmlFor="rem-template">{t('cod.rem.template')}</label>
            <select id="rem-template" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
              <option value="">{t('cod.rem.templateDefault')}</option>
              {list.map((x) => (
                <option key={x.publicId} value={x.publicId}>{x.name}</option>
              ))}
            </select>
          </div>
        ) : (
          <p className="help">{t('cod.rem.templateStandard')}</p>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
