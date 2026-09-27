// Lote F7A (P1) — panel 'Actividad reciente' de Pulso (GET /api/v1/analytics/activity).
// Pestañas por módulo de negocio (solo las que el servidor devuelve en `visibleModules`), ventana 24 h / 48 h / Hoy e
// interruptor 'Solo obligatorios'. Tabla (tarjetas bajo 720 px) con hora local, evento (chip por familia y marca de
// obligatorio), referencia con enlace a la ficha, detalle y quién. 'Ver más' pide la siguiente página (skip) y acumula.
// Buscador libre (QBox) sobre las filas ya cargadas, aplicado después de los filtros (pestaña, ventana, obligatorios).
// Lo monta Pulse solo con `analytics.view` y el módulo ANALYTICS; si el usuario no ve ningún módulo el panel no se pinta.
import { useMemo, useState, type CSSProperties } from 'react'
import { Link } from 'react-router-dom'
import { useAccess } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { parseApiDate } from '../../kernel/api/dates'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip, DataTable, EmptyState, matchesQ, Panel, QBox, Spinner, Tabs, type DataColumn } from '../../kernel/ui'
import {
  ACTIVITY_WINDOWS,
  activityLink,
  activityTabs,
  activityTone,
  formatEventTime,
  useActivity,
  type ActivityEventDto,
  type ActivityFilters,
  type ActivityModule,
  type ActivityWindow,
} from './activity'

/** Fila de la tabla: el evento del API con una clave estable (su posición en lo acumulado). */
type ActivityRow = ActivityEventDto & { rowId: string }

// Marca 'oblig.' del mock (.ob): mayúsculas pequeñas en tono de peligro, sin envolver.
const MANDATORY_MARK: CSSProperties = {
  fontSize: 9.5,
  fontWeight: 700,
  letterSpacing: '.06em',
  textTransform: 'uppercase',
  color: 'var(--danger)',
  background: 'var(--dangerbg)',
  border: '1px solid color-mix(in srgb, var(--danger) 35%, transparent)',
  borderRadius: 5,
  padding: '1px 5px',
  marginLeft: 6,
  whiteSpace: 'nowrap',
}
const EVENT_CELL: CSSProperties = { display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', rowGap: 4, maxWidth: '100%' }
const FOOTER: CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  gap: 8,
  flexWrap: 'wrap',
  width: '100%',
  fontSize: 12.5,
  color: 'var(--muted)',
}

/** Códigos de error con los que el panel simplemente no se pinta (el usuario no puede ver la actividad). */
const HIDDEN_ON = new Set(['forbidden', 'module_disabled'])

