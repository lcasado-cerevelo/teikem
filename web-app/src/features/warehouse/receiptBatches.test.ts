import { describe, expect, it } from 'vitest'
import { batchableUnits, heldForFinalUnits, pendingBatchUnits } from './receiptBatches'

const line = (o: Record<string, unknown>) => ({ trackingTypeCode: 'NONE', receivedQty: 0, postedQty: 0, damagedQty: 0, allocatedToCrossDock: 0, ...o })

describe('receiptBatches', () => {
  it('cuenta lo recibido menos lo ya confirmado en tandas', () => {
    expect(batchableUnits(line({ receivedQty: 8, postedQty: 5 }))).toBe(3)
    expect(batchableUnits(line({ receivedQty: 5, postedQty: 5 }))).toBe(0)
  })
  it('Lote 187: las series completas y el daño entran; series incompletas y cruce de muelle esperan la confirmación final', () => {
    const lines = [
      line({ receivedQty: 4 }),
      line({ trackingTypeCode: 'SERIAL', receivedQty: 2, serialNumbers: ['S1', 'S2'] }),
      line({ trackingTypeCode: 'SERIAL', receivedQty: 3, serialNumbers: ['S1'] }),
      line({ receivedQty: 3, damagedQty: 1 }),
      line({ receivedQty: 6, allocatedToCrossDock: 6 }),
    ]
    expect(pendingBatchUnits(lines)).toBe(9)
    expect(heldForFinalUnits(lines)).toBe(9)
  })
  it('sin líneas no hay nada', () => {
    expect(pendingBatchUnits(undefined)).toBe(0)
  })
})
