// Lote F8a (P3) — Gráficos (`/analytics/charts`, `analytics.view` + ANALYTICS): visualizaciones sobre una fuente de
// datos, agrupadas por módulo de negocio, con el mismo editor que Indicadores (agrega "Agrupar por" y "Tipo").
// Fase 10b: la tarjeta lleva el rango en línea y un solo switch de Pulso (ver `ChartCard`).
// Lote 15: chip "De la compañía" en los gráficos sin dueño que no son de sistema (Editar/Eliminar según `canEdit`, es decir
// `analytics.manage`); al eliminar uno se avisa que no vuelve.
import { useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { EmptyState } from '../../kernel/ui/EmptyState'
import { Panel } from '../../kernel/ui/Panel'
import { IconLock } from '../../kernel/ui/screenIcons'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useChartData, useCharts, useDeleteChart, useSetMyDateRange, useSetMyPulse, type AnalyticsDefinition } from './api'
import { ChartVisual } from './ChartVisual'
import { DefinitionEditor } from './DefinitionEditor'
import { CUSTOM_RANGE, effectiveRangeCaption, groupDefinitions, isCompanyChart, visibilityBadgeKey } from './definitions'
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
              <Panel icon={<Icon />} title={moduleLabel(g.module)} badge={g.items.length}>
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
        message={t(toDelete && isCompanyChart(toDelete) ? 'analytics.charts.deleteCompanyBody' : 'analytics.charts.deleteBody', { name: toDelete?.name ?? '' })}
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

// Fase 10b (solo Gráficos; la tarjeta de Indicadores no cambia): como la maqueta (`chartCardHtml`), el rango de fecha
// se elige en la propia tarjeta con un `<select>` (y Desde/Hasta en línea para "Rango personalizado") en vez del
// diálogo, y hay UN solo switch "Mostrar en Pulso del día". Ambos son preferencias del usuario (`my-date-range`,
// `my-pulse`), no cambios a la definición: la maqueta muta la definición pero documenta que en el sistema real sería
// una preferencia por usuario, y así cualquiera que ve la tarjeta los puede usar (también en gráficos de sistema). El
// valor por defecto de la compañía ("Mostrar en Pulso del día" y el rango) se sigue editando en el editor del gráfico.
function ChartCard({ chart, onEdit, onDelete }: { chart: AnalyticsDefinition; onEdit: () => void; onDelete: () => void }) {
  const t = useT()
  const lang = useLang()
  const data = useChartData(chart.id ?? null)
  const setMyPulse = useSetMyPulse()
  const { data: modes = [] } = useLookups('DateRangeMode', { includeDisabled: true })

  const caption = effectiveRangeCaption(
    chart,
    (code) => modes.find((m) => m.code === code)?.label ?? code,
    (ymd) => formatYmd(ymd, lang),
  )
  const canPickRange = Boolean(chart.canChangeDate && chart.dateRangeApplies && chart.id != null)
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
          {isCompanyChart(chart) && <Chip tone="cap">{t('analytics.charts.companyBadge')}</Chip>}
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
      {caption && !canPickRange && (
        <p className="sub chart-range-locked" title={chart.dateRangeApplies ? t('analytics.range.lockedHint') : undefined}>
          {chart.dateRangeApplies && <IconLock />}
          {caption}
        </p>
      )}
      <div className="chart-card-foot">
        {canPickRange && <ChartRangeInline chart={chart} modes={modes} />}
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
    </Panel>
  )
}

/**
 * Selector de rango en la tarjeta (`PUT .../charts/{id}/my-date-range`). Un modo relativo se guarda al elegirlo;
 * "Rango personalizado" muestra Desde/Hasta y se guarda cuando ambas fechas están y Desde ≤ Hasta. Mientras tanto la
 * elección queda solo en la tarjeta (el gráfico sigue con el rango guardado).
 */
function ChartRangeInline({ chart, modes }: { chart: AnalyticsDefinition; modes: { code: string; label: string; isEnabled: boolean }[] }) {
  const t = useT()
  const setRange = useSetMyDateRange()
  const saved = { mode: chart.effectiveDateRangeMode ?? '', from: chart.effectiveDateFrom ?? '', to: chart.effectiveDateTo ?? '' }
  // Borrador local: se descarta cuando cambia lo guardado (p. ej. tras guardar o recargar la lista).
  const savedKey = `${saved.mode}|${saved.from}|${saved.to}`
  const [draft, setDraft] = useState({ key: savedKey, mode: saved.mode, from: saved.from, to: saved.to })
  const current = draft.key === savedKey ? draft : { key: savedKey, ...saved }
  const baseId = `chart-range-${chart.id}`
  const name = chart.name ?? ''

  const options = modes.filter((m) => m.isEnabled || m.code === current.mode)
  const custom = current.mode === CUSTOM_RANGE
  const fromAfterTo = custom && current.from !== '' && current.to !== '' && current.from > current.to

  const save = (mode: string, from: string, to: string) => {
    if (chart.id == null) return
    const isCustom = mode === CUSTOM_RANGE
    setRange.mutate(
      { kind: 'chart', id: chart.id, body: { dateRangeMode: mode, dateFrom: isCustom ? from : null, dateTo: isCustom ? to : null } },
      {
        onError: (err) => {
          // Vuelve a mostrar lo guardado.
          setDraft({ key: savedKey, ...saved })
          toast.error(applyProblemDetails(err).title)
        },
      },
    )
  }
  const update = (patch: Partial<{ mode: string; from: string; to: string }>) => {
    const next = { ...current, ...patch }
    setDraft(next)
    if (next.mode !== CUSTOM_RANGE) {
      if (next.mode && next.mode !== saved.mode) save(next.mode, '', '')
      return
    }
    const changed = next.mode !== saved.mode || next.from !== saved.from || next.to !== saved.to
    if (changed && next.from && next.to && next.from <= next.to) save(next.mode, next.from, next.to)
  }

  return (
    <div className="chart-range">
      <select
        id={baseId}
        aria-label={t('analytics.range.cardMode', { name })}
        value={current.mode}
        disabled={setRange.isPending}
        onChange={(e) => update({ mode: e.target.value })}
      >
        {current.mode === '' && <option value="">{t('analytics.range.choose')}</option>}
        {options.map((m) => (
          <option key={m.code} value={m.code}>
            {m.label}
          </option>
        ))}
      </select>
      {custom && (
        <div className="chart-range-days">
          <input
            type="date"
            aria-label={t('analytics.range.from')}
            aria-invalid={fromAfterTo || undefined}
            aria-describedby={fromAfterTo ? `${baseId}-err` : undefined}
            value={current.from}
            onChange={(e) => update({ from: e.target.value })}
          />
          <input type="date" aria-label={t('analytics.range.to')} value={current.to} onChange={(e) => update({ to: e.target.value })} />
        </div>
      )}
      {fromAfterTo && (
        <p className="ferr" id={`${baseId}-err`}>
          {t('analytics.range.errors.fromAfterTo')}
        </p>
      )}
    </div>
  )
}