export function ActivityPanel() {
  const t = useT()
  const lang = useLang()
  const { permissions, modules } = useAccess()
  const [module, setModule] = useState<ActivityModule | null>(null)
  const [win, setWin] = useState<ActivityWindow>('24h')
  const [onlyMandatory, setOnlyMandatory] = useState(false)
  const [q, setQ] = useState('')

  // Sin pestaña elegida no se manda `module`: el servidor lee el primer módulo visible.
  const filters = useMemo<ActivityFilters>(
    () => ({ ...(module ? { module } : {}), window: win, onlyMandatory }),
    [module, win, onlyMandatory],
  )
  const query = useActivity(filters)
  const pages = query.data?.pages
  const tabs = useMemo(() => activityTabs(pages?.[0]?.visibleModules), [pages])
  const total = pages?.[pages.length - 1]?.total ?? 0

  const rows = useMemo<ActivityRow[]>(
    () =>
      (pages ?? []).flatMap((p, pi) =>
        (p.items ?? []).map((e, i) => ({ ...e, rowId: `${pi}-${i}-${e.code ?? ''}-${e.entityType ?? ''}-${e.entityId ?? ''}` })),
      ),
    [pages],
  )
  // Búsqueda libre sobre lo acumulado (no va al API): evento, código, referencia, detalle y quién.
  const shown = useMemo(
    () => (q.trim() ? rows.filter((r) => matchesQ(q, r.label, r.code, r.reference, r.detail, r.userName)) : rows),
    [rows, q],
  )

  const columns = useMemo<DataColumn<ActivityRow>[]>(() => {
    const canOpen = (r: ActivityRow) => {
      const link = activityLink(r)
      return link && permissions.has(link.perm) && modules.has(link.module) ? link : null
    }
    return [
      {
        id: 'time',
        header: t('analytics.activity.columns.time'),
        cell: (r) => (
          <time dateTime={r.occurredAtUtc ?? undefined} style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
            {formatEventTime(r.occurredAtUtc, lang)}
          </time>
        ),
        sortValue: (r) => (r.occurredAtUtc ? parseApiDate(r.occurredAtUtc) : null),
      },
      {
        id: 'event',
        header: t('analytics.activity.columns.event'),
        card: 'title',
        cell: (r) => (
          <span style={EVENT_CELL}>
            <Chip tone={activityTone(r.code)}>{r.label || r.code}</Chip>
            {r.mandatory && (
              <span style={MANDATORY_MARK} title={t('analytics.activity.mandatoryTitle')}>
                {t('analytics.activity.mandatoryMark')}
              </span>
            )}
          </span>
        ),
        sortValue: (r) => r.label || r.code,
      },
      {
        id: 'reference',
        header: t('analytics.activity.columns.reference'),
        cell: (r) => {
          const text = r.reference ?? ''
          const link = canOpen(r)
          return link ? (
            <Link to={link.to} style={{ overflowWrap: 'anywhere' }}>
              {text}
            </Link>
          ) : (
            <span style={{ overflowWrap: 'anywhere' }}>{text}</span>
          )
        },
        sortValue: (r) => r.reference ?? null,
      },
      {
        id: 'detail',
        header: t('analytics.activity.columns.detail'),
        cell: (r) => <span style={{ overflowWrap: 'anywhere' }}>{r.detail ?? ''}</span>,
      },
      {
        id: 'user',
        header: t('analytics.activity.columns.user'),
        cell: (r) => r.userName ?? (r.userId == null ? t('analytics.activity.systemUser') : ''),
        sortValue: (r) => r.userName ?? null,
      },
    ]
  }, [t, lang, permissions, modules])

  const title = t('analytics.activity.title')
  const subtitle = t('analytics.activity.subtitle')

  // Primera carga: todavía no se sabe qué módulos ve el usuario.
  if (query.isPending) {
    return (
      <Panel title={title} subtitle={subtitle}>
        <Spinner block label={t('common.loading')} />
      </Panel>
    )
  }

  const problem = query.error ? applyProblemDetails(query.error) : null
  if (!pages) {
    // Error sin datos previos: sin acceso (403) el panel no se pinta; otro error se muestra en su lugar.
    if (!problem || HIDDEN_ON.has(problem.code)) return null
    return (
      <Panel title={title} subtitle={subtitle}>
        <EmptyState title={problem.title} />
      </Panel>
    )
  }

  // El servidor no devolvió ninguna pestaña (sin permiso de ningún módulo o módulos apagados): sin panel.
  if (tabs.length === 0) return null
  const active: ActivityModule = module && tabs.includes(module) ? module : tabs[0]

  const controls = (
    <>
      <Tabs
        tabs={tabs.map((m) => ({ key: m, label: t(`analytics.activity.modules.${m}`) }))}
        value={active}
        onChange={(m) => setModule(m)}
        label={t('analytics.activity.tabsLabel')}
      />
      <select
        className="btn sm"
        style={{ padding: '6px 8px', maxWidth: '100%' }}
        aria-label={t('analytics.activity.windowLabel')}
        value={win}
        onChange={(e) => setWin(e.target.value as ActivityWindow)}
      >
        {ACTIVITY_WINDOWS.map((w) => (
          <option key={w} value={w}>
            {t(`analytics.activity.windows.${w}`)}
          </option>
        ))}
      </select>
      <label className="sw">
        <input type="checkbox" role="switch" checked={onlyMandatory} onChange={(e) => setOnlyMandatory(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{t('analytics.activity.onlyMandatory')}</span>
      </label>
    </>
  )

  const footer =
    total > 0 || problem ? (
      <div style={FOOTER}>
        <span>
          {total > 0 &&
            t(total === 1 ? 'analytics.activity.footerOne' : 'analytics.activity.footer', {
              count: total.toLocaleString(lang),
              window: t(`analytics.activity.windowFooter.${win}`),
            })}
          {problem && (
            <span role="alert" style={{ color: 'var(--danger)', marginLeft: total > 0 ? 8 : 0 }}>
              {problem.title}
            </span>
          )}
        </span>
        {query.hasNextPage && (
          <button type="button" className="btn sm" disabled={query.isFetchingNextPage} onClick={() => void query.fetchNextPage()}>
            {query.isFetchingNextPage ? t('common.loading') : t('analytics.activity.more')}
          </button>
        )}
      </div>
    ) : undefined

  return (
    <Panel title={title} subtitle={subtitle} actions={controls} footer={footer} flush>
      <div className="qrow">
        <QBox value={q} onChange={setQ} />
      </div>
      <DataTable
        columns={columns}
        rows={shown}
        rowKey={(r) => r.rowId}
        defaultSort={{ id: 'time', desc: true }}
        loading={query.isPlaceholderData}
        label={t('analytics.activity.tableLabel')}
        // sin coincidencias del buscador: el vacío genérico de la tabla ('Sin resultados')
        empty={rows.length > 0 ? undefined : <EmptyState title={t('analytics.activity.empty')} />}
      />
    </Panel>
  )
}
