// Lote F8a (P3) — Indicadores (`/analytics/indicators`, `analytics.view` + ANALYTICS): un KPI de un solo número por
// tarjeta, agrupadas por módulo de negocio. El API ya filtra lo que el usuario puede leer (§2.2 del plan: permiso
// `analytics.view`, visibilidad y fuente de datos legible); esta pantalla no vuelve a filtrar.
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
import { useDeleteIndicator, useIndicatorValue, useIndicators, useSaveIndicator, useSetMyPulse, type AnalyticsDefinition } from './api'
import { DefinitionEditor } from './DefinitionEditor'
import { DefinitionRangeModal } from './DefinitionRangeModal'
import { effectiveRangeCaption, groupDefinitions, visibilityBadgeKey, withShowInPulse } from './definitions'
import { formatValue, formatYmd } from './format'
import { MODULE_GROUP_ICON } from './moduleIcons'
import './pulse.css'

export default function IndicatorsPage() {
  const t = useT()
  const { data, isLoading, error } = useIndicators()
  const { data: moduleLookups = [] } = useLookups('BusinessModule')
  const groups = useMemo(() => groupDefinitions(data), [data])
  const [editing, setEditing] = useState<'new' | AnalyticsDefinition | null>(null)
  const [toDelete, setToDelete] = useState<AnalyticsDefinition | null>(null)
  const del = useDeleteIndicator()

  const moduleLabel = (code: string) => moduleLookups.find((m) => m.code === code)?.label ?? code

  return (
    <div className="wrap pulse">
      <div className="head">
        <div>
          <h1>{t('analytics.indicators.title')}</h1>
          <p>{t('analytics.indicators.sub')}</p>
        </div>
        <div className="act">
          <Can perm="analytics.manage">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              {t('analytics.indicators.newInd')}
            </button>
          </Can>
        </div>
      </div>

      {error && <EmptyState title={error instanceof ApiError ? error.title : t('errors.generic')} />}
      {!error && isLoading && <Spinner block label={t('common.loading')} />}
      {!error && !isLoading && groups.length === 0 && <EmptyState title={t('analytics.indicators.noneYet')} />}
      {!error &&
        groups.map((g) => {
          const Icon = MODULE_GROUP_ICON[g.group]
          return (
            <div key={g.module || '—'} className="def-group">
              <Panel title={<><Icon /> {moduleLabel(g.module)}</>} actions={<span className="def-count">{g.items.length}</span>}>
                <div className="cols" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 300px), 1fr))' }}>
                  {g.items.map((ind) => (
                    <IndicatorCard key={ind.id} indicator={ind} onEdit={() => setEditing(ind)} onDelete={() => setToDelete(ind)} />
                  ))}
                </div>
              </Panel>
            </div>
          )
        })}
      <p className="note">{t('analytics.indicators.note')}</p>

      {editing && (
        <DefinitionEditor
          kind="indicator"
          definition={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}
      <ConfirmDialog
        open={toDelete != null}
        tone="danger"
        title={t('analytics.indicators.deleteTitle')}
        message={t('analytics.indicators.deleteBody', { name: toDelete?.name ?? '' })}
        onConfirm={async () => {
          if (toDelete?.id == null) return
          await del.mutateAsync(toDelete.id)
          toast.success(t('analytics.indicators.deletedMsg'))
        }}
        onClose={() => setToDelete(null)}
      />
    </div>
  )
}

