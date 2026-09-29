// Lote F8a (P3) — Gráficos (`/analytics/charts`, `analytics.view` + ANALYTICS): visualizaciones sobre una fuente de
// datos, agrupadas por módulo de negocio, con el mismo editor que Indicadores (agrega "Agrupar por" y "Tipo").
import { useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { EmptyState } from '../../kernel/ui/EmptyState'
import { Panel } from '../../kernel/ui/Panel'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useChartData, useCharts, useDeleteChart, useSaveChart, useSetMyPulse, type AnalyticsDefinition } from './api'
import { ChartVisual } from './ChartVisual'
import { DefinitionEditor } from './DefinitionEditor'
import { DefinitionRangeModal } from './DefinitionRangeModal'
import { effectiveRangeCaption, groupDefinitions, visibilityBadgeKey, withShowInPulse } from './definitions'
import { formatYmd } from './format'
import { MODULE_GROUP_ICON } from './moduleIcons'
import './pulse.css'

export default function ChartsPage() {
  const t = useT()
  const { data, isLoading, error } = useCharts()
  const { data: moduleLookups = [] } = useLookups('BusinessModule')
  const groups = useMemo(() => groupDefinitions(data), [data])
  const [editing, setEditing] = useState<'new' | AnalyticsDefinition | null>(null)
  const [toDelete, setToDelete] = useState<AnalyticsDefinition | null>(null)
  const del = useDeleteChart()

  const moduleLabel = (code: string) => moduleLookups.find((m) => m.code === code)?.label ?? code

  return (
    <div className="wrap pulse">
      <div className="head">
        <div>
          <h1>{t('analytics.charts.title')}</h1>
          <p>{t('analytics.charts.sub')}</p>
        </div>
        <div className="act">
          <Can perm="analytics.manage">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              {t('analytics.charts.newChart')}
            </button>
          </Can>
        </div>
      </div>

      {error && <EmptyState title={error instanceof ApiError ? error.title : t('errors.generic')} />}
      {!error && isLoading && <Spinner block label={t('common.loading')} />}
      {!error && !isLoading && groups.length === 0 && <EmptyState title={t('analytics.charts.noneYet')} />}
      {!error &&
        groups.map((g) => {
          const Icon = MODULE_GROUP_ICON[g.group]
          return (
            <div key={g.module || '—'} className="def-group">
              <Panel title={<><Icon /> {moduleLabel(g.module)}</>} actions={<span className="def-count">{g.items.length}</span>}>
                <div className="cols" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 380px), 1fr))' }}>
                  {g.items.map((ch) => (
                    <ChartCard key={ch.id} chart={ch} onEdit={() => setEditing(ch)} onDelete={() => setToDelete(ch)} />
                  ))}
                </div>
              </Panel>
            </div>
          )
        })}
      <p className="note">{t('analytics.charts.note')}</p>

      {editing && <DefinitionEditor kind="chart" definition={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={toDelete != null}
        tone="danger"
        title={t('analytics.charts.deleteTitle')}
        message={t('analytics.charts.deleteBody', { name: toDelete?.name ?? '' })}
        onConfirm={async () => {
          if (toDelete?.id == null) return
          await del.mutateAsync(toDelete.id)
          toast.success(t('analytics.charts.deletedMsg'))
        }}
        onClose={() => setToDelete(null)}
      />
    </div>
  )
}

function ChartCard({ chart, onEdit, onDelete }: { chart: AnalyticsDefinition; onEdit: () => void; onDelete: () => void }) {
  const t = useT()
  const lang = useLang()
  const data = useChartData(chart.id ?? null)
  const setMyPulse = useSetMyPulse()
  const saveChart = useSaveChart()
  const [rangeOpen, setRangeOpen] = useState(false)
  const { data: modes = [] } = useLookups('DateRangeMode', { includeDisabled: true })

  const caption = effectiveRangeCaption(
    chart,
    (code) => modes.find((m) => m.code === code)?.label ?? code,
    (ymd) => formatYmd(ymd, lang),
  )
  const visKey = visibilityBadgeKey(chart.visibility)
  const visLabel = t(
    visKey === 'all' ? 'analytics.indicators.visAllBadge' : visKey === 'shared' ? 'analytics.indicators.visSharedBadge' : 'analytics.indicators.visPrivateBadge',
    { n: chart.shares?.length ?? 0 },
  )

  return (
    <Panel
      title={
        <>
          {chart.name} {chart.isSystem && <Chip tone="cap">{t('analytics.charts.systemBadge')}</Chip>}
        </>
      }
    >
      {data.isLoading ? (
        <Spinner label={t('common.loading')} />
      ) : data.error ? (
        <p className="pulse-muted">{data.error instanceof ApiError ? data.error.title : t('errors.generic')}</p>
      ) : (
        <ChartVisual chartType={chart.chartType} points={data.data?.points} isMoney={chart.isMoney ?? false} name={chart.name} />
      )}
      {caption && <p className="sub">{caption}</p>}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8, marginTop: 12, borderTop: '1px solid var(--line-2)', paddingTop: 10 }}>
        <label className="sw">
          <input
            type="checkbox"
            role="switch"
            checked={chart.effectiveShowInPulse ?? false}
            onChange={(e) => {
              if (chart.id == null) return
              setMyPulse.mutate(
                { kind: 'chart', id: chart.id, showInPulse: e.target.checked },
                { onError: (err) => toast.error(applyProblemDetails(err).title) },
              )
            }}
          />
          <span className="tk" aria-hidden="true" />
          {t('analytics.pulse.showInPulseMine')}
        </label>
        {chart.canEdit && (
          <label className="sw">
            <input
              type="checkbox"
              role="switch"
              checked={chart.showInPulse ?? false}
              onChange={(e) => {
                if (chart.id == null) return
                saveChart.mutate(
                  { id: chart.id, body: withShowInPulse(chart, 'chart', e.target.checked) },
                  { onError: (err) => toast.error(applyProblemDetails(err).title) },
                )
              }}
            />
            <span className="tk" aria-hidden="true" />
            {t('analytics.pulse.showInPulseCompany')}
          </label>
        )}
        {chart.canChangeDate && chart.id != null && (
          <button type="button" className="btn sm" onClick={() => setRangeOpen(true)}>
            {t('analytics.range.edit')}
          </button>
        )}
        <Chip>{visLabel}</Chip>
      </div>
      {!chart.isSystem && chart.ownerName && <p className="help">{t('analytics.charts.createdBy', { name: chart.ownerName })}</p>}
      {chart.canEdit && (
        <div className="nact" style={{ marginTop: 8 }}>
          <button type="button" className="btn sm" onClick={onEdit}>
            {t('analytics.editor.editBtn')}
          </button>
          <button type="button" className="btn sm" onClick={onDelete}>
            {t('analytics.editor.deleteBtn')}
          </button>
        </div>
      )}
      {rangeOpen && chart.id != null && (
        <DefinitionRangeModal
          kind="chart"
          id={chart.id}
          name={chart.name ?? ''}
          dateRangeMode={chart.effectiveDateRangeMode}
          dateFrom={chart.effectiveDateFrom}
          dateTo={chart.effectiveDateTo}
          onClose={() => setRangeOpen(false)}
        />
      )}
    </Panel>
  )
}
