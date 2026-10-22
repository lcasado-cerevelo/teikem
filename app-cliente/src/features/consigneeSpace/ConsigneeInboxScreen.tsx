// Avisos del consignatario con acceso propio (Lote 117): la bandeja de lo que se le avisó (más nuevos primero), con el estado del envío y, si lo tiene, el enlace al seguimiento de la orden.
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useState } from 'react'
import { Text } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Chip, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { formatDateTime } from '../format'
import { type ConsigneeInboxItemDto, listConsigneeInbox, PAGE_SIZE } from '../portalApi'

const STATE_TONE: Record<string, 'ok' | 'info' | 'warn' | 'danger' | 'neutral'> = { SENT: 'ok', PENDING: 'info', SKIPPED: 'warn', FAILED: 'danger' }

export function ConsigneeInboxScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const [items, setItems] = useState<ConsigneeInboxItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (skip: number) => {
    setLoading(true)
    setError(null)
    try {
      const page = await listConsigneeInbox(skip, PAGE_SIZE)
      setItems((prev) => (skip === 0 ? (page.items ?? []) : [...prev, ...(page.items ?? [])]))
      setTotal(page.total ?? 0)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load(0)
  }, [load])

  return (
    <Screen wide>
      <Title>{t('consigneeSpace.inbox.title')}</Title>
      <Muted>{t('consigneeSpace.inbox.help')}</Muted>
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
        </>
      )}
      {!loading && !error && items.length === 0 && <Muted>{t('consigneeSpace.inbox.empty')}</Muted>}
      {items.map((n) => (
        <Card key={n.id}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{n.title}</Text>
          {!!n.waybill && <Muted>{t('consigneeSpace.inbox.waybill', { value: n.waybill })}</Muted>}
          <Chip label={t(`consigneeSpace.inbox.states.${n.state}`)} tone={STATE_TONE[n.state ?? ''] ?? 'neutral'} />
          <Muted>{(n.channel === 'SMS' ? t('consigneeSpace.inbox.sms') : t('consigneeSpace.inbox.email')) + ' · ' + formatDateTime(n.sentAtUtc ?? n.createdAtUtc, lang)}</Muted>
          {!!n.trackToken && <Button title={t('consigneeSpace.inbox.track')} variant="secondary" onPress={() => router.push(`/track/${n.trackToken}`)} testID={`inbox-track-${n.id}`} />}
        </Card>
      ))}
      {loading && <Spinner />}
      {!loading && items.length < total && <Button title={t('consigneeSpace.inbox.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => void load(items.length)} />}
    </Screen>
  )
}
