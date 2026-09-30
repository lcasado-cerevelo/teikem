import { useLayoutEffect, useState, type RefObject } from 'react'

/**
 * Ancho en px (entero, `getBoundingClientRect().width`) del elemento de `ref`, al día con `ResizeObserver`. 0 mientras no
 * se haya medido (jsdom sin layout: 0). La ref debe apuntar a un elemento que se monta con el componente (no condicional):
 * se observa el que haya al montar. Sirve para decidir por el ancho de un PANEL (no de la ventana), p. ej. `forceCards`:
 * `const ref = useRef<HTMLDivElement>(null); const w = useElementWidth(ref); <div ref={ref}><DataTable forceCards={w > 0 && w < 640} … /></div>`
 */
export function useElementWidth(ref: RefObject<Element | null>): number {
  const [width, setWidth] = useState(0)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setWidth(Math.round(el.getBoundingClientRect().width))
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [ref])

  return width
}
