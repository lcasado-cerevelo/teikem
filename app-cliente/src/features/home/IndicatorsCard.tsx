// Indicadores del cliente en el Inicio (Lote 120): de los últimos 7, 30 o 90 días. Lo que no aplica (sin fecha prometida, sin facturación o sin COD) no se muestra.
import { useEffect, useState } from 'react'
import { Pressable, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Card, Muted, Notice, Spinner, useColors } from '../../kernel/ui/ui'
import { formatMoney } from '../format'
import { getIndicators, type PortalIndicatorsDto } from '../portalApi'

const PERIODS = [7, 30, 90] as const

export function IndicatorsCard() {
  const colors = useColors()
  const { t, lang } = useT()
  const [days, setDays] = useState<number>(30)
  const [data, setData] = useState<PortalIndicatorsDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    setError(null)
    getIndicators(days).then((d) => alive && setData(d)).catch((e) => alive && setError(apiErrorMessage(e)))
    return () => { alive = false }
  }, [days])

  const row = (label: string, value: string, testID?: string) => (
    <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 12, paddingVertical: 3 }}>
      <Muted>{label}</Muted>
      <Text style={{ color: colors.text, fontWeight: '700' }} testID={testID}>{value}</Text>
    </View>
  )

  return (
    <Card>
      <Text style={{ fontWeight: '700', color: colors.text }}>{t('indicators.title')}</Text>
      <View style={{ flexDirection: 'row', gap: 8, marginVertical: 6 }}>
        {PERIODS.map((p) => (
          <Pressable key={p} onPress={() => setDays(p)} accessibilityRole="button" accessibilityState={{ selected: days === p }} testID={`indicators-days-${p}`}
            style={{ paddingVertical: 5, paddingHorizontal: 12, borderRadius: 16, borderWidth: 1, borderColor: days === p ? colors.brand : colors.line, backgroundColor: days === p ? colors.brand : 'transparent' }}>
            <Text style={{ color: days === p ? colors.onBrand : colors.text, fontWeight: '600' }}>{t('indicators.days', { n: p })}</Text>
          </Pressable>
        ))}
      </View>
      {!!error && <Notice>{error}</Notice>}
      {!data && !error && <Spinner />}
      {!!data && (
        <>
          {row(t('indicators.created'), String(data.created), 'ind-created')}
          {row(t('indicators.delivered'), String(data.delivered), 'ind-delivered')}
          {data.onTimePct != null && row(t('indicators.onTime'), `${data.onTimePct}% (${data.onTime}/${data.deliveredWithPromise})`, 'ind-ontime')}
          {data.avgDeliveryHours != null && row(t('indicators.avgHours'), t('indicators.hours', { n: data.avgDeliveryHours ?? 0 }), 'ind-hours')}
          {row(t('indicators.failed'), String(data.failedAttempts), 'ind-failed')}
          {row(t('indicators.open'), `${data.openNow} · ${t('indicators.inTransit', { n: data.inTransitNow ?? 0 })}`, 'ind-open')}
          {data.invoiceBalance != null && row(t('indicators.invoiceBalance'), `${formatMoney(data.invoiceBalance, lang)}${(data.overdueInvoices ?? 0) > 0 ? ` · ${t('indicators.overdue', { n: data.overdueInvoices ?? 0 })}` : ''}`, 'ind-invoices')}
          {data.codToRemit != null && data.codToRemit > 0 && row(t('indicators.cod'), formatMoney(data.codToRemit, lang), 'ind-cod')}
        </>
      )}
    </Card>
  )
}
