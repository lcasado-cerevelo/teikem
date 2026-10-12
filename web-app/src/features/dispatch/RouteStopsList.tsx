// Lote F-E2a — «Órdenes en esta ruta»: la secuencia de la versión vigente con ▲ ▼ para reordenar y quitar (el arrastre es de F-E2b).
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, IconTrash } from '../../kernel/ui'
import { orderedStops, type RouteStop } from './dispatchRules'

export function RouteStopsList({
  stops,
  editable,
  busy,
  onMove,
  onRemove,
}: {
  stops: readonly RouteStop[] | null | undefined
  editable: boolean
  busy: boolean
  onMove: (index: number, delta: -1 | 1) => void
  onRemove: (stop: RouteStop) => void
}) {
  const t = useT()
  const f = useFormat()
  const list = orderedStops(stops)
  if (list.length === 0) return <p className="disp-note">{t('dispatch.stops.empty')}</p>
  return (
    <ul className="disp-stops" aria-label={t('dispatch.stops.title')}>
      {list.map((s, i) => {
        const town = [s.city, s.postalCode].filter(Boolean).join(' · ')
        return (
          <li key={s.id} className="disp-stop">
            <span className="seq">{i + 1}</span>
            <div className="body">
              <span className="ref">{s.orderNumber}</span>
              <span className="meta">{[s.consigneeName, town].filter(Boolean).join(' · ')}</span>
              {(s.lateForWindow || s.isApproximate) && (
                <span className="tags">
                  {s.lateForWindow && <Chip tone="fail">{t('dispatch.stops.late')}</Chip>}
                  {s.isApproximate && <Chip tone="warn">{t('dispatch.stops.approx')}</Chip>}
                </span>
              )}
            </div>
            {s.plannedArrivalUtc && <span className="eta" title={t('dispatch.stops.eta')}>{f.time(s.plannedArrivalUtc)}</span>}
            {editable && (
              <span className="btns">
                <button
                  type="button"
                  className="rowbtn"
                  disabled={busy || i === 0}
                  aria-label={t('dispatch.stops.moveUp', { number: s.orderNumber ?? '' })}
                  title={t('dispatch.stops.moveUp', { number: s.orderNumber ?? '' })}
                  onClick={() => onMove(i, -1)}
                >
                  <span aria-hidden="true">▲</span>
                </button>
                <button
                  type="button"
                  className="rowbtn"
                  disabled={busy || i === list.length - 1}
                  aria-label={t('dispatch.stops.moveDown', { number: s.orderNumber ?? '' })}
                  title={t('dispatch.stops.moveDown', { number: s.orderNumber ?? '' })}
                  onClick={() => onMove(i, 1)}
                >
                  <span aria-hidden="true">▼</span>
                </button>
                <button
                  type="button"
                  className="rowbtn"
                  disabled={busy}
                  aria-label={t('dispatch.stops.remove', { number: s.orderNumber ?? '' })}
                  title={t('dispatch.stops.remove', { number: s.orderNumber ?? '' })}
                  onClick={() => onRemove(s)}
                >
                  <IconTrash />
                </button>
              </span>
            )}
          </li>
        )
      })}
    </ul>
  )
}
