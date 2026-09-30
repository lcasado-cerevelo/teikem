// Lote 13 — pestaña 'Avisos de llegada' de Recibo (`?tab=asns`), extraída de la lista: filtros al API (Almacén, Cliente
// dueño con buscador, Referencia —contiene, 300 ms— y Llegada esperada `expectedFrom`/`expectedTo`), sin buscador libre
// (decisión del dueño). Tabla local (el API devuelve hasta 200 avisos): Aviso, Almacén, Cliente/OC, Llegada esperada,
// Estatus, Líneas y Recibo (enlaza al recibo elegido en la lista: `?receipt=`). "Recibir" (warehouse.receive, aviso
// pendiente sin recibo) abre el modal del encabezado con el aviso precargado; "Cancelar aviso" (warehouse.receive) con
// confirmación; "Nuevo aviso" abre `AsnCreateModal`.
import { useId, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  ClientPicker,
  ConfirmDialog,
  DataTable,
  DateRangeFilter,
  EMPTY_RANGE,
  Filters,
  IconCheckin,
  Panel,
  toast,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import { useAsns, useSaveAsn, type AsnDto } from './api'
import { AsnCreateModal } from './AsnCreateModal'
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
  const [reference, setReference] = useState('')
  const [expected, setExpected] = useState<DateRange>(EMPTY_RANGE)
  const [creating, setCreating] = useState(false)
  const [toCancel, setToCancel] = useState<AsnDto | null>(null)
  const save = useSaveAsn()
  const ref = useDebounced(reference.trim())

  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId ?? undefined,
      clientPublicId: clientPublicId ?? undefined,
      reference: ref || undefined,
      expectedFrom: expected.from || undefined,
      expectedTo: expected.to || undefined,
    }),
    [warehousePublicId, clientPublicId, ref, expected],
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
            #{a.id}
            {a.reference ? ` · ${a.reference}` : ''}
          </span>
        ),
        sortValue: (a) => a.id ?? 0,
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
        cell: (a) => <StatusChip domain={ASN_STATUS_DOMAIN} code={a.statusCode} label={a.status} />,
        sortValue: (a) => a.status ?? a.statusCode,
      },
      { id: 'lines', header: t('warehouse.asns.columns.lines'), cell: (a) => a.lines?.length ?? 0, sortValue: (a) => a.lines?.length ?? 0, align: 'end' },
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
        key: 'cancel',
        label: t('warehouse.asns.cancel'),
        perm: 'warehouse.receive',
        visible: (a) => a.statusCode === 'EXPECTED',
        onClick: (a) => setToCancel(a),
        tone: 'danger',
      },
    ],
    [t, onReceive],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setWarehousePublicId(null)
          setClientPublicId(null)
          setReference('')
          setExpected(EMPTY_RANGE)
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
        <TextFilter label={t('warehouse.asns.filters.reference')} value={reference} onChange={setReference} />
        <DateRangeFilter label={t('warehouse.asns.filters.expected')} value={expected} onChange={setExpected} />
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
