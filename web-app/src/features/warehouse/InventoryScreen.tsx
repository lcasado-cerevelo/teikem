// Pantalla C (Lote F6; Lote 14 P7) — Kárdex de movimientos (maqueta `ledger()`, ítem propio del menú desde la Fase 8):
// `/warehouse/kardex` con pestañas Kárdex (la primera, sin parámetro) / Saldos (`?tab=balances`) / Conciliación
// (`?tab=reconciliation`). `/warehouse/inventory` (dirección anterior) redirige aquí conservando la consulta.
// Lectura: inventory.view + WMS_LOTSERIAL (aplicado por la ruta). Ajustar / Transferir / Ejecutar conciliación / resolver un
// descuadre: inventory.adjust.
// Lote 14:
// - Los tres tabs COMPARTEN los filtros (hallazgo 6): el estado vive aquí (`InventoryFilterState`, `kardexView.ts`) y cambiar
//   de pestaña no descarta nada; un filtro que la pestaña no aplica se atenúa (`InventoryFilterBar`).
// - Resumen arriba (`SummaryBar`, `GET /inventory/transactions/summary` con los MISMOS filtros): Movimientos · Entradas (mov.)
//   · Entradas (uds) · Salidas (mov.) · Salidas (uds) · Internos; en Saldos además En mano y Disponible (D13, del
//   `BalancePageDto`).
// - Kárdex: columnas Fecha, Hora, Tipo (chip de color), SKU, Producto, Dueño, Categoría, Cantidad (color por signo),
//   Almacén/Posición, Lote/Serie, Motivo, Origen y Usuario; clic en la fila → `KardexTransactionModal` (`?txn=<id>`).
// - Conciliación: descuadres (`DiscrepanciesPanel`) y su detalle (`?discrepancy=<publicId>` → `DiscrepancyModal`).
// Contrato de la URL (se lee UNA vez al montar; los filtros valen para las tres pestañas): `tab`, `warehousePublicIds`,
// `product` (publicId), `categoryIds`, `types`, `reasons`, `direction` (IN/OUT), `manualOnly=true`, `from`/`to`
// (YYYY-MM-DD), `refEntity` + `refId` (documento de origen, p. ej. los ajustes de un conteo), `status` (descuadres),
// `txn=<id>` (abre el detalle de un movimiento) y `discrepancy=<publicId>` (abre un descuadre; implica la pestaña
// Conciliación si no se indica otra). Al cambiar de pestaña la URL queda solo con la pestaña; los filtros se quedan.
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import type { components } from '../../kernel/api/schema'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, EmptyState, Modal, Panel, QBox, Spinner, SummaryBar, Tabs, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconDoc, IconLayers } from '../../kernel/ui/screenIcons'
import {
  exportInventoryBalances,
  exportInventoryTransactions,
  productLabel,
  useInventoryBalances,
  useInventoryTransactions,
  useKardexSummary,
  useLotGenealogy,
  useProductsByPublicId,
  useSerialTrace,
  type BalanceDto,
  type KardexRowDto,
} from './api'
import { DiscrepanciesPanel } from './DiscrepanciesPanel'
import { DiscrepancyModal } from './DiscrepancyModal'
import { InventoryAdjustModal } from './InventoryAdjustModal'
import { InventoryFilterBar } from './InventoryFilterBar'
import { InventoryTransferModal } from './InventoryTransferModal'
import { KardexTransactionModal } from './KardexTransactionModal'
import { useKardexColumns, KARDEX_COLUMNS } from './kardexColumns'
import {
  balancesQuery,
  filtersFromUrl,
  kardexQuery,
  rangeInverted,
  summaryQuery,
  tabFromParam,
  summaryItems,
  txnParam,
  type InventoryFilterState,
  type InventoryTab,
} from './kardexView'
import { formatDate, formatDateTime, formatMoneyValue } from './lineRules'
import type { ProductFilterItem } from './pickers'

const PAGE_SIZE = 25

