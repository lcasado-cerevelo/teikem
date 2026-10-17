// Bandeja de avisos: los resúmenes que la compañía configuró mandar a este cliente por el canal «bandeja del portal» (recogido, salió a entrega, entregado, COD procesado, factura emitida), los más nuevos primero.
import { useCallback, useEffect, useState } from 'react'
import { Text } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Chip, colors, Muted, Notice, Screen, Spinner, Title } from '../../kernel/ui/ui'
import { formatDateTime } from '../format'
import { listNotifications, PAGE_SIZE, type PortalNotificationDto } from '../portalApi'

const KIND_TONE: Record<string, 'ok' | 'info' | 'warn' | 'neutral'> = { DELIVERED: 'ok', PICKED_UP: 'info', OUT_FOR_DELIVERY: 'info', COD_PROCESSED: 'warn', INVOICE_ISSUED: 'warn' }

export function NoticesScreen() {
  const { t, lang } = useT()
  const [items, setItems] = useState<PortalNotificationDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (skip: number) => {
    setLoading(true)
    setError(null)
    try {
      const page = await listNotifications(skip, PAGE_SIZE)
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
    <Screen>
      <Title>{t('notices.title')}</Title>
      <Muted>{t('notices.help')}</Muted>
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
        </>
      )}
      {!loading && !error && items.length === 0 && <Muted>{t('notices.empty')}</Muted>}
      {items.map((n) => (
        <Card key={n.publicId}>
          <Chip label={t(`notices.kinds.${n.kind}`)} tone={KIND_TONE[n.kind ?? ''] ?? 'neutral'} />
          <Text style={{ fontWeight: '700', color: colors.text }}>{n.subject}</Text>
          <Text style={{ color: colors.text, lineHeight: 20 }}>{n.bodyText}</Text>
          <Muted>{formatDateTime(n.createdAtUtc, lang)}</Muted>
        </Card>
      ))}
      {loading && <Spinner />}
      {!loading && items.length < total && <Button title={t('notices.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => void load(items.length)} />}
    </Screen>
  )
}
