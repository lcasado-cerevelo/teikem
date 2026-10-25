// Almacén del cliente (Lote 154): MIS existencias, productos, movimientos y el valor de mi inventario, solo lectura. Cuatro secciones en una pestaña; «Valor» solo si mi acceso lo incluye. Todo llega ya
// limitado a mi cliente por el servidor (lo mío, sin posiciones ni lo de nadie más).
import { useCallback, useEffect, useRef, useState } from 'react'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { CellText, DataTable, ExportButton, Kpi, KpiRow, PageHeader, type Column } from '../../kernel/ui/desk'
import { type CsvColumn, csvDay, fetchAll, filtersSentence, runExport, type ExportFormat } from '../../kernel/ui/tableExport'
import { DateRangeFilter } from '../../kernel/ui/DateRangeFilter'
import { type DayRange, hasRange } from '../../kernel/ui/dateRanges'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Card, Chip, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { formatDate, formatDateTime, formatMoney, formatQty } from '../format'
import { useOffer } from '../capabilities'
import { cancelArrival, cancelExit, type PortalArrivalDto, type PortalExitListItemDto, type PortalMovementDto, type PortalProductDto, type PortalStockItemDto, type ValuationRowDto, getValuation, listArrivals, listExits, listMovements, listStock, listWarehouseProducts, PAGE_SIZE } from '../portalApi'
import { EditExitForm, EditProductForm, NewArrivalForm, NewExitForm, NewProductForm } from './CreateForms'
import { Segments } from './Segments'
import type { Lang } from '../../kernel/i18n/i18n'

type Section = 'stock' | 'products' | 'movements' | 'value' | 'arrivals' | 'exits'

interface Page<T> {
  items?: T[] | null
  total?: number
}

/** Lista paginada con «Cargar más», búsqueda que espera a que se deje de escribir y `exportAll` (todas las filas del filtro vigente, para exportar). */
function usePaged<T>(load: (search: string, skip: number, take: number, range: DayRange) => Promise<Page<T>>) {
  const [search, setSearch] = useState('')
  const [range, setRange] = useState<DayRange>({ from: '', to: '' })   // solo lo usan las listas con filtro de fecha (movimientos y salidas)
  const [query, setQuery] = useState('')
  const [items, setItems] = useState<T[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const seq = useRef(0)
  useEffect(() => {
    const id = setTimeout(() => setQuery(search.trim()), 350)
    return () => clearTimeout(id)
  }, [search])
  const run = useCallback(
    async (skip: number) => {
      const mine = ++seq.current
      setLoading(true)
      setError(null)
      try {
        const page = await load(query, skip, PAGE_SIZE, range)
        if (mine !== seq.current) return
        setItems((prev) => (skip === 0 ? (page.items ?? []) : [...prev, ...(page.items ?? [])]))
        setTotal(page.total ?? 0)
        return page
      } catch (e) {
        if (mine === seq.current) setError(apiErrorMessage(e))
      } finally {
        if (mine === seq.current) setLoading(false)
      }
    },
    [load, query, range],
  )
  useEffect(() => {
    void run(0)
  }, [run])
  const exportAll = async (format: ExportFormat, title: string, columns: readonly CsvColumn<T>[], filters: string) => {
    const all = await fetchAll((skip, take) => load(query, skip, take, range))
    await runExport({ format, title, columns, rows: all.items, filters })
  }
  return { search, setSearch, query, range, setRange, items, total, loading, error, reload: () => void run(0), more: () => void run(items.length), exportAll }
}

/** «Filtros: Buscar «x» · Del … al …» de una lista de Almacén. */
const listFilters = (t: (key: string, vars?: Record<string, string | number>) => string, query: string, range: DayRange) =>
  filtersSentence([!!query && t('table.fSearch', { q: query }), hasRange(range) && t('table.fRange', { from: range.from || '…', to: range.to || '…' })])

function Footer({ loading, error, shown, total, empty, onRetry, onMore, onExport }: { loading: boolean; error: string | null; shown: number; total: number; empty: string; onRetry: () => void; onMore: () => void; onExport?: (format: ExportFormat) => Promise<void> }) {
  const { t } = useT()
  return (
    <>
      {!loading && total > 0 && (
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
          <Muted>{t('table.count', { shown, total })}</Muted>
          {onExport && <ExportButton onExport={onExport} testID="warehouse-export" />}
        </View>
      )}
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={onRetry} />
        </>
      )}
      {!loading && !error && shown === 0 && <Muted>{empty}</Muted>}
      {loading && <Spinner />}
      {!loading && shown < total && <Button title={t('warehouse.loadMore', { shown, total })} variant="secondary" onPress={onMore} testID="warehouse-more" />}
    </>
  )
}

