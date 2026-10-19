// Una factura mía (Lote 119), solo lectura: encabezado con lo pagado y el saldo, las líneas y los pagos recibidos. No hay ninguna acción: no se marca como pagada desde el portal.
import { Stack, useLocalSearchParams } from 'expo-router'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Chip, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { formatDate, formatMoney } from '../format'
import { getInvoice } from '../portalApi'
import { invoiceTone } from './invoiceRules'

export function InvoiceDetailScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const { id } = useLocalSearchParams<{ id: string }>()
  const invoice = useAsync(() => getInvoice(String(id)), [id])
  const i = invoice.data
  const money = (n: number | null | undefined) => formatMoney(n, lang)
  const row = (label: string, value: string, bold = false) => (
    <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 12, paddingVertical: 2 }}>
      <Muted>{label}</Muted>
      <Text style={{ color: colors.text, fontWeight: bold ? '800' : '600' }}>{value}</Text>
    </View>
  )

  return (
    <>
      <Stack.Screen options={{ title: i?.number ?? t('invoices.detailTitle'), headerShown: true }} />
      <Screen>
        {!!invoice.error && (
          <>
            <Notice>{apiErrorMessage(invoice.error)}</Notice>
            <Button title={t('common.retry')} variant="secondary" onPress={invoice.reload} />
          </>
        )}
        {invoice.loading && !i && <Spinner />}
        {!!i && (
          <>
            <Title>{i.number}</Title>
            <Chip label={i.statusLabel ?? i.status ?? ''} tone={invoiceTone(i.status)} />
            <Card>
              {!!i.issueDate && row(t('invoices.issuedLabel'), formatDate(i.issueDate, lang))}
              {!!i.dueDate && row(t('invoices.dueLabel'), formatDate(i.dueDate, lang))}
              {row(t('invoices.subtotal'), money(i.subtotal))}
              {row(t('invoices.tax'), money(i.taxAmount))}
              {row(t('invoices.total'), money(i.total), true)}
              {row(t('invoices.paid'), money(i.paid))}
              {(i.credited ?? 0) > 0 && row(t('invoices.credited'), money(i.credited))}
              {row(t('invoices.balanceLabel'), money(i.balance), true)}
            </Card>
            <Card>
              <Text style={{ fontWeight: '700', color: colors.text }}>{t('invoices.lines')}</Text>
              {(i.lines ?? []).map((l, n) => (
                <View key={n} style={{ paddingVertical: 4 }}>
                  <Text style={{ color: colors.text }}>{l.description}</Text>
                  <Muted>{`${l.quantity} × ${money(l.unitAmount)} = ${money(l.lineSubtotal)}${l.orderNumber ? ` · ${t('invoices.order', { number: l.orderNumber })}` : ''}`}</Muted>
                </View>
              ))}
            </Card>
            {(i.payments ?? []).length > 0 && (
              <Card>
                <Text style={{ fontWeight: '700', color: colors.text }}>{t('invoices.payments')}</Text>
                {(i.payments ?? []).map((p, n) => (
                  <View key={n} style={{ paddingVertical: 4 }}>
                    <Text style={{ color: colors.text }}>{`${money(p.amount)} · ${p.methodLabel}`}</Text>
                    <Muted>{[formatDate(p.date, lang), p.reference].filter(Boolean).join(' · ')}</Muted>
                  </View>
                ))}
              </Card>
            )}
          </>
        )}
      </Screen>
    </>
  )
}
