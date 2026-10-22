// Envíos del consignatario con acceso propio (Lote 117d): las órdenes de su cliente que se entregan en SU consignatario, con la barra de progreso; búsqueda por conduce y «Ver más». SOLO LECTURA: el consignatario no crea órdenes.
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Text } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Chip, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { formatDate, statusTone } from '../format'
import { ProgressBar } from '../progress/ProgressBar'
import { toProgressInfo } from '../progress/progressInfo'
import { type ConsigneeOrderListItemDto, listConsigneeOrders, PAGE_SIZE } from '../portalApi'

export function ConsigneeOrdersScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [items, setItems] = useState<ConsigneeOrderListItemDto[]>([])
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
        const page = await listConsigneeOrders(query, skip, PAGE_SIZE)
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
    <Screen wide>
      <Title>{t('consigneeOrders.title')}</Title>
      <Muted>{t('consigneeOrders.help')}</Muted>
      <Field label={t('consigneeOrders.search')} value={search} onChangeText={setSearch} placeholder={t('consigneeOrders.searchPlaceholder')} autoCapitalize="none" autoCorrect={false} testID="shipments-search" />
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
        </>
      )}
      {!loading && !error && items.length === 0 && <Muted>{t('consigneeOrders.empty')}</Muted>}
      {items.map((o) => (
        <Card key={o.publicId} onPress={() => router.push({ pathname: '/order/[id]', params: { id: o.publicId as string } })}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{t('consigneeOrders.waybill', { value: o.waybillNumber ?? '' })}</Text>
          <Muted>{[o.serviceTypeLabel, formatDate(o.createdAtUtc, lang)].filter(Boolean).join(' · ')}</Muted>
          {toProgressInfo(o.progress) ? <ProgressBar compact progress={toProgressInfo(o.progress)!} /> : <Chip label={o.statusLabel ?? o.status ?? ''} tone={statusTone(o.status)} />}
        </Card>
      ))}
      {loading && <Spinner />}
      {!loading && items.length < total && <Button title={t('consigneeOrders.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => void load(items.length)} />}
    </Screen>
  )
}
