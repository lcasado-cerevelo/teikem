import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../db/database'
import { findProductByCode } from './productLookup'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  const db = getDb()
  db.runSync(
    "INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, owner_client_public_id, owner_name, is_active) VALUES (1, 'p1', 'SKU-1', 'Uno', '7501234', 'NONE', 'client-1', 'Cliente 1', 1)",
  )
  db.runSync(
    "INSERT INTO product (id, public_id, sku, name, tracking_type_code, owner_client_public_id, owner_name, is_active) VALUES (2, 'p2', 'SKU-2', 'Propio', 'NONE', NULL, NULL, 1)",
  )
})

describe('findProductByCode', () => {
  it('trae el dueño 3PL cuando el producto lo tiene', () => {
    expect(findProductByCode('SKU-1')).toMatchObject({ ownerClientPublicId: 'client-1', ownerName: 'Cliente 1' })
  })

  it('inventario propio: sin dueño', () => {
    expect(findProductByCode('SKU-2')).toMatchObject({ ownerClientPublicId: null, ownerName: null })
  })
})
