// ¿Falta aceptar la política de privacidad y los términos de uso? (null = todavía no se sabe). La raíz lo consulta al tener sesión y manda a /legal; al aceptar se marca como resuelto.
import { useSyncExternalStore } from 'react'

let required: boolean | null = null
const listeners = new Set<() => void>()

function emit(next: boolean | null): void {
  required = next
  listeners.forEach((l) => l())
}

export const setLegalRequired = (value: boolean | null): void => emit(value)

export function useLegalRequired(): boolean | null {
  return useSyncExternalStore(
    (l) => {
      listeners.add(l)
      return () => listeners.delete(l)
    },
    () => required,
    () => required,
  )
}
