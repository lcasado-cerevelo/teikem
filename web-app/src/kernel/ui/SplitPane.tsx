// Dos paneles lado a lado con una barra arrastrable (ratón, dedo o teclado) que recuerda su posición por pantalla en
// localStorage (`teikem.split.<storageKey>`). Bajo `stackBelow` (o si el contenedor no alcanza para los dos anchos
// mínimos) es una sola columna sin barra: primero el panel A. Sin overflow:hidden: los desplegables salen del panel.
import { useId, useRef, useState, type CSSProperties, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import {
  clampSplitRatio,
  ratioFromPointer,
  readSplitRatio,
  splitBounds,
  splitStorageKey,
  SPLIT_BAR_PX,
  SPLIT_DEFAULT_RATIO,
  SPLIT_KEY_STEP,
  SPLIT_MAX_RATIO,
  SPLIT_MIN_PX,
  SPLIT_MIN_RATIO,
  SPLIT_STACK_BELOW,
} from './splitRatio'
import { useT } from '../i18n/useT'
import { useElementWidth } from './useElementWidth'
import { useMediaQuery } from './useMediaQuery'
import './ui.css'

export interface SplitPaneProps {
  /** Clave de la pantalla: la posición se guarda en localStorage como `teikem.split.<storageKey>`. Estable. */
  storageKey: string
  /** Proporción inicial del panel A (0–1). Por defecto 0.6 (60/40). Enter o doble clic en la barra vuelven a ella. */
  defaultRatio?: number
  /** Límites de la proporción del panel A. Por defecto 0.35 / 0.75. */
  minRatio?: number
  maxRatio?: number
  /** Ancho mínimo en px de [A, B]. Por defecto [420, 320]. */
  minPx?: readonly [number, number]
  /** Ancho de ventana (px) desde el que se apila en una columna sin barra. Por defecto 900 (como el cajón del menú). */
  stackBelow?: number
  /** Nombre accesible de la barra (ya traducido). Por defecto `ui.split.resize` ("Cambiar el ancho de los paneles"). */
  label?: string
  className?: string
  /** [panel A (izquierda / arriba), panel B (derecha / abajo)]. */
  children: [ReactNode, ReactNode]
}

function readStored(key: string, def: number): number {
  try {
    return readSplitRatio(window.localStorage.getItem(key), def)
  } catch {
    return def
  }
}

function writeStored(key: string, ratio: number | null) {
  try {
    if (ratio === null) window.localStorage.removeItem(key)
    else window.localStorage.setItem(key, String(Math.round(ratio * 10000) / 10000))
  } catch {
    // almacenamiento no disponible (modo privado, cuota): la posición vale solo en esta visita
  }
}

const pct = (r: number) => Math.round(r * 100)

/**
 * `<SplitPane storageKey="pickBatches"><CollectPanel /><PickBatchesPanel /></SplitPane>`
 * Barra: arrastre con Pointer Events (`setPointerCapture`), ←/→ 5 %, Home/End a los extremos, Enter o doble clic = 60/40.
 * Se guarda al soltar (y con cada tecla).
 */
export function SplitPane({
  storageKey,
  defaultRatio = SPLIT_DEFAULT_RATIO,
  minRatio = SPLIT_MIN_RATIO,
  maxRatio = SPLIT_MAX_RATIO,
  minPx = SPLIT_MIN_PX,
  stackBelow = SPLIT_STACK_BELOW,
  label,
  className,
  children,
}: SplitPaneProps) {
  const t = useT()
  const key = splitStorageKey(storageKey)
  const [ratio, setRatio] = useState(() => readStored(key, defaultRatio))
  const [dragging, setDragging] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(containerRef)
  const narrowViewport = useMediaQuery(`(max-width: ${stackBelow}px)`)
  const paneId = useId()

  // ancho útil (sin la barra); 0 = sin medir (jsdom): solo cuentan minRatio/maxRatio
  const useful = width > 0 ? width - SPLIT_BAR_PX : 0
  const tooNarrow = width > 0 && useful < minPx[0] + minPx[1]
  const stacked = narrowViewport || tooNarrow
  const [lo, hi] = splitBounds(useful, minPx, minRatio, maxRatio)
  const current = clampSplitRatio(ratio, useful, minPx, minRatio, maxRatio)
  // arrastre en curso y último valor arrastrado: refs para que los manejadores del puntero no dependan del render
  const drag = useRef<{ active: boolean; value: number }>({ active: false, value: current })

  const apply = (next: number) => {
    const r = clampSplitRatio(next, useful, minPx, minRatio, maxRatio)
    setRatio(r)
    writeStored(key, r)
  }

  const reset = () => {
    setRatio(defaultRatio)
    writeStored(key, null)
  }

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (e.pointerType === 'mouse' && e.button !== 0) return
    e.preventDefault()
    const bar = e.currentTarget
    if (typeof bar.setPointerCapture === 'function') bar.setPointerCapture(e.pointerId)
    drag.current = { active: true, value: current }
    setDragging(true)
  }

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    if (!drag.current.active) return
    const box = containerRef.current?.getBoundingClientRect()
    if (!box || box.width <= 0) return
    const r = ratioFromPointer(e.clientX - box.left, box.width)
    const next = clampSplitRatio(r, box.width - SPLIT_BAR_PX, minPx, minRatio, maxRatio)
    drag.current.value = next
    setRatio(next)
  }

  // al soltar (o si se pierde la captura): se guarda una sola vez
  const endDrag = (e: PointerEvent<HTMLDivElement>) => {
    if (!drag.current.active) return
    drag.current.active = false
    const bar = e.currentTarget
    if (typeof bar.hasPointerCapture === 'function' && bar.hasPointerCapture(e.pointerId)) bar.releasePointerCapture(e.pointerId)
    setDragging(false)
    writeStored(key, drag.current.value)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    switch (e.key) {
      case 'ArrowLeft':
        apply(current - SPLIT_KEY_STEP)
        break
      case 'ArrowRight':
        apply(current + SPLIT_KEY_STEP)
        break
      case 'Home':
        apply(lo)
        break
      case 'End':
        apply(hi)
        break
      case 'Enter':
        reset()
        break
      default:
        return
    }
    e.preventDefault()
  }

  const style = stacked
    ? undefined
    : ({ '--split-a': `calc((100% - ${SPLIT_BAR_PX}px) * ${current.toFixed(4)})` } as CSSProperties)

  return (
    <div ref={containerRef} className={['split', stacked ? 'stacked' : '', className].filter(Boolean).join(' ')} style={style}>
      <div className="split-pane" id={paneId}>
        {children[0]}
      </div>
      {!stacked && (
        <div
          className={dragging ? 'split-bar dragging' : 'split-bar'}
          role="separator"
          aria-orientation="vertical"
          aria-label={label ?? t('ui.split.resize')}
          aria-controls={paneId}
          aria-valuenow={pct(current)}
          aria-valuemin={pct(lo)}
          aria-valuemax={pct(hi)}
          tabIndex={0}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={endDrag}
          onPointerCancel={endDrag}
          onLostPointerCapture={endDrag}
          onKeyDown={onKeyDown}
          onDoubleClick={reset}
        >
          <span className="split-grip" aria-hidden="true" />
        </div>
      )}
      <div className="split-pane">{children[1]}</div>
    </div>
  )
}