function IndicatorCard({ indicator, onEdit, onDelete }: { indicator: AnalyticsDefinition; onEdit: () => void; onDelete: () => void }) {
  const t = useT()
  const lang = useLang()
  const value = useIndicatorValue(indicator.id ?? null)
  const setMyPulse = useSetMyPulse()
  const saveIndicator = useSaveIndicator()
  const [rangeOpen, setRangeOpen] = useState(false)
  const { data: modes = [] } = useLookups('DateRangeMode', { includeDisabled: true })

  const caption = effectiveRangeCaption(
    indicator,
    (code) => modes.find((m) => m.code === code)?.label ?? code,
    (ymd) => formatYmd(ymd, lang),
  )
  const visKey = visibilityBadgeKey(indicator.visibility)
  const visLabel = t(
    visKey === 'all' ? 'analytics.indicators.visAllBadge' : visKey === 'shared' ? 'analytics.indicators.visSharedBadge' : 'analytics.indicators.visPrivateBadge',
    { n: indicator.shares?.length ?? 0 },
  )

  return (
    <Panel>
      <div style={{ textAlign: 'center', padding: '4px 0' }}>
        {indicator.isSystem && <Chip tone="cap">{t('analytics.indicators.systemBadge')}</Chip>}
        <div className="big" style={{ fontSize: 26 }}>
          {value.isLoading ? (
            <Spinner label={t('common.loading')} />
          ) : value.error ? (
            <span className="pulse-muted">{value.error instanceof ApiError ? value.error.title : t('errors.generic')}</span>
          ) : (
            formatValue(value.data?.value ?? null, indicator.isMoney ?? false)
          )}
        </div>
        <p style={{ margin: '6px 0 0' }}>{indicator.name}</p>
        {caption && <p className="sub">{caption}</p>}

        <label className="sw" style={{ justifyContent: 'center', marginTop: 10 }}>
          <input
            type="checkbox"
            role="switch"
            checked={indicator.effectiveShowInPulse ?? false}
            onChange={(e) => {
              if (indicator.id == null) return
              setMyPulse.mutate(
                { kind: 'indicator', id: indicator.id, showInPulse: e.target.checked },
                { onError: (err) => toast.error(applyProblemDetails(err).title) },
              )
            }}
          />
          <span className="tk" aria-hidden="true" />
          {t('analytics.pulse.showInPulseMine')}
        </label>
        {indicator.canEdit && (
          <label className="sw" style={{ justifyContent: 'center', marginTop: 6 }}>
            <input
              type="checkbox"
              role="switch"
              checked={indicator.showInPulse ?? false}
              onChange={(e) => {
                if (indicator.id == null) return
                saveIndicator.mutate(
                  { id: indicator.id, body: withShowInPulse(indicator, 'indicator', e.target.checked) },
                  { onError: (err) => toast.error(applyProblemDetails(err).title) },
                )
              }}
            />
            <span className="tk" aria-hidden="true" />
            {t('analytics.pulse.showInPulseCompany')}
          </label>
        )}

        {indicator.canChangeDate && indicator.id != null && (
          <div style={{ marginTop: 8 }}>
            <button type="button" className="btn sm" onClick={() => setRangeOpen(true)}>
              {t('analytics.range.edit')}
            </button>
          </div>
        )}

        <div style={{ marginTop: 8 }}>
          <Chip>{visLabel}</Chip>
        </div>
        {!indicator.isSystem && indicator.ownerName && <p className="help">{t('analytics.indicators.createdBy', { name: indicator.ownerName })}</p>}

        {indicator.canEdit && (
          <div className="nact" style={{ justifyContent: 'center', marginTop: 8 }}>
            <button type="button" className="btn sm" onClick={onEdit}>
              {t('analytics.editor.editBtn')}
            </button>
            <button type="button" className="btn sm" onClick={onDelete}>
              {t('analytics.editor.deleteBtn')}
            </button>
          </div>
        )}
      </div>
      {rangeOpen && indicator.id != null && (
        <DefinitionRangeModal
          kind="indicator"
          id={indicator.id}
          name={indicator.name ?? ''}
          dateRangeMode={indicator.effectiveDateRangeMode}
          dateFrom={indicator.effectiveDateFrom}
          dateTo={indicator.effectiveDateTo}
          onClose={() => setRangeOpen(false)}
        />
      )}
    </Panel>
  )
}
