// Reglas puras de los ríos del Pulso (Lote 88): a dónde lleva cada tarjeta y las barritas de 7 días. Sin React.
export type RiverKind = 'orders' | 'cod'

/** La pantalla donde se atiende cada tarjeta (todas existen en el menú; el panel solo se pinta con el permiso y el módulo de sus datos). */
export function nodeHref(kind: RiverKind, key: string): string {
  if (kind === 'orders') {
    switch (key) {
      case 'ROUTES':
        return '/ops/dispatch'
      case 'ON_ROUTE':
        return '/ops/monitor'
      default:
        return '/orders'
    }
  }
  switch (key) {
    case 'COLLECTED':
      return '/money/cod?tab=reconcile'
    case 'RECONCILED':
    case 'REMITTANCE':
      return '/money/cod?tab=remittances'
    default:
      return '/money/cod'
  }
}

export interface SparkPoint {
  date: string
  value: number
}

/** Los últimos N días (el último es hoy, `day` = «AAAA-MM-DD» de la compañía) con su fecha; sin barritas, null. */
export function sparkPoints(spark: readonly number[] | null | undefined, day: string): SparkPoint[] | null {
  if (!spark || spark.length === 0 || !day) return null
  const end = new Date(`${day}T00:00:00`)
  return spark.map((value, i) => {
    const d = new Date(end)
    d.setDate(d.getDate() - (spark.length - 1 - i))
    const iso = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    return { date: iso, value }
  })
}