function StockSection({ lang }: { lang: Lang }) {
  const colors = useColors()
  const { t } = useT()
  const wide = useIsWide()
  const list = usePaged(useCallback((s: string, skip: number, take: number) => listStock(s, skip, take), []))
  const columns: Column<PortalStockItemDto>[] = [
    { key: 'sku', title: t('warehouse.col.sku'), width: 160, render: (i) => <CellText bold>{i.sku}</CellText>, csv: (i) => i.sku },
    { key: 'name', title: t('warehouse.col.product'), flex: 3, render: (i) => <CellText>{i.productName}</CellText>, csv: (i) => i.productName },
    { key: 'wh', title: t('warehouse.col.warehouse'), width: 110, render: (i) => <CellText muted>{i.warehouseCode}</CellText>, csv: (i) => i.warehouseCode },
    { key: 'lot', title: t('warehouse.col.lot'), width: 120, render: (i) => <CellText muted>{i.lotNumber ?? '—'}</CellText>, csv: (i) => i.lotNumber ?? '' },
    { key: 'exp', title: t('warehouse.col.expires'), width: 110, render: (i) => <CellText muted>{i.expiryDate ? formatDate(i.expiryDate, lang) : '—'}</CellText>, csv: (i) => i.expiryDate ?? '' },
    { key: 'onHand', title: t('warehouse.onHand'), width: 100, align: 'right', render: (i) => <CellText align="right" bold>{formatQty(i.onHand, lang)}</CellText>, csv: (i) => i.onHand },
    { key: 'reserved', title: t('warehouse.reserved'), width: 100, align: 'right', render: (i) => <CellText align="right" muted>{formatQty(i.reserved, lang)}</CellText>, csv: (i) => i.reserved },
    { key: 'available', title: t('warehouse.available'), width: 110, align: 'right', render: (i) => <CellText align="right" bold>{formatQty(i.available, lang)}</CellText>, csv: (i) => i.available },
  ]
  const exportAll = (format: ExportFormat) => list.exportAll(format, t('warehouse.sections.stock'), columns, filtersSentence([!!list.query && t('table.fSearch', { q: list.query })]))
  if (wide) {
    return (
      <>
        <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchStock')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
        <DataTable columns={columns} rows={list.items} keyOf={(i) => `${i.productPublicId}|${i.warehousePublicId}|${i.lotNumber ?? ''}|${i.expiryDate ?? ''}`} empty={list.loading ? undefined : list.query ? t('warehouse.noMatches') : t('warehouse.emptyStock')} total={list.loading && list.items.length === 0 ? undefined : list.total} onExport={exportAll} testID="stock-table" />
        {list.loading && <Spinner />}
        {!list.loading && list.items.length < list.total && <Button title={t('warehouse.loadMore', { shown: list.items.length, total: list.total })} variant="secondary" onPress={list.more} testID="warehouse-more" />}
        {!!list.error && <Notice>{list.error}</Notice>}
      </>
    )
  }
  return (
    <>
      <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchStock')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
      {list.items.map((i, n) => (
        <Card key={`${i.productPublicId}|${i.warehousePublicId}|${i.lotNumber ?? ''}|${n}`}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{i.sku} · {i.productName}</Text>
          <Muted>{[i.warehouseCode, i.lotNumber ? t('warehouse.lot', { lot: i.lotNumber }) : null, i.expiryDate ? t('warehouse.expires', { date: formatDate(i.expiryDate, lang) }) : null].filter(Boolean).join(' · ')}</Muted>
          <View style={{ flexDirection: 'row', gap: 16, flexWrap: 'wrap' }}>
            <Text style={{ color: colors.text }}>{t('warehouse.onHand')}: <Text style={{ fontWeight: '700' }}>{formatQty(i.onHand, lang)}</Text></Text>
            <Text style={{ color: colors.text }}>{t('warehouse.available')}: <Text style={{ fontWeight: '700' }}>{formatQty(i.available, lang)}</Text></Text>
            {(i.reserved ?? 0) > 0 && <Text style={{ color: colors.muted }}>{t('warehouse.reserved')}: {formatQty(i.reserved, lang)}</Text>}
          </View>
        </Card>
      ))}
      <Footer loading={list.loading} error={list.error} shown={list.items.length} total={list.total} empty={list.query ? t('warehouse.noMatches') : t('warehouse.emptyStock')} onRetry={list.reload} onMore={list.more} onExport={exportAll} />
    </>
  )
}

