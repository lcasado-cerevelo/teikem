// Mis órdenes: búsqueda por conduce, orden, factura o consignatario, y «Cargar más» (20 por página). Solo salen las del cliente de la sesión (lo garantiza el servidor).
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { CellText, DataTable, PageHeader, type Column } from '../../kernel/ui/desk'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Card, Chip, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { formatDate, formatMoney, statusTone } from '../format'
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
        const page = await listOrders({ search: query, skip, take: PAGE_SIZE })
        if (mine !== seq.current) return
        setItems((prev) => (skip === 0 ? (page.items ?? []) : [...prev, ...(page.items ?? [])]))
        setTotal(page.total ?? 0)
      } catch (e) {
        if (mine === seq.current) setError(apiErrorMessage(e))
      } finally {
        if (mine === seq.current) setLoading(false)
      }
    },
    [query],
  )

  useEffect(() => {
    void load(0)
  }, [load])

  const open = (o: PortalOrderListItemDto) => router.push({ pathname: '/order/[id]', params: { id: o.publicId as string } })
  const columns: Column<PortalOrderListItemDto>[] = [
    { key: 'waybill', title: t('orders.col.waybill'), width: 130, render: (o) => <CellText bold>{o.waybillNumber || o.orderNumber}</CellText> },
    { key: 'order', title: t('orders.col.order'), width: 110, render: (o) => <CellText muted>{o.orderNumber}</CellText> },
    { key: 'date', title: t('orders.col.date'), width: 110, render: (o) => <CellText>{formatDate(o.createdAtUtc, lang)}</CellText> },
    { key: 'consignee', title: t('orders.col.consignee'), flex: 2, render: (o) => <CellText>{o.consigneeName}</CellText> },
    { key: 'city', title: t('orders.col.city'), flex: 1, render: (o) => <CellText muted>{[o.consigneeCity, o.consigneeState].filter(Boolean).join(', ')}</CellText> },
    { key: 'pieces', title: t('orders.col.pieces'), width: 70, align: 'right', render: (o) => <CellText align="right">{String(o.totalPieces ?? 0)}</CellText> },
    { key: 'cod', title: t('orders.col.cod'), width: 100, align: 'right', render: (o) => <CellText align="right">{o.codAmount ? formatMoney(o.codAmount, lang) : '—'}</CellText> },
    { key: 'status', title: t('orders.col.status'), width: 170, render: (o) => <Chip label={o.statusLabel ?? o.status ?? ''} tone={statusTone(o.status)} /> },
  ]

  if (wide) {
    return (
      <Screen wide>
        <PageHeader title={t('orders.title')} subtitle={total > 0 ? t('orders.count', { n: total }) : undefined}>
          <View style={{ width: 320 }}>
            <Field label={t('orders.search')} value={search} onChangeText={setSearch} placeholder={t('orders.searchPlaceholder')} autoCapitalize="none" returnKeyType="search" testID="orders-search" />
          </View>
          {canCreate && <Button title={t('orders.new')} onPress={() => router.push('/order/new')} testID="orders-new" />}
        </PageHeader>
        {!!error && (
          <>
            <Notice>{error}</Notice>
            <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
          </>
        )}
        <DataTable columns={columns} rows={items} keyOf={(o) => String(o.publicId)} onRowPress={open} empty={loading ? undefined : query ? t('orders.noMatches') : t('orders.empty')} testID="orders-table" />
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
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
        </>
      )}
      {!loading && !error && items.length === 0 && <Muted>{query ? t('orders.noMatches') : t('orders.empty')}</Muted>}
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
