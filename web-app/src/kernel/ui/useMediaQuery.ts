import { useCallback, useSyncExternalStore } from 'react'

/** true mientras la media query se cumpla. Sin `matchMedia` (pruebas, SSR) devuelve false. */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return () => {}
      const mql = window.matchMedia(query)
      mql.addEventListener('change', onChange)
      return () => mql.removeEventListener('change', onChange)
    },
    [query],
  )
  const snapshot = () =>
    typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(query).matches : false
  return useSyncExternalStore(subscribe, snapshot, () => false)
}

/** Punto de corte en el que las tablas pasan a tarjetas (igual que base.css). */
export const CARDS_QUERY = '(max-width: 720px)'