function ProductsSection({ lang, canCreate }: { lang: Lang; canCreate: boolean }) {
  const colors = useColors()
  const { t } = useT()
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const wide = useIsWide()
  const list = usePaged(useCallback((s: string, skip: number, take: number) => listWarehouseProducts(s, skip, take), []))
  const columns: Column<PortalProductDto>[] = [
    { key: 'sku', title: t('warehouse.col.sku'), width: 160, render: (p) => <CellText bold>{p.sku}</CellText>, csv: (p) => p.sku },
    { key: 'name', title: t('warehouse.col.product'), flex: 3, render: (p) => <CellText>{p.name}</CellText>, csv: (p) => p.name },
    { key: 'brand', title: t('warehouse.col.brand'), flex: 1, render: (p) => <CellText muted>{[p.brand, p.category].filter(Boolean).join(' · ') || '—'}</CellText>, csv: (p) => [p.brand, p.category].filter(Boolean).join(' · ') },
    { key: 'barcode', title: t('warehouse.col.barcode'), width: 150, render: (p) => <CellText muted>{p.barcode ?? '—'}</CellText>, csv: (p) => p.barcode ?? '' },
    { key: 'track', title: t('warehouse.col.tracking'), width: 100, render: (p) => (p.trackingType === 'LOT' ? <Chip label={t('warehouse.tracking.LOT')} tone="info" /> : p.trackingType === 'SERIAL' ? <Chip label={t('warehouse.tracking.SERIAL')} tone="info" /> : <CellText muted>—</CellText>), csv: (p) => (p.trackingType === 'LOT' || p.trackingType === 'SERIAL' ? t(`warehouse.tracking.${p.trackingType}`) : '') },
    { key: 'onHand', title: t('warehouse.onHand'), width: 100, align: 'right', render: (p) => <CellText align="right" bold>{formatQty(p.onHand, lang)}</CellText>, csv: (p) => p.onHand },
    { key: 'available', title: t('warehouse.available'), width: 110, align: 'right', render: (p) => <CellText align="right">{formatQty(p.available, lang)}</CellText>, csv: (p) => p.available },
    ...(canCreate ? [{ key: 'edit', title: '', width: 90, align: 'right' as const, render: (p: PortalProductDto) => <Button title={t('create.product.edit')} variant="link" onPress={() => setEditing(p.publicId ?? null)} testID={`edit-product-${p.sku}`} /> }] : []),
  ]
  const exportAll = (format: ExportFormat) => list.exportAll(format, t('warehouse.sections.products'), columns, filtersSentence([!!list.query && t('table.fSearch', { q: list.query })]))
  if (wide) {
    const editingProduct = list.items.find((p) => p.publicId === editing)
    return (
      <>
        <View style={{ flexDirection: 'row', alignItems: 'flex-end', gap: 12, flexWrap: 'wrap' }}>
          <View style={{ flex: 1, minWidth: 260 }}>
            <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchProducts')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
          </View>
          {canCreate && !creating && <Button title={t('create.product.title')} onPress={() => setCreating(true)} testID="create-product" />}
        </View>
        {creating && <NewProductForm onCancel={() => setCreating(false)} onDone={() => { setCreating(false); list.reload() }} />}
        {editingProduct && <EditProductForm key={editingProduct.publicId} product={editingProduct} onCancel={() => setEditing(null)} onDone={() => { setEditing(null); list.reload() }} />}
        <DataTable columns={columns} rows={list.items} keyOf={(p) => String(p.publicId)} empty={list.loading ? undefined : list.query ? t('warehouse.noMatches') : t('warehouse.emptyProducts')} total={list.loading && list.items.length === 0 ? undefined : list.total} onExport={exportAll} testID="products-table" />
        {list.loading && <Spinner />}
        {!list.loading && list.items.length < list.total && <Button title={t('warehouse.loadMore', { shown: list.items.length, total: list.total })} variant="secondary" onPress={list.more} testID="warehouse-more" />}
        {!!list.error && <Notice>{list.error}</Notice>}
      </>
    )
  }
  return (
    <>
      {canCreate && !creating && <Button title={t('create.product.title')} onPress={() => setCreating(true)} testID="create-product" />}
      {creating && <NewProductForm onCancel={() => setCreating(false)} onDone={() => { setCreating(false); list.reload() }} />}
      <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchProducts')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
      {list.items.map((p) => editing === p.publicId ? (
        <EditProductForm key={p.publicId} product={p} onCancel={() => setEditing(null)} onDone={() => { setEditing(null); list.reload() }} />
      ) : (
        <Card key={p.publicId}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{p.sku} · {p.name}</Text>
          <Muted>{[p.brand, p.category, p.barcode].filter(Boolean).join(' · ')}</Muted>
          <View style={{ flexDirection: 'row', gap: 16, flexWrap: 'wrap', alignItems: 'center' }}>
            <Text style={{ color: colors.text }}>{t('warehouse.onHand')}: <Text style={{ fontWeight: '700' }}>{formatQty(p.onHand, lang)}</Text></Text>
            <Text style={{ color: colors.text }}>{t('warehouse.available')}: <Text style={{ fontWeight: '700' }}>{formatQty(p.available, lang)}</Text></Text>
            {p.trackingType === 'LOT' && <Chip label={t('warehouse.tracking.LOT')} tone="info" />}
            {p.trackingType === 'SERIAL' && <Chip label={t('warehouse.tracking.SERIAL')} tone="info" />}
          </View>
          {canCreate && <Button title={t('create.product.edit')} variant="link" onPress={() => setEditing(p.publicId ?? null)} testID={`edit-product-${p.sku}`} />}
        </Card>
      ))}
      <Footer loading={list.loading} error={list.error} shown={list.items.length} total={list.total} empty={list.query ? t('warehouse.noMatches') : t('warehouse.emptyProducts')} onRetry={list.reload} onMore={list.more} onExport={exportAll} />
    </>
  )
}

