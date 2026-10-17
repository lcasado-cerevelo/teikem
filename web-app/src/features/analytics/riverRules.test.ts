// Pruebas de las reglas puras de los ríos del Pulso (Lote 88).
import { describe, expect, it } from 'vitest'
import { nodeHref, sparkPoints } from './riverRules'

describe('a dónde lleva cada tarjeta', () => {
  it('el río de órdenes', () => {
    expect(nodeHref('orders', 'ENTRY')).toBe('/orders')
    expect(nodeHref('orders', 'IN_WAREHOUSE')).toBe('/orders')
    expect(nodeHref('orders', 'TO_DISPATCH')).toBe('/ops/dispatch')
    expect(nodeHref('orders', 'ON_ROUTE')).toBe('/ops/monitor')
    expect(nodeHref('orders', 'DELIVERED')).toBe('/orders')
  })
  it('el río del COD abre la pestaña que atiende cada tarjeta', () => {
    expect(nodeHref('cod', 'TO_COLLECT')).toBe('/money/cod')
    expect(nodeHref('cod', 'COLLECTED')).toBe('/money/cod?tab=reconcile')
    expect(nodeHref('cod', 'RECONCILED')).toBe('/money/cod?tab=remittances')
    expect(nodeHref('cod', 'REMITTANCE')).toBe('/money/cod?tab=remittances')
  })
})

describe('barritas de 7 días', () => {
  it('el último punto es hoy y los demás van hacia atrás, cruzando el mes', () => {
    const pts = sparkPoints([0, 1, 2, 3, 4, 5, 6], '2026-10-03')!
    expect(pts.map((p) => p.date)).toEqual(['2026-09-27', '2026-09-28', '2026-09-29', '2026-09-30', '2026-10-01', '2026-10-02', '2026-10-03'])
    expect(pts.map((p) => p.value)).toEqual([0, 1, 2, 3, 4, 5, 6])
  })
  it('sin barritas o sin día no hay gráfico', () => {
    expect(sparkPoints(null, '2026-10-03')).toBeNull()
    expect(sparkPoints([], '2026-10-03')).toBeNull()
    expect(sparkPoints([1], '')).toBeNull()
  })
})
