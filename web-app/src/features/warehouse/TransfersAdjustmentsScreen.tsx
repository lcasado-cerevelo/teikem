// Lote 14 (P6) — 'Transferencias y ajustes' (`/warehouse/transfers-adjustments`, inventory.view + WMS_LOTSERIAL; en el menú
// justo después de Recolección y empaque, D1). Dos pestañas: Ajustes (la primera, sin parámetro) y Transferencias
// (`?tab=transfers`). Cada una lista del Kárdex (`GET /inventory/transactions`, paginado en el servidor) TODOS los
// movimientos de su tipo, también los del sistema (D12: conteo, recibo, acomodo, reabasto), con "Solo manuales":
// - Ajustes: filtros Fecha, Almacén, Producto, Dueño, Subir/Bajar (dirección), Motivo y "Solo manuales"; columnas Fecha,
//   Hora, SKU, Producto, Dueño, Almacén/Posición, Lote/Serie, Cantidad ±, Motivo, Nota, Origen y Usuario; "Reporte de
//   ajustes" con los MISMOS filtros (hallazgo 22).
// - Transferencias: filtros Fecha, Almacén de origen, Almacén de destino, Producto, Dueño y "Solo manuales"; columnas Fecha,
//   Hora, SKU, Producto, Origen, Destino, Lote/Serie, Cantidad, Origen del movimiento (Manual/Acomodo o reabasto/Conteo…),
//   Nota y Usuario.
// Arriba de cada lista, el resumen de esos movimientos (`SummaryBar`). "Ajustar" y "Transferir" (inventory.adjust) abren los
// modales del Lote 14 (Subir/Bajar; origen → ítem → destino con lote y series). Clic en una fila = `KardexTransactionModal`.
// Los filtros de cada pestaña son suyos y se conservan al cambiar de pestaña.
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, DateRangeFilter, Filters, Panel, SearchSelect, SelectFilter, SummaryBar, Tabs } from '../../kernel/ui'
import { IconPencil } from '../../kernel/ui/screenIcons'
import {
  exportInventoryTransactions,
  useInventoryOwners,
  useInventoryTransactions,
  useKardexSummary,
  useWarehouses,
  warehouseLabel,
} from './api'
import { ToggleFilter } from './filterControls'
import { InventoryAdjustModal } from './InventoryAdjustModal'
import { AdjustmentsReportButton } from './InventoryReportButtons'
import { InventoryTransferModal } from './InventoryTransferModal'
import { KardexTransactionModal } from './KardexTransactionModal'
import { useKardexColumns, type KardexColumnId } from './kardexColumns'
import { OWN_OWNER, rangeInverted, summaryItems, type KardexDirection } from './kardexView'
import {
  adjustmentsQuery,
  describeMovementFilters,
  EMPTY_MOVEMENT_FILTERS,
  movementTabFromParam,
  transfersQuery,
  type MovementFilterState,
  type MovementTab,
} from './movementFilters'
import { OwnerFilter, ProductMultiFilter } from './pickers'

const PAGE_SIZE = 25

const ADJUSTMENT_COLUMNS: readonly KardexColumnId[] = ['date', 'time', 'sku', 'product', 'owner', 'position', 'lotSerial', 'qty', 'reason', 'notes', 'origin', 'user']
const TRANSFER_COLUMNS: readonly KardexColumnId[] = ['date', 'time', 'sku', 'product', 'from', 'to', 'lotSerial', 'amount', 'origin', 'notes', 'user']

const T = 'warehouse.transfersAdjustments'