/**
 * Productos del filtro para pintar: los que llegaron por URL sin SKU (pueden ser varios) toman "SKU · Nombre" de su ficha
 * (`GET /api/v1/products/{publicId}`); mientras llega se muestra '…' y, si la ficha no se puede leer (404/403), un texto
 * fijo para que la píldora se pueda quitar. Un elemento sin SKU sigue pendiente aunque el filtro lo haya guardado con '…'.
 */
function useResolvedProducts(products: ProductFilterItem[]): ProductFilterItem[] {
  const t = useT()
  const pending = useMemo(() => products.filter((p) => !p.sku).map((p) => p.publicId), [products])
  const results = useProductsByPublicId(pending, { handleAccessDenied: false })
  return products.map((p) => {
    if (p.sku) return p
    const r = results[pending.indexOf(p.publicId)]
    const found = r?.data?.product
    if (found) return { publicId: p.publicId, sku: found.sku ?? '', label: productLabel(found) }
    return { ...p, label: r?.isError ? t('warehouse.inventory.productUnavailable') : '…' }
  })
}

/** Resumen de movimientos con los filtros compartidos (Kárdex y Saldos). */
function KardexSummaryBar({ filters, balances }: { filters: InventoryFilterState; balances?: { onHand: number | null; available: number | null } }) {
  const t = useT()
  const lang = useLang()
  const inverted = rangeInverted(filters.range)
  const query = useMemo(() => summaryQuery(filters), [filters])
  const { data, isFetching } = useKardexSummary(query, { enabled: !inverted, handleAccessDenied: false })
  return <SummaryBar label={t('warehouse.inventory.summary.label')} loading={isFetching && !data} items={summaryItems(inverted ? undefined : data, t, lang, balances)} />
}

// =====================================================================================================================
// Modal de genealogía de lote (se abre desde una fila de Saldos con lotId)
// =====================================================================================================================
function GenealogyModal({ lotId, onClose, onOpenTxn }: { lotId: number | null; onClose: () => void; onOpenTxn: (id: number) => void }) {
  const t = useT()
  const lang = useLang()
  const kardexColumns = useKardexColumns(['date', 'time', 'type', 'sku', 'qty', 'position', 'lotSerial', 'ref'])
  const { data, isLoading, error } = useLotGenealogy(lotId)

  const destinationColumns = useMemo<DataColumn<components['schemas']['GenealogyDestinationDto']>[]>(
    () => [
      { id: 'ref', header: t('warehouse.inventory.kardex.columns.ref'), cell: (d) => d.refLabel ?? '', sortValue: (d) => d.refLabel, card: 'title' },
      { id: 'client', header: t('warehouse.inventory.genealogy.client'), cell: (d) => d.clientName ?? '', sortValue: (d) => d.clientName },
      {
        id: 'consignee',
        header: t('warehouse.inventory.genealogy.consignee'),
        cell: (d) => d.consigneeName ?? '',
        sortValue: (d) => d.consigneeName,
        card: 'hidden',
      },
      { id: 'quantity', header: t('warehouse.inventory.kardex.columns.quantity'), cell: (d) => d.quantity, sortValue: (d) => d.quantity, align: 'end' },
    ],
    [t],
  )

  return (
    <Modal open={lotId != null} title={t('warehouse.inventory.genealogy.title', { lot: data?.lotNumber ?? '' })} onClose={onClose} size="lg">
      {isLoading && <Spinner block label={t('common.loading')} />}
      {error && (
        <p className="pb ferr" role="alert">
          {error.message}
        </p>
      )}
      {data && (
        <div className="pb">
          <div className="r2">
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.manufactureDate')}</label>
              <p>{formatDate(data.manufactureDate, lang) || '—'}</p>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.expiryDate')}</label>
              <p>{formatDate(data.expiryDate, lang) || '—'}</p>
            </div>
          </div>
          <div className="r3">
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.qtyIn')}</label>
              <p>{data.qtyIn}</p>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.qtyOut')}</label>
              <p>{data.qtyOut}</p>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.qtyOnHand')}</label>
              <p>{data.qtyOnHand}</p>
            </div>
          </div>
          <h3>{t('warehouse.inventory.genealogy.movements')}</h3>
          <DataTable
            label={t('warehouse.inventory.genealogy.movements')}
            columns={kardexColumns}
            rows={data.movements ?? []}
            rowKey={(m) => m.id ?? 0}
            pageSize={10}
            onRowClick={(m) => m.id != null && onOpenTxn(m.id)}
            dense
          />
          <h3>{t('warehouse.inventory.genealogy.destinations')}</h3>
          {data.destinations && data.destinations.length > 0 ? (
            <DataTable
              label={t('warehouse.inventory.genealogy.destinations')}
              columns={destinationColumns}
              rows={data.destinations}
              rowKey={(d) => `${d.refEntityCode ?? ''}-${d.refId ?? 0}`}
              pageSize={10}
              dense
            />
          ) : (
            <EmptyState title={t('warehouse.inventory.genealogy.noDestinations')} />
          )}
        </div>
      )}
    </Modal>
  )
}

