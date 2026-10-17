// Liquidación a choferes (Lote 96): la lista, «Nueva liquidación» (período y chofer; se ve lo que entraría), y la ficha con las líneas por día, el ajuste de una línea y el flujo revisar → aprobar
// (reautenticación) → exportar, cancelar. Ver pide driverpay.view; generar, revisar, ajustar y cancelar, driverpay.manage; aprobar, driverpay.approve; exportar, driverpay.export. Teikem calcula y exporta; no emite el pago.
import { useQueryClient } from '@tanstack/react-query'
import { Fragment, useEffect, useMemo, useState } from 'react'
import { Can, useCan, useModule } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, Modal, Panel, QBox, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import { downloadExportFile } from '../billing/exportFile'
import { useDrivers } from '../drivers/api'
import {
  exportSettlement,
  settlementKeys,
  settlementPageReader,
  useSettlement,
  useSettlementMutations,
  useSettlementPreview,
  useSettlements,
  useSettlementTemplates,
  type DriverSettlementDto,
  type DriverSettlementLineDto,
  type DriverSettlementListItemDto,
} from './settlementApi'
import { adjustError, canAdjust, canApprove, canCancel, canExport, canReview, defaultPeriod, groupByDay, newSettlementError, parseAmount, SETTLEMENT_STATUSES } from './settlementRules'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

/** El selector de chofer: con permiso de ver la flota lista los choferes activos; sin él, queda «todos» (la liquidación de todos los choferes). */
function DriverSelect({ id, value, onChange, allLabel }: { id: string; value: string; onChange: (v: string) => void; allLabel: string }) {
  const t = useT()
  const canFleet = useCan('fleet.view')
  const drivers = useDrivers(canFleet)
  const items = drivers.data ?? []
  return (
    <select id={id} aria-label={t('pay.driver')} value={value} onChange={(e) => onChange(e.target.value)} disabled={!canFleet && items.length === 0}>
      <option value="">{allLabel}</option>
      {items.map((d) => (
        <option key={d.publicId} value={d.publicId}>{[d.code, d.fullName].filter(Boolean).join(' · ')}</option>
      ))}
    </select>
  )
}

// ---------------------------------------------------------------- lista

export function SettlementsTab({ onOpen, onNew }: { onOpen: (id: string) => void; onNew: () => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [driver, setDriver] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useSettlements(status, driver, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<DriverSettlementListItemDto>[]>(
    () => [
      { id: 'number', header: t('pay.col.number'), cell: (r) => <b>{r.number}</b>, exportValue: (r) => r.number ?? '', sortValue: (r) => r.number ?? '', card: 'title' },
      { id: 'driver', header: t('pay.driver'), cell: (r) => (r.driverName ? `${r.driverCode} · ${r.driverName}` : t('pay.allDrivers')), exportValue: (r) => r.driverName ?? '', sortValue: (r) => r.driverName ?? '' },
      { id: 'period', header: t('pay.col.period'), cell: (r) => `${f.date(r.periodStart ?? '')} – ${f.date(r.periodEnd ?? '')}`, exportValue: (r) => `${r.periodStart} ${r.periodEnd}`, sortValue: (r) => r.periodStart ?? '' },
      { id: 'lines', header: t('pay.col.lines'), cell: (r) => r.lineCount, exportValue: (r) => r.lineCount ?? 0, sortValue: (r) => r.lineCount ?? 0, align: 'end' },
      { id: 'total', header: t('pay.col.total'), cell: (r) => <b>{f.money(r.totalAmount ?? 0)}</b>, exportValue: (r) => r.totalAmount ?? 0, sortValue: (r) => r.totalAmount ?? 0, align: 'end' },
      { id: 'status', header: t('pay.col.status'), cell: (r) => <Chip color={r.statusColor ?? undefined}>{r.status}</Chip>, exportValue: (r) => r.status ?? '', sortValue: (r) => r.status ?? '' },
    ],
    [t, f],
  )
  return (
    <Panel
      flush
      icon={<IconCart />}
      title={t('pay.panel')}
      badge={list.isPending ? undefined : list.data?.total}
      actions={
        <Can perm="driverpay.manage">
          <button type="button" className="btn flow" onClick={onNew}>{t('pay.new')}</button>
        </Can>
      }
    >
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('pay.search')} />
          <select aria-label={t('pay.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
            <option value="">{t('pay.allStatuses')}</option>
            {SETTLEMENT_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`pay.statuses.${s}`)}</option>
            ))}
          </select>
          <DriverSelect id="pay-filter-driver" value={driver} onChange={(v) => (setDriver(v), setPage(1))} allLabel={t('pay.allDrivers')} />
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('pay.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(r) => r.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(r) => onOpen(r.publicId!)}
            empty={t('pay.empty')}
            exportRows={() => fetchAllPages<DriverSettlementListItemDto>(settlementPageReader(status, driver, search))}
          />
        )}
      </div>
    </Panel>
  )
}

