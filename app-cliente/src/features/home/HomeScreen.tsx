// Inicio: saludo, cliente actual, cuántas órdenes hay en total y por estatus, y las más recientes (toca una para ver su ficha).
// 2026-10-22: en la web de escritorio es un tablero (fichas de indicadores, envíos recientes en tabla y el resumen por estatus al lado); en el celular, las tarjetas de siempre.
import { useRouter } from 'expo-router'
import { useState } from 'react'
import { Pressable, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { CellText, DataTable, PageHeader, type Column } from '../../kernel/ui/desk'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Card, Chip, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { formatDate, statusTone } from '../format'
import { ProgressBar } from '../progress/ProgressBar'
import { toProgressInfo } from '../progress/progressInfo'
import { getMe, getSummary, type PortalOrderListItemDto } from '../portalApi'
import { IndicatorsCard } from './IndicatorsCard'
import { IndicatorsKpis } from './IndicatorsKpis'

const PERIODS = [7, 30, 90] as const

export function HomeScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const wide = useIsWide()
  const [days, setDays] = useState<number>(30)
  const me = useAsync(getMe, [])
  const summary = useAsync(getSummary, [])

  const first = (me.data?.fullName ?? '').split(' ')[0]

  if (wide) {
    const recent = summary.data?.recentOrders ?? []
    const open = (o: PortalOrderListItemDto) => router.push({ pathname: '/order/[id]', params: { id: o.publicId as string } })
    const columns: Column<PortalOrderListItemDto>[] = [
      { key: 'waybill', title: t('orders.col.waybill'), width: 130, render: (o) => <CellText bold>{o.waybillNumber || o.orderNumber}</CellText> },
      { key: 'date', title: t('orders.col.date'), width: 110, render: (o) => <CellText>{formatDate(o.createdAtUtc, lang)}</CellText> },
      { key: 'consignee', title: t('orders.col.consignee'), flex: 2, render: (o) => <CellText>{o.consigneeName}</CellText> },
      { key: 'city', title: t('orders.col.city'), flex: 1, render: (o) => <CellText muted>{o.consigneeCity}</CellText> },
      { key: 'status', title: t('orders.col.status'), width: 170, render: (o) => <Chip label={o.statusLabel ?? o.status ?? ''} tone={statusTone(o.status)} /> },
    ]
    return (
      <Screen wide>
        <PageHeader title={first ? t('home.greeting', { name: first }) : t('home.greetingPlain')} subtitle={me.data ? t('home.client', { client: me.data.currentClient?.name ?? '' }) : undefined}>
          {PERIODS.map((p) => (
            <Pressable
              key={p}
              onPress={() => setDays(p)}
              accessibilityRole="button"
              accessibilityState={{ selected: days === p }}
              testID={`indicators-days-${p}`}
              style={{ paddingVertical: 6, paddingHorizontal: 14, borderRadius: 18, borderWidth: 1, borderColor: days === p ? colors.brand : colors.line, backgroundColor: days === p ? colors.brand : colors.card }}
            >
              <Text style={{ color: days === p ? colors.onBrand : colors.text, fontWeight: '600' }}>{t('indicators.days', { n: p })}</Text>
            </Pressable>
          ))}
        </PageHeader>
        {!!(me.error || summary.error) && <Notice>{apiErrorMessage(me.error ?? summary.error)}</Notice>}
        <IndicatorsKpis days={days} />
        <View style={{ flexDirection: 'row', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <View style={{ flex: 3, flexBasis: 520, gap: 8, minWidth: 320 }}>
            <Text style={{ fontWeight: '800', fontSize: 16, color: colors.text }}>{t('home.recent')}</Text>
            <DataTable columns={columns} rows={recent} keyOf={(o) => String(o.publicId)} onRowPress={open} empty={summary.loading ? undefined : t('home.noOrders')} testID="home-recent" />
            {recent.length > 0 && <Button title={t('home.seeAll')} variant="secondary" onPress={() => router.push('/orders')} />}
          </View>
          <View style={{ flex: 1, flexBasis: 280, gap: 12, minWidth: 260 }}>
            <Card>
              <Text style={{ fontSize: 34, fontWeight: '800', color: colors.brand }} testID="home-total">{summary.data?.totalOrders ?? '—'}</Text>
              <Muted>{t('home.totalOrders')}</Muted>
            </Card>
            {(summary.data?.byStatus ?? []).length > 0 && (
              <Card>
                <Text style={{ fontWeight: '700', color: colors.text }}>{t('home.byStatus')}</Text>
                <View style={{ gap: 8, marginTop: 4 }}>
                  {(summary.data?.byStatus ?? []).map((s) => (
                    <View key={s.status} style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
                      <Chip label={s.statusLabel ?? s.status ?? ''} tone={statusTone(s.status)} />
                      <Text style={{ fontWeight: '700', color: colors.text }}>{s.count}</Text>
                    </View>
                  ))}
                </View>
              </Card>
            )}
            <Button title={t('home.invoices')} variant="secondary" onPress={() => router.push('/invoices')} testID="home-invoices" />
          </View>
        </View>
      </Screen>
    )
  }

  return (
    <Screen wide>
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
            <IndicatorsCard />
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
