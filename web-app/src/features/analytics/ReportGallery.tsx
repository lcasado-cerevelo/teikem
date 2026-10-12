// Lote F-F2 — galería de «Vistas e informes»: un panel por fuente de datos (acordeón, abierto por defecto, dos por fila en escritorio)
// con sus informes —por defecto y de usuarios, mezclados—; el «+» del panel abre el constructor con la fuente ya decidida.
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { EmptyState } from '../../kernel/ui/EmptyState'
import { IconEdit, IconTrash } from '../../kernel/ui/actionIcons'
import { IconChevronDown, IconPlus } from '../../kernel/ui/icons'
import { matchesQ } from '../../kernel/ui/matchesQ'
import { QBox } from '../../kernel/ui/QBox'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useDataSources } from './api'
import { useDeleteReport, useReports } from './reportsApi'
import { ViewBadge } from './ViewBadge'
import { ViewBuilderModal } from './ViewBuilderModal'
import { canManageView, fieldIndex, groupReportsBySource, viewSubtitle, type ReportDto, type SourceGroup } from './viewRules'

export function ReportGallery() {
  const t = useT()
  const [, setParams] = useSearchParams()
  const sources = useDataSources()
  const reports = useReports()
  const del = useDeleteReport()
  const [q, setQ] = useState('')
  const [closed, setClosed] = useState<ReadonlySet<string>>(new Set())
  const [building, setBuilding] = useState<{ source: string; report: ReportDto | null } | null>(null)
  const [toDelete, setToDelete] = useState<ReportDto | null>(null)

  const groups = useMemo(
    () => groupReportsBySource(reports.data ?? [], sources.data ?? [], q.trim() ? (...texts) => matchesQ(q, ...texts) : undefined),
    [reports.data, sources.data, q],
  )
  const loading = sources.isLoading || reports.isLoading
  const error = sources.error ?? reports.error

  const open = (id: number | undefined) => {
    if (id != null) setParams({ view: String(id) })
  }
  const toggle = (key: string) =>
    setClosed((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <>
      <div className="head">
        <div>
          <h1>{t('analytics.reports.title')}</h1>
          <p>{t('analytics.reports.sub')}</p>
        </div>
      </div>
      <div className="qrow vw-q">
        <QBox value={q} onChange={setQ} placeholder={t('analytics.reports.search')} />
      </div>

      {error ? (
        <EmptyState title={error instanceof ApiError ? error.title : t('errors.generic')} />
      ) : loading ? (
        <Spinner block label={t('common.loading')} />
      ) : groups.length === 0 ? (
        <EmptyState title={t(q.trim() ? 'analytics.reports.noMatches' : 'analytics.reports.noSources')} />
      ) : (
        <div className="vw-grid">
          {groups.map((g) => (
            <SourcePanel
              key={g.source.key}
              group={g}
              sources={sources.data ?? []}
              open={!closed.has(g.source.key ?? '') || q.trim() !== ''}
              onToggle={() => toggle(g.source.key ?? '')}
              onNew={() => setBuilding({ source: g.source.key ?? '', report: null })}
              onOpen={(r) => open(r.id)}
              onEdit={(r) => setBuilding({ source: g.source.key ?? '', report: r })}
              onDelete={setToDelete}
            />
          ))}
        </div>
      )}
      <div className="note vw-note">{t('analytics.reports.note')}</div>

      {building && <ViewBuilderModal source={building.source} report={building.report} reports={reports.data ?? []} onClose={() => setBuilding(null)} />}
      <ConfirmDialog
        open={toDelete != null}
        tone="danger"
        title={t('analytics.reports.deleteTitle')}
        message={t('analytics.reports.deleteBody', { name: toDelete?.name ?? '' })}
        confirmLabel={t('analytics.reports.deleteBtn')}
        onConfirm={async () => {
          if (toDelete?.id == null) return
          await del.mutateAsync(toDelete.id)
          toast.success(t('analytics.reports.deleted', { name: toDelete.name ?? '' }))
        }}
        onClose={() => setToDelete(null)}
      />
    </>
  )
}

interface SourcePanelProps {
  group: SourceGroup
  sources: Parameters<typeof fieldIndex>[1]
  open: boolean
  onToggle: () => void
  onNew: () => void
  onOpen: (r: ReportDto) => void
  onEdit: (r: ReportDto) => void
  onDelete: (r: ReportDto) => void
}

function SourcePanel({ group, sources, open, onToggle, onNew, onOpen, onEdit, onDelete }: SourcePanelProps) {
  const t = useT()
  const { source, views } = group
  const label = source.label ?? source.key ?? ''
  const index = useMemo(() => fieldIndex(source, sources), [source, sources])
  return (
    <section className="panel vw-panel" aria-label={label}>
      <header className="ph2 vw-head">
        <button type="button" className="vw-toggle" aria-expanded={open} onClick={onToggle}>
          <span className="kit-ic">
            <IconDoc />
          </span>
          <span className="vw-name">{label}</span>
          <span className="r vw-count">{views.length}</span>
          <span className={open ? 'vw-chev open' : 'vw-chev'} aria-hidden="true">
            <IconChevronDown />
          </span>
        </button>
        <Can perm="analytics.manage">
          <button type="button" className="rowbtn vw-new" title={t('analytics.reports.newView')} aria-label={t('analytics.reports.newViewIn', { source: label })} onClick={onNew}>
            <IconPlus />
          </button>
        </Can>
      </header>
      {open && (
        <div className="pb vw-body">
          {views.length === 0 ? (
            <div className="empty vw-empty">
              <p>{t('analytics.reports.noViewsForSource')}</p>
            </div>
          ) : (
            views.map((v) => {
              const sub = viewSubtitle(v, index)
              const combined = (v.secondary ?? []).map((k) => (source.relations ?? []).find((r) => r.key === k)?.label ?? k)
              return (
                <div key={v.id} className="unrow vw-view">
                  <button type="button" className="vw-open" onClick={() => onOpen(v)}>
                    <b>{v.name}</b>
                    <span className="meta">
                      {sub.kind === 'grouped' ? t('analytics.reports.groupedBy', { fields: sub.fields }) : sub.columns === 1 ? t('analytics.reports.columnsCountOne') : t('analytics.reports.columnsCount', { n: sub.columns })}
                      {combined.length > 0 ? ` · + ${combined.join(', ')}` : ''}
                    </span>
                  </button>
                  <ViewBadge report={v} />
                  <Can perm="analytics.manage">
                    {canManageView(v) && (
                      <>
                        <button type="button" className="rowbtn" title={t('analytics.reports.editBtn')} aria-label={t('analytics.reports.editView', { name: v.name ?? '' })} onClick={() => onEdit(v)}>
                          <IconEdit />
                        </button>
                        <button type="button" className="rowbtn danger" title={t('analytics.reports.deleteBtn')} aria-label={t('analytics.reports.deleteView', { name: v.name ?? '' })} onClick={() => onDelete(v)}>
                          <IconTrash />
                        </button>
                      </>
                    )}
                  </Can>
                </div>
              )
            })
          )}
        </div>
      )}
    </section>
  )
}