// ---------------------------------------------------------------- líneas

function breakdownText(l: DriverSettlementLineDto, f: ReturnType<typeof useFormat>, t: (k: string, p?: Record<string, string | number>) => string): string {
  return (l.breakdown ?? [])
    .map((b) => {
      const what = b.kind === 'ATTEMPT' ? t('pay.line.attempt', { n: b.attemptNumber ?? 0 }) : b.kind === 'TRIP' ? t('pay.line.tripPay') : t('pay.line.deliveryPay')
      return `${what} ${f.money(b.amount ?? 0)}${b.note ? ` (${b.note})` : ''}`
    })
    .join(' · ')
}

/** Las líneas de la liquidación, agrupadas por día del servicio (un solo encabezado de columnas, con el subtotal de cada día). */
function LinesTable({ lines, canEdit, onAdjust }: { lines: readonly DriverSettlementLineDto[]; canEdit: boolean; onAdjust: (l: DriverSettlementLineDto) => void }) {
  const t = useT()
  const f = useFormat()
  const groups = useMemo(() => groupByDay(lines), [lines])
  if (lines.length === 0) return <p className="help">{t('pay.noLines')}</p>
  return (
    <div className="pay-lines" role="region" aria-label={t('pay.lines')}>
      <table className="pay-table">
        <thead>
          <tr>
            <th>{t('pay.driver')}</th>
            <th>{t('pay.line.detail')}</th>
            <th className="num">{t('pay.line.attempts')}</th>
            <th>{t('pay.line.breakdown')}</th>
            <th className="num">{t('pay.line.calculated')}</th>
            <th className="num">{t('pay.line.amount')}</th>
            {canEdit && <th aria-label={t('pay.line.adjust')} />}
          </tr>
        </thead>
        <tbody>
          {groups.map((g) => (
            <Fragment key={g.date}>
              <tr className="pay-day">
                <th colSpan={canEdit ? 7 : 6} scope="colgroup">
                  {f.date(g.date)} <span className="pay-sub">{t('pay.line.subtotal', { amount: f.money(g.subtotal) })}</span>
                </th>
              </tr>
              {g.lines.map((l) => (
                <tr key={l.id} className={l.rateMissing ? 'pay-missing' : undefined}>
                  <td>{[l.driverCode, l.driverName].filter(Boolean).join(' · ')}</td>
                  <td>{l.kind === 'TRIP' ? <span className="pay-kind">{t('pay.line.trip')}</span> : null}{l.description}</td>
                  <td className="num">{l.kind === 'DELIVERY' ? l.attemptCount : '—'}</td>
                  <td className="pay-break">{breakdownText(l, f, t)}</td>
                  <td className="num">{f.money(l.calculatedAmount ?? 0)}</td>
                  <td className="num">
                    <b>{f.money(l.amount ?? 0)}</b>
                    {l.adjusted && <div className="pay-adj" title={l.adjustmentNote ?? ''}>{t('pay.line.adjusted')}{l.adjustmentNote ? `: ${l.adjustmentNote}` : ''}</div>}
                  </td>
                  {canEdit && (
                    <td>
                      <button type="button" className="btn sm" onClick={() => onAdjust(l)}>{t('pay.line.adjust')}</button>
                    </td>
                  )}
                </tr>
              ))}
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function Totals({ total, deliveries, trips }: { total: number; deliveries: number; trips: number }) {
  const t = useT()
  const f = useFormat()
  return (
    <dl className="pay-meta pay-totals">
      <div><dt>{t('pay.deliveries')}</dt><dd>{f.money(deliveries)}</dd></div>
      <div><dt>{t('pay.trips')}</dt><dd>{f.money(trips)}</dd></div>
      <div><dt>{t('pay.col.total')}</dt><dd><b>{f.money(total)}</b></dd></div>
    </dl>
  )
}

// ---------------------------------------------------------------- nueva liquidación

export function NewSettlementModal({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const t = useT()
  const { generate } = useSettlementMutations()
  const initial = useMemo(() => defaultPeriod(new Date()), [])
  const [driver, setDriver] = useState('')
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const problem = newSettlementError(from, to)
  const preview = useSettlementPreview(driver, from, to, problem === null)
  const data = preview.data
  const lines = data?.lines ?? []
  const save = async () => {
    if (problem) return setError(t(`pay.errors.${problem}`))
    setError(null)
    try {
      const saved = await generate.mutateAsync({ driverPublicId: driver || undefined, periodStart: from, periodEnd: to, note: note.trim() || undefined })
      toast.success(t('pay.created', { number: saved.number ?? '' }))
      onCreated(saved.publicId!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="lg"
      title={t('pay.newTitle')}
      onClose={onClose}
      dismissible={!generate.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={generate.isPending}>{t('pay.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={generate.isPending || problem !== null || lines.length === 0}>{generate.isPending ? t('common.loading') : t('pay.generate')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('pay.newHelp')}</p>
        <div className="pay-dates">
          <div className="f">
            <label htmlFor="pay-driver">{t('pay.driver')}</label>
            <DriverSelect id="pay-driver" value={driver} onChange={setDriver} allLabel={t('pay.allDrivers')} />
          </div>
          <div className="f">
            <label htmlFor="pay-from">{t('pay.from')}</label>
            <input id="pay-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="f">
            <label htmlFor="pay-to">{t('pay.to')}</label>
            <input id="pay-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
        </div>
        <div className="f">
          <label htmlFor="pay-note">{t('pay.note')}</label>
          <input id="pay-note" value={note} maxLength={500} autoComplete="off" onChange={(e) => setNote(e.target.value)} />
        </div>
        {problem && <p className="ferr" role="alert">{t(`pay.errors.${problem}`)}</p>}
        {problem === null && preview.isPending && <Spinner block />}
        {preview.isError && <p className="ferr" role="alert">{applyProblemDetails(preview.error).title}</p>}
        {data && (
          <>
            <h3 className="exp-h">{t('pay.preview', { n: lines.length })}</h3>
            <p className="help">{t('pay.formulaIs', { formula: t(`pay.formulas.${data.payoutFormulaCode}`) })}</p>
            <Totals
              total={data.totalAmount ?? 0}
              deliveries={lines.filter((l) => l.kind === 'DELIVERY').reduce((s, l) => s + (l.amount ?? 0), 0)}
              trips={lines.filter((l) => l.kind === 'TRIP').reduce((s, l) => s + (l.amount ?? 0), 0)}
            />
            {(data.skippedNoDriver ?? 0) > 0 && <p className="pay-warn" role="status">{t('pay.skipped', { n: data.skippedNoDriver ?? 0 })}</p>}
            {lines.some((l) => l.rateMissing) && <p className="pay-warn" role="status">{t('pay.rateMissing')}</p>}
            {lines.length === 0 ? <p className="help">{t('pay.nothing')}</p> : <LinesTable lines={lines} canEdit={false} onAdjust={() => undefined} />}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- ficha

export function SettlementModal({ id, onClose }: { id: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const rec = useSettlement(id)
  const canManage = useCan('driverpay.manage')
  const { review, approve, cancel } = useSettlementMutations()
  const [dialog, setDialog] = useState<'cancel' | null>(null)
  const [adjusting, setAdjusting] = useState<DriverSettlementLineDto | null>(null)
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const data = rec.data
  const status = data?.statusCode

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
      title={t('pay.modalTitle', { number: data?.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {data && canCancel(status) && (
            <Can perm="driverpay.manage">
              <button type="button" className="btn danger" onClick={() => setDialog('cancel')}>{t('pay.cancel')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('pay.close')}</button>
          {data && canExport(status) && (
            <Can perm="driverpay.export">
              <button type="button" className="btn" onClick={() => setExporting(true)}>{t('pay.export')}</button>
            </Can>
          )}
          {data && canReview(status) && (
            <Can perm="driverpay.manage">
              <button type="button" className="btn flow" disabled={review.isPending} onClick={() => void run(review.mutateAsync(id), t('pay.reviewed'))}>{t('pay.review')}</button>
            </Can>
          )}
          {data && canApprove(status) && (
            <Can perm="driverpay.approve">
              <button type="button" className="btn flow" disabled={approve.isPending} onClick={() => void run(approve.mutateAsync(id), t('pay.approved'))}>{t('pay.approve')}</button>
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
            <dl className="pay-meta">
              <div><dt>{t('pay.col.status')}</dt><dd><Chip color={data.statusColor ?? undefined}>{data.status}</Chip></dd></div>
              <div><dt>{t('pay.driver')}</dt><dd>{data.driverName ? `${data.driverCode} · ${data.driverName}` : t('pay.allDrivers')}</dd></div>
              <div><dt>{t('pay.col.period')}</dt><dd>{f.date(data.periodStart ?? '')} – {f.date(data.periodEnd ?? '')}</dd></div>
              <div><dt>{t('pay.formula')}</dt><dd>{t(`pay.formulas.${data.payoutFormulaCode}`)}</dd></div>
              <div><dt>{t('pay.currency')}</dt><dd>{data.currency}</dd></div>
              {data.note && <div><dt>{t('pay.note')}</dt><dd>{data.note}</dd></div>}
              {data.reviewedAtUtc && <div><dt>{t('pay.reviewedAt')}</dt><dd>{f.dateTime(data.reviewedAtUtc)}{data.reviewedBy ? ` · ${data.reviewedBy}` : ''}</dd></div>}
              {data.approvedAtUtc && <div><dt>{t('pay.approvedAt')}</dt><dd>{f.dateTime(data.approvedAtUtc)}{data.approvedBy ? ` · ${data.approvedBy}` : ''}</dd></div>}
              {data.exportedAtUtc && <div><dt>{t('pay.exportedAt')}</dt><dd>{f.dateTime(data.exportedAtUtc)}</dd></div>}
            </dl>
            <Totals total={data.totalAmount ?? 0} deliveries={data.deliveriesAmount ?? 0} trips={data.tripsAmount ?? 0} />
            {(data.skippedNoDriver ?? 0) > 0 && <p className="pay-warn" role="status">{t('pay.skipped', { n: data.skippedNoDriver ?? 0 })}</p>}
            <p className="help">{t(`pay.hint.${status}`)}</p>
            <LinesTable lines={data.lines ?? []} canEdit={canManage && canAdjust(status)} onAdjust={setAdjusting} />
            {error && <p className="ferr" role="alert">{error}</p>}
          </>
        )}
      </div>
      {adjusting && data && <AdjustDialog settlement={data} line={adjusting} onClose={() => setAdjusting(null)} />}
      <ConfirmDialog
        open={dialog === 'cancel'}
        title={t('pay.cancelTitle', { number: data?.number ?? '' })}
        message={<p>{t('pay.cancelBody')}</p>}
        confirmLabel={t('pay.cancel')}
        tone="danger"
        onConfirm={async () => {
          await cancel.mutateAsync({ id })
          toast.success(t('pay.cancelled', { number: data?.number ?? '' }))
          onClose()
        }}
        onClose={() => setDialog(null)}
      />
      {exporting && data && <ExportDialog settlement={data} onClose={() => setExporting(false)} />}
    </Modal>
  )
}

/** Ajuste manual del monto de una línea: el calculado se conserva y el motivo es obligatorio si el monto cambia. */
function AdjustDialog({ settlement, line, onClose }: { settlement: DriverSettlementDto; line: DriverSettlementLineDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { adjust } = useSettlementMutations()
  const calculated = line.calculatedAmount ?? 0
  const [amount, setAmount] = useState(String(line.amount ?? 0))
  const [note, setNote] = useState(line.adjustmentNote ?? '')
  const [error, setError] = useState<string | null>(null)
  return (
    <Modal
      open
      title={t('pay.adjustTitle', { description: line.description ?? '' })}
      onClose={onClose}
      dismissible={!adjust.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={adjust.isPending}>{t('pay.close')}</button>
          <button
            type="button"
            className="btn flow"
            disabled={adjust.isPending}
            onClick={async () => {
              const problem = adjustError(amount, note, calculated)
              if (problem) return setError(t(`pay.errors.adjust.${problem}`))
              setError(null)
              try {
                await adjust.mutateAsync({ id: settlement.publicId!, lineId: line.id!, amount: parseAmount(amount).value ?? 0, note: note.trim() })
                toast.success(t('pay.adjusted'))
                onClose()
              } catch (e) {
                setError(applyProblemDetails(e).title)
              }
            }}
          >
            {t('pay.adjustSave')}
          </button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('pay.adjustHelp', { calculated: f.money(calculated) })}</p>
        <div className="f">
          <label htmlFor="adj-amount">{t('pay.line.amount')}</label>
          <input id="adj-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </div>
        <div className="f">
          <label htmlFor="adj-note">{t('pay.adjustNote')}</label>
          <input id="adj-note" value={note} maxLength={250} autoComplete="off" onChange={(e) => setNote(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

/** Exportar el archivo de la liquidación (una fila por línea). Con permisos para ver las plantillas se puede elegir; sin ellos se usa la estándar. */
function ExportDialog({ settlement, onClose }: { settlement: DriverSettlementDto; onClose: () => void }) {
  const t = useT()
  const canBilling = useCan('billing.view')
  const canPay = useCan('driverpay.view')
  const billingOn = useModule('BILLING')
  const canTemplates = billingOn && (canBilling || canPay)
  const templates = useSettlementTemplates(canTemplates)
  const qc = useQueryClient()
  const [templateId, setTemplateId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const list = templates.data ?? []
  return (
    <Modal
      open
      title={t('pay.exportTitle', { number: settlement.number ?? '' })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('pay.close')}</button>
          <button
            type="button"
            className="btn flow"
            disabled={busy}
            onClick={async () => {
              setBusy(true)
              setError(null)
              try {
                downloadExportFile(await exportSettlement(settlement.publicId!, templateId || null))
                await qc.invalidateQueries({ queryKey: settlementKeys.all })   // exportar pasa una aprobada a exportada
                toast.success(t('pay.exported'))
                onClose()
              } catch (e) {
                setError(applyProblemDetails(e).title)
              } finally {
                setBusy(false)
              }
            }}
          >
            {busy ? t('common.loading') : t('pay.download')}
          </button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('pay.exportHelp')}</p>
        {list.length > 0 ? (
          <div className="f">
            <label htmlFor="pay-template">{t('pay.template')}</label>
            <select id="pay-template" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
              <option value="">{t('pay.templateDefault')}</option>
              {list.map((x) => (
                <option key={x.publicId} value={x.publicId}>{x.name}</option>
              ))}
            </select>
          </div>
        ) : (
          <p className="help">{t('pay.templateStandard')}</p>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
