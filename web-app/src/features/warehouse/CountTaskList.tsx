// Lote 14 (P8) — panel izquierdo de 'Conteo cíclico': "Tareas de conteo" de la maqueta `conteo()` (un conteo por fila; los de
// "lo cambiado" son de una posición). Cada fila: la posición (o "N posiciones") con su `StatusChip` (Pendiente, Contado,
// Concordancia, Diferencia: colores del catálogo) y, en tono tenue, "CC-… · Zona A · N líneas · fecha", el origen ("Lo
// cambiado" marcado) y a quién está asignado. Clic = elegir (`?count=`). Íconos a la derecha (no se anidan botones):
// Asignar (D10: la tarea COUNT del conteo, `taskId` + `POST /warehouse-tasks/{taskId}/assign`, warehouse.manage; solo
// abiertos) y Eliminar (solo Pendiente, warehouse.count, con confirmación; queda en la auditoría). Sin `QBox` (Cambios.pdf
// p. 14: la lista se filtra con los filtros de arriba); pie `ListPager` con Exportar todo lo filtrado.
import { useCallback, useMemo, useState } from 'react'
import type { FetchAllResult } from '../../kernel/api/fetchAllPages'
import { useCan } from '../../kernel/access'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { ConfirmDialog, IconClip, IconTrash, IconUserPlus, ListPager, Panel, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { useCycleCountAction, type CycleCountDto } from './api'
import { COUNT_STATUS_DOMAIN, countWhere, isChangesOrigin, isCountClosed } from './countView'
import { formatDateTime, formatNumber } from './lineRules'
import { AssignTaskModal } from './taskDialogs'

export interface CountTaskListProps {
  items: readonly CycleCountDto[]
  total: number | undefined
  loading?: boolean
  error?: Error | null
  selectedId: number | null
  onSelect: (id: number) => void
  /** Tras eliminar un conteo (la pantalla quita `?count=` si era el elegido). */
  onDeleted?: (id: number) => void
  page: number
  pageSize: number
  onPage: (page: number) => void
  onPageSize: (size: number) => void
  exportRows: () => Promise<readonly CycleCountDto[] | FetchAllResult<CycleCountDto>>
}

export function CountTaskList(props: CountTaskListProps) {
  const { items, total, loading, error, selectedId, onSelect } = props
  const t = useT()
  const lang = useLang()
  const canManage = useCan('warehouse.manage')
  const canCount = useCan('warehouse.count')
  const action = useCycleCountAction()
  const [assigning, setAssigning] = useState<CycleCountDto | null>(null)
  const [deleting, setDeleting] = useState<CycleCountDto | null>(null)

  /** Quién cuenta: quienes ya capturaron líneas y, si nadie, quien abrió el conteo (en la app, quien lo está contando). */
  const whoCounts = useCallback(
    (c: CycleCountDto): string | null => {
      const counters = c.capturedByNames ?? []
      if (counters.length > 0) return t('warehouse.cycleCounts.list.countedBy', { names: counters.join(', ') })
      return c.createdByName ? t('warehouse.cycleCounts.list.openedBy', { name: c.createdByName }) : null
    },
    [t],
  )

  const whereText = useCallback(
    (c: CycleCountDto) => {
      const w = countWhere(c)
      if (w.kind === 'bin') return w.code
      if (w.kind === 'many') return t('warehouse.cycleCounts.list.positions', { n: w.bins })
      // conteo abierto (por producto) que todavía no tiene ninguna línea: no es "sin posiciones", es que aún no se cuenta nada
      if (c.originCode === 'PRODUCT' && (c.lineCount ?? 0) === 0) return t('warehouse.cycleCounts.list.noLinesYet')
      return t('warehouse.cycleCounts.list.noBins')
    },
    [t],
  )

  // columnas del archivo exportado (la lista no es una tabla)
  const exportColumns = useMemo<DataColumn<CycleCountDto>[]>(
    () => [
      { id: 'number', header: t('warehouse.cycleCounts.columns.number'), cell: (c) => c.number ?? '' },
      { id: 'status', header: t('warehouse.cycleCounts.columns.status'), cell: (c) => c.status ?? c.statusCode ?? '' },
      { id: 'origin', header: t('warehouse.cycleCounts.columns.origin'), cell: (c) => c.origin ?? c.originCode ?? '' },
      { id: 'warehouse', header: t('warehouse.cycleCounts.columns.warehouse'), cell: (c) => c.warehouseCode ?? '' },
      { id: 'bin', header: t('warehouse.cycleCounts.columns.bin'), cell: (c) => whereText(c) },
      { id: 'zone', header: t('warehouse.cycleCounts.columns.zone'), cell: (c) => c.zoneCode ?? '' },
      { id: 'lines', header: t('warehouse.cycleCounts.columns.lines'), cell: (c) => formatNumber(c.lineCount, lang), exportValue: (c) => c.lineCount ?? 0 },
      { id: 'counted', header: t('warehouse.cycleCounts.columns.progress'), cell: (c) => formatNumber(c.countedLines, lang), exportValue: (c) => c.countedLines ?? 0 },
      // a ciegas (sin warehouse.count) el API manda null: no se exporta la columna
      ...(canCount
        ? [
            {
              id: 'netVariance',
              header: t('warehouse.cycleCounts.columns.netVariance'),
              cell: (c: CycleCountDto) => (c.netVariance == null ? '' : formatNumber(c.netVariance, lang)),
              exportValue: (c: CycleCountDto) => c.netVariance ?? null,
            },
          ]
        : []),
      { id: 'assignedTo', header: t('warehouse.cycleCounts.columns.assignedTo'), cell: (c) => c.assignedToName ?? '' },
      { id: 'createdAt', header: t('warehouse.cycleCounts.columns.createdAt'), cell: (c) => formatDateTime(c.createdAtUtc, lang), exportValue: (c) => c.createdAtUtc },
      { id: 'reconciledAt', header: t('warehouse.cycleCounts.columns.reconciledAt'), cell: (c) => formatDateTime(c.reconciledAtUtc, lang), exportValue: (c) => c.reconciledAtUtc },
    ],
    [t, lang, canCount, whereText],
  )

  let body
  if (loading && items.length === 0) body = <Spinner block />
  else if (error)
    body = (
      <p className="pb ferr" role="alert">
        {error.message || t('errors.generic')}
      </p>
    )
  else if (items.length === 0)
    body = (
      <div className="empty rcp-empty sm">
        <div>
          <IconClip />
          <p>{t('warehouse.cycleCounts.list.empty')}</p>
        </div>
      </div>
    )
  else
    body = (
      <ul className="cc-list-items">
        {items.map((c) => {
          const id = c.id ?? 0
          const on = id === selectedId
          const w = countWhere(c)
          const closed = isCountClosed(c.statusCode)
          const canAssign = canManage && c.taskId != null && !closed
          const canDelete = canCount && c.statusCode === 'OPEN'
          const meta = [
            c.number,
            w.kind === 'bin' && w.zone ? t('warehouse.cycleCounts.list.zone', { zone: w.zone }) : c.warehouseCode,
            t('warehouse.cycleCounts.list.lines', { n: c.lineCount ?? 0 }),
            formatDateTime(c.createdAtUtc, lang),
          ].filter(Boolean)
          return (
            <li key={id} className={on ? 'unrow cc-row on' : 'unrow cc-row'}>
              <button type="button" className="cc-row-main" aria-current={on ? 'true' : undefined} onClick={() => onSelect(id)}>
                <span className="cc-row-top">
                  <span className="ref cc-row-where">{whereText(c)}</span>
                  <StatusChip domain={COUNT_STATUS_DOMAIN} code={c.statusCode} label={c.status} />
                </span>
                <span className="meta cc-row-meta">{meta.join(' · ')}</span>
                <span className="meta cc-row-meta">
                  {c.origin && <span className={isChangesOrigin(c) ? 'tag' : 'tag cc-plain'}>{c.origin}</span>}
                  {c.taskId != null && (
                    <span className="cc-row-who">
                      {c.assignedToName ? t('warehouse.cycleCounts.list.assigned', { name: c.assignedToName }) : t('warehouse.cycleCounts.list.unassigned')}
                    </span>
                  )}
                  {whoCounts(c) && <span className="cc-row-who">{whoCounts(c)}</span>}
                </span>
              </button>
              {(canAssign || canDelete) && (
                <span className="cc-row-act">
                  {canAssign && (
                    <button
                      type="button"
                      className="rowbtn"
                      aria-label={t('warehouse.cycleCounts.list.assign', { number: c.number ?? '' })}
                      title={t('warehouse.cycleCounts.list.assign', { number: c.number ?? '' })}
                      onClick={() => setAssigning(c)}
                    >
                      <IconUserPlus />
                    </button>
                  )}
                  {canDelete && (
                    <button
                      type="button"
                      className="rowbtn danger"
                      aria-label={t('warehouse.cycleCounts.list.delete', { number: c.number ?? '' })}
                      title={t('warehouse.cycleCounts.list.delete', { number: c.number ?? '' })}
                      onClick={() => setDeleting(c)}
                    >
                      <IconTrash />
                    </button>
                  )}
                </span>
              )}
            </li>
          )
        })}
      </ul>
    )

  return (
    <Panel flush icon={<IconClip />} title={t('warehouse.cycleCounts.list.title')} badge={total ?? undefined} className="cc-list">
      <div className="cc-list-body" role="group" aria-label={t('warehouse.cycleCounts.list.aria')} aria-busy={loading || undefined}>
        {body}
      </div>
      <ListPager
        page={props.page}
        pageSize={props.pageSize}
        total={total ?? 0}
        onPage={props.onPage}
        onPageSize={props.onPageSize}
        exportColumns={exportColumns}
        exportRows={props.exportRows}
        exportFileName={t('warehouse.cycleCounts.list.exportName')}
      />

      <AssignTaskModal
        open={assigning !== null}
        task={assigning ? { id: assigning.taskId ?? 0, assignedToUserId: assigning.assignedToUserId ?? null } : null}
        onClose={() => setAssigning(null)}
      />
      <ConfirmDialog
        open={deleting !== null}
        tone="danger"
        title={t('warehouse.cycleCounts.deleteTitle')}
        message={t('warehouse.cycleCounts.deleteBody', { number: deleting?.number ?? '' })}
        confirmLabel={t('warehouse.cycleCounts.deleteConfirm')}
        onConfirm={async () => {
          if (!deleting?.id) return
          const id = deleting.id
          await action.mutateAsync({ id, action: 'delete' })
          toast.success(t('warehouse.cycleCounts.deleted', { number: deleting.number ?? '' }))
          props.onDeleted?.(id)
        }}
        onClose={() => setDeleting(null)}
      />
    </Panel>
  )
}
