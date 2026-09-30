// Lógica pura de ComboSelect (selección única con buscador): tipo de opción y coincidencia exacta (lector de código de barras).
import { normalizeQ } from './matchesQ'

export interface ComboOption {
  /** Valor que se guarda ('' no es un valor válido: significa "ninguno"). */
  value: string
  /** Texto de la opción y del campo cuando está elegida. */
  label: string
  /** Texto secundario (tenue) que también se busca, p. ej. el tipo de una zona. */
  hint?: string
}

/** Opción cuyo valor o etiqueta es exactamente `text` (sin mayúsculas ni acentos), o undefined. */
export function exactComboMatch(options: readonly ComboOption[], text: string): ComboOption | undefined {
  const q = normalizeQ(text.trim())
  if (!q) return undefined
  return options.find((o) => normalizeQ(o.value) === q) ?? options.find((o) => normalizeQ(o.label) === q)
}
