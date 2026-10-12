// Lotes F-E2a/F-E2b — «Órdenes en esta ruta»: la secuencia de la versión vigente. Se reordena con ▲ ▼, con el teclado en el asa (↑ ↓) o ARRASTRANDO el asa
// (Pointer Events propios: ratón, lápiz y dedo; sin librería). También selecciona la parada (sincronizada con el plano), pone el pin manual y quita.
import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, IconPin, IconTrash } from '../../kernel/ui'
import { IconGrip } from '../analytics/pulseIcons'
import { reorderIds, rowIndexAt, stopHasPoint, stopIds, type RouteStop } from './dispatchRules'

/** Cerca del borde de la lista (px) el arrastre la desplaza sola. */
const EDGE = 28

export function RouteStopsList({
  stops,
  editable,
  canPin,
  busy,
  selectedId,
  onSelect,
  onMove,
  onReorder,
  onPin,
  onRemove,
}: {
  /** Ya en el orden mostrado. */
  stops: readonly RouteStop[]
  editable: boolean
  canPin: boolean
  busy: boolean
  selectedId: number | null
  onSelect: (id: number) => void
  onMove: (index: number, delta: -1 | 1) => void
  onReorder: (ids: number[]) => void
  onPin: (stop: RouteStop) => void
  onRemove: (stop: RouteStop) => void
}) {
  const t = useT()
  const f = useFormat()
  const listRef = useRef<HTMLUListElement>(null)
  const rows = useRef<(HTMLLIElement | null)[]>([])
  const [drag, setDrag] = useState<{ from: number; to: number } | null>(null)
  // la parada elegida (en el plano o aquí) se acerca a la vista DENTRO de la lista, sin mover la página
  useEffect(() => {
    const ul = listRef.current
    const li = ul?.querySelector(`[data-stop="${selectedId}"]`)
    if (!ul || !(li instanceof HTMLElement)) return
    if (li.offsetTop < ul.scrollTop) ul.scrollTop = li.offsetTop
    else if (li.offsetTop + li.offsetHeight > ul.scrollTop + ul.clientHeight) ul.scrollTop = li.offsetTop + li.offsetHeight - ul.clientHeight
  }, [selectedId])
  if (stops.length === 0) return <p className="disp-note">{t('dispatch.stops.empty')}</p>

  const down = (e: PointerEvent<HTMLButtonElement>, index: number) => {
    if (busy || (e.pointerType === 'mouse' && e.button !== 0)) return
    e.preventDefault()
    e.currentTarget.setPointerCapture?.(e.pointerId)
    setDrag({ from: index, to: index })
  }
  const moveOver = (e: PointerEvent<HTMLButtonElement>) => {
    if (!drag) return
    const to = rowIndexAt(
      stops.map((_, i) => rows.current[i]?.getBoundingClientRect().bottom ?? 0),
      e.clientY,
    )
    if (to !== drag.to && to >= 0) setDrag({ from: drag.from, to })
    const box = listRef.current?.getBoundingClientRect()
    if (box && listRef.current) {
      if (e.clientY < box.top + EDGE) listRef.current.scrollTop -= 10
      else if (e.clientY > box.bottom - EDGE) listRef.current.scrollTop += 10
    }
  }
  const up = (e: PointerEvent<HTMLButtonElement>) => {
    if (!drag) return
    e.currentTarget.releasePointerCapture?.(e.pointerId)
    const { from, to } = drag
    setDrag(null)
    const next = reorderIds(stopIds(stops), from, to)
    if (next) onReorder(next)
  }
  const grip = (e: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
      e.preventDefault()
      onMove(index, e.key === 'ArrowUp' ? -1 : 1)
    }
  }

  return (
    <ul className={`disp-stops${drag ? ' dragging' : ''}`} aria-label={t('dispatch.stops.title')} ref={listRef}>
      {stops.map((s, i) => {
        const town = [s.city, s.postalCode].filter(Boolean).join(' · ')
        const number = s.orderNumber ?? ''
        const located = stopHasPoint(s)
        const manual = (s.geocodeAccuracyCode ?? '').toUpperCase() === 'MANUAL'
        const classes = ['disp-stop', s.id === selectedId ? 'on' : '', drag?.from === i ? 'drag' : '', drag && drag.to === i && drag.from !== i ? (drag.to > drag.from ? 'over-down' : 'over-up') : '']
          .filter(Boolean)
          .join(' ')
        return (
          <li key={s.id} className={classes} ref={(el) => { rows.current[i] = el }} data-stop={s.id}>
            {editable && (
              <button
                type="button"
                className="disp-grip"
                disabled={busy}
                aria-label={t('dispatch.stops.handle', { number })}
                title={t('dispatch.stops.handle', { number })}
                onPointerDown={(e) => down(e, i)}
                onPointerMove={moveOver}
                onPointerUp={up}
                onPointerCancel={() => setDrag(null)}
                onKeyDown={(e) => grip(e, i)}
              >
                <IconGrip />
              </button>
            )}
            <span className="seq">{i + 1}</span>
            <button type="button" className="body disp-stop-pick" aria-pressed={s.id === selectedId} aria-label={t('dispatch.stops.select', { number })} onClick={() => onSelect(s.id ?? 0)}>
              <span className="ref">{s.orderNumber}</span>
              <span className="meta">{[s.consigneeName, town].filter(Boolean).join(' · ')}</span>
              {(s.lateForWindow || !located || manual || s.isApproximate) && (
                <span className="tags">
                  {s.lateForWindow && <Chip tone="fail">{t('dispatch.stops.late')}</Chip>}
                  {!located ? <Chip tone="warn">{t('dispatch.stops.noPin')}</Chip> : manual ? <Chip tone="cap">{t('dispatch.stops.manual')}</Chip> : s.isApproximate && <Chip tone="warn">{t('dispatch.stops.approx')}</Chip>}
                </span>
              )}
            </button>
            {s.plannedArrivalUtc && <span className="eta" title={t('dispatch.stops.eta')}>{f.time(s.plannedArrivalUtc)}</span>}
            {(editable || canPin) && (
              <span className="btns">
                {canPin && (
                  <button
                    type="button"
                    className="rowbtn"
                    disabled={busy}
                    aria-label={t(located ? 'dispatch.stops.movePin' : 'dispatch.stops.setPin', { number })}
                    title={t(located ? 'dispatch.stops.movePin' : 'dispatch.stops.setPin', { number })}
                    onClick={() => onPin(s)}
                  >
                    <IconPin />
                  </button>
                )}
                {editable && (
                  <>
                    <button
                      type="button"
                      className="rowbtn"
                      disabled={busy || i === 0}
                      aria-label={t('dispatch.stops.moveUp', { number })}
                      title={t('dispatch.stops.moveUp', { number })}
                      onClick={() => onMove(i, -1)}
                    >
                      <span aria-hidden="true">▲</span>
                    </button>
                    <button
                      type="button"
                      className="rowbtn"
                      disabled={busy || i === stops.length - 1}
                      aria-label={t('dispatch.stops.moveDown', { number })}
                      title={t('dispatch.stops.moveDown', { number })}
                      onClick={() => onMove(i, 1)}
                    >
                      <span aria-hidden="true">▼</span>
                    </button>
                    <button type="button" className="rowbtn" disabled={busy} aria-label={t('dispatch.stops.remove', { number })} title={t('dispatch.stops.remove', { number })} onClick={() => onRemove(s)}>
                      <IconTrash />
                    </button>
                  </>
                )}
              </span>
            )}
          </li>
        )
      })}
    </ul>
  )
}
