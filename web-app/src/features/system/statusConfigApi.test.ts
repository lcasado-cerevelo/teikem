import { describe, expect, it } from 'vitest'
import { swapOrder } from './statusConfigApi'

const rows = [
  { code: 'ACTIVE', sortOrder: 1 },
  { code: 'SUSPENDED', sortOrder: 2 },
  { code: 'PROSPECT', sortOrder: 3 },
]

describe('swapOrder', () => {
  it('intercambia el orden de dos etapas vecinas', () => {
    expect(swapOrder(rows, 2, -1)).toEqual([['PROSPECT', 2], ['SUSPENDED', 3]])
    expect(swapOrder(rows, 0, 1)).toEqual([['ACTIVE', 2], ['SUSPENDED', 1]])
  })

  it('no sube la primera ni baja la última', () => {
    expect(swapOrder(rows, 0, -1)).toBeNull()
    expect(swapOrder(rows, 2, 1)).toBeNull()
  })

  it('con el mismo orden las separa en la dirección pedida', () => {
    const tied = [{ code: 'A', sortOrder: 5 }, { code: 'B', sortOrder: 5 }]
    expect(swapOrder(tied, 1, -1)).toEqual([['B', 4], ['A', 5]])
    expect(swapOrder(tied, 0, 1)).toEqual([['A', 6], ['B', 5]])
  })
})
