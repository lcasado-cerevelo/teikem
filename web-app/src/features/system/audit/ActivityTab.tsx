// Seguridad y auditoría → Actividad (maqueta `auditoriaActividadTab`): una sola tabla con la bitácora de cambios y los eventos
// de seguridad (`GET /audit/activity`, paginada en el servidor), filtro de tipo Todo / Cambios / Seguridad (segmento de la
// maqueta), Fecha (días de la compañía) y buscador libre (al API, 300 ms entre teclas); columnas Cuándo, Tipo, Usuario y Detalle
// ordenables (en la página que llegó; el archivo, en todo lo filtrado) y "Exportar CSV" con TODAS las filas filtradas en el
// orden de la tabla (`exportTable`: valores escapados, fecha como fecha, protección contra fórmulas).
import { useCallback, useMemo, useRef, useState } from 'react'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import {
  Chip,
  DataTable,
  DateRangeFilter,
  EmptyState,
  Filters,
  IconClock,
  Panel,
  QBox,
  toast,
  useRegisterFilter,
  type DataColumn,
} from '../../../kernel/ui'
import { exportTable } from '../../../kernel/ui/exportTable'
import { IconDownload } from '../../../kernel/ui/icons'
import { applyProblemDetails } from '../../../kernel/api/client'
import { useDebounced } from '../../warehouse/lineRules'
import { fetchAllActivity, useActivity } from '../auditApi'
import {
  ACTIVITY_KINDS,
  activityBadge,
  activityDetailParts,
  activityDetailText,
  activityQuery,
  DETAIL_PARTS_SHOWN,
  EMPTY_ACTIVITY_FILTERS,
  sortRows,
  type ActivityBadge,
  type ActivityFilters,
  type ActivityKind,
  type ActivityRowDto,
  type DetailTexts,
  type SortState,
} from './auditView'

const NO_ROWS: ActivityRowDto[] = []
const KIND_LABEL: Record<ActivityKind, string> = { all: 'filterAll', changes: 'filterChanges', security: 'filterSecurity' }
const BADGE_LABEL: Record<ActivityBadge, string> = { change: 'badgeChange', event: 'badgeEvent', alert: 'badgeAlert' }
const BADGE_TONE = { change: 'disp', event: 'deliv', alert: 'fail' } as const

