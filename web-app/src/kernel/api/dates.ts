// Fechas del API. El backend escribe los DATETIME2 (UTC) sin zona ("2026-09-27T14:00:00.123"); en JavaScript una
// fecha-hora sin desplazamiento se interpreta como hora LOCAL. Aquí se leen siempre como UTC (misma regla que la DSL).

const HAS_ZONE = /(?:[zZ]|[+-]\d{2}:?\d{2})$/

/**
 * Convierte una marca de tiempo del API en `Date`: si trae hora y no trae zona se toma como UTC (se agrega 'Z').
 * Una fecha sola ('YYYY-MM-DD') ya se interpreta como UTC y se deja igual. Cadena inválida → `Invalid Date`.
 */
export function parseApiDate(iso: string): Date {
  const s = iso.trim()
  return new Date(HAS_ZONE.test(s) || !s.includes('T') ? s : `${s}Z`)
}
