// Estado y lógica pura de la paleta de comandos ("Buscar o ejecutar…"). El componente está en CommandPalette.tsx;
// el shell la monta una sola vez y cualquier pantalla puede abrirla con `openCommandPalette()`.
import { useEffect, useRef, useSyncExternalStore } from 'react'
import type { ReactNode } from 'react'
import { matchesQ } from './matchesQ'

/** Un destino de la paleta (el shell la llena con los ítems visibles del menú). */
export interface CommandItem {
  /** Identificador único (p. ej. la ruta). */
  id: string
  /** Clave del grupo (los ítems llegan ya ordenados; los de un mismo grupo, juntos). */
  group: string
  /** Etiqueta del grupo, ya traducida. */
  groupLabel: string
  /** Título, ya traducido. */
  title: string
  /** Subtítulo, ya traducido. */
  subtitle?: string
  /** Ícono opcional (SVG). */
  icon?: ReactNode
}

/** Ítems que coinciden con `q` (cada palabra en el título o el subtítulo, sin mayúsculas ni acentos), en su orden. */
export function filterCommands<T extends CommandItem>(items: readonly T[], q: string): T[] {
  return items.filter((it) => matchesQ(q, it.title, it.subtitle))
}

/** Agrupa ítems consecutivos del mismo grupo conservando el orden de llegada. */
export function groupCommands<T extends CommandItem>(items: readonly T[]): { group: string; label: string; items: T[] }[] {
  const out: { group: string; label: string; items: T[] }[] = []
  for (const it of items) {
    const last = out[out.length - 1]
    if (last && last.group === it.group) last.items.push(it)
    else out.push({ group: it.group, label: it.groupLabel, items: [it] })
  }
  return out
}

/** true si el foco está en un campo de texto (ahí `/` se escribe, no abre la paleta). */
export function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
}

/** true si la tecla es un atajo de la paleta: `/` o Ctrl/⌘+K. */
export function isPaletteShortcut(e: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'altKey'>): boolean {
  if (e.altKey) return false
  if (e.key === '/' && !e.ctrlKey && !e.metaKey) return true
  return (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k'
}

// ---- estado abierto/cerrado (un solo lugar para toda la app) ----
let open = false
const listeners = new Set<() => void>()

function emit(): void {
  listeners.forEach((l) => l())
}

/** Abre la paleta (p. ej. el botón "Abrir otra pantalla" de una pantalla pendiente). */
export function openCommandPalette(): void {
  if (open) return
  open = true
  emit()
}

/** Cierra la paleta. */
export function closeCommandPalette(): void {
  if (!open) return
  open = false
  emit()
}

/** true si la paleta está abierta. */
export function isCommandPaletteOpen(): boolean {
  return open
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Estado de la paleta; se vuelve a pintar al abrir o cerrar. */
export function useCommandPaletteOpen(): boolean {
  return useSyncExternalStore(subscribe, isCommandPaletteOpen, isCommandPaletteOpen)
}

/**
 * Atajos globales `/` y Ctrl/⌘+K → `onOpen()`, solo fuera de un campo de texto y sin otro diálogo modal abierto
 * (un formulario en un Modal no pierde la tecla). Lo monta el shell una vez.
 */
export function useCommandPaletteShortcut(onOpen: () => void = openCommandPalette): void {
  const ref = useRef(onOpen)
  useEffect(() => {
    ref.current = onOpen
  })
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || !isPaletteShortcut(e) || isEditableTarget(e.target)) return
      if (document.querySelector('[aria-modal="true"]')) return
      e.preventDefault()
      ref.current()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])
}