/** Una pestaña: filtros (propios), resumen y lista paginada de los movimientos de su tipo. */
function MovementsTab({
  kind,
  filters,
  onFilters,
  onOpenTxn,
}: {
  kind: MovementTab
  filters: MovementFilterState
  onFilters: (f: MovementFilterState) => void
  onOpenTxn: (id: number) => void
}) {
  const t = useT()
  const lang = useLang()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const columns = useKardexColumns(kind === 'adjustments' ? ADJUSTMENT_COLUMNS : TRANSFER_COLUMNS)
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const { data: reasons = [] } = useLookups('AdjustmentReason', { includeDisabled: true })
  const reasonOptions = useMemo(() => reasons.map((r) => ({ value: r.code, label: r.label })), [reasons])

  const set = (patch: Partial<MovementFilterState>) => {
    setPage(1)
    onFilters({ ...filters, ...patch })
  }

  const inverted = rangeInverted(filters.range)
  const base = useMemo(() => (kind === 'adjustments' ? adjustmentsQuery(filters) : transfersQuery(filters)), [kind, filters])
  const query = useMemo(() => ({ ...base, skip: (page - 1) * pageSize, take: pageSize }), [base, page, pageSize])
  const { data, isLoading, error } = useInventoryTransactions(query, { enabled: !inverted })
  const summary = useKardexSummary(base, { enabled: !inverted, handleAccessDenied: false })
  const title = t(kind === 'adjustments' ? `${T}.tabAdjustments` : `${T}.tabTransfers`)

  return (
    <>
      <Filters label={t(`${T}.filters.label`)} onClear={() => set(EMPTY_MOVEMENT_FILTERS)}>
        <DateRangeFilter label={t(`${T}.filters.range`)} value={filters.range} onChange={(range) => set({ range })} />
        {kind === 'adjustments' ? (
          <SearchSelect
            label={t(`${T}.filters.warehouse`)}
            options={warehouseOptions}
            value={filters.warehousePublicIds}
            onChange={(warehousePublicIds) => set({ warehousePublicIds })}
          />
        ) : (
          <>
            <SearchSelect
              label={t(`${T}.filters.fromWarehouse`)}
              options={warehouseOptions}
              value={filters.fromWarehousePublicIds}
              onChange={(fromWarehousePublicIds) => set({ fromWarehousePublicIds })}
            />
            <SearchSelect
              label={t(`${T}.filters.toWarehouse`)}
              options={warehouseOptions}
              value={filters.toWarehousePublicIds}
              onChange={(toWarehousePublicIds) => set({ toWarehousePublicIds })}
            />
          </>
        )}
        <ProductMultiFilter label={t(`${T}.filters.product`)} value={filters.products} onChange={(products) => set({ products })} includeInactive />
        <OwnerFilter label={t(`${T}.filters.owner`)} value={filters.owners} onChange={(owners) => set({ owners })} />
        {kind === 'adjustments' && (
          <>
            <SelectFilter
              label={t(`${T}.filters.direction`)}
              value={filters.direction}
              onChange={(v) => set({ direction: v as KardexDirection })}
              options={[
                { value: 'IN', label: t(`${T}.filters.up`) },
                { value: 'OUT', label: t(`${T}.filters.down`) },
              ]}
            />
            <SearchSelect label={t(`${T}.filters.reason`)} options={reasonOptions} value={filters.reasons} onChange={(reasons) => set({ reasons })} />
          </>
        )}
        <ToggleFilter label={t(`${T}.filters.manualOnly`)} checked={filters.manualOnly} onChange={(manualOnly) => set({ manualOnly })} />
      </Filters>

      <SummaryBar label={t('warehouse.inventory.summary.label')} loading={summary.isFetching && !summary.data} items={summaryItems(inverted ? undefined : summary.data, t, lang).filter((i) => i.key !== 'internal' || kind === 'transfers')} />

      <Panel flush icon={<IconPencil />} title={title} badge={data ? (data.total ?? 0) : undefined}>
        {inverted ? (
          <p className="pb ferr" role="alert">
            {t('warehouse.inventory.kardex.errors.fromAfterTo')}
          </p>
        ) : error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={title}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(r) => r.id ?? 0}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportInventoryTransactions(base)}
            onRowClick={(r) => r.id != null && onOpenTxn(r.id)}
            empty={<p className="pb help">{t(kind === 'adjustments' ? `${T}.emptyAdjustments` : `${T}.emptyTransfers`)}</p>}
          />
        )}
      </Panel>
    </>
  )
}

/** "Reporte de ajustes" con los filtros de la pestaña Ajustes (consulta del Kárdex y "Filtros aplicados" con nombres). */
function AdjustmentsReport({ filters }: { filters: MovementFilterState }) {
  const t = useT()
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const { data: owners = [] } = useInventoryOwners({ handleAccessDenied: false })
  const { data: reasons = [] } = useLookups('AdjustmentReason', { includeDisabled: true })
  const query = useMemo(
    () => ({
      kardexQuery: adjustmentsQuery(filters),
      filterLabels: describeMovementFilters(
        filters,
        {
          warehouses: new Map(warehouses.map((w) => [w.publicId ?? '', warehouseLabel(w)])),
          owners: new Map(owners.map((o) => [o.isOwn ? OWN_OWNER : (o.clientPublicId ?? ''), o.name ?? ''])),
          reasons: new Map(reasons.map((r) => [r.code, r.label])),
        },
        t,
      ),
    }),
    [filters, warehouses, owners, reasons, t],
  )
  return <AdjustmentsReportButton query={query} />
}

export default function TransfersAdjustmentsScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const tab = movementTabFromParam(params.get('tab'))
  const [adjFilters, setAdjFilters] = useState<MovementFilterState>(EMPTY_MOVEMENT_FILTERS)
  const [trfFilters, setTrfFilters] = useState<MovementFilterState>(EMPTY_MOVEMENT_FILTERS)
  const [adjusting, setAdjusting] = useState(false)
  const [transferring, setTransferring] = useState(false)
  const [txnId, setTxnId] = useState<number | null>(null)

  const changeTab = (next: MovementTab) => {
    if (next === tab) return
    setParams(next === 'adjustments' ? {} : { tab: next }, { replace: true })
  }

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('nav.transfersAdjustments.title')}</h1>
          <p>{t('nav.transfersAdjustments.subtitle')}</p>
        </div>
        <div className="act">
          {tab === 'adjustments' && <AdjustmentsReport filters={adjFilters} />}
          <Can perm="inventory.adjust">
            <button type="button" className="btn" onClick={() => setAdjusting(true)}>
              {t(`${T}.newAdjustment`)}
            </button>
            <button type="button" className="btn flow" onClick={() => setTransferring(true)}>
              {t(`${T}.newTransfer`)}
            </button>
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<MovementTab>
          label={t('nav.transfersAdjustments.title')}
          value={tab}
          onChange={changeTab}
          tabs={[
            { key: 'adjustments', label: t(`${T}.tabAdjustments`) },
            { key: 'transfers', label: t(`${T}.tabTransfers`) },
          ]}
        />
      </div>

      {tab === 'adjustments' ? (
        <MovementsTab key="adjustments" kind="adjustments" filters={adjFilters} onFilters={setAdjFilters} onOpenTxn={setTxnId} />
      ) : (
        <MovementsTab key="transfers" kind="transfers" filters={trfFilters} onFilters={setTrfFilters} onOpenTxn={setTxnId} />
      )}

      <InventoryAdjustModal open={adjusting} onClose={() => setAdjusting(false)} />
      <InventoryTransferModal open={transferring} onClose={() => setTransferring(false)} />
      <KardexTransactionModal txnId={txnId} onClose={() => setTxnId(null)} />
    </div>
  )
}
