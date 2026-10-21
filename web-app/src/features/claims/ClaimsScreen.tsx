// Operación → Reclamaciones (Lote 111, módulo LTL_GROUND, permiso claims.view): las piezas faltantes siguen Por verificar → Perdido → Reclamada → Resuelta.
// Pestaña «Por verificar»: órdenes con piezas faltantes; su ficha marca perdidas (claims.manage) y crea la reclamación REC-##### con valor declarado y notas.
// Pestaña «Reclamaciones»: la lista y su ficha, donde se resuelve (pagada con monto, rechazada o cerrada).
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Modal, Panel, QBox, Spinner, Tabs, toast, type DataColumn, RemountOnSearch } from '../../kernel/ui'
import { IconBox } from '../../kernel/ui/screenIcons'
import {
  claimsPageReader,
  toVerifyPageReader,
  useClaim,
  useClaimMutations,
  useClaims,
  useToVerify,
  type ClaimListItemDto,
  type MissingOrderDto,
  type MissingPieceDto,
} from './claimsApi'
import { CLAIM_NOTES_MAX, CLAIM_RESOLUTIONS, createFormError, parseAmount, resolveFormError, toResolveRequest, type ResolveForm } from './claimsRules'
import './claims.css'

type TabKey = 'toVerify' | 'claims'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

function ClaimsScreenBody() {
  const t = useT()
  // `?q=<número>` (búsqueda global): abre la lista de reclamaciones ya filtrada por ese número
  const [params] = useSearchParams()
  const [tab, setTab] = useState<TabKey>(params.get('q') ? 'claims' : 'toVerify')
  const [order, setOrder] = useState<string | null>(null)
  const [claim, setClaim] = useState<string | null>(null)
  return (
    <div className="claims-screen">
      <div className="head">
        <div>
          <h1>{t('claims.title')}</h1>
          <p>{t('claims.subtitle')}</p>
        </div>
      </div>
      <Tabs<TabKey>
        label={t('claims.tabs.label')}
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'toVerify', label: t('claims.tabs.toVerify') },
          { key: 'claims', label: t('claims.tabs.claims') },
        ]}
      />
      {tab === 'toVerify' ? <ToVerifyTab onOpen={setOrder} /> : <ClaimsTab onOpen={setClaim} />}
      {order !== null && (
        <MissingOrderModal
          key={order}
          orderId={order}
          onClose={() => setOrder(null)}
          onClaimed={(id) => {
            setOrder(null)
            setTab('claims')
            setClaim(id)
          }}
        />
      )}
      {claim !== null && <ClaimModal key={claim} id={claim} onClose={() => setClaim(null)} />}
    </div>
  )
}

// ---------------------------------------------------------------- por verificar

