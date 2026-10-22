// Indicadores del cliente como fichas (versión de escritorio del Inicio): los mismos números de IndicatorsCard, de los últimos `days` días. Lo que no aplica no se muestra.
import { useEffect, useState } from 'react'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Kpi, KpiRow } from '../../kernel/ui/desk'
import { Notice, Spinner } from '../../kernel/ui/ui'
import { formatMoney } from '../format'
import { getIndicators, type PortalIndicatorsDto } from '../portalApi'

export function IndicatorsKpis({ days }: { days: number }) {
  const { t, lang } = useT()
  const [data, setData] = useState<PortalIndicatorsDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    setError(null)
    getIndicators(days).then((d) => alive && setData(d)).catch((e) => alive && setError(apiErrorMessage(e)))
    return () => {
      alive = false
    }
  }, [days])

  if (error) return <Notice>{error}</Notice>
  if (!data) return <Spinner />
  return (
    <KpiRow>
      <Kpi label={t('indicators.created')} value={String(data.created ?? 0)} testID="ind-created" />
      <Kpi label={t('indicators.delivered')} value={String(data.delivered ?? 0)} tone="ok" testID="ind-delivered" />
      {data.onTimePct != null && <Kpi label={t('indicators.onTime')} value={`${data.onTimePct}%`} hint={`${data.onTime}/${data.deliveredWithPromise}`} testID="ind-ontime" />}
      {data.avgDeliveryHours != null && <Kpi label={t('indicators.avgHours')} value={t('indicators.hours', { n: data.avgDeliveryHours ?? 0 })} testID="ind-hours" />}
      <Kpi label={t('indicators.failed')} value={String(data.failedAttempts ?? 0)} tone={(data.failedAttempts ?? 0) > 0 ? 'danger' : undefined} testID="ind-failed" />
      <Kpi label={t('indicators.open')} value={String(data.openNow ?? 0)} hint={t('indicators.inTransit', { n: data.inTransitNow ?? 0 })} testID="ind-open" />
      {data.invoiceBalance != null && (
        <Kpi
          label={t('indicators.invoiceBalance')}
          value={formatMoney(data.invoiceBalance, lang)}
          tone={(data.overdueInvoices ?? 0) > 0 ? 'warn' : undefined}
          hint={(data.overdueInvoices ?? 0) > 0 ? t('indicators.overdue', { n: data.overdueInvoices ?? 0 }) : undefined}
          testID="ind-balance"
        />
      )}
      {data.codToRemit != null && data.codToRemit > 0 && <Kpi label={t('indicators.cod')} value={formatMoney(data.codToRemit, lang)} testID="ind-cod" />}
    </KpiRow>
  )
}
