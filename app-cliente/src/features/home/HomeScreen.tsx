// Inicio: saludo, cliente actual, cuántas órdenes hay en total y por estatus, y las más recientes (toca una para ver su ficha).
import { useRouter } from 'expo-router'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Chip, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { statusTone } from '../format'
import { ProgressBar } from '../progress/ProgressBar'
import { toProgressInfo } from '../progress/progressInfo'
import { getMe, getSummary } from '../portalApi'

export function HomeScreen() {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const me = useAsync(getMe, [])
  const summary = useAsync(getSummary, [])

  const first = (me.data?.fullName ?? '').split(' ')[0]
  return (
    <Screen>
      <Title>{first ? t('home.greeting', { name: first }) : t('home.greetingPlain')}</Title>
      {!!me.data && <Muted>{t('home.client', { client: me.data.currentClient?.name ?? '' })}</Muted>}
      {!!(me.error || summary.error) && (
        <>
          <Notice>{apiErrorMessage(me.error ?? summary.error)}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={() => { me.reload(); summary.reload() }} />
        </>
      )}
      {summary.loading && !summary.data ? (
        <Spinner />
      ) : (
        !!summary.data && (
          <>
            <Card>
              <Text style={{ fontSize: 34, fontWeight: '800', color: colors.brand }} testID="home-total">{summary.data.totalOrders}</Text>
              <Muted>{t('home.totalOrders')}</Muted>
            </Card>
            {(summary.data.byStatus ?? []).length > 0 && (
              <Card>
                <Text style={{ fontWeight: '700', color: colors.text }}>{t('home.byStatus')}</Text>
                <View style={{ gap: 8, marginTop: 4 }}>
                  {(summary.data.byStatus ?? []).map((s) => (
                    <View key={s.status} style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
                      <Chip label={s.statusLabel ?? s.status ?? ''} tone={statusTone(s.status)} />
                      <Text style={{ fontWeight: '700', color: colors.text }}>{s.count}</Text>
                    </View>
                  ))}
                </View>
              </Card>
            )}
            <Button title={t('home.invoices')} variant="secondary" onPress={() => router.push('/invoices')} testID="home-invoices" />
            <Text style={{ fontWeight: '700', fontSize: 16, color: colors.text, marginTop: 4 }}>{t('home.recent')}</Text>
            {(summary.data.recentOrders ?? []).length === 0 && <Muted>{t('home.noOrders')}</Muted>}
            {(summary.data.recentOrders ?? []).map((o) => (
              <Card key={o.publicId} onPress={() => router.push({ pathname: '/order/[id]', params: { id: o.publicId as string } })}>
                <Text style={{ fontWeight: '700', color: colors.text }}>{o.waybillNumber || o.orderNumber}</Text>
                <Muted>{[o.consigneeName, o.consigneeCity].filter(Boolean).join(' · ')}</Muted>
                {toProgressInfo(o.progress) ? <ProgressBar compact progress={toProgressInfo(o.progress)!} /> : <Chip label={o.statusLabel ?? o.status ?? ''} tone={statusTone(o.status)} />}
              </Card>
            ))}
            {(summary.data.recentOrders ?? []).length > 0 && <Button title={t('home.seeAll')} variant="secondary" onPress={() => router.push('/orders')} />}
          </>
        )
      )}
    </Screen>
  )
}
