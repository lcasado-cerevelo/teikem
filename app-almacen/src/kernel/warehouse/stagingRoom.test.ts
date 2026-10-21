import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../db/database'
import { localStagingFree } from './stagingRoom'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

function bin(id: number, zone: string, cap: number | null, known = 1) {
  getDb().runSync(
    "INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active, max_capacity_qty, capacity_known) VALUES (?, ?, 'wh', ?, 1, ?, ?)",
    [id, `B${id}`, zone, cap, known],
  )
}
function stock(binId: number, qty: number) {
  getDb().runSync(
    "INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, qty_on_hand, qty_reserved, updated_at_utc) VALUES (?, 'wh', ?, 1, 'p', ?, 0, 'x')",
    [binId * 10, binId, qty],
  )
}

describe('localStagingFree', () => {
  it('suma el cupo libre de las posiciones STAGING con cupo', () => {
    bin(1, 'STAGING', 100)
    bin(2, 'STAGING', 50)
    bin(3, 'STORAGE', 10)
    stock(1, 70)
    stock(2, 60)   // más que el cupo: no resta de más
    expect(localStagingFree('wh')).toBe(30)
  })
  it('null si ninguna posición STAGING tiene cupo conocido', () => {
    bin(1, 'STAGING', null)
    bin(2, 'STAGING', 20, 0)
    expect(localStagingFree('wh')).toBeNull()
    expect(localStagingFree('otro')).toBeNull()
  })
})
