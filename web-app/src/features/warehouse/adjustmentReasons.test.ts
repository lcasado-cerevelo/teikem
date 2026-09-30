import { describe, expect, it } from 'vitest'
import { reasonAllowed, reasonsForDirection, selectableAdjustmentReasons, signedAdjustQuantity } from './adjustmentReasons'

describe('selectableAdjustmentReasons', () => {
  it('oculta los motivos reservados al sistema, incluido el saldo inicial de la migración', () => {
    const reasons = [
      { code: 'DAMAGE', label: 'Daño' },
      { code: 'RECEIPT_VARIANCE', label: 'Diferencia en recepción' },
      { code: 'COUNT_VARIANCE', label: 'Diferencia de conteo' },
      { code: 'PICK_BATCH_REVERSAL', label: 'Reversa de recolección' },
      { code: 'OPENING_BALANCE', label: 'Saldo inicial (migración)' },
    ]
    const visible = selectableAdjustmentReasons(reasons)
    expect(visible.map((r) => r.code)).toEqual(['DAMAGE'])
    expect(visible.map((r) => r.label)).not.toContain('Saldo inicial (migración)')
  })
})

// Lote 14 (D11): Subir/Bajar con cantidad positiva y motivos según la dirección.
describe('reasonsForDirection / reasonAllowed / signedAdjustQuantity', () => {
  const catalog = ['RECEIPT_VARIANCE', 'COUNT_VARIANCE', 'DAMAGE', 'LOSS', 'FOUND', 'EXPIRED', 'PO_SHORTAGE', 'PICK_BATCH_REVERSAL', 'OTHER', 'OPENING_BALANCE'].map(
    (code) => ({ code }),
  )

  it('al subir: Encontrado sí; Daño, Pérdida y Vencido no; nunca los del sistema', () => {
    expect(reasonsForDirection(catalog, 'up').map((r) => r.code)).toEqual(['FOUND', 'PO_SHORTAGE', 'OTHER'])
  })

  it('al bajar: Daño, Pérdida y Vencido sí; Encontrado no', () => {
    expect(reasonsForDirection(catalog, 'down').map((r) => r.code)).toEqual(['DAMAGE', 'LOSS', 'EXPIRED', 'PO_SHORTAGE', 'OTHER'])
  })

  it('sin dirección: todos los seleccionables', () => {
    expect(reasonsForDirection(catalog, '').map((r) => r.code)).toEqual(['DAMAGE', 'LOSS', 'FOUND', 'EXPIRED', 'PO_SHORTAGE', 'OTHER'])
  })

  it('reasonAllowed quita el motivo que deja de valer al cambiar de dirección', () => {
    expect(reasonAllowed('FOUND', 'down')).toBe(false)
    expect(reasonAllowed('DAMAGE', 'up')).toBe(false)
    expect(reasonAllowed('OTHER', 'up')).toBe(true)
    expect(reasonAllowed('COUNT_VARIANCE', 'up')).toBe(false)
    expect(reasonAllowed('', 'down')).toBe(true)
  })

  it('el signo lo pone la pantalla: subir = positivo, bajar = negativo (el API recibe la cantidad con signo)', () => {
    expect(signedAdjustQuantity('up', 2)).toBe(2)
    expect(signedAdjustQuantity('down', 2)).toBe(-2)
    expect(signedAdjustQuantity('down', -3)).toBe(-3)
  })
})