// =====================================================================================================================
// Modal de rastro de serie (se abre desde una fila de Saldos o Kárdex)
// =====================================================================================================================
function SerialTraceModal({
  productPublicId,
  initialSerialNumber,
  onClose,
  onOpenTxn,
}: {
  productPublicId: string | null
  initialSerialNumber: string
  onClose: () => void
  onOpenTxn: (id: number) => void
}) {
  const t = useT()
  const lang = useLang()
  const kardexColumns = useKardexColumns(['date', 'time', 'type', 'qty', 'position', 'lotSerial', 'ref', 'user'])
  // Montado solo mientras hay una petición abierta (ver `key` en InventoryScreen): el estado inicial ya trae el
  // número de serie de la fila que abrió el modal, sin necesidad de sincronizarlo con un efecto.
  const [serialNumber, setSerialNumber] = useState(initialSerialNumber)

  const { data, isLoading, error } = useSerialTrace({ productPublicId: productPublicId ?? undefined, serialNumber: serialNumber || undefined })

  return (
    <Modal open title={t('warehouse.inventory.serialTrace.title')} onClose={onClose} size="lg">
      <div className="pb">
        <div className="f">
          <label htmlFor="serial-trace-input">{t('warehouse.inventory.serialTrace.serialNumber')}</label>
          <input id="serial-trace-input" type="text" value={serialNumber} onChange={(e) => setSerialNumber(e.target.value)} maxLength={60} />
        </div>
        {isLoading && <Spinner block label={t('common.loading')} />}
        {error && (
          <p className="ferr" role="alert">
            {error.message}
          </p>
        )}
        {!isLoading && !error && serialNumber && !data && <p className="help">{t('warehouse.inventory.serialTrace.notFound')}</p>}
        {data && (
          <>
            <div className="r3">
              <div className="f">
                <label>{t('warehouse.inventory.serialTrace.status')}</label>
                <p>{data.serial?.status ?? ''}</p>
              </div>
              <div className="f">
                <label>{t('warehouse.inventory.serialTrace.warehouse')}</label>
                <p>{data.serial?.warehouseCode ?? '—'}</p>
              </div>
              <div className="f">
                <label>{t('warehouse.inventory.serialTrace.bin')}</label>
                <p>{data.serial?.binCode ?? '—'}</p>
              </div>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.serialTrace.lot')}</label>
              <p>{data.serial?.lotNumber ?? '—'}</p>
            </div>
            <h3>{t('warehouse.inventory.serialTrace.movements')}</h3>
            <DataTable
              label={t('warehouse.inventory.serialTrace.movements')}
              columns={kardexColumns}
              rows={data.movements ?? []}
              rowKey={(m) => m.id ?? 0}
              pageSize={10}
              onRowClick={(m) => m.id != null && onOpenTxn(m.id)}
              dense
            />
            <h3>{t('warehouse.inventory.serialTrace.statusHistory')}</h3>
            {(data.statusHistory ?? []).length > 0 ? (
              <ul>
                {(data.statusHistory ?? []).map((h) => (
                  <li key={h.id}>
                    {formatDateTime(h.changedAtUtc, lang)} — {h.fromLabel ?? h.fromCode ?? '—'} → {h.toLabel ?? h.toCode} ({h.changedByName ?? ''})
                  </li>
                ))}
              </ul>
            ) : (
              <p className="help">—</p>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}

// =====================================================================================================================
// Pestaña Saldos
// =====================================================================================================================
interface PageProps {
  filters: InventoryFilterState
  page: number
  pageSize: number
  onPage: (page: number) => void
  onPageSize: (size: number) => void
  text: string
  onText: (text: string) => void
}

function BalancesTab({
  filters,
  page,
  pageSize,
  onPage,
  onPageSize,
  text,
  onText,
  onGenealogy,
  onSerialTrace,
}: PageProps & { onGenealogy: (lotId: number) => void; onSerialTrace: (productPublicId: string) => void }) {
  const t = useT()
  const lang = useLang()
  const base = useMemo(() => balancesQuery(filters), [filters])
  const query = useMemo(() => ({ ...base, skip: (page - 1) * pageSize, take: pageSize }), [base, page, pageSize])
  const { data, isLoading, error } = useInventoryBalances(query)

  const columns = useMemo<DataColumn<BalanceDto>[]>(
    () => [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'warehouse', header: t('warehouse.inventory.balances.columns.warehouse'), cell: (b) => b.warehouseCode, sortValue: (b) => b.warehouseCode },
      { id: 'bin', header: t('warehouse.inventory.balances.columns.bin'), cell: (b) => b.binCode ?? '', sortValue: (b) => b.binCode },
      { id: 'zone', header: t('warehouse.inventory.balances.columns.zone'), cell: (b) => b.zoneCode ?? '', sortValue: (b) => b.zoneCode, card: 'hidden' },
      { id: 'sku', header: t('warehouse.inventory.balances.columns.sku'), cell: (b) => <span className="ref">{b.sku}</span>, sortValue: (b) => b.sku, card: 'title' },
      { id: 'product', header: t('warehouse.inventory.balances.columns.product'), cell: (b) => b.productName, sortValue: (b) => b.productName },
      { id: 'owner', header: t('warehouse.inventory.balances.columns.owner'), cell: (b) => b.ownerName ?? '', sortValue: (b) => b.ownerName, card: 'hidden' },
      { id: 'category', header: t('warehouse.inventory.balances.columns.category'), cell: (b) => b.categoryName ?? '', sortValue: (b) => b.categoryName, card: 'hidden' },
      { id: 'lot', header: t('warehouse.inventory.balances.columns.lot'), cell: (b) => b.lotNumber ?? '', sortValue: (b) => b.lotNumber },
      { id: 'expiry', header: t('warehouse.inventory.balances.columns.expiry'), cell: (b) => formatDate(b.expiryDate, lang), sortValue: (b) => b.expiryDate },
      { id: 'onHand', header: t('warehouse.inventory.balances.columns.onHand'), cell: (b) => b.qtyOnHand, sortValue: (b) => b.qtyOnHand, align: 'end' },
      { id: 'reserved', header: t('warehouse.inventory.balances.columns.reserved'), cell: (b) => b.qtyReserved, sortValue: (b) => b.qtyReserved, align: 'end' },
      { id: 'available', header: t('warehouse.inventory.balances.columns.available'), cell: (b) => b.qtyAvailable, sortValue: (b) => b.qtyAvailable, align: 'end' },
      { id: 'cost', header: t('warehouse.inventory.balances.columns.cost'), cell: (b) => formatMoneyValue(b.costValue, lang), exportValue: (b) => b.costValue, sortValue: (b) => b.costValue, align: 'end', card: 'hidden' },
      { id: 'sale', header: t('warehouse.inventory.balances.columns.sale'), cell: (b) => formatMoneyValue(b.saleValue, lang), exportValue: (b) => b.saleValue, sortValue: (b) => b.saleValue, align: 'end', card: 'hidden' },
      {
        id: 'updated',
        header: t('warehouse.inventory.balances.columns.updated'),
        cell: (b) => formatDateTime(b.updatedAtUtc, lang),
        sortValue: (b) => b.updatedAtUtc,
        card: 'hidden',
      },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<BalanceDto>[]>(
    () => [
      {
        key: 'genealogy',
        label: t('warehouse.inventory.balances.actions.genealogy'),
        perm: 'inventory.view',
        visible: (b) => b.lotId != null,
        onClick: (b) => onGenealogy(b.lotId!),
      },
      {
        key: 'serialTrace',
        label: t('warehouse.inventory.balances.actions.serialTrace'),
        perm: 'inventory.view',
        onClick: (b) => onSerialTrace(b.productPublicId!),
      },
    ],
    [t, onGenealogy, onSerialTrace],
  )

  const totals = { onHand: data ? (data.totalOnHand ?? 0) : null, available: data ? (data.totalAvailable ?? 0) : null }

  return (
    <>
      <KardexSummaryBar filters={filters} balances={totals} />
      <Panel flush icon={<IconLayers />} title={t('warehouse.inventory.tabBalances')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={onText} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.inventory.tabBalances')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(b) => b.id ?? 0}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={onPage}
            onPageSize={onPageSize}
            exportRows={() => exportInventoryBalances(base)}
            rowActions={actions}
          />
        )}
      </Panel>
    </>
  )
}

// =====================================================================================================================
// Pestaña Kárdex
// =====================================================================================================================
function KardexTab({
  filters,
  page,
  pageSize,
  onPage,
  onPageSize,
  text,
  onText,
  onSerialTrace,
  onOpenTxn,
}: PageProps & { onSerialTrace: (productPublicId: string, serialNumber: string) => void; onOpenTxn: (id: number) => void }) {
  const t = useT()
  const columns = useKardexColumns(KARDEX_COLUMNS)
  const inverted = rangeInverted(filters.range)
  const base = useMemo(() => kardexQuery(filters), [filters])
  const query = useMemo(() => ({ ...base, skip: (page - 1) * pageSize, take: pageSize }), [base, page, pageSize])
  const { data, isLoading, error } = useInventoryTransactions(query, { enabled: !inverted })

  const actions = useMemo<RowAction<KardexRowDto>[]>(
    () => [
      {
        key: 'serialTrace',
        label: t('warehouse.inventory.kardex.actions.serialTrace'),
        perm: 'inventory.view',
        visible: (r) => Boolean(r.serialNumber) && Boolean(r.productPublicId),
        onClick: (r) => onSerialTrace(r.productPublicId!, r.serialNumber!),
      },
    ],
    [t, onSerialTrace],
  )

  return (
    <>
      <KardexSummaryBar filters={filters} />
      <Panel flush icon={<IconDoc />} title={t('warehouse.inventory.tabKardex')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={onText} />
        </div>
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
            label={t('warehouse.inventory.tabKardex')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(r) => r.id ?? 0}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={onPage}
            onPageSize={onPageSize}
            exportRows={() => exportInventoryTransactions(base)}
            rowActions={actions}
            onRowClick={(r) => r.id != null && onOpenTxn(r.id)}
          />
        )}
      </Panel>
    </>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function InventoryScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  // `?discrepancy=` sin pestaña abre Conciliación (el "Revisar" de "Necesita tu atención" ya manda tab=reconciliation)
  const tab = params.get('discrepancy') && !params.get('tab') ? 'reconciliation' : tabFromParam(params.get('tab'))
  // Solo al montar: la URL inicial trae filtros para las TRES pestañas (enlaces de Pulso, reportes, conteo, atención).
  const [filters, setFilters] = useState<InventoryFilterState>(() => filtersFromUrl(params))
  const [text, setText] = useState(filters.search)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const txnId = txnParam(params)
  const discrepancy = params.get('discrepancy')

  // el buscador va al API con una pausa de 250 ms; solo un texto distinto al aplicado vuelve a la página 1
  useEffect(() => {
    const next = text.trim()
    if (next === filters.search) return
    const h = setTimeout(() => {
      setFilters((f) => ({ ...f, search: next }))
      setPage(1)
    }, 250)
    return () => clearTimeout(h)
  }, [text, filters.search])

  const changeFilters = useCallback((patch: Partial<InventoryFilterState>) => {
    setFilters((f) => ({ ...f, ...patch }))
    setPage(1)
  }, [])
  const shownProducts = useResolvedProducts(filters.products)

  function changeTab(next: InventoryTab) {
    if (next === tab) return
    setPage(1)
    setParams(next === 'kardex' ? {} : { tab: next }, { replace: true })
  }

  /** Abre o cierra un parámetro de detalle (`txn`, `discrepancy`) sin tocar el resto de la URL. */
  const setDetailParam = useCallback(
    (name: 'txn' | 'discrepancy', value: string | null) => {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          if (value) next.set(name, value)
          else next.delete(name)
          if (name === 'discrepancy' && value && !next.get('tab')) next.set('tab', 'reconciliation')
          return next
        },
        { replace: true },
      )
    },
    [setParams],
  )
  const openTxn = useCallback((id: number) => setDetailParam('txn', String(id)), [setDetailParam])

  const [adjusting, setAdjusting] = useState(false)
  const [transferring, setTransferring] = useState(false)
  const [genealogyLotId, setGenealogyLotId] = useState<number | null>(null)
  const [serialTrace, setSerialTrace] = useState<{ productPublicId: string; serialNumber: string } | null>(null)

  const pageProps: PageProps = {
    filters,
    page,
    pageSize,
    onPage: setPage,
    onPageSize: (size) => {
      setPageSize(size)
      setPage(1)
    },
    text,
    onText: setText,
  }

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.inventory.title')}</h1>
          <p>{t('warehouse.inventory.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="inventory.adjust">
            <button type="button" className="btn" onClick={() => setAdjusting(true)}>
              {t('warehouse.inventory.adjust')}
            </button>
            <button type="button" className="btn flow" onClick={() => setTransferring(true)}>
              {t('warehouse.inventory.transfer')}
            </button>
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<InventoryTab>
          label={t('warehouse.inventory.title')}
          value={tab}
          onChange={changeTab}
          tabs={[
            { key: 'kardex', label: t('warehouse.inventory.tabKardex') },
            { key: 'balances', label: t('warehouse.inventory.tabBalances') },
            { key: 'reconciliation', label: t('warehouse.inventory.tabReconciliation') },
          ]}
        />
      </div>

      <InventoryFilterBar tab={tab} value={filters} onChange={changeFilters} shownProducts={shownProducts} />

      {tab === 'kardex' && (
        <KardexTab
          {...pageProps}
          onSerialTrace={(productPublicId, serialNumber) => setSerialTrace({ productPublicId, serialNumber })}
          onOpenTxn={openTxn}
        />
      )}
      {tab === 'balances' && (
        <BalancesTab
          {...pageProps}
          onGenealogy={setGenealogyLotId}
          onSerialTrace={(productPublicId) => setSerialTrace({ productPublicId, serialNumber: '' })}
        />
      )}
      {tab === 'reconciliation' && (
        <DiscrepanciesPanel
          filters={filters}
          page={page}
          pageSize={pageSize}
          onPage={pageProps.onPage}
          onPageSize={pageProps.onPageSize}
          onOpen={(publicId) => setDetailParam('discrepancy', publicId)}
        />
      )}

      <InventoryAdjustModal open={adjusting} onClose={() => setAdjusting(false)} />
      <InventoryTransferModal open={transferring} onClose={() => setTransferring(false)} />
      <GenealogyModal lotId={genealogyLotId} onClose={() => setGenealogyLotId(null)} onOpenTxn={openTxn} />
      {serialTrace && (
        <SerialTraceModal
          key={`${serialTrace.productPublicId}-${serialTrace.serialNumber}`}
          productPublicId={serialTrace.productPublicId}
          initialSerialNumber={serialTrace.serialNumber}
          onClose={() => setSerialTrace(null)}
          onOpenTxn={openTxn}
        />
      )}
      <DiscrepancyModal publicId={discrepancy} onClose={() => setDetailParam('discrepancy', null)} onOpenTxn={openTxn} />
      <KardexTransactionModal txnId={txnId} onClose={() => setDetailParam('txn', null)} />
    </div>
  )
}
