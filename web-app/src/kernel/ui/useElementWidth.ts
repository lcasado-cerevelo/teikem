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

/**
 * Lote 15 — alto en px (entero, `getBoundingClientRect().height`) de `element`, al día con `ResizeObserver`; 0 sin elemento o
 * sin medir (jsdom). Recibe el ELEMENTO (callback ref guardada en un estado), no una ref: así sirve para uno que se monta
 * después o que cambia (p. ej. la franja fija del Pulso, que solo existe cuando es la primera sección). Para el
 * desplazamiento de filas fijas: `const [el, setEl] = useState<HTMLDivElement | null>(null); const h = useElementHeight(el)`
 * → `<div ref={setEl}>…</div>` y `style={{ '--alto': `${h}px` }}`.
 */
export function useElementHeight(element: Element | null): number {
  // el alto medido va con SU elemento: otro elemento (o ninguno) no hereda el alto anterior
  const [measured, setMeasured] = useState<{ element: Element; height: number } | null>(null)

  useLayoutEffect(() => {
    if (!element) return
    const measure = () => {
      const height = Math.round(element.getBoundingClientRect().height)
      setMeasured((prev) => (prev?.element === element && prev.height === height ? prev : { element, height }))
    }
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(measure)
    ro.observe(element)
    return () => ro.disconnect()
  }, [element])

  return element && measured?.element === element ? measured.height : 0
}
