// Lote F-E1 — Lista «Escaneados en esta sesión». Vive en memoria del navegador (no hay historial en el servidor): sobrevive a abrir una
// orden y volver con «Atrás», pero se pierde al recargar la página, al cerrar sesión o al cambiar de compañía (cada usuario y compañía
// tiene la suya: el `scope`).
import { useContext, useSyncExternalStore } from 'react'
import { SessionContext } from '../../app/session'
import { pushEntry, type KnownOrder, type ScanEntry } from './scanRules'

const EMPTY: readonly ScanEntry[] = []

let scope = ''
let entries: readonly ScanEntry[] = EMPTY
let counter = 1
const listeners = new Set<() => void>()

const emit = () => listeners.forEach((l) => l())

/** Número único de un escaneo (clave de la fila). */
export function nextEntryId(): number {
  return counter++
}

function ensureScope(next: string) {
  if (scope !== next) {
    scope = next
    entries = EMPTY
  }
}

export function addScanEntry(forScope: string, entry: ScanEntry): void {
  ensureScope(forScope)
  entries = pushEntry(entries, entry)
  emit()
}

/** Completa lo que se sabe de la orden de un escaneo (sin pisar lo que ya estaba). */
export function updateScanKnown(forScope: string, id: number, more: (known: KnownOrder) => KnownOrder): void {
  if (scope !== forScope) return
  entries = entries.map((e) => (e.id === id ? { ...e, known: more(e.known) } : e))
  emit()
}

/** Solo para pruebas: vacía la sesión. */
export function resetScanSession(): void {
  scope = ''
  entries = EMPTY
  emit()
}

const subscribe = (listener: () => void) => {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** Alcance de la sesión: usuario y compañía activos (sin sesión, uno fijo para las pruebas). */
export function useScanScope(): string {
  const me = useContext(SessionContext)?.me
  return `${me?.tenantId ?? 0}:${me?.userId ?? 0}`
}

/** Los escaneos de esta sesión (lo último primero) para el usuario y la compañía de `forScope`. */
export function useScanEntries(forScope: string): readonly ScanEntry[] {
  const all = useSyncExternalStore(subscribe, () => entries)
  return scope === forScope ? all : EMPTY
}
