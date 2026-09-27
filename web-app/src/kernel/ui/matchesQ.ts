// Búsqueda libre sobre lo que se muestra (QBox). Se aplica DESPUÉS de los filtros estructurados.

export type QText = string | number | boolean | null | undefined

/** Minúsculas y sin acentos: "Camión" y "camion" coinciden. */
export function normalizeQ(value: string): string {
  return value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
}

/**
 * true si cada palabra de `q` aparece en alguno de los textos (sin distinguir mayúsculas ni acentos).
 * `q` vacío → true. `rows.filter((r) => matchesQ(q, r.code, r.name, r.statusLabel))`.
 */
export function matchesQ(q: string | null | undefined, ...texts: QText[]): boolean {
  const terms = normalizeQ(q ?? '')
    .split(/\s+/)
    .filter(Boolean)
  if (terms.length === 0) return true
  const haystack = normalizeQ(
    texts
      .filter((x) => x !== null && x !== undefined && x !== '')
      .map(String)
      .join(' '),
  )
  return terms.every((term) => haystack.includes(term))
}
