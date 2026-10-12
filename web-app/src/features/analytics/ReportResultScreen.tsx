// Lote F-F2 — resultado de una vista (`/analytics/reports?view=<id>`): título, fuente y combinadas, insignia, resumen de filtros y
// agrupación, Editar / Eliminar (solo `analytics.manage` y si la vista es editable), «Volver a Vistas», rango de fechas (solo si la
// fuente tiene campo de fecha; no se guarda en la vista), búsqueda libre sobre lo cargado, tabla ordenable con Exportar (en el
// cliente, hasta 5000 filas) y totales. El servidor filtra, agrupa, agrega y ordena; aquí solo se pinta.
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { EmptyState } from '../../kernel/ui/EmptyState'
import { EMPTY_RANGE, type DateRange } from '../../kernel/ui/dateRange'
import { DateRangeFilter, Filters, SelectFilter } from '../../kernel/ui/Filters'
import { Panel } from '../../kernel/ui/Panel'
import { QBox } from '../../kernel/ui/QBox'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useDataSources } from './api'
import { filterReportRows } from './reportResult'
import { ReportResultTable } from './ReportResultTable'
import { useReportFormatters } from './useReportFormatters'
import { useDeleteReport, useReports, useRunReport } from './reportsApi'
import { ViewBadge } from './ViewBadge'
import { ViewBuilderModal } from './ViewBuilderModal'
import { canManageView, countFilterConditions, decorateResult, fieldIndex, parseGroupJson, runBody, type AggregateFn } from './viewRules'

const EMPTY_LOOKUPS: ReturnType<typeof useLookups>['data'] = []

export function ReportResultScreen({ id }: { id: number }) {
  const t = useT()
  const navigate = useNavigate()
  const sources = useDataSources()
  const reports = useReports()
  const dateModes = useLookups('DateRangeMode').data ?? EMPTY_LOOKUPS
  const del = useDeleteReport()
  const fmt = useReportFormatters()

  const report = (reports.data ?? []).find((r) => r.id === id) ?? null
  const source = (sources.data ?? []).find((s) => s.key === report?.baseEntityType)
  const index = useMemo(() => fieldIndex(source, sources.data ?? []), [source, sources.data])
  const hasDate = Boolean(source?.dateField)

  const [mode, setMode] = useState('ALL')
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [q, setQ] = useState('')
  const [editing, setEditing] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const body = runBody(mode, range.from, range.to, hasDate)
  const run = useRunReport(report ? id : null, body)
  const decorated = useMemo(
    () => decorateResult(run.data, report, index, (fn: AggregateFn) => t(`analytics.reports.fn.${fn}`)),
    [run.data, report, index, t],
  )
  const shown = useMemo(() => filterReportRows(decorated, q, fmt), [decorated, q, fmt])

  const back = () => navigate('/analytics/reports')

  if (reports.isLoading || sources.isLoading) return <Spinner block label={t('common.loading')} />
  const listError = reports.error ?? sources.error
  if (listError) return <EmptyState title={listError instanceof ApiError ? listError.title : t('errors.generic')} />
  if (!report)
    return (
      <>
        <div className="head">
          <div>
            <h1>{t('analytics.reports.notFoundTitle')}</h1>
            <p>{t('analytics.reports.notFoundBody')}</p>
          </div>
          <div className="act">
            <button type="button" className="btn flow" onClick={back}>
              {t('analytics.reports.back')}
            </button>
          </div>
        </div>
      </>
    )

  const group = parseGroupJson(report.groupJson)
  const nFilters = countFilterConditions(report.filterJson)
  const groupedBy = group.by.map((k) => index.get(k)?.label ?? k).join(', ')
  const combined = (report.secondary ?? []).map((k) => (source?.relations ?? []).find((r) => r.key === k)?.label ?? k)
  const editable = canManageView(report)
  const total = run.data?.total ?? 0
  const loaded = run.data?.rows?.length ?? 0
  const incompleteRange = mode === 'CUSTOM' && body == null

  return (
    <>
      <div className="head">
        <div>
          <h1>{report.name}</h1>
          <p>
            {source?.label ?? report.baseEntityType}
            {combined.length > 0 ? ` + ${combined.join(' + ')}` : ''} <ViewBadge report={report} />
          </p>
          {(nFilters > 0 || group.by.length > 0) && (
            <p className="vw-note-line">
              {nFilters > 0 ? t('analytics.reports.filtersApplied', { n: nFilters }) : ''}
              {nFilters > 0 && group.by.length > 0 ? ' · ' : ''}
              {group.by.length > 0 ? t('analytics.reports.groupedBy', { fields: groupedBy }) : ''}
            </p>
          )}
        </div>
        <div className="act">
          <Can perm="analytics.manage">
            {editable && (
              <>
                <button type="button" className="btn" onClick={() => setEditing(true)}>
                  {t('analytics.reports.editBtn')}
                </button>
                <button type="button" className="btn" onClick={() => setConfirmDelete(true)}>
                  {t('analytics.reports.deleteBtn')}
                </button>
              </>
            )}
          </Can>
          <button type="button" className="btn flow" onClick={back}>
            {t('analytics.reports.back')}
          </button>
        </div>
      </div>

      {hasDate && (
        <Filters>
          <>
            <SelectFilter
              label={t('analytics.reports.range')}
              value={mode}
              onChange={(v) => {
                setMode(v)
                if (v !== 'CUSTOM') setRange(EMPTY_RANGE)
              }}
              options={dateModes.map((m) => ({ value: m.code, label: m.label }))}
              allLabel={null}
            />
            {mode === 'CUSTOM' && <DateRangeFilter label={t('analytics.reports.rangeCustom')} value={range} onChange={setRange} />}
          </>
        </Filters>
      )}
      <div className="qrow vw-q">
        <QBox value={q} onChange={setQ} placeholder={t('analytics.reports.searchRows')} />
      </div>
      {incompleteRange && <p className="help">{t('analytics.reports.rangeIncomplete')}</p>}
      {range.from && range.to && range.from > range.to && <p className="ferr">{t('analytics.range.errors.fromAfterTo')}</p>}

      {run.error && (
        <p className="ferr" role="alert">
          {applyProblemDetails(run.error).title}
        </p>
      )}
      <Panel flush icon={<IconDoc />} title={report.name ?? ''} badge={shown ? (q.trim() ? `${shown.rows?.length ?? 0} / ${loaded}` : loaded) : undefined}>
        {!run.error && <ReportResultTable result={shown} name={report.name ?? ''} loading={run.isLoading} emptyText={t(q.trim() ? 'analytics.reports.emptySearch' : 'analytics.reports.emptyView')} />}
        {total > loaded && loaded > 0 && (
          <p className="note vw-trunc" role="status">
            {t('analytics.reports.truncated', { shown: loaded, total })}
          </p>
        )}
      </Panel>

      {editing && <ViewBuilderModal source={report.baseEntityType ?? ''} report={report} reports={reports.data ?? []} onClose={() => setEditing(false)} />}
      <ConfirmDialog
        open={confirmDelete}
        tone="danger"
        title={t('analytics.reports.deleteTitle')}
        message={t('analytics.reports.deleteBody', { name: report.name ?? '' })}
        confirmLabel={t('analytics.reports.deleteBtn')}
        onConfirm={async () => {
          await del.mutateAsync(id)
          toast.success(t('analytics.reports.deleted', { name: report.name ?? '' }))
          back()
        }}
        onClose={() => setConfirmDelete(false)}
      />
    </>
  )
}
