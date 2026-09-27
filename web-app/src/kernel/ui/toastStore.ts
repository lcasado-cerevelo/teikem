// Estado de los avisos (toast). Sin React: `toast.success()` se puede llamar desde onSuccess de una mutación.

export type ToastKind = 'success' | 'error' | 'info'

export interface ToastItem {
  id: number
  kind: ToastKind
  message: string
}

let current: ToastItem | null = null
let seq = 0
let timer: ReturnType<typeof setTimeout> | null = null
const listeners = new Set<() => void>()

function emit() {
  listeners.forEach((l) => l())
}

export function subscribeToast(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function getToast(): ToastItem | null {
  return current
}

export function dismissToast(): void {
  if (timer) clearTimeout(timer)
  timer = null
  current = null
  emit()
}

/** Muestra un aviso (reemplaza al anterior). Errores 6 s, el resto 3.5 s. */
export function pushToast(kind: ToastKind, message: string): void {
  if (timer) clearTimeout(timer)
  current = { id: ++seq, kind, message }
  emit()
  timer = setTimeout(dismissToast, kind === 'error' ? 6000 : 3500)
}