function ToVerifyTab({ onOpen }: { onOpen: (orderPublicId: string) => void }) {
  const t = useT()
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useToVerify(search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<MissingOrderDto>[]>(
    () => [
      { id: 'order', header: t('claims.col.order'), cell: (o) => <b>{o.orderNumber}</b>, exportValue: (o) => o.orderNumber ?? '', sortValue: (o) => o.orderNumber ?? '', card: 'title' },
      { id: 'waybill', header: t('claims.col.waybill'), cell: (o) => o.waybillNumber ?? '—', exportValue: (o) => o.waybillNumber ?? '', sortValue: (o) => o.waybillNumber ?? '' },
      { id: 'client', header: t('claims.col.client'), cell: (o) => o.clientName, exportValue: (o) => o.clientName ?? '', sortValue: (o) => o.clientName ?? '' },
      { id: 'orderStatus', header: t('claims.col.orderStatus'), cell: (o) => o.orderStatusLabel, exportValue: (o) => o.orderStatusLabel ?? '', sortValue: (o) => o.orderStatusLabel ?? '' },
      {
        id: 'missing',
        header: t('claims.col.missing'),
        cell: (o) => t('claims.missingOf', { missing: o.pieces?.length ?? 0, total: o.totalPieces ?? 0 }),
        exportValue: (o) => o.pieces?.length ?? 0,
        sortValue: (o) => o.pieces?.length ?? 0,
        align: 'end',
      },
    ],
    [t],
  )
  return (
    <Panel flush icon={<IconBox />} title={t('claims.toVerify.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('claims.toVerify.search')} />
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('claims.toVerify.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(o) => o.orderPublicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(o) => onOpen(o.orderPublicId!)}
            empty={t('claims.toVerify.empty')}
            exportRows={() => fetchAllPages<MissingOrderDto>(toVerifyPageReader(search))}
          />
        )}
      </div>
    </Panel>
  )
}

function PiecesTable({ pieces }: { pieces: MissingPieceDto[] }) {
  const t = useT()
  const f = useFormat()
  const columns = useMemo<DataColumn<MissingPieceDto>[]>(
    () => [
      { id: 'piece', header: t('claims.col.piece'), cell: (p) => <b>{p.packageNumber}</b>, sortValue: (p) => p.packageNumber ?? '', card: 'title' },
      { id: 'line', header: t('claims.col.line'), cell: (p) => p.lineDescription ?? '—', sortValue: (p) => p.lineDescription ?? '' },
      { id: 'status', header: t('claims.col.status'), cell: (p) => <Chip>{p.statusLabel}</Chip>, sortValue: (p) => p.statusLabel ?? '' },
      { id: 'since', header: t('claims.col.since'), cell: (p) => (p.missingAtUtc ? f.dateTime(p.missingAtUtc) : '—'), sortValue: (p) => p.missingAtUtc ?? '' },
      { id: 'claim', header: t('claims.col.claim'), cell: (p) => p.claimNumber ?? '—', sortValue: (p) => p.claimNumber ?? '' },
    ],
    [t, f],
  )
  return <DataTable label={t('claims.pieces')} columns={columns} rows={pieces} rowKey={(p) => p.packageNumber ?? ''} empty={t('claims.noPieces')} />
}

/** Ficha de una orden con faltantes: marcar perdidas y crear la reclamación. */
function MissingOrderModal({ orderId, onClose, onClaimed }: { orderId: string; onClose: () => void; onClaimed: (claimPublicId: string) => void }) {
  const t = useT()
  const list = useToVerify('', 0, 200)
  const order = list.data?.items?.find((o) => o.orderPublicId === orderId)
  const { markLost, create } = useClaimMutations()
  const [value, setValue] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const pieces = order?.pieces ?? []
  const toVerify = pieces.filter((p) => p.status === 'TO_VERIFY').length
  const lost = pieces.filter((p) => p.status === 'LOST').length
  const busy = markLost.isPending || create.isPending

  const lose = async () => {
    setError(null)
    try {
      await markLost.mutateAsync({ orderPublicId: orderId })
      toast.success(t('claims.markedLost', { count: toVerify }))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  const claim = async () => {
    const problem = createFormError({ declaredValue: value, notes })
    if (problem) return setError(t(`claims.errors.${problem}`))
    setError(null)
    try {
      const saved = await create.mutateAsync({ orderPublicId: orderId, declaredValue: parseAmount(value), notes: notes.trim() || null })
      toast.success(t('claims.created', { number: saved.number ?? '' }))
      onClaimed(saved.publicId!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="lg"
      title={t('claims.order.title', { number: order?.orderNumber ?? '' })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('claims.close')}</button>
          <Can perm="claims.manage">
            {toVerify > 0 && (
              <button type="button" className="btn" onClick={() => void lose()} disabled={busy}>{t('claims.markLost', { count: toVerify })}</button>
            )}
            {lost > 0 && (
              <button type="button" className="btn flow" onClick={() => void claim()} disabled={busy}>{busy ? t('common.loading') : t('claims.create', { count: lost })}</button>
            )}
          </Can>
        </>
      }
    >
      <div className="pb">
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : !order ? (
          <p className="help">{t('claims.order.gone')}</p>
        ) : (
          <>
            <dl className="claims-meta">
              <div><dt>{t('claims.col.client')}</dt><dd>{order.clientName}</dd></div>
              <div><dt>{t('claims.col.waybill')}</dt><dd>{order.waybillNumber ?? '—'}</dd></div>
              <div><dt>{t('claims.col.orderStatus')}</dt><dd>{order.orderStatusLabel}</dd></div>
              <div><dt>{t('claims.col.missing')}</dt><dd>{t('claims.missingOf', { missing: pieces.length, total: order.totalPieces ?? 0 })}</dd></div>
            </dl>
            <PiecesTable pieces={pieces} />
            {lost > 0 && (
              <Can perm="claims.manage">
                <h3 className="exp-h">{t('claims.newClaim')}</h3>
                <div className="f">
                  <label htmlFor="claim-value">{t('claims.declaredValue')}</label>
                  <input id="claim-value" inputMode="decimal" value={value} autoComplete="off" onChange={(e) => (setError(null), setValue(e.target.value))} />
                </div>
                <div className="f">
                  <label htmlFor="claim-notes">{t('claims.notes')}</label>
                  <textarea id="claim-notes" rows={3} value={notes} maxLength={CLAIM_NOTES_MAX} onChange={(e) => (setError(null), setNotes(e.target.value))} />
                </div>
              </Can>
            )}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- reclamaciones

function ClaimsTab({ onOpen }: { onOpen: (publicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState(() => new URLSearchParams(window.location.search).get('q') ?? '')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useClaims(status, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<ClaimListItemDto>[]>(
    () => [
      { id: 'number', header: t('claims.col.number'), cell: (c) => <b>{c.number}</b>, exportValue: (c) => c.number ?? '', sortValue: (c) => c.number ?? '', card: 'title' },
      { id: 'order', header: t('claims.col.order'), cell: (c) => c.orderNumber, exportValue: (c) => c.orderNumber ?? '', sortValue: (c) => c.orderNumber ?? '' },
      { id: 'client', header: t('claims.col.client'), cell: (c) => c.clientName, exportValue: (c) => c.clientName ?? '', sortValue: (c) => c.clientName ?? '' },
      { id: 'description', header: t('claims.col.description'), cell: (c) => c.description, exportValue: (c) => c.description ?? '', sortValue: (c) => c.description ?? '' },
      { id: 'pieces', header: t('claims.col.pieces'), cell: (c) => c.pieces ?? 0, exportValue: (c) => c.pieces ?? 0, sortValue: (c) => c.pieces ?? 0, align: 'end' },
      {
        id: 'value',
        header: t('claims.col.value'),
        cell: (c) => (c.declaredValue == null ? '—' : f.money(c.declaredValue)),
        exportValue: (c) => c.declaredValue ?? '',
        sortValue: (c) => c.declaredValue ?? 0,
        align: 'end',
      },
      { id: 'status', header: t('claims.col.status'), cell: (c) => <Chip>{c.statusLabel}</Chip>, exportValue: (c) => c.statusLabel ?? '', sortValue: (c) => c.statusLabel ?? '' },
      { id: 'created', header: t('claims.col.created'), cell: (c) => f.dateTime(c.createdAtUtc ?? ''), exportValue: (c) => c.createdAtUtc ?? '', sortValue: (c) => c.createdAtUtc ?? '' },
    ],
    [t, f],
  )
  return (
    <Panel flush icon={<IconBox />} title={t('claims.list.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('claims.list.search')} />
          <select aria-label={t('claims.list.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
            <option value="">{t('claims.list.allStatuses')}</option>
            <option value="CLAIMED">{t('claims.statuses.CLAIMED')}</option>
            <option value="RESOLVED">{t('claims.statuses.RESOLVED')}</option>
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('claims.list.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(c) => c.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(c) => onOpen(c.publicId!)}
            empty={t('claims.list.empty')}
            exportRows={() => fetchAllPages<ClaimListItemDto>(claimsPageReader(status, search))}
          />
        )}
      </div>
    </Panel>
  )
}

function ClaimModal({ id, onClose }: { id: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const claim = useClaim(id)
  const { resolve } = useClaimMutations()
  const [form, setForm] = useState<ResolveForm>({ resolution: '', amountPaid: '', notes: '' })
  const [error, setError] = useState<string | null>(null)
  const data = claim.data
  const open = data !== undefined && data.status !== 'RESOLVED'
  const set = (patch: Partial<ResolveForm>) => {
    setError(null)
    setForm((x) => ({ ...x, ...patch }))
  }
  const save = async () => {
    const problem = resolveFormError(form)
    if (problem) return setError(t(`claims.errors.${problem}`))
    try {
      const saved = await resolve.mutateAsync({ id, body: toResolveRequest(form, data?.rowVersion) })
      toast.success(t('claims.resolved', { number: saved.number ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="lg"
      title={t('claims.claim.title', { number: data?.number ?? '' })}
      onClose={onClose}
      dismissible={!resolve.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={resolve.isPending}>{t('claims.close')}</button>
          {open && (
            <Can perm="claims.manage">
              <button type="button" className="btn flow" onClick={() => void save()} disabled={resolve.isPending}>{resolve.isPending ? t('common.loading') : t('claims.resolve')}</button>
            </Can>
          )}
        </>
      }
    >
      <div className="pb">
        {claim.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(claim.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="claims-meta">
              <div><dt>{t('claims.col.status')}</dt><dd><Chip>{data.statusLabel}</Chip></dd></div>
              <div><dt>{t('claims.col.order')}</dt><dd>{data.orderNumber}</dd></div>
              <div><dt>{t('claims.col.waybill')}</dt><dd>{data.waybillNumber ?? '—'}</dd></div>
              <div><dt>{t('claims.col.client')}</dt><dd>{data.clientName}</dd></div>
              <div><dt>{t('claims.col.value')}</dt><dd>{data.declaredValue == null ? '—' : f.money(data.declaredValue)}</dd></div>
              <div><dt>{t('claims.col.created')}</dt><dd>{f.dateTime(data.createdAtUtc ?? '')}</dd></div>
            </dl>
            <p>{data.description}</p>
            {data.notes && <p className="help">{data.notes}</p>}
            <PiecesTable pieces={data.pieces ?? []} />
            {data.status === 'RESOLVED' ? (
              <dl className="claims-meta">
                <div><dt>{t('claims.resolution')}</dt><dd>{t(`claims.resolutions.${data.resolution ?? 'CLOSED'}`)}</dd></div>
                {data.resolution === 'PAID' && <div><dt>{t('claims.amountPaid')}</dt><dd>{f.money(data.amountPaid ?? 0)}</dd></div>}
                <div><dt>{t('claims.resolvedAt')}</dt><dd>{data.resolvedAtUtc ? f.dateTime(data.resolvedAtUtc) : '—'}</dd></div>
                {data.resolutionNotes && <div><dt>{t('claims.notes')}</dt><dd>{data.resolutionNotes}</dd></div>}
              </dl>
            ) : (
              <Can perm="claims.manage">
                <h3 className="exp-h">{t('claims.resolveTitle')}</h3>
                <div className="f">
                  <label htmlFor="claim-resolution">{t('claims.resolution')}</label>
                  <select id="claim-resolution" value={form.resolution} onChange={(e) => set({ resolution: e.target.value as ResolveForm['resolution'] })}>
                    <option value="">{t('claims.pick')}</option>
                    {CLAIM_RESOLUTIONS.map((r) => (
                      <option key={r} value={r}>{t(`claims.resolutions.${r}`)}</option>
                    ))}
                  </select>
                </div>
                {form.resolution === 'PAID' && (
                  <div className="f">
                    <label htmlFor="claim-paid">{t('claims.amountPaid')}</label>
                    <input id="claim-paid" inputMode="decimal" value={form.amountPaid} autoComplete="off" onChange={(e) => set({ amountPaid: e.target.value })} />
                  </div>
                )}
                <div className="f">
                  <label htmlFor="claim-res-notes">{t('claims.notes')}</label>
                  <textarea id="claim-res-notes" rows={3} value={form.notes} maxLength={CLAIM_NOTES_MAX} onChange={(e) => set({ notes: e.target.value })} />
                </div>
              </Can>
            )}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// `?q=` (búsqueda global) se lee al abrir: si cambia estando abierta, la lista se vuelve a abrir con el filtro nuevo
export default function ClaimsScreen() {
  return (
    <RemountOnSearch>
      <ClaimsScreenBody />
    </RemountOnSearch>
  )
}