export function ActivityTab() {
  const t = useT()
  const f = useFormat()
  const [filters, setFilters] = useState<ActivityFilters>(EMPTY_ACTIVITY_FILTERS)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [sort, setSort] = useState<SortState | null>(null)
  const [exporting, setExporting] = useState(false)
  const text = useDebounced(filters.text, 300)

  const base = useMemo(() => ({ ...filters, text }), [filters, text])
  const query = useMemo(() => activityQuery(base, (page - 1) * pageSize, pageSize), [base, page, pageSize])
  const activity = useActivity(query)

  const tx = useMemo<DetailTexts>(
    () => ({ yes: t('system.audit.activity.yes'), no: t('system.audit.activity.no'), empty: t('system.audit.activity.emptyValue') }),
    [t],
  )
  const badgeText = useCallback((r: ActivityRowDto) => t(`system.audit.activity.${BADGE_LABEL[activityBadge(r)]}`), [t])

  // valor de orden de cada columna (mismo criterio en la tabla y en el archivo)
  const sortValue = useCallback(
    (r: ActivityRowDto, id: string): string | number | null => {
      if (id === 'when') return r.createdAtUtc ?? null
      if (id === 'type') return badgeText(r)
      if (id === 'user') return r.userName ?? null
      return activityDetailText(r, tx)
    },
    [badgeText, tx],
  )

  const columns: DataColumn<ActivityRowDto>[] = [
    {
      id: 'when',
      header: t('system.audit.activity.colWhen'),
      sortable: true,
      sortValue: (r) => r.createdAtUtc ?? '',
      exportValue: (r) => r.createdAtUtc ?? null,
      cell: (r) => <span className="mono aud-when">{f.dateTime(r.createdAtUtc)}</span>,
    },
    {
      id: 'type',
      header: t('system.audit.activity.colType'),
      sortable: true,
      sortValue: (r) => badgeText(r),
      exportValue: (r) => badgeText(r),
      cell: (r) => <Chip tone={BADGE_TONE[activityBadge(r)]}>{badgeText(r)}</Chip>,
    },
    {
      id: 'user',
      header: t('system.audit.activity.colUser'),
      sortable: true,
      sortValue: (r) => r.userName ?? '',
      exportValue: (r) => r.userName ?? null,
      cell: (r) => r.userName ?? t('system.audit.activity.noUser'),
    },
    {
      id: 'detail',
      header: t('system.audit.activity.colDetail'),
      card: 'title',
      sortable: true,
      sortValue: (r) => activityDetailText(r, tx),
      exportValue: (r) => activityDetailText(r, tx),
      cell: (r) => {
        const parts = activityDetailParts(r.detail, tx)
        const shown = parts.slice(0, DETAIL_PARTS_SHOWN).join(' · ')
        const rest = parts.length - DETAIL_PARTS_SHOWN
        return (
          <span className="aud-detail" title={r.ipAddress ? t('system.audit.activity.ip', { ip: r.ipAddress }) : undefined}>
            {r.type}
            {parts.length > 0 && (
              <span className="aud-detail-parts">
                {shown}
                {rest > 0 && ` ${t('system.audit.activity.more', { n: rest })}`}
              </span>
            )}
          </span>
        )
      },
    },
  ]

  const rows = useMemo(() => sortRows(activity.data?.items ?? NO_ROWS, sort, sortValue), [activity.data, sort, sortValue])

  // todas las filas filtradas (no solo la página) en el orden de la tabla
  const allRows = async () => {
    const all = await fetchAllActivity(activityQuery(base, 0, 200))
    return { items: sortRows(all.items, sort, sortValue), truncated: all.truncated }
  }

  const exportCsv = async () => {
    setExporting(true)
    try {
      const { items, truncated } = await allRows()
      await exportTable('csv', columns, items, { title: t('system.audit.activity.fileName'), locale: f.lang })
      if (truncated) toast.info(t('system.audit.activity.exportTruncated', { n: items.length }))
      else toast.success(t('system.audit.activity.exported', { n: items.length }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    } finally {
      setExporting(false)
    }
  }

  const change = (next: Partial<ActivityFilters>) => {
    setFilters((cur) => ({ ...cur, ...next }))
    setPage(1)
  }

  const kindRef = useRef<HTMLFieldSetElement>(null)
  useRegisterFilter(t('system.audit.activity.kindLabel'), filters.kind === 'all' ? null : t(`system.audit.activity.${KIND_LABEL[filters.kind]}`), kindRef)
  const dirty = filters.kind !== 'all' || filters.text !== '' || filters.range.from !== '' || filters.range.to !== ''

  return (
    <>
      <Filters onClear={dirty ? () => change(EMPTY_ACTIVITY_FILTERS) : undefined}>
        <fieldset className="aud-kind" ref={kindRef}>
          <legend>{t('system.audit.activity.kindLabel')}</legend>
          <div className="seg">
            {ACTIVITY_KINDS.map((k) => (
              <button key={k} type="button" className={filters.kind === k ? 'on' : undefined} aria-pressed={filters.kind === k} onClick={() => change({ kind: k })}>
                {t(`system.audit.activity.${KIND_LABEL[k]}`)}
              </button>
            ))}
          </div>
        </fieldset>
        <DateRangeFilter label={t('system.audit.activity.dateLabel')} value={filters.range} onChange={(range) => change({ range })} />
      </Filters>

      <Panel flush icon={<IconClock />} title={t('system.audit.activity.title')} badge={activity.data ? (activity.data.total ?? 0) : undefined}>
        <div className="qrow aud-qrow">
          <QBox value={filters.text} onChange={(v) => change({ text: v })} placeholder={t('system.audit.activity.search')} />
          <button type="button" className="btn sm aud-export" onClick={() => void exportCsv()} disabled={exporting || !activity.data?.total}>
            <IconDownload /> {exporting ? t('system.audit.activity.exporting') : t('system.audit.activity.exportBtn')}
          </button>
        </div>
        {activity.isError ? (
          <EmptyState
            title={applyProblemDetails(activity.error).title}
            action={
              <button type="button" className="btn" onClick={() => void activity.refetch()}>
                {t('common.retry')}
              </button>
            }
          />
        ) : (
          <DataTable
            label={t('system.audit.activity.title')}
            exportFileName={t('system.audit.activity.fileName')}
            columns={columns}
            rows={rows}
            rowKey={(r) => `${r.kind}-${r.id}`}
            sort={sort}
            onSort={setSort}
            page={page}
            pageSize={pageSize}
            total={activity.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(n) => {
              setPageSize(n)
              setPage(1)
            }}
            exportRows={allRows}
            loading={activity.isPending}
            empty={<EmptyState title={t('system.audit.activity.noneYet')} />}
          />
        )}
      </Panel>
    </>
  )
}
