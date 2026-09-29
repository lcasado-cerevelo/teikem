// Tema claro/oscuro de la interfaz: `data-theme` en <html> (los tokens de styles/tokens.css), guardado en
// localStorage `teikem.theme`; sin elección guardada, el del sistema (`prefers-color-scheme`). Cambiarlo solo cambia
// el atributo: no desmonta nada. Sin dependencias de React salvo `useTheme()`.
import { useSyncExternalStore } from 'react'

export type Theme = 'light' | 'dark'
export const THEME_STORAGE_KEY = 'teikem.theme'

function systemTheme(): Theme {
  try {
    return globalThis.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
  } catch {
    return 'dark'
  }
}

/** Tema guardado por el usuario o, si no hay, el del sistema (oscuro si no se puede saber). */
export function initialTheme(): Theme {
  try {
    const stored = globalThis.localStorage?.getItem(THEME_STORAGE_KEY)
    if (stored === 'light' || stored === 'dark') return stored
  } catch {
    // sin almacenamiento: el del sistema
  }
  return systemTheme()
}

let current: Theme = initialTheme()
const listeners = new Set<() => void>()

function apply(theme: Theme): void {
  if (typeof document !== 'undefined') document.documentElement.dataset.theme = theme
}

/** Pone en <html> el tema inicial. `main.tsx` la llama antes de pintar (sin parpadeo). */
export function initTheme(): Theme {
  apply(current)
  return current
}

export function getTheme(): Theme {
  return current
}

/** Cambia el tema: `data-theme` en <html> y localStorage. No recarga ni desmonta pantallas. */
export function setTheme(theme: Theme): void {
  try {
    globalThis.localStorage?.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // sin almacenamiento: vale para esta pestaña
  }
  apply(theme)
  if (theme === current) return
  current = theme
  listeners.forEach((l) => l())
}

export function subscribeTheme(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Tema actual; se vuelve a pintar al cambiar (p. ej. la marca elige su variante con él). */
export function useTheme(): Theme {
  return useSyncExternalStore(subscribeTheme, getTheme, getTheme)
}
