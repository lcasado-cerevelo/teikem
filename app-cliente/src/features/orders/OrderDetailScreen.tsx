// Ficha de una orden: conduce, estatus, dirección de entrega, paquetes, COD y la línea de tiempo (cada cambio de estatus con su fecha). Una orden de otro cliente responde igual que una inexistente: «no encontrada».
import { Stack, useLocalSearchParams } from 'expo-router'
import { Linking, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Card, Chip, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { formatDate, formatDateTime, formatMoney, statusTone } from '../format'
import { ProgressBar, ProgressSteps } from '../progress/ProgressBar'
import { toProgressInfo } from '../progress/progressInfo'
import { getOrder } from '../portalApi'

export function OrderDetailScreen() {
  const colors = useColors()
  const wide = useIsWide()   // en escritorio no hay encabezado de pila: la pantalla trae su título y «← Atrás»
  const { t, lang } = useT()
  const { id } = useLocalSearchParams<{ id: string }>()
  const order = useAsync(() => getOrder(String(id)), [id])
  const o = order.data

  return (
    <>
      <Stack.Screen options={{ title: o?.waybillNumber || o?.orderNumber || t('order.title'), headerShown: !wide }} />
      <Screen wide>
        {!!order.error && (
          <>
            <Notice>{apiErrorMessage(order.error)}</Notice>
            <Button title={t('common.retry')} variant="secondary" onPress={order.reload} />
          </>
        )}
        {order.loading && !o && <Spinner />}
        {!!o && (
          <>
            <Title>{o.waybillNumber || o.orderNumber}</Title>
            <Chip label={o.statusLabel ?? o.status ?? ''} tone={statusTone(o.status)} />
            {toProgressInfo(o.progress) && (
              <Card>
                <ProgressBar progress={toProgressInfo(o.progress)!} />
                {(o.stepLabels ?? []).length > 0 && <ProgressSteps labels={o.stepLabels ?? []} progress={toProgressInfo(o.progress)!} />}
              </Card>
            )}
            <Card>
              <Row label={t('order.waybill')} value={o.waybillNumber} />
              <Row label={t('order.orderNumber')} value={o.orderNumber} />
              <Row label={t('order.invoice')} value={o.clientInvoiceNumber} />
              <Row label={t('order.service')} value={o.serviceTypeLabel} />
              <Row label={t('order.created')} value={formatDateTime(o.createdAtUtc, lang)} />
              {!!o.promisedDate && <Row label={t('order.promised')} value={formatDate(o.promisedDate, lang)} />}
              <Row label={t('order.pieces')} value={String(o.totalPieces ?? 0)} />
              {o.codAmount != null && <Row label={t('order.cod')} value={`${formatMoney(o.codAmount, lang)}${o.codStatusLabel ? ` · ${o.codStatusLabel}` : ''}`} />}
              {!!o.invoiceProcessedAtUtc && <Row label={t('order.invoiceProcessed')} value={formatDateTime(o.invoiceProcessedAtUtc, lang)} />}
            </Card>
            <Card>
              <Text style={{ fontWeight: '700', color: colors.text }}>{t('order.delivery')}</Text>
              {!!o.delivery?.name && <Text style={{ color: colors.text }}>{o.delivery.name}</Text>}
              <Text style={{ color: colors.text }}>{[o.delivery?.line1, o.delivery?.line2].filter(Boolean).join(', ')}</Text>
              <Muted>{[o.delivery?.city, o.delivery?.state, o.delivery?.postalCode].filter(Boolean).join(', ')}</Muted>
            </Card>
            {(o.packages ?? []).length > 0 && (
              <Card>
                <Text style={{ fontWeight: '700', color: colors.text }}>{t('order.packages')}</Text>
                {(o.packages ?? []).map((p, i) => (
                  <Text key={i} style={{ color: colors.text }}>{`${p.pieces} × ${p.description}`}</Text>
                ))}
              </Card>
            )}
            {!!o.deliveryProof && (
              <Card>
                <Text style={{ fontWeight: '700', color: colors.text }}>{t('order.proof.title')}</Text>
                <Row label={t('order.proof.recipient')} value={o.deliveryProof.recipientName} />
                <Row label={t('order.proof.at')} value={formatDateTime(o.deliveryProof.deliveredAtUtc, lang)} />
                <Muted>{o.deliveryProof.signedByRecipient ? t('order.proof.signedByRecipient') : o.deliveryProof.registeredByStaff ? t('order.proof.byStaff') : ''}</Muted>
                {o.deliveryProof.latitude != null && o.deliveryProof.longitude != null && (
                  <Button title={t('order.proof.map')} variant="link" onPress={() => void Linking.openURL(`https://www.google.com/maps?q=${o.deliveryProof!.latitude},${o.deliveryProof!.longitude}`)} testID="order-proof-map" />
                )}
              </Card>
            )}
            <Card>
              <Text style={{ fontWeight: '700', color: colors.text }}>{t('order.timeline')}</Text>
              {(o.timeline ?? []).length === 0 && <Muted>{t('order.noTimeline')}</Muted>}
              {(o.timeline ?? []).map((e, i) => (
                <View key={i} style={{ flexDirection: 'row', gap: 10, alignItems: 'flex-start', paddingVertical: 4 }}>
                  <View style={{ width: 10, height: 10, borderRadius: 5, marginTop: 5, backgroundColor: colors.flow }} />
                  <View style={{ flex: 1 }}>
                    <Text style={{ fontWeight: '600', color: colors.text }}>{e.statusLabel ?? e.status}</Text>
                    <Muted>{formatDateTime(e.atUtc, lang)}</Muted>
                  </View>
                </View>
              ))}
            </Card>
          </>
        )}
      </Screen>
    </>
  )
}

function Row({ label, value }: { label: string; value?: string | null }) {
  const colors = useColors()
  return (
    <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 12 }}>
      <Muted>{label}</Muted>
      <Text style={{ color: colors.text, fontWeight: '500', flexShrink: 1, textAlign: 'right' }}>{value || '—'}</Text>
    </View>
  )
}
