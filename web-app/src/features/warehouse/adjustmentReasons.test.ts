import { describe, expect, it } from 'vitest'
import { selectableAdjustmentReasons } from './adjustmentReasons'

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
