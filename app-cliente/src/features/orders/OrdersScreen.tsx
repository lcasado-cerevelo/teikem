// Mis órdenes: búsqueda por conduce, orden, factura o consignatario, y «Cargar más» (20 por página). Solo salen las del cliente de la sesión (lo garantiza el servidor).
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Text } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Chip, colors, Field, Muted, Notice, Screen, Spinner, Title } from '../../kernel/ui/ui'
import { formatDate, statusTone } from '../format'
import { listOrders, PAGE_SIZE, type PortalOrderListItemDto } from '../portalApi'

export function OrdersScreen() {
  const { t, lang } = useT()
  const router = useRouter()
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [items, setItems] = useState<PortalOrderListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const seq = useRef(0)

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

  return (
    <Screen>
      <Title>{t('orders.title')}</Title>
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
          <Chip label={o.statusLabel ?? o.status ?? ''} tone={statusTone(o.status)} />
          <Muted>{formatDate(o.createdAtUtc, lang)}</Muted>
        </Card>
      ))}
      {loading && <Spinner />}
      {!loading && items.length < total && <Button title={t('orders.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => void load(items.length)} />}
    </Screen>
  )
}
