import { adjustIssue, afterQty, buildAdjustRequest, reasonsFor, signedQuantity } from './adjustLogic'

describe('Ajustar — lógica', () => {
  it('motivos según la dirección (como en la web): Encontrado solo al subir; Daño, Pérdida y Vencido solo al bajar; Otro en las dos', () => {
    expect(reasonsFor('up').map((r) => r.code)).toEqual(['FOUND', 'OTHER'])
    expect(reasonsFor('down').map((r) => r.code)).toEqual(['DAMAGE', 'LOSS', 'EXPIRED', 'OTHER'])
    expect(reasonsFor(null)).toHaveLength(5)
  })

  it('el ajuste es solo de cantidad: signo según la dirección, nada de posición destino', () => {
    expect(signedQuantity('up', 3)).toBe(3)
    expect(signedQuantity('down', 3)).toBe(-3)
    expect(afterQty(10, 'down', 4)).toBe(6)
    expect(afterQty(10, 'up', 4)).toBe(14)
    const body = buildAdjustRequest({ warehousePublicId: 'wh', productPublicId: 'p', binId: 5, lotId: null, direction: 'down', quantity: 2, reason: 'LOSS', note: '  se perdió  ' })
    expect(body).toEqual({ productPublicId: 'p', warehousePublicId: 'wh', binId: 5, quantity: -2, reason: 'LOSS', notes: 'se perdió', lotId: null })
    expect(Object.keys(body)).not.toContain('toBinId')
  })

  it('valida en el orden en que se llena: dirección, cantidad, tope al bajar, motivo y nota obligatoria', () => {
    const base = { direction: 'down' as const, qtyText: '2', reason: 'LOSS', note: 'x', available: 5 }
    expect(adjustIssue({ ...base, direction: null })).toBe('direction')
    expect(adjustIssue({ ...base, qtyText: '' })).toBe('qty')
    expect(adjustIssue({ ...base, qtyText: '0' })).toBe('qty')
    expect(adjustIssue({ ...base, qtyText: '6' })).toBe('tooMany')
    expect(adjustIssue({ ...base, direction: 'up', qtyText: '6', reason: 'FOUND' })).toBeNull() // subir no tiene tope
    expect(adjustIssue({ ...base, reason: null })).toBe('reason')
    expect(adjustIssue({ ...base, direction: 'up', reason: 'LOSS' })).toBe('reason') // un motivo de bajar no vale al subir
    expect(adjustIssue({ ...base, note: '  ' })).toBe('note')
    expect(adjustIssue({ ...base, note: 'x'.repeat(301) })).toBe('noteTooLong')
    expect(adjustIssue(base)).toBeNull()
  })
})
