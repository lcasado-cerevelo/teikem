// Lote 13 — pestaña 'Avisos de llegada' de Recibo (`?tab=asns`), extraída de la lista: filtros al API (Almacén, Cliente
// dueño con buscador, Referencia —contiene, 300 ms— y Llegada esperada `expectedFrom`/`expectedTo`), sin buscador libre
// (decisión del dueño). Tabla local (el API devuelve hasta 200 avisos): Aviso, Almacén, Cliente/OC, Llegada esperada,
// Estatus, Líneas y Recibo (enlaza al recibo elegido en la lista: `?receipt=`). "Recibir" (warehouse.receive, aviso
// pendiente sin recibo) abre el modal del encabezado con el aviso precargado; "Cancelar aviso" (warehouse.receive) con
// confirmación; "Nuevo aviso" abre `AsnCreateModal`.
import { useId, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  ClientPicker,
  ConfirmDialog,
  DataTable,
  DateRangeFilter,
  EMPTY_RANGE,
  Filters,
  IconCheckin,
  Panel,
  SearchSelect,
  toast,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import { ToggleFilter } from './filterControls'
import { useAsns, useSaveAsn, type AsnDto } from './api'
import { AsnCreateModal } from './AsnCreateModal'
import { AssignDocumentModal } from './AssignDocumentModal'
import { TextFilter } from './filterControls'
import { formatDate, useDebounced } from './lineRules'
import { WarehousePicker } from './pickers'

const ASN_STATUS_DOMAIN = 'AsnStatus'
const PAGE_SIZE = 25

export function AsnsTab({ onReceive }: { onReceive: (asn: AsnDto) => void }) {
  const t = useT()
  const lang = useLang()
  const whId = useId()
  const clientId = useId()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [clientPublicId, setClientPublicId] = useState<string | null>(null)
  // `?q=` (desde «Buscar o ejecutar»): el número del aviso (LLE-…) va como filtro de referencia, que también busca por número
  const [searchParams] = useSearchParams()
  const [reference, setReference] = useState(() => searchParams.get('q') ?? '')
  const [status, setStatus] = useState<string[]>([])
  const statuses = useStatuses('AsnStatus')
  const statusOptions = useMemo(() => (statuses.data ?? []).map((s) => ({ value: s.code, label: s.label })), [statuses.data])
  const [expected, setExpected] = useState<DateRange>(EMPTY_RANGE)
  const [creating, setCreating] = useState(false)
  const [toCancel, setToCancel] = useState<AsnDto | null>(null)
  const [toEdit, setToEdit] = useState<AsnDto | null>(null)
  const [toAssign, setToAssign] = useState<AsnDto | null>(null)   // Lote 171
  const [mineOnly, setMineOnly] = useState(false)   // Lote 171: solo los asignados a mí
  const save = useSaveAsn()
  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const ref = useDebounced(reference.trim())

  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId ?? undefined,
      clientPublicId: clientPublicId ?? undefined,
      reference: ref || undefined,
      assignedToMe: mineOnly || undefined,
      status: status.length ? status : undefined,
      expectedFrom: expected.from || undefined,
      expectedTo: expected.to || undefined,
    }),
    [warehousePublicId, clientPublicId, ref, status, expected, mineOnly],
  )
  const { data, isLoading, error } = useAsns(query)
  const rows = useMemo(() => data ?? [], [data])

  const columns = useMemo<DataColumn<AsnDto>[]>(
    () => [
      {
        id: 'asn',
        header: t('warehouse.asns.columns.asn'),
        cell: (a) => (
          <span className="ref">
            {a.number || `#${a.id}`}
            {a.reference ? ` · ${a.reference}` : ''}
          </span>
        ),
        sortValue: (a) => a.number || String(a.id ?? 0),
        card: 'title',
      },
      { id: 'warehouse', header: t('warehouse.asns.columns.warehouse'), cell: (a) => a.warehouseCode, sortValue: (a) => a.warehouseCode },
      {
        id: 'owner',
        header: t('warehouse.asns.columns.owner'),
        cell: (a) => a.clientName ?? (a.purchaseOrderNumber ? `${t('warehouse.receipts.origin.PO')} ${a.purchaseOrderNumber}` : '—'),
        sortValue: (a) => a.clientName ?? a.purchaseOrderNumber,
      },
      { id: 'expected', header: t('warehouse.asns.columns.expectedDate'), cell: (a) => formatDate(a.expectedDate, lang) || '—', sortValue: (a) => a.expectedDate },
      {
        id: 'status',
        header: t('warehouse.asns.columns.status'),
        cell: (a) => (
          <>
            <StatusChip domain={ASN_STATUS_DOMAIN} code={a.statusCode} label={a.status} />
            {a.partiallyReceived && <Chip tone="route">{t('warehouse.asns.partial')}</Chip>}
          </>
        ),
        sortValue: (a) => a.status ?? a.statusCode,
        exportValue: (a) => (a.partiallyReceived ? `${a.status} · ${t('warehouse.asns.partial')}` : (a.status ?? '')),
      },
      {
        id: 'received',
        header: t('warehouse.asns.columns.received'),
        cell: (a) => ((a.postedQty ?? 0) > 0 ? `${a.postedQty} / ${(a.lines ?? []).reduce((n, l) => n + (l.expectedQty ?? 0), 0)}` : '—'),
        sortValue: (a) => a.postedQty ?? 0,
        align: 'end',
      },
      { id: 'lines', header: t('warehouse.asns.columns.lines'), cell: (a) => a.lines?.length ?? 0, sortValue: (a) => a.lines?.length ?? 0, align: 'end' },
      {
        id: 'assignee',
        header: t('warehouse.assign.column'),
        cell: (a) => a.assignedToName ?? '—',
        sortValue: (a) => a.assignedToName ?? '',
      },
      {
        id: 'receipt',
        header: t('warehouse.asns.columns.receipt'),
        cell: (a) =>
          a.receiptPublicId ? (
            <Link className="ref" to={`/warehouse/receipts?receipt=${encodeURIComponent(a.receiptPublicId)}`} onClick={(e) => e.stopPropagation()}>
              {a.receiptNumber}
            </Link>
          ) : (
            '—'
          ),
        sortValue: (a) => a.receiptNumber,
      },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<AsnDto>[]>(
    () => [
      {
        key: 'receive',
        label: t('warehouse.asns.receive'),
        perm: 'warehouse.receive',
        visible: (a) => a.statusCode === 'EXPECTED' && !a.receiptPublicId,
        onClick: onReceive,
        tone: 'flow',
      },
      {
        key: 'take',
        label: t('warehouse.assign.take'),
        perm: 'warehouse.receive',
        visible: (a) => a.statusCode === 'EXPECTED' && !a.receiptPublicId && a.assignedToUserId == null,
        onClick: (a) => void run(save.mutateAsync({ action: 'take', id: a.id ?? 0 }), t('warehouse.assign.taken')),
      },
      {
        key: 'assign',
        label: t('warehouse.assign.action'),
        perm: 'warehouse.manage',
        visible: (a) => a.statusCode === 'EXPECTED' && !a.receiptPublicId,
        onClick: (a) => setToAssign(a),
      },
      {
        key: 'edit',
        label: t('warehouse.asns.edit'),
        perm: 'warehouse.receive',
        // solo un aviso de cliente que sigue esperado y sin recibo abierto (uno nacido de una orden de compra se cambia en la orden)
        visible: (a) => a.statusCode === 'EXPECTED' && !a.receiptPublicId && !a.purchaseOrderPublicId,
        onClick: (a) => setToEdit(a),
      },
      {
        key: 'cancel',
        label: t('warehouse.asns.cancel'),
        perm: 'warehouse.receive',
        visible: (a) => a.statusCode === 'EXPECTED',
        onClick: (a) => setToCancel(a),
        tone: 'danger',
      },
    ],
    [t, onReceive, save],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setWarehousePublicId(null)
          setClientPublicId(null)
          setReference('')
          setStatus([])
          setExpected(EMPTY_RANGE)
          setMineOnly(false)
        }}
      >
        <div className="f">
          <label htmlFor={whId}>{t('warehouse.asns.filters.warehouse')}</label>
          <WarehousePicker
            id={whId}
            value={warehousePublicId}
            onChange={(v) => setWarehousePublicId(v)}
            placeholder={t('warehouse.asns.filters.anyWarehouse')}
            filterLabel={t('warehouse.asns.filters.warehouse')}
          />
        </div>
        <div className="f">
          <label htmlFor={clientId}>{t('warehouse.asns.filters.client')}</label>
          <ClientPicker
            id={clientId}
            value={clientPublicId}
            onChange={(v) => setClientPublicId(v)}
            includeInactive
            placeholder={t('warehouse.asns.filters.anyClient')}
            filterLabel={t('warehouse.asns.filters.client')}
          />
        </div>
        <SearchSelect label={t('warehouse.asns.filters.status')} options={statusOptions} value={status} onChange={setStatus} />
        <TextFilter label={t('warehouse.asns.filters.reference')} value={reference} onChange={setReference} />
        <DateRangeFilter label={t('warehouse.asns.filters.expected')} value={expected} onChange={setExpected} mode="future" />
        <ToggleFilter label={t('warehouse.assign.mineOnly')} checked={mineOnly} onChange={setMineOnly} />
      </Filters>
      <Panel
        flush
        icon={<IconCheckin />}
        title={t('warehouse.asns.title')}
        badge={data ? rows.length : undefined}
        actions={
          <Can perm="warehouse.receive">
            <button type="button" className="btn flow sm" onClick={() => setCreating(true)}>
              {t('warehouse.asns.new')}
            </button>
          </Can>
        }
      >
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.asns.title')}
            columns={columns}
            rows={rows}
            rowKey={(a) => a.id ?? 0}
            defaultSort={{ id: 'asn', desc: true }}
            pageSize={PAGE_SIZE}
            loading={isLoading}
            rowActions={actions}
          />
        )}
      </Panel>
      {creating && <AsnCreateModal onClose={() => setCreating(false)} />}
      {toEdit && <AsnCreateModal key={toEdit.id} asn={toEdit} onClose={() => setToEdit(null)} />}
      {toAssign && (
        <AssignDocumentModal
          title={t('warehouse.assign.title', { number: toAssign.number || `#${toAssign.id}` })}
          currentUserId={toAssign.assignedToUserId}
          onSave={(userId) => save.mutateAsync({ action: 'assign', id: toAssign.id ?? 0, userId })}
          onClose={() => setToAssign(null)}
        />
      )}
      <ConfirmDialog
        open={toCancel !== null}
        tone="danger"
        title={t('warehouse.asns.cancelTitle')}
        message={t('warehouse.asns.cancelBody', { id: toCancel?.id ?? '' })}
        confirmLabel={t('warehouse.asns.cancel')}
        onConfirm={async () => {
          if (!toCancel?.id) return
          await save.mutateAsync({ action: 'cancel', id: toCancel.id })
          toast.success(t('warehouse.asns.cancelled'))
        }}
        onClose={() => setToCancel(null)}
      />
    </>
  )
}
