// Pantalla C (Lote F6) — Inventario: saldos, Kárdex, ajustes, transferencias, genealogía, rastro de serie y
// conciliación. `/warehouse/inventory` con pestañas Saldos / Kárdex / Conciliación. Sin StatusPipeline: el ledger
// no tiene estatus de entidad, solo mensajes de validación.
// Lectura: inventory.view + WMS_LOTSERIAL (aplicado por la ruta). Ajuste/transferencia/ejecutar conciliación:
// inventory.adjust.
import { useEffect, useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { parseApiDate } from '../../kernel/api/dates'
import type { components } from '../../kernel/api/schema'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  EMPTY_RANGE,
  DateRangeFilter,
  EmptyState,
  Filters,
  Modal,
  Panel,
  QBox,
  SearchSelect,
  Spinner,
  Tabs,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import {
  useInventoryBalances,
  useInventoryReconciliation,
  useInventoryTransactions,
  useLotGenealogy,
  useProductCategories,
  useProducts,
  useSerialTrace,
  useWarehouses,
  productLabel,
  warehouseLabel,
  type BalanceDto,
} from './api'
import { InventoryAdjustModal } from './InventoryAdjustModal'
import { InventoryTransferModal } from './InventoryTransferModal'
import { ProductPicker } from './pickers'

type KardexRowDto = components['schemas']['KardexRowDto']
type ReconciliationRowDto = components['schemas']['ReconciliationRowDto']

type TabKey = 'balances' | 'kardex' | 'reconciliation'
const PAGE_SIZE = 25
const PRODUCT_OPTION_LIMIT = 200

function formatDate(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium' }).format(date)
}

function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

/** Filtro booleano (fuera de un <Form>: no usa react-hook-form), como en ProductListScreen. */
function ToggleFilter({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="f">
      <label className="sw">
        <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{label}</span>
      </label>
    </div>
  )
}

/** Columnas de un Kárdex (compartidas por la pestaña y los modales de genealogía / rastro de serie). */
function useKardexColumns(): DataColumn<KardexRowDto>[] {
  const t = useT()
  const lang = useLang()
  return useMemo(
    () => [
      { id: 'date', header: t('warehouse.inventory.kardex.columns.date'), cell: (r) => formatDateTime(r.createdAtUtc, lang), card: 'title' },
      { id: 'type', header: t('warehouse.inventory.kardex.columns.type'), cell: (r) => <Chip>{r.type ?? r.typeCode}</Chip> },
      { id: 'sku', header: t('warehouse.inventory.kardex.columns.sku'), cell: (r) => <span className="ref">{r.sku}</span> },
      { id: 'product', header: t('warehouse.inventory.kardex.columns.product'), cell: (r) => r.productName },
      {
        id: 'warehouse',
        header: t('warehouse.inventory.kardex.columns.warehouse'),
        cell: (r) => [r.fromWarehouseCode, r.toWarehouseCode].filter(Boolean).join(' → ') || r.fromWarehouseCode || r.toWarehouseCode || '',
      },
      {
        id: 'bin',
        header: t('warehouse.inventory.kardex.columns.bin'),
        cell: (r) => [r.fromBinCode, r.toBinCode].filter(Boolean).join(' → ') || r.fromBinCode || r.toBinCode || '',
      },
      {
        id: 'quantity',
        header: t('warehouse.inventory.kardex.columns.quantity'),
        cell: (r) => {
          const q = r.signedQuantity ?? r.quantity ?? 0
          return <span className="ref">{q > 0 ? `+${q}` : q}</span>
        },
        align: 'end',
      },
      { id: 'ref', header: t('warehouse.inventory.kardex.columns.ref'), cell: (r) => r.refLabel ?? '' },
    ],
    [t, lang],
  )
}

// =====================================================================================================================
// Modal de genealogía de lote (se abre desde una fila de Saldos con lotId)
// =====================================================================================================================
function GenealogyModal({ lotId, onClose }: { lotId: number | null; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const kardexColumns = useKardexColumns()
  const { data, isLoading, error } = useLotGenealogy(lotId)

  const destinationColumns = useMemo<DataColumn<components['schemas']['GenealogyDestinationDto']>[]>(
    () => [
      { id: 'ref', header: t('warehouse.inventory.kardex.columns.ref'), cell: (d) => d.refLabel ?? '', card: 'title' },
      { id: 'client', header: t('warehouse.inventory.genealogy.client'), cell: (d) => d.clientName ?? '' },
      { id: 'consignee', header: t('warehouse.inventory.genealogy.consignee'), cell: (d) => d.consigneeName ?? '', card: 'hidden' },
      { id: 'quantity', header: t('warehouse.inventory.kardex.columns.quantity'), cell: (d) => d.quantity, align: 'end' },
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
}: {
  productPublicId: string | null
  initialSerialNumber: string
  onClose: () => void
}) {
  const t = useT()
  const lang = useLang()
  const kardexColumns = useKardexColumns()
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
function BalancesTab({ onGenealogy, onSerialTrace }: { onGenealogy: (lotId: number) => void; onSerialTrace: (productPublicId: string) => void }) {
  const t = useT()
  const lang = useLang()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [warehousePublicIds, setWarehousePublicIds] = useState<string[]>([])
  const [productPublicIds, setProductPublicIds] = useState<string[]>([])
  const [categoryIds, setCategoryIds] = useState<string[]>([])
  const [lotNumber, setLotNumber] = useState('')
  const [includeZero, setIncludeZero] = useState(false)
  const [onlyAvailable, setOnlyAvailable] = useState(false)
  const [page, setPage] = useState(1)

  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const { data: warehouses = [] } = useWarehouses({ includeInactive: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const { data: productPage } = useProducts({ activeOnly: true, take: PRODUCT_OPTION_LIMIT })
  const productOptions = useMemo(() => (productPage?.items ?? []).map((p) => ({ value: p.publicId ?? '', label: productLabel(p) })), [productPage])
  const { data: categories = [] } = useProductCategories()
  const categoryOptions = useMemo(() => categories.map((c) => ({ value: String(c.id), label: c.name ?? '' })), [categories])

  const query = useMemo(
    () => ({
      warehousePublicIds: warehousePublicIds.length > 0 ? warehousePublicIds : undefined,
      productPublicIds: productPublicIds.length > 0 ? productPublicIds : undefined,
      categoryIds: categoryIds.length > 0 ? categoryIds.map(Number) : undefined,
      lotNumber: lotNumber || undefined,
      includeZero: includeZero || undefined,
      onlyAvailable: onlyAvailable || undefined,
      search: search || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [warehousePublicIds, productPublicIds, categoryIds, lotNumber, includeZero, onlyAvailable, search, page],
  )
  const { data, isLoading, error } = useInventoryBalances(query)

  const columns = useMemo<DataColumn<BalanceDto>[]>(
    () => [
      { id: 'warehouse', header: t('warehouse.inventory.balances.columns.warehouse'), cell: (b) => b.warehouseCode },
      { id: 'bin', header: t('warehouse.inventory.balances.columns.bin'), cell: (b) => b.binCode ?? '' },
      { id: 'zone', header: t('warehouse.inventory.balances.columns.zone'), cell: (b) => b.zoneCode ?? '', card: 'hidden' },
      { id: 'sku', header: t('warehouse.inventory.balances.columns.sku'), cell: (b) => <span className="ref">{b.sku}</span>, card: 'title' },
      { id: 'product', header: t('warehouse.inventory.balances.columns.product'), cell: (b) => b.productName },
      { id: 'lot', header: t('warehouse.inventory.balances.columns.lot'), cell: (b) => b.lotNumber ?? '' },
      { id: 'expiry', header: t('warehouse.inventory.balances.columns.expiry'), cell: (b) => formatDate(b.expiryDate, lang) },
      { id: 'onHand', header: t('warehouse.inventory.balances.columns.onHand'), cell: (b) => b.qtyOnHand, align: 'end' },
      { id: 'reserved', header: t('warehouse.inventory.balances.columns.reserved'), cell: (b) => b.qtyReserved, align: 'end' },
      { id: 'available', header: t('warehouse.inventory.balances.columns.available'), cell: (b) => b.qtyAvailable, align: 'end' },
      { id: 'cost', header: t('warehouse.inventory.balances.columns.cost'), cell: (b) => b.costValue ?? '', align: 'end', card: 'hidden' },
      { id: 'sale', header: t('warehouse.inventory.balances.columns.sale'), cell: (b) => b.saleValue ?? '', align: 'end', card: 'hidden' },
      { id: 'updated', header: t('warehouse.inventory.balances.columns.updated'), cell: (b) => formatDateTime(b.updatedAtUtc, lang), card: 'hidden' },
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

  return (
    <>
      <Filters
        onClear={() => {
          setWarehousePublicIds([])
          setProductPublicIds([])
          setCategoryIds([])
          setLotNumber('')
          setIncludeZero(false)
          setOnlyAvailable(false)
        }}
      >
        <SearchSelect
          label={t('warehouse.inventory.balances.filters.warehouse')}
          options={warehouseOptions}
          value={warehousePublicIds}
          onChange={setWarehousePublicIds}
        />
        <SearchSelect
          label={t('warehouse.inventory.balances.filters.product')}
          options={productOptions}
          value={productPublicIds}
          onChange={setProductPublicIds}
        />
        <SearchSelect
          label={t('warehouse.inventory.balances.filters.category')}
          options={categoryOptions}
          value={categoryIds}
          onChange={setCategoryIds}
        />
        <div className="f">
          <label htmlFor="balances-lot">{t('warehouse.inventory.balances.filters.lotNumber')}</label>
          <input id="balances-lot" type="text" value={lotNumber} onChange={(e) => setLotNumber(e.target.value)} maxLength={60} />
        </div>
        <ToggleFilter label={t('warehouse.inventory.balances.filters.includeZero')} checked={includeZero} onChange={setIncludeZero} />
        <ToggleFilter label={t('warehouse.inventory.balances.filters.onlyAvailable')} checked={onlyAvailable} onChange={setOnlyAvailable} />
      </Filters>

      <Panel flush title={t('warehouse.inventory.tabBalances')} subtitle={data ? t('warehouse.inventory.balances.count', { count: data.total ?? 0 }) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={setText} />
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
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
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
function KardexTab({ onSerialTrace }: { onSerialTrace: (productPublicId: string, serialNumber: string) => void }) {
  const t = useT()
  const columns = useKardexColumns()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [types, setTypes] = useState<string[]>([])
  const [warehousePublicIds, setWarehousePublicIds] = useState<string[]>([])
  const [productPublicIds, setProductPublicIds] = useState<string[]>([])
  const [lotNumber, setLotNumber] = useState('')
  const [serialNumber, setSerialNumber] = useState('')
  const [page, setPage] = useState(1)

  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const { data: warehouses = [] } = useWarehouses({ includeInactive: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const { data: productPage } = useProducts({ activeOnly: true, take: PRODUCT_OPTION_LIMIT })
  const productOptions = useMemo(() => (productPage?.items ?? []).map((p) => ({ value: p.publicId ?? '', label: productLabel(p) })), [productPage])
  const { data: txnTypes = [] } = useLookups('InventoryTxnType')
  const typeOptions = useMemo(() => txnTypes.map((o) => ({ value: o.code, label: o.label })), [txnTypes])

  const rangeInvalid = Boolean(range.from && range.to && range.from > range.to)

  const query = useMemo(
    () => ({
      from: range.from || undefined,
      to: range.to || undefined,
      types: types.length > 0 ? types : undefined,
      warehousePublicIds: warehousePublicIds.length > 0 ? warehousePublicIds : undefined,
      productPublicIds: productPublicIds.length > 0 ? productPublicIds : undefined,
      lotNumber: lotNumber || undefined,
      serialNumber: serialNumber || undefined,
      search: search || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [range, types, warehousePublicIds, productPublicIds, lotNumber, serialNumber, search, page],
  )
  const { data, isLoading, error } = useInventoryTransactions(query, { enabled: !rangeInvalid })

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
      <Filters
        onClear={() => {
          setRange(EMPTY_RANGE)
          setTypes([])
          setWarehousePublicIds([])
          setProductPublicIds([])
          setLotNumber('')
          setSerialNumber('')
        }}
      >
        <DateRangeFilter label={t('warehouse.inventory.kardex.filters.range')} value={range} onChange={setRange} />
        <SearchSelect label={t('warehouse.inventory.kardex.filters.type')} options={typeOptions} value={types} onChange={setTypes} />
        <SearchSelect
          label={t('warehouse.inventory.kardex.filters.warehouse')}
          options={warehouseOptions}
          value={warehousePublicIds}
          onChange={setWarehousePublicIds}
        />
        <SearchSelect
          label={t('warehouse.inventory.kardex.filters.product')}
          options={productOptions}
          value={productPublicIds}
          onChange={setProductPublicIds}
        />
        <div className="f">
          <label htmlFor="kardex-lot">{t('warehouse.inventory.kardex.filters.lotNumber')}</label>
          <input id="kardex-lot" type="text" value={lotNumber} onChange={(e) => setLotNumber(e.target.value)} maxLength={60} />
        </div>
        <div className="f">
          <label htmlFor="kardex-serial">{t('warehouse.inventory.kardex.filters.serialNumber')}</label>
          <input id="kardex-serial" type="text" value={serialNumber} onChange={(e) => setSerialNumber(e.target.value)} maxLength={60} />
        </div>
      </Filters>

      <Panel flush title={t('warehouse.inventory.tabKardex')} subtitle={data ? t('warehouse.inventory.kardex.count', { count: data.total ?? 0 }) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={setText} />
        </div>
        {rangeInvalid ? (
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
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            rowActions={actions}
          />
        )}
      </Panel>
    </>
  )
}

// =====================================================================================================================
// Pestaña Conciliación
// =====================================================================================================================
function ReconciliationTab() {
  const t = useT()
  const lang = useLang()
  const [productPublicId, setProductPublicId] = useState<string | null>(null)
  const { data, isLoading, error, refetch, isFetched } = useInventoryReconciliation(
    { productPublicId: productPublicId ?? undefined },
    { enabled: false },
  )

  const mismatchColumns = useMemo<DataColumn<ReconciliationRowDto>[]>(
    () => [
      { id: 'sku', header: t('warehouse.inventory.reconciliation.columns.sku'), cell: (r) => <span className="ref">{r.sku}</span>, card: 'title' },
      { id: 'warehouse', header: t('warehouse.inventory.reconciliation.columns.warehouse'), cell: (r) => r.warehouseCode ?? '' },
      { id: 'bin', header: t('warehouse.inventory.reconciliation.columns.bin'), cell: (r) => r.binCode ?? '' },
      { id: 'lot', header: t('warehouse.inventory.reconciliation.columns.lot'), cell: (r) => r.lotNumber ?? '' },
      { id: 'ledger', header: t('warehouse.inventory.reconciliation.columns.ledgerQty'), cell: (r) => r.ledgerQty, align: 'end' },
      { id: 'balance', header: t('warehouse.inventory.reconciliation.columns.balanceQty'), cell: (r) => r.balanceQty, align: 'end' },
    ],
    [t],
  )

  return (
    <Panel title={t('warehouse.inventory.tabReconciliation')} subtitle={t('warehouse.inventory.reconciliation.subtitle')}>
      <div className="r2">
        <div className="f">
          <label>{t('warehouse.inventory.reconciliation.product')}</label>
          <ProductPicker value={productPublicId} onChange={(publicId) => setProductPublicId(publicId)} />
        </div>
        <Can perm="inventory.adjust">
          <div className="f">
            <label>&nbsp;</label>
            <button type="button" className="btn flow" onClick={() => refetch()} disabled={isLoading}>
              {isLoading ? t('common.loading') : t('warehouse.inventory.reconciliation.run')}
            </button>
          </div>
        </Can>
      </div>
      {error && (
        <p className="ferr" role="alert">
          {error.message}
        </p>
      )}
      {data && (
        <>
          <p>
            {t('warehouse.inventory.reconciliation.checkedAt')}: {formatDateTime(data.checkedAtUtc, lang)} ·{' '}
            {t('warehouse.inventory.reconciliation.balancesChecked', { count: data.balancesChecked ?? 0 })}
          </p>
          {(data.mismatches ?? []).length > 0 ? (
            <DataTable
              label={t('warehouse.inventory.tabReconciliation')}
              columns={mismatchColumns}
              rows={data.mismatches ?? []}
              rowKey={(r) => `${r.productPublicId ?? ''}-${r.binCode ?? ''}-${r.lotNumber ?? ''}`}
              pageSize={25}
            />
          ) : (
            <EmptyState title={t('warehouse.inventory.reconciliation.ok')} />
          )}
        </>
      )}
      {!data && !error && isFetched && !isLoading && <EmptyState title={t('warehouse.inventory.reconciliation.ok')} />}
    </Panel>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function InventoryScreen() {
  const t = useT()
  const [tab, setTab] = useState<TabKey>('balances')
  const [adjusting, setAdjusting] = useState(false)
  const [transferring, setTransferring] = useState(false)
  const [genealogyLotId, setGenealogyLotId] = useState<number | null>(null)
  const [serialTrace, setSerialTrace] = useState<{ productPublicId: string; serialNumber: string } | null>(null)

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
        <Tabs<TabKey>
          label={t('warehouse.inventory.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'balances', label: t('warehouse.inventory.tabBalances') },
            { key: 'kardex', label: t('warehouse.inventory.tabKardex') },
            { key: 'reconciliation', label: t('warehouse.inventory.tabReconciliation') },
          ]}
        />
      </div>

      {tab === 'balances' && (
        <BalancesTab
          onGenealogy={setGenealogyLotId}
          onSerialTrace={(productPublicId) => setSerialTrace({ productPublicId, serialNumber: '' })}
        />
      )}
      {tab === 'kardex' && (
        <KardexTab onSerialTrace={(productPublicId, serialNumber) => setSerialTrace({ productPublicId, serialNumber })} />
      )}
      {tab === 'reconciliation' && <ReconciliationTab />}

      <InventoryAdjustModal open={adjusting} onClose={() => setAdjusting(false)} />
      <InventoryTransferModal open={transferring} onClose={() => setTransferring(false)} />
      <GenealogyModal lotId={genealogyLotId} onClose={() => setGenealogyLotId(null)} />
      {serialTrace && (
        <SerialTraceModal
          key={`${serialTrace.productPublicId}-${serialTrace.serialNumber}`}
          productPublicId={serialTrace.productPublicId}
          initialSerialNumber={serialTrace.serialNumber}
          onClose={() => setSerialTrace(null)}
        />
      )}
    </div>
  )
}
