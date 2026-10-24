// Mis órdenes: búsqueda por conduce, orden, factura o consignatario, filtro de FECHA con rangos predefinidos (2026-10-23, pedido de Luis: igual que en la web del personal) y «Cargar más»
// (20 por página). La tabla dice cuántos envíos se ven de cuántos hay. Solo salen los del cliente de la sesión (lo garantiza el servidor).
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { CellText, DataTable, ExportButton, PageHeader, type Column } from '../../kernel/ui/desk'
import { csvDay, fetchAll, filtersSentence, runExport, type ExportFormat } from '../../kernel/ui/tableExport'
import { DateRangeFilter } from '../../kernel/ui/DateRangeFilter'
import { type DayRange, dayEndExclusiveUtc, dayStartUtc, hasRange } from '../../kernel/ui/dateRanges'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Card, Chip, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { formatDate, formatMoney, statusTone } from '../format'
import { StatusWithAttempts } from '../progress/FailedAttempts'
import { ProgressBar } from '../progress/ProgressBar'
import { toProgressInfo } from '../progress/progressInfo'
import { getOrderForm, listOrders, PAGE_SIZE, type PortalOrderListItemDto } from '../portalApi'

export function OrdersScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const wide = useIsWide()
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [range, setRange] = useState<DayRange>({ from: '', to: '' })
  const [items, setItems] = useState<PortalOrderListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const seq = useRef(0)
  const [canCreate, setCanCreate] = useState(false)   // solo los roles que pueden crear órdenes ven el botón

  useEffect(() => {
    getOrderForm().then((f) => setCanCreate(!!f.canCreate)).catch(() => setCanCreate(false))
  }, [])

  // la búsqueda espera a que se deje de escribir
  useEffect(() => {
    const id = setTimeout(() => setQuery(search.trim()), 350)
    return () => clearTimeout(id)
  }, [search])

  const load = useCallback(
    async (skip: number) => {
      const mine = ++seq.current
      setLoading(true)
      setError(null)
      try {
        const page = await listOrders({ search: query, fromUtc: dayStartUtc(range.from), toUtc: dayEndExclusiveUtc(range.to), skip, take: PAGE_SIZE })
        if (mine !== seq.current) return
        setItems((prev) => (skip === 0 ? (page.items ?? []) : [...prev, ...(page.items ?? [])]))
        setTotal(page.total ?? 0)
      } catch (e) {
        if (mine === seq.current) setError(apiErrorMessage(e))
      } finally {
        if (mine === seq.current) setLoading(false)
      }
    },
    [query, range.from, range.to],
  )

  useEffect(() => {
    void load(0)
  }, [load])

  const filtered = !!query || hasRange(range)
  const noneText = filtered ? (hasRange(range) ? t('orders.noMatchesRange') : t('orders.noMatches')) : t('orders.empty')
  const open = (o: PortalOrderListItemDto) => router.push({ pathname: '/order/[id]', params: { id: o.publicId as string } })
  const columns: Column<PortalOrderListItemDto>[] = [
    { key: 'waybill', title: t('orders.col.waybill'), width: 130, render: (o) => <CellText bold>{o.waybillNumber || o.orderNumber}</CellText>, csv: (o) => o.waybillNumber || o.orderNumber },
    { key: 'order', title: t('orders.col.order'), width: 110, render: (o) => <CellText muted>{o.orderNumber}</CellText>, csv: (o) => o.orderNumber },
    { key: 'date', title: t('orders.col.date'), width: 110, render: (o) => <CellText>{formatDate(o.createdAtUtc, lang)}</CellText>, csv: (o) => csvDay(o.createdAtUtc) },
    { key: 'consignee', title: t('orders.col.consignee'), flex: 2, render: (o) => <CellText>{o.consigneeName}</CellText>, csv: (o) => o.consigneeName },
    { key: 'city', title: t('orders.col.city'), flex: 1, render: (o) => <CellText muted>{[o.consigneeCity, o.consigneeState].filter(Boolean).join(', ')}</CellText>, csv: (o) => [o.consigneeCity, o.consigneeState].filter(Boolean).join(', ') },
    { key: 'pieces', title: t('orders.col.pieces'), width: 70, align: 'right', render: (o) => <CellText align="right">{String(o.totalPieces ?? 0)}</CellText>, csv: (o) => o.totalPieces ?? 0 },
    { key: 'cod', title: t('orders.col.cod'), width: 100, align: 'right', render: (o) => <CellText align="right">{o.codAmount ? formatMoney(o.codAmount, lang) : '—'}</CellText>, csv: (o) => o.codAmount ?? '' },
    { key: 'status', title: t('orders.col.status'), width: 170, render: (o) => <StatusWithAttempts status={o.status} label={o.statusLabel} attempts={o.progress?.failedAttempts} />, csv: (o) => [o.statusLabel ?? o.status, (o.progress?.failedAttempts ?? 0) > 0 ? t((o.progress?.failedAttempts ?? 0) === 1 ? 'orders.attempts.one' : 'orders.attempts.many', { n: o.progress?.failedAttempts ?? 0 }) : ''].filter(Boolean).join(' · ') },
  ]
  // exportar: TODO lo que cumple el filtro (búsqueda y fechas), no solo lo cargado
  const exportAll = async (format: ExportFormat) => {
    const all = await fetchAll((skip, take) => listOrders({ search: query, fromUtc: dayStartUtc(range.from), toUtc: dayEndExclusiveUtc(range.to), skip, take }))
    const filters = filtersSentence([!!query && t('table.fSearch', { q: query }), hasRange(range) && t('table.fRange', { from: range.from || '…', to: range.to || '…' })])
    await runExport({ format, title: t('orders.title'), columns, rows: all.items, filters })
  }

  if (wide) {
    return (
      <Screen wide>
        <PageHeader title={t('orders.title')} subtitle={total > 0 ? t('orders.count', { n: total }) : undefined}>
          <View style={{ width: 300 }}>
            <Field label={t('orders.search')} value={search} onChangeText={setSearch} placeholder={t('orders.searchPlaceholder')} autoCapitalize="none" returnKeyType="search" testID="orders-search" />
          </View>
          <DateRangeFilter value={range} onChange={setRange} testID="orders-range" />
          {canCreate && <Button title={t('orders.new')} onPress={() => router.push('/order/new')} testID="orders-new" />}
        </PageHeader>
        {!!error && (
          <>
            <Notice>{error}</Notice>
            <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
          </>
        )}
        <DataTable columns={columns} rows={items} keyOf={(o) => String(o.publicId)} onRowPress={open} empty={loading ? undefined : noneText} total={loading && items.length === 0 ? undefined : total} onExport={exportAll} testID="orders-table" />
        {loading && <Spinner />}
        {!loading && items.length < total && <Button title={t('orders.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => void load(items.length)} />}
      </Screen>
    )
  }

  return (
    <Screen wide>
      <Title>{t('orders.title')}</Title>
      {canCreate && <Button title={t('orders.new')} onPress={() => router.push('/order/new')} testID="orders-new" />}
      <Field label={t('orders.search')} value={search} onChangeText={setSearch} placeholder={t('orders.searchPlaceholder')} autoCapitalize="none" returnKeyType="search" testID="orders-search" />
      <DateRangeFilter value={range} onChange={setRange} testID="orders-range" />
      <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        {!loading && total > 0 ? <Muted>{t('table.count', { shown: items.length, total })}</Muted> : <View />}
        {total > 0 && <ExportButton onExport={exportAll} testID="orders-export" />}
      </View>
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
        </>
      )}
      {!loading && !error && items.length === 0 && <Muted>{noneText}</Muted>}
      {items.map((o) => (
        <Card key={o.publicId} onPress={() => router.push({ pathname: '/order/[id]', params: { id: o.publicId as string } })}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{o.waybillNumber || o.orderNumber}</Text>
          <Muted>{t('orders.orderNumber', { number: o.orderNumber ?? '' })}</Muted>
          <Text style={{ color: colors.text }}>{[o.consigneeName, o.consigneeCity].filter(Boolean).join(' · ')}</Text>
          {toProgressInfo(o.progress) ? <ProgressBar compact progress={toProgressInfo(o.progress)!} /> : <Chip label={o.statusLabel ?? o.status ?? ''} tone={statusTone(o.status)} />}
          <Muted>{formatDate(o.createdAtUtc, lang)}</Muted>
        </Card>
      ))}
      {loading && <Spinner />}
      {!loading && items.length < total && <Button title={t('orders.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => void load(items.length)} />}
    </Screen>
  )
}
