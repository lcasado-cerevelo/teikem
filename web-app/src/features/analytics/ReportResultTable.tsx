// Lote F18 (Rentas F-R2) — tabla del resultado de una vista del motor de Análisis (columnas y filas tal como las devuelve
// `POST /analytics/reports/{id}/run`; los totales de una vista agrupada van en una franja `SummaryBar` bajo la tabla). Ordenable
// por columna en el cliente, paginada, tarjetas bajo 720 px y Exportar como toda tabla del kit. No recalcula nada.
import { useMemo } from 'react'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { DataTable, EmptyState, SummaryBar, type DataColumn } from '../../kernel/ui'
import { reportCellText, reportSortValue, reportTotalsItems, type ReportFormatters, type ReportRow, type ReportRunResult } from './reportResult'

export interface ReportResultTableProps {
  result: ReportRunResult | null | undefined
  /** Nombre de la vista: etiqueta de la tabla y del archivo exportado. */
  name: string
  loading?: boolean
  emptyText?: string
}

export function ReportResultTable({ result, name, loading, emptyText }: ReportResultTableProps) {
  const t = useT()
  const f = useFormat()
  const fmt = useMemo<ReportFormatters>(
    () => ({ number: (n) => f.number(n), money: (n) => f.money(n), dateTime: (v) => f.dateTime(v), yes: t('analytics.reportRun.yes'), no: t('analytics.reportRun.no') }),
    [f, t],
  )
  const columns = useMemo<DataColumn<ReportRow>[]>(
    () =>
      (result?.columns ?? []).map((c, i) => ({
        id: c.key ?? `c${i}`,
        header: c.label ?? c.key ?? '',
        cell: (row) => reportCellText(row[c.key ?? ''], c, fmt),
        sortValue: (row) => reportSortValue(row, c),
        align: c.type === 'Number' ? ('end' as const) : undefined,
      })),
    [result, fmt],
  )
  const rows = useMemo(() => (result?.rows ?? []) as ReportRow[], [result])
  const index = useMemo(() => new Map(rows.map((r, i) => [r, i])), [rows])
  const totals = useMemo(() => reportTotalsItems(result, fmt), [result, fmt])
  return (
    <>
      <DataTable
        label={name}
        columns={columns}
        rows={rows}
        rowKey={(row) => index.get(row) ?? 0}
        loading={loading}
        exportFileName={name}
        empty={<EmptyState title={emptyText ?? t('analytics.reportRun.empty')} />}
      />
      {totals.length > 0 && (
        <div className="pb">
          <SummaryBar label={t('analytics.reportRun.totals')} items={totals} aside={t('analytics.reportRun.totals')} />
        </div>
      )}
    </>
  )
}
