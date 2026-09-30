// Máscara de teléfono `(xxx)xxx-xxxx` (10 dígitos, Puerto Rico / EE. UU.). Lógica pura.

/** Solo los dígitos de un texto. */
export function phoneDigits(v: string): string {
  return v.replace(/\D/g, '')
}

/** Aplica la máscara mientras se escribe: hasta 10 dígitos; parcial mientras no se completan. */
export function formatPhone(v: string): string {
  const d = phoneDigits(v).slice(0, 10)
  if (d.length === 0) return ''
  if (d.length <= 3) return `(${d}`
  if (d.length <= 6) return `(${d.slice(0, 3)})${d.slice(3)}`
  return `(${d.slice(0, 3)})${d.slice(3, 6)}-${d.slice(6)}`
}

/** Teléfono guardado en otro formato: con exactamente 10 dígitos se muestra normalizado; si no, tal cual. */
export function normalizeStoredPhone(v: string | null | undefined): string {
  if (!v) return ''
  return phoneDigits(v).length === 10 ? formatPhone(v) : v
}

/** Vacío o exactamente 10 dígitos. */
export function isValidPhone(v: string): boolean {
  const n = phoneDigits(v).length
  return n === 0 || n === 10
}
