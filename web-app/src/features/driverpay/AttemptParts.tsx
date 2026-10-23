// Intentos de entrega (Lote 96): la lista (fuente de verdad de la liquidación), registrar un intento a mano (hasta que exista la app del chofer) y anularlo con motivo. Ver pide driverpay.view; registrar y anular, driverpay.manage.
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { useLookups } from '../../kernel/catalogs'
import { Chip, type DataColumn, DataTable, DateField, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import { useDrivers } from '../drivers/api'
import { attemptPageReader, useAttemptMutations, useAttempts, type AttemptQuery, type DeliveryAttemptDto } from './settlementApi'
import { newAttemptError } from './settlementRules'

const EMPTY: AttemptQuery = { driver: '', from: '', to: '', state: '', settled: '', withoutDriver: false, includeVoided: false, skip: 0, take: 25 }

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

export function AttemptsTab() {
  const t = useT()
  const f = useFormat()
  const canFleet = useCan('fleet.view')
  const drivers = useDrivers(canFleet)
  const [filters, setFilters] = useState<AttemptQuery>(EMPTY)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [creating, setCreating] = useState(false)
  const [voiding, setVoiding] = useState<DeliveryAttemptDto | null>(null)
  const query = { ...filters, skip: (page - 1) * pageSize, take: pageSize }
  const list = useAttempts(query)
  const set = (patch: Partial<AttemptQuery>) => (setFilters((s) => ({ ...s, ...patch })), setPage(1))
  const columns = useMemo<DataColumn<DeliveryAttemptDto>[]>(
    () => [
      { id: 'order', header: t('pay.att.order'), cell: (a) => (<><b>{a.orderNumber}</b>{a.clientName ? <span className="pay-sub">{a.clientName}</span> : null}</>), exportValue: (a) => a.orderNumber ?? '', sortValue: (a) => a.orderNumber ?? '', card: 'title' },
      { id: 'invoice', header: t('pay.att.invoice'), cell: (a) => a.clientInvoiceNumber ?? '—', exportValue: (a) => a.clientInvoiceNumber ?? '', sortValue: (a) => a.clientInvoiceNumber ?? '' },
      { id: 'n', header: t('pay.att.number'), cell: (a) => a.attemptNumber, exportValue: (a) => a.attemptNumber ?? 0, sortValue: (a) => a.attemptNumber ?? 0, align: 'end' },
      { id: 'driver', header: t('pay.driver'), cell: (a) => (a.driverName ? `${a.driverCode} · ${a.driverName}` : <Chip tone="warn">{t('pay.att.noDriver')}</Chip>), exportValue: (a) => a.driverName ?? '', sortValue: (a) => a.driverName ?? '' },
      { id: 'when', header: t('pay.att.when'), cell: (a) => f.dateTime(a.attemptedAtUtc ?? ''), exportValue: (a) => a.attemptedAtUtc ?? '', sortValue: (a) => a.attemptedAtUtc ?? '' },
      {
        id: 'result',
        header: t('pay.att.result'),
        cell: (a) => (a.delivered ? <Chip tone="deliv">{t('pay.att.delivered')}</Chip> : <Chip tone="warn">{a.failureReason ? `${t('pay.att.failed')} · ${a.failureReason}` : t('pay.att.failed')}</Chip>),
        exportValue: (a) => (a.delivered ? t('pay.att.delivered') : (a.failureReason ?? t('pay.att.failed'))),
        sortValue: (a) => (a.delivered ? 1 : 0),
      },
      {
        id: 'distance',
        header: t('pay.att.distance'),
        // App del chofer: a cuántos metros de la dirección estaba el teléfono (el GPS del teléfono no es el de la dirección; lejos = más de 300 m)
        cell: (a) => (a.distanceFromStopMeters == null ? (a.source === 'APP' ? <Chip tone="warn">{t('pay.att.noGps')}</Chip> : '—') : a.distanceFromStopMeters > 300 ? <Chip tone="fail">{t('pay.att.far', { m: a.distanceFromStopMeters })}</Chip> : t('pay.att.meters', { m: a.distanceFromStopMeters })),
        exportValue: (a) => (a.distanceFromStopMeters == null ? '' : String(a.distanceFromStopMeters)),
        sortValue: (a) => a.distanceFromStopMeters ?? -1,
        align: 'end',
      },
      { id: 'source', header: t('pay.att.source'), cell: (a) => t(`pay.att.sources.${a.source}`), exportValue: (a) => a.source ?? '', sortValue: (a) => a.source ?? '' },
      {
        id: 'state',
        header: t('pay.col.status'),
        cell: (a) => (!a.isActive ? <Chip tone="fail" title={a.voidReason ?? undefined}>{t('pay.att.voided')}</Chip> : a.settled ? <Chip>{t('pay.att.settled')}</Chip> : <Chip tone="route">{t('pay.att.open')}</Chip>),
        exportValue: (a) => (!a.isActive ? t('pay.att.voided') : a.settled ? t('pay.att.settled') : t('pay.att.open')),
        sortValue: (a) => (a.isActive ? (a.settled ? 1 : 0) : 2),
      },
      {
        id: 'actions',
        header: '',
        cell: (a) =>
          a.isActive && !a.settled ? (
            <Can perm="driverpay.manage">
              <button type="button" className="btn sm" onClick={() => setVoiding(a)}>{t('pay.att.void')}</button>
            </Can>
          ) : null,
      },
    ],
    [t, f],
  )
  return (
    <>
      <Panel
        flush
        icon={<IconCart />}
        title={t('pay.att.panel')}
        badge={list.isPending ? undefined : list.data?.total}
        actions={
          <Can perm="driverpay.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>{t('pay.att.new')}</button>
          </Can>
        }
      >
        <div className="pb">
          <p className="help">{t('pay.att.help')}</p>
          <div className="qrow tax-filters">
            <select aria-label={t('pay.driver')} value={filters.driver} onChange={(e) => set({ driver: e.target.value })} disabled={!canFleet}>
              <option value="">{t('pay.allDrivers')}</option>
              {(drivers.data ?? []).map((d) => (
                <option key={d.publicId} value={d.publicId}>{[d.code, d.fullName].filter(Boolean).join(' · ')}</option>
              ))}
            </select>
            <select aria-label={t('pay.att.result')} value={filters.state} onChange={(e) => set({ state: e.target.value as AttemptQuery['state'] })}>
              <option value="">{t('pay.att.anyResult')}</option>
              <option value="delivered">{t('pay.att.delivered')}</option>
              <option value="failed">{t('pay.att.failed')}</option>
            </select>
            <select aria-label={t('pay.att.settledFilter')} value={filters.settled} onChange={(e) => set({ settled: e.target.value as AttemptQuery['settled'] })}>
              <option value="">{t('pay.att.anySettled')}</option>
              <option value="no">{t('pay.att.open')}</option>
              <option value="yes">{t('pay.att.settled')}</option>
            </select>
            <DateField aria-label={t('pay.from')} value={filters.from} onChange={(v) => set({ from: v })} />
            <DateField aria-label={t('pay.to')} value={filters.to} onChange={(v) => set({ to: v })} />
            <label className="vw-check"><input type="checkbox" checked={filters.withoutDriver} onChange={(e) => set({ withoutDriver: e.target.checked })} /> {t('pay.att.onlyNoDriver')}</label>
            <label className="vw-check"><input type="checkbox" checked={filters.includeVoided} onChange={(e) => set({ includeVoided: e.target.checked })} /> {t('pay.att.includeVoided')}</label>
          </div>
          {list.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('pay.att.panel')}
              columns={columns}
              rows={list.data?.items ?? []}
              rowKey={(a) => a.publicId ?? ''}
              page={page}
              pageSize={pageSize}
              total={list.data?.total ?? 0}
              onPage={setPage}
              onPageSize={(s) => (setPageSize(s), setPage(1))}
              empty={t('pay.att.empty')}
              exportRows={() => fetchAllPages<DeliveryAttemptDto>(attemptPageReader(filters))}
            />
          )}
        </div>
      </Panel>
      {creating && <NewAttemptModal onClose={() => setCreating(false)} />}
      {voiding && <VoidDialog attempt={voiding} onClose={() => setVoiding(null)} />}
    </>
  )
}

