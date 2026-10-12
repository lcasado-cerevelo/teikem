// Formateadores de la compañía y del idioma para pintar (y buscar en) el resultado de una vista.
import { useMemo } from 'react'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n/useT'
import type { ReportFormatters } from './reportResult'

export function useReportFormatters(): ReportFormatters {
  const t = useT()
  const f = useFormat()
  return useMemo<ReportFormatters>(
    () => ({ number: (n) => f.number(n), money: (n) => f.money(n), dateTime: (v) => f.dateTime(v), yes: t('analytics.reportRun.yes'), no: t('analytics.reportRun.no') }),
    [f, t],
  )
}
