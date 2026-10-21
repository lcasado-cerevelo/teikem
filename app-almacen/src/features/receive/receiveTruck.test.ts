// Lote 184 — «Recibir juntos» (el camión): la regla del dueño y el cuerpo del envío con la llave.
import { truckBlock } from './receiveTruck'
import { buildReceiptBody, buildReceiptPartBody } from './receiveLogic'

describe('camión (recibir juntos)', () => {
  it('el primer documento fija el dueño; otro dueño no entra y el mismo sí', () => {
    expect(truckBlock(null, 'C:asics')).toBeNull()
    expect(truckBlock('C:asics', 'C:asics')).toBeNull()
    expect(truckBlock('C:asics', 'C:otra')).toBe('otherOwner')
    expect(truckBlock('C:asics', 'OWN')).toBe('otherOwner')
    expect(truckBlock('OWN', 'OWN')).toBeNull()
  })

  it('el cuerpo lleva la llave del camión solo si hay camión', () => {
    const doc = { asnId: 7, purchaseOrderPublicId: null }
    expect(buildReceiptBody('w1', doc, [], null)).not.toHaveProperty('groupKey')
    expect(buildReceiptBody('w1', doc, [], null, 'k-1')).toMatchObject({ groupKey: 'k-1', asnId: 7, confirm: true })
    expect(buildReceiptPartBody('w1', doc, [], 'ck')).not.toHaveProperty('groupKey')
    expect(buildReceiptPartBody('w1', doc, [], 'ck', 'k-1')).toMatchObject({ groupKey: 'k-1', clientKey: 'ck' })
  })
})