/** Busca una orden por número, factura, empaque o consignatario (la misma búsqueda libre de Órdenes). */
function useOrderSearch(text: string) {
  const q = text.trim()
  return useQuery({
    queryKey: ['/api/v1/orders', 'pay-attempt', q],
    queryFn: () => unwrap(api.GET('/api/v1/orders', { params: { query: { search: q, take: 8 } } })),
    enabled: q.length >= 3,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

function NewAttemptModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const canFleet = useCan('fleet.view')
  const drivers = useDrivers(canFleet)
  const reasons = useLookups('DeliveryFailureReason').data ?? []
  const { create } = useAttemptMutations()
  const [text, setText] = useState('')
  const found = useOrderSearch(useDebounced(text))
  const [order, setOrder] = useState<{ id: string; label: string } | null>(null)
  const [driver, setDriver] = useState('')
  const [delivered, setDelivered] = useState(true)
  const [reason, setReason] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const problem = newAttemptError(order?.id ?? null, driver)
  const save = async () => {
    if (problem) return setError(t(`pay.att.errors.${problem}`))
    setError(null)
    try {
      await create.mutateAsync({ orderPublicId: order!.id, driverPublicId: driver, delivered, failureReason: delivered ? undefined : reason || undefined, notes: notes.trim() || undefined })
      toast.success(t('pay.att.created', { order: order!.label }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      title={t('pay.att.newTitle')}
      onClose={onClose}
      dismissible={!create.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={create.isPending}>{t('pay.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={create.isPending}>{create.isPending ? t('common.loading') : t('pay.att.save')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('pay.att.newHelp')}</p>
        <div className="f">
          <label htmlFor="att-order">{t('pay.att.order')}</label>
          {order ? (
            <div className="pay-picked">
              <b>{order.label}</b>
              <button type="button" className="btn sm" onClick={() => setOrder(null)}>{t('pay.att.change')}</button>
            </div>
          ) : (
            <>
              <input id="att-order" value={text} placeholder={t('pay.att.orderPh')} autoComplete="off" onChange={(e) => setText(e.target.value)} />
              {found.isFetching && <Spinner />}
              {(found.data?.items ?? []).length > 0 && (
                <ul className="pay-hits" aria-label={t('pay.att.hits')}>
                  {(found.data?.items ?? []).map((o) => (
                    <li key={o.publicId}>
                      <button type="button" className="btn sm" onClick={() => setOrder({ id: o.publicId!, label: o.orderNumber ?? '' })}>
                        {[o.orderNumber, o.clientInvoiceNumber, o.consigneeName].filter(Boolean).join(' · ')}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>
        <div className="f">
          <label htmlFor="att-driver">{t('pay.driver')}</label>
          <select id="att-driver" value={driver} onChange={(e) => setDriver(e.target.value)} disabled={!canFleet}>
            <option value="">{t('pay.att.pickDriver')}</option>
            {(drivers.data ?? []).map((d) => (
              <option key={d.publicId} value={d.publicId}>{[d.code, d.fullName].filter(Boolean).join(' · ')}</option>
            ))}
          </select>
        </div>
        <div className="f">
          <label htmlFor="att-result">{t('pay.att.result')}</label>
          <select id="att-result" value={delivered ? 'yes' : 'no'} onChange={(e) => setDelivered(e.target.value === 'yes')}>
            <option value="yes">{t('pay.att.delivered')}</option>
            <option value="no">{t('pay.att.failed')}</option>
          </select>
        </div>
        {!delivered && (
          <div className="f">
            <label htmlFor="att-reason">{t('pay.att.reason')}</label>
            <select id="att-reason" value={reason} onChange={(e) => setReason(e.target.value)}>
              <option value="">{t('pay.att.noReason')}</option>
              {reasons.map((r) => (
                <option key={r.code} value={r.code}>{r.label}</option>
              ))}
            </select>
          </div>
        )}
        <div className="f">
          <label htmlFor="att-notes">{t('pay.note')}</label>
          <input id="att-notes" value={notes} maxLength={400} autoComplete="off" onChange={(e) => setNotes(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

function VoidDialog({ attempt, onClose }: { attempt: DeliveryAttemptDto; onClose: () => void }) {
  const t = useT()
  const { void: voidAttempt } = useAttemptMutations()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <Modal
      open
      title={t('pay.att.voidTitle', { order: attempt.orderNumber ?? '', n: attempt.attemptNumber ?? 0 })}
      onClose={onClose}
      dismissible={!voidAttempt.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={voidAttempt.isPending}>{t('pay.close')}</button>
          <button
            type="button"
            className="btn danger"
            disabled={voidAttempt.isPending}
            onClick={async () => {
              if (reason.trim() === '') return setError(t('pay.att.errors.reason'))
              setError(null)
              try {
                await voidAttempt.mutateAsync({ id: attempt.publicId!, reason: reason.trim() })
                toast.success(t('pay.att.voided'))
                onClose()
              } catch (e) {
                setError(applyProblemDetails(e).title)
              }
            }}
          >
            {t('pay.att.void')}
          </button>
        </>
      }
    >
      <div className="pb">
        <p>{t('pay.att.voidBody')}</p>
        <div className="f">
          <label htmlFor="att-void-reason">{t('pay.att.voidReason')}</label>
          <input id="att-void-reason" value={reason} maxLength={400} autoComplete="off" onChange={(e) => setReason(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
