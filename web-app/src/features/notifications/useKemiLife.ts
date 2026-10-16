// «Kemi con vida»: parpadea, mira a los lados, guiña y de vez en cuando hace una mueca; con el puntero encima se queda pendiente y las pupilas la siguen.
// Lo más liviano posible: no hay estado de React ni ciclo de animación; solo temporizadores con espera al azar que cambian atributos del dibujo (`data-mood`, `data-blink`) y
// variables CSS, y el movimiento lo hace el CSS con transform y opacity. No corre con la pestaña oculta ni con «reducir movimiento» del sistema, ni sin poder preguntarlo (pruebas).
import { useCallback, useEffect, useRef, type PointerEvent, type RefObject } from 'react'
import { BIG_EVERY, BIG_MOODS, BIG_MS, BLINK_EVERY, BLINK_MS, MICRO_EVERY, MICRO_MOODS, MICRO_MS, between, pickMood, pupilOffset, type KemiMood } from './kemiLife'

export interface KemiLifeHandlers {
  onPointerEnter: (e: PointerEvent<HTMLElement>) => void
  onPointerMove: (e: PointerEvent<HTMLElement>) => void
  onPointerLeave: () => void
}

/** `enabled` = Kemi despierta, sin globo de aviso y sin panel abierto (con eso hecho ella ya llama la atención sola). */
export function useKemiLife(svg: RefObject<SVGSVGElement | null>, enabled: boolean): KemiLifeHandlers {
  const hovering = useRef(false)
  const frame = useRef(0)

  const setMood = useCallback((mood: KemiMood) => {
    const el = svg.current
    if (!el) return
    if (mood === 'calm') el.removeAttribute('data-mood')
    else el.setAttribute('data-mood', mood)
  }, [svg])

  useEffect(() => {
    if (!enabled || typeof window === 'undefined' || typeof window.matchMedia !== 'function') return
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
    const timers = new Set<number>()
    const later = (fn: () => void, ms: number) => {
      const id = window.setTimeout(() => {
        timers.delete(id)
        fn()
      }, ms)
      timers.add(id)
    }
    let last: KemiMood | null = null
    const run = (every: readonly [number, number], act: () => void) => {
      const step = () => {
        // con la pestaña oculta o el puntero encima no se hace nada, pero se vuelve a programar
        if (!document.hidden && !hovering.current) act()
        later(step, between(Math.random, every))
      }
      later(step, between(Math.random, every))
    }
    const blink = () => {
      svg.current?.setAttribute('data-blink', '1')
      later(() => svg.current?.removeAttribute('data-blink'), BLINK_MS)
    }
    const gesture = (list: readonly KemiMood[], ms: readonly [number, number]) => () => {
      const mood = pickMood(Math.random, list, last)
      last = mood
      setMood(mood)
      later(() => {
        if (!hovering.current) setMood('calm')
      }, between(Math.random, ms))
    }
    run(BLINK_EVERY, blink)
    run(MICRO_EVERY, gesture(MICRO_MOODS, MICRO_MS))
    run(BIG_EVERY, gesture(BIG_MOODS, BIG_MS))
    return () => {
      timers.forEach((id) => window.clearTimeout(id))
      cancelAnimationFrame(frame.current)
      const el = svg.current
      el?.removeAttribute('data-mood')
      el?.removeAttribute('data-blink')
    }
  }, [enabled, svg, setMood])

  const onPointerEnter = useCallback(() => {
    hovering.current = true
    setMood('alert')
  }, [setMood])

  const onPointerMove = useCallback(
    (e: PointerEvent<HTMLElement>) => {
      const el = svg.current
      if (!hovering.current || !el) return
      const rect = e.currentTarget.getBoundingClientRect()
      const { clientX, clientY } = e
      cancelAnimationFrame(frame.current)
      frame.current = requestAnimationFrame(() => {
        const p = pupilOffset(clientX - (rect.left + rect.width / 2), clientY - (rect.top + rect.height / 2))
        el.style.setProperty('--lx', `${p.x}px`)
        el.style.setProperty('--ly', `${p.y}px`)
      })
    },
    [svg],
  )

  const onPointerLeave = useCallback(() => {
    hovering.current = false
    cancelAnimationFrame(frame.current)
    const el = svg.current
    el?.style.removeProperty('--lx')
    el?.style.removeProperty('--ly')
    setMood('calm')
  }, [svg, setMood])

  return { onPointerEnter, onPointerMove, onPointerLeave }
}