function MovementsSection({ lang }: { lang: Lang }) {
  const colors = useColors()
  const { t } = useT()
  const wide = useIsWide()
  const list = usePaged(useCallback((s: string, skip: number, take: number, r: DayRange) => listMovements(s, skip, take, r), []))
  const columns: Column<PortalMovementDto>[] = [
    { key: 'date', title: t('warehouse.col.date'), width: 150, render: (m) => <CellText>{formatDateTime(m.createdAtUtc, lang)}</CellText>, csv: (m) => csvDay(m.createdAtUtc) },
    { key: 'type', title: t('warehouse.col.type'), width: 150, render: (m) => <CellText bold>{m.type}</CellText>, csv: (m) => m.type },
    { key: 'sku', title: t('warehouse.col.sku'), width: 150, render: (m) => <CellText>{m.sku}</CellText>, csv: (m) => m.sku },
    { key: 'name', title: t('warehouse.col.product'), flex: 2, render: (m) => <CellText muted>{m.productName}</CellText>, csv: (m) => m.productName },
    { key: 'qty', title: t('warehouse.col.qty'), width: 110, align: 'right', render: (m) => { const q = m.quantity ?? 0; return <Chip label={`${q > 0 ? '+' : ''}${formatQty(q, lang)}`} tone={q > 0 ? 'ok' : q < 0 ? 'danger' : 'neutral'} /> }, csv: (m) => m.quantity ?? 0 },
    { key: 'wh', title: t('warehouse.col.warehouse'), width: 100, render: (m) => <CellText muted>{m.warehouseCode}</CellText>, csv: (m) => m.warehouseCode },
    { key: 'lot', title: t('warehouse.col.lot'), width: 110, render: (m) => <CellText muted>{m.lotNumber ?? '—'}</CellText>, csv: (m) => m.lotNumber ?? '' },
    { key: 'ref', title: t('warehouse.col.reference'), flex: 1, render: (m) => <CellText muted>{[m.reference, m.reason].filter(Boolean).join(' · ') || '—'}</CellText>, csv: (m) => [m.reference, m.reason].filter(Boolean).join(' · ') },
  ]
  const exportAll = (format: ExportFormat) => list.exportAll(format, t('warehouse.sections.movements'), columns, listFilters(t, list.query, list.range))
  if (wide) {
    return (
      <>
        <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchMovements')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
        <DateRangeFilter value={list.range} onChange={list.setRange} testID="movements-range" />
        <DataTable columns={columns} rows={list.items} keyOf={(m) => String(m.id)} empty={list.loading ? undefined : list.query ? t('warehouse.noMatches') : t('warehouse.emptyMovements')} total={list.loading && list.items.length === 0 ? undefined : list.total} onExport={exportAll} testID="movements-table" />
        {list.loading && <Spinner />}
        {!list.loading && list.items.length < list.total && <Button title={t('warehouse.loadMore', { shown: list.items.length, total: list.total })} variant="secondary" onPress={list.more} testID="warehouse-more" />}
        {!!list.error && <Notice>{list.error}</Notice>}
      </>
    )
  }
  return (
    <>
      <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchMovements')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
      <DateRangeFilter value={list.range} onChange={list.setRange} testID="movements-range" />
      {list.items.map((m) => {
        const qty = m.quantity ?? 0
        return (
          <Card key={String(m.id)}>
            <Text style={{ fontWeight: '700', color: colors.text }}>{m.type} · {m.sku}</Text>
            <Muted>{m.productName}</Muted>
            <View style={{ flexDirection: 'row', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
              <Chip label={`${qty > 0 ? '+' : ''}${formatQty(qty, lang)}`} tone={qty > 0 ? 'ok' : qty < 0 ? 'danger' : 'neutral'} />
              <Muted>{[m.warehouseCode, m.lotNumber ? t('warehouse.lot', { lot: m.lotNumber }) : null, m.reference].filter(Boolean).join(' · ')}</Muted>
            </View>
            <Muted>{formatDateTime(m.createdAtUtc, lang)}{m.reason ? ` · ${m.reason}` : ''}</Muted>
          </Card>
        )
      })}
      <Footer loading={list.loading} error={list.error} shown={list.items.length} total={list.total} empty={list.query || hasRange(list.range) ? t('warehouse.noMatches') : t('warehouse.emptyMovements')} onRetry={list.reload} onMore={list.more} onExport={exportAll} />
    </>
  )
}

/** Mis avisos de llegada: los creo aquí (si la compañía me lo permite) y puedo cancelar los que siguen esperados. */
function ArrivalsSection({ lang }: { lang: Lang }) {
  const colors = useColors()
  const { t } = useT()
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<PortalArrivalDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [range, setRange] = useState<DayRange>({ from: '', to: '' })
  useEffect(() => {
    const id = setTimeout(() => setQuery(search.trim()), 350)
    return () => clearTimeout(id)
  }, [search])
  const list = useAsync(() => listArrivals(query, range), [query, range.from, range.to])
  const arrivalColumns: CsvColumn<PortalArrivalDto>[] = [
    { title: t('create.arrivals.number'), csv: (a) => a.number ?? '' },
    { title: t('warehouse.col.reference'), csv: (a) => a.reference ?? '' },
    { title: t('warehouse.col.warehouse'), csv: (a) => a.warehouseCode ?? '' },
    { title: t('create.arrivals.expectedShort'), csv: (a) => a.expectedDate ?? '' },
    { title: t('warehouse.col.status'), csv: (a) => a.status ?? '' },
    { title: t('create.arrivals.lines'), csv: (a) => (a.lines ?? []).map((l) => `${l.sku} × ${l.expectedQty ?? 0}`).join('; ') },
  ]
  const exportAll = (format: ExportFormat) => runExport({ format, title: t('warehouse.sections.arrivals'), columns: arrivalColumns, rows: list.data ?? [], filters: listFilters(t, query, range) })
  async function cancel(id: number) {
    setError(null)
    try {
      await cancelArrival(id)
      list.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }
  return (
    <>
      {!creating && <Button title={t('create.arrival.title')} onPress={() => setCreating(true)} testID="create-arrival" />}
      {creating && <NewArrivalForm onCancel={() => setCreating(false)} onDone={() => { setCreating(false); list.reload() }} />}
      {editing && <NewArrivalForm key={editing.id} arrival={editing} onCancel={() => setEditing(null)} onDone={() => { setEditing(null); list.reload() }} />}
      {!!error && <Notice>{error}</Notice>}
      <Field label={t('warehouse.search')} value={search} onChangeText={setSearch} placeholder={t('warehouse.searchArrivals')} autoCapitalize="none" returnKeyType="search" testID="arrivals-search" />
      <DateRangeFilter value={range} onChange={setRange} label={t('warehouse.expectedDate')} testID="arrivals-range" />
      {(list.data ?? []).length > 0 && (
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
          <Muted>{t('table.count', { shown: (list.data ?? []).length, total: (list.data ?? []).length })}</Muted>
          <ExportButton onExport={exportAll} testID="warehouse-export" />
        </View>
      )}
      {(list.data ?? []).map((a) => (
        <Card key={String(a.id)}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Text style={{ fontWeight: '700', color: colors.text }}>{[a.number, a.reference].filter(Boolean).join(' · ') || `#${a.id}`}</Text>
            <Chip label={a.status ?? ''} tone={a.statusCode === 'EXPECTED' ? 'info' : a.statusCode === 'CANCELLED' ? 'neutral' : 'ok'} />
          </View>
          <Muted>{[a.warehouseCode, a.expectedDate ? t('create.arrivals.expected', { date: formatDate(a.expectedDate, lang) }) : null].filter(Boolean).join(' · ')}</Muted>
          {(a.lines ?? []).map((l, n) => (
            <Text key={n} style={{ color: colors.text }}>{l.sku} · {l.productName} — <Text style={{ fontWeight: '700' }}>{formatQty(l.expectedQty, lang)}</Text>{l.lotNumber ? ` · ${t('warehouse.lot', { lot: l.lotNumber })}` : ''}</Text>
          ))}
          {a.canCancel && <Button title={t('create.editNotice')} variant="secondary" onPress={() => { setCreating(false); setEditing(a) }} />}
          {a.canCancel && <Button title={t('create.cancelNotice')} variant="secondary" onPress={() => void cancel(a.id as number)} />}
        </Card>
      ))}
      {list.loading && <Spinner />}
      {!!list.error && <Notice>{apiErrorMessage(list.error)}</Notice>}
      {!list.loading && !list.error && (list.data ?? []).length === 0 && <Muted>{query || hasRange(range) ? t('warehouse.noMatches') : t('create.arrivals.empty')}</Muted>}
    </>
  )
}

/** Mis avisos de salida: los creo aquí (si la compañía me lo permite) y cancelo los que siguen solicitados. */
function ExitsSection({ lang }: { lang: Lang }) {
  const colors = useColors()
  const { t } = useT()
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const list = usePaged(useCallback((s: string, skip: number, take: number, r: DayRange) => listExits(s, skip, take, r), []))
  const exitColumns: CsvColumn<PortalExitListItemDto>[] = [
    { title: t('create.arrivals.number'), csv: (n) => n.number ?? '' },
    { title: t('warehouse.col.warehouse'), csv: (n) => n.warehouseCode ?? '' },
    { title: t('warehouse.col.reference'), csv: (n) => n.reference ?? '' },
    { title: t('create.exits.destination'), csv: (n) => n.destination ?? '' },
    { title: t('create.exits.requestedDate'), csv: (n) => n.requestedDate ?? '' },
    { title: t('warehouse.col.status'), csv: (n) => n.status ?? '' },
    { title: t('create.exits.ordered'), csv: (n) => n.qtyOrdered ?? 0 },
    { title: t('create.exits.shipped'), csv: (n) => n.qtyShipped ?? 0 },
  ]
  const exportAll = (format: ExportFormat) => list.exportAll(format, t('warehouse.sections.exits'), exitColumns, listFilters(t, list.query, list.range))
  async function cancel(publicId: string) {
    setError(null)
    try {
      await cancelExit(publicId)
      list.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }
  return (
    <>
      {!creating && <Button title={t('create.exit.title')} onPress={() => setCreating(true)} testID="create-exit" />}
      {creating && <NewExitForm onCancel={() => setCreating(false)} onDone={() => { setCreating(false); list.reload() }} />}
      {editing && <EditExitForm key={editing} publicId={editing} onCancel={() => setEditing(null)} onDone={() => { setEditing(null); list.reload() }} />}
      {!!error && <Notice>{error}</Notice>}
      <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchExits')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
      <DateRangeFilter value={list.range} onChange={list.setRange} label={t('warehouse.requestedDate')} testID="exits-range" />
      {list.items.map((n) => (
        <Card key={n.publicId}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Text style={{ fontWeight: '700', color: colors.text }}>{n.number}</Text>
            <Chip label={n.status ?? ''} tone={n.statusCode === 'REQUESTED' ? 'info' : n.statusCode === 'CANCELLED' ? 'neutral' : 'ok'} />
            {n.hasBackorder && <Chip label={t('create.exits.pending')} tone="warn" />}
          </View>
          <Muted>{[n.warehouseCode, n.reference, n.destination, n.requestedDate ? formatDate(n.requestedDate, lang) : null].filter(Boolean).join(' · ')}</Muted>
          <Text style={{ color: colors.text }}>{formatQty(n.qtyShipped, lang)} / {formatQty(n.qtyOrdered, lang)}</Text>
          {n.statusCode === 'REQUESTED' && <Button title={t('create.editNotice')} variant="secondary" onPress={() => { setCreating(false); setEditing(n.publicId as string) }} />}
          {n.statusCode === 'REQUESTED' && <Button title={t('create.cancelNotice')} variant="secondary" onPress={() => void cancel(n.publicId as string)} />}
        </Card>
      ))}
      <Footer loading={list.loading} error={list.error} shown={list.items.length} total={list.total} empty={list.query || hasRange(list.range) ? t('warehouse.noMatches') : t('create.exits.empty')} onRetry={list.reload} onMore={list.more} onExport={exportAll} />
    </>
  )
}

function ValueSection({ lang }: { lang: Lang }) {
  const colors = useColors()
  const { t } = useT()
  const [report, setReport] = useState<{ totalValue: number; totalQty: number; unvaluedQty: number; method: string } | null>(null)
  const load = useCallback(async (s: string, skip: number, take: number) => {
    const page = await getValuation(s, skip, take)
    setReport({ totalValue: page.totalValue ?? 0, totalQty: page.totalQty ?? 0, unvaluedQty: page.unvaluedQty ?? 0, method: page.method ?? 'AVERAGE' })
    return page
  }, [])
  const wide = useIsWide()
  const list = usePaged(load)
  const columns: Column<ValuationRowDto>[] = [
    { key: 'sku', title: t('warehouse.col.sku'), width: 160, render: (r) => <CellText bold>{r.sku}</CellText>, csv: (r) => r.sku },
    { key: 'name', title: t('warehouse.col.product'), flex: 3, render: (r) => <CellText>{r.productName}</CellText>, csv: (r) => r.productName },
    { key: 'onHand', title: t('warehouse.onHand'), width: 110, align: 'right', render: (r) => <CellText align="right">{formatQty(r.onHand, lang)}</CellText>, csv: (r) => r.onHand },
    { key: 'cost', title: t('warehouse.unitCost'), width: 130, align: 'right', render: (r) => <CellText align="right" muted>{r.unitCost == null ? '—' : formatMoney(r.unitCost, lang)}</CellText>, csv: (r) => r.unitCost ?? '' },
    { key: 'value', title: t('warehouse.value'), width: 140, align: 'right', render: (r) => <CellText align="right" bold>{formatMoney(r.value, lang)}</CellText>, csv: (r) => r.value },
  ]
  const exportAll = (format: ExportFormat) => list.exportAll(format, t('warehouse.sections.value'), columns, filtersSentence([!!list.query && t('table.fSearch', { q: list.query })]))
  if (wide) {
    return (
      <>
        {!!report && (
          <KpiRow>
            <Kpi label={t('warehouse.valueTotal')} value={formatMoney(report.totalValue, lang)} hint={t(`warehouse.method.${report.method}`)} testID="warehouse-value-total" />
            <Kpi label={t('warehouse.col.units')} value={formatQty(report.totalQty, lang)} />
            {report.unvaluedQty > 0 && <Kpi label={t('warehouse.col.unvalued')} value={formatQty(report.unvaluedQty, lang)} tone="warn" hint={t('warehouse.unvaluedShort')} />}
          </KpiRow>
        )}
        <Muted>{t('warehouse.valueHelp')}</Muted>
        <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchProducts')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
        <DataTable columns={columns} rows={list.items} keyOf={(r) => String(r.productPublicId)} empty={list.loading ? undefined : list.query ? t('warehouse.noMatches') : t('warehouse.emptyStock')} total={list.loading && list.items.length === 0 ? undefined : list.total} onExport={exportAll} testID="value-table" />
        {list.loading && <Spinner />}
        {!list.loading && list.items.length < list.total && <Button title={t('warehouse.loadMore', { shown: list.items.length, total: list.total })} variant="secondary" onPress={list.more} testID="warehouse-more" />}
        {!!list.error && <Notice>{list.error}</Notice>}
      </>
    )
  }
  return (
    <>
      {!!report && (
        <Card>
          <Muted>{t('warehouse.valueTotal')}</Muted>
          <Text style={{ fontSize: 30, fontWeight: '800', color: colors.brand }} testID="warehouse-value-total">{formatMoney(report.totalValue, lang)}</Text>
          <Muted>{t('warehouse.valueUnits', { qty: formatQty(report.totalQty, lang) })} · {t(`warehouse.method.${report.method}`)}</Muted>
          {report.unvaluedQty > 0 && <Notice tone="warn">{t('warehouse.unvalued', { qty: formatQty(report.unvaluedQty, lang) })}</Notice>}
          <Muted>{t('warehouse.valueHelp')}</Muted>
        </Card>
      )}
      <Field label={t('warehouse.search')} value={list.search} onChangeText={list.setSearch} placeholder={t('warehouse.searchProducts')} autoCapitalize="none" returnKeyType="search" testID="warehouse-search" />
      {list.items.map((r) => (
        <Card key={r.productPublicId}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{r.sku} · {r.productName}</Text>
          <View style={{ flexDirection: 'row', gap: 16, flexWrap: 'wrap' }}>
            <Text style={{ color: colors.text }}>{t('warehouse.onHand')}: <Text style={{ fontWeight: '700' }}>{formatQty(r.onHand, lang)}</Text></Text>
            <Text style={{ color: colors.text }}>{t('warehouse.unitCost')}: {r.unitCost == null ? '—' : formatMoney(r.unitCost, lang)}</Text>
            <Text style={{ color: colors.text }}>{t('warehouse.value')}: <Text style={{ fontWeight: '700' }}>{formatMoney(r.value, lang)}</Text></Text>
          </View>
        </Card>
      ))}
      <Footer loading={list.loading} error={list.error} shown={list.items.length} total={list.total} empty={list.query ? t('warehouse.noMatches') : t('warehouse.emptyStock')} onRetry={list.reload} onMore={list.more} onExport={exportAll} />
    </>
  )
}

export function WarehouseScreen() {
  const { t, lang } = useT()
  const offer = useOffer()
  const [section, setSection] = useState<Section>('stock')
  const sections: { key: Section; label: string }[] = [
    { key: 'stock', label: t('warehouse.sections.stock') },
    { key: 'products', label: t('warehouse.sections.products') },
    { key: 'movements', label: t('warehouse.sections.movements') },
    ...(offer.canSeeValue ? [{ key: 'value' as const, label: t('warehouse.sections.value') }] : []),
    ...(offer.canCreateArrivals ? [{ key: 'arrivals' as const, label: t('warehouse.sections.arrivals') }] : []),
    ...(offer.canCreateExits ? [{ key: 'exits' as const, label: t('warehouse.sections.exits') }] : []),
  ]
  const current = sections.some((s) => s.key === section) ? section : 'stock'
  const wide = useIsWide()
  return (
    <Screen wide>
      {wide ? <PageHeader title={t('warehouse.title')} subtitle={offer.companyName ?? undefined} /> : <Title>{t('warehouse.title')}</Title>}
      {!wide && !!offer.companyName && <Muted>{offer.companyName}</Muted>}
      <Segments value={current} onChange={(k) => setSection(k as Section)} options={sections} />
      {current === 'stock' && <StockSection lang={lang} />}
      {current === 'products' && <ProductsSection lang={lang} canCreate={offer.canCreateProducts} />}
      {current === 'movements' && <MovementsSection lang={lang} />}
      {current === 'value' && offer.canSeeValue && <ValueSection lang={lang} />}
      {current === 'arrivals' && offer.canCreateArrivals && <ArrivalsSection lang={lang} />}
      {current === 'exits' && offer.canCreateExits && <ExitsSection lang={lang} />}
    </Screen>
  )
}
