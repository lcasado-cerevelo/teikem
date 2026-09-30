// Lote 14 — 'Transferencias y ajustes' (filtros → consulta del Kárdex y "Filtros aplicados" del reporte) y los armadores
// puros de los modales de ajuste y transferencia (cuerpo con signo, lotes, ítems de una posición, series).
import { describe, expect, it } from 'vitest'
import type { BalanceDto } from './api'
import { OWN_OWNER } from './kardexView'
import {
  adjustmentsQuery,
  describeMovementFilters,
  EMPTY_MOVEMENT_FILTERS,
  movementTabFromParam,
  transfersQuery,
  type MovementFilterState,
} from './movementFilters'
import {
  adjustmentBody,
  availableAt,
  lotOptions,
  parseTransferItemKey,
  serialsAt,
  transferItemKey,
  transferItemOptions,
  type AdjustFormValues,
} from './movementForms'

const f: MovementFilterState = {
  ...EMPTY_MOVEMENT_FILTERS,
  range: { from: '2026-09-01', to: '' },
  warehousePublicIds: ['W1'],
  fromWarehousePublicIds: ['W6'],
  toWarehousePublicIds: ['W7'],
  products: [{ publicId: 'P1', sku: 'TORN-01', label: 'TORN-01 · Tornillo' }],
  owners: [OWN_OWNER],
  reasons: ['DAMAGE'],
  direction: 'OUT',
  manualOnly: true,
}

describe('movementFilters', () => {
  it('?tab: Ajustes sin parámetro, transfers = Transferencias', () => {
    expect(movementTabFromParam(null)).toBe('adjustments')
    expect(movementTabFromParam('transfers')).toBe('transfers')
    expect(movementTabFromParam('x')).toBe('adjustments')
  })

  it('Ajustes: tipo ADJUSTMENT + fechas, almacén, producto, dueño, motivo, dirección y solo manuales (sin origen/destino)', () => {
    expect(adjustmentsQuery(f)).toEqual({
      types: ['ADJUSTMENT'],
      from: '2026-09-01',
      to: undefined,
      productPublicIds: ['P1'],
      ownerClientPublicIds: undefined,
      includeOwn: true,
      manualOnly: true,
      warehousePublicIds: ['W1'],
      reasons: ['DAMAGE'],
      direction: 'OUT',
    })
  })

  it('Transferencias: tipo TRANSFER + almacén de origen y de destino (sin motivo ni dirección); por defecto TODAS (D12)', () => {
    const q = transfersQuery(f)
    expect(q.types).toEqual(['TRANSFER'])
    expect(q.fromWarehousePublicIds).toEqual(['W6'])
    expect(q.toWarehousePublicIds).toEqual(['W7'])
    expect(q).not.toHaveProperty('reasons')
    expect(q).not.toHaveProperty('direction')
    expect(transfersQuery(EMPTY_MOVEMENT_FILTERS).manualOnly).toBeUndefined()
  })

  it('"Filtros aplicados" del Reporte de ajustes con nombres (un id sin nombre se muestra tal cual)', () => {
    const t = (k: string) => (k.endsWith('.down') ? 'Bajar' : k.endsWith('.yes') ? 'Sí' : k.split('.').pop() ?? k)
    const out = describeMovementFilters(
      f,
      { warehouses: new Map([['W1', 'ALM-01 · Principal']]), owners: new Map([[OWN_OWNER, 'Propio']]), reasons: new Map() },
      t,
    )
    expect(out.map((o) => o.value)).toEqual(['typeAdjustment', '2026-09-01 – …', 'ALM-01 · Principal', 'TORN-01 · Tornillo', 'Propio', 'Bajar', 'DAMAGE', 'Sí'])
  })
})

const values: AdjustFormValues = {
  direction: 'down',
  productPublicId: 'P1',
  warehousePublicId: 'W1',
  binId: '10',
  quantity: 3,
  reason: 'DAMAGE',
  notes: '  Caja rota ',
  lotNumber: '',
  lotId: '8',
  serialText: '',
  serials: [],
}

describe('adjustmentBody (D11: la pantalla pone el signo)', () => {
  it('Bajar = cantidad negativa; nota recortada; LOT al bajar = lotId', () => {
    expect(adjustmentBody(values, 'LOT')).toEqual({
      productPublicId: 'P1',
      warehousePublicId: 'W1',
      binId: 10,
      quantity: -3,
      reason: 'DAMAGE',
      notes: 'Caja rota',
      lot: undefined,
      lotId: 8,
      serialNumbers: undefined,
    })
  })

  it('Subir un LOT = lote por número; sin rastreo no manda lote', () => {
    const up = { ...values, direction: 'up', lotNumber: ' L-9 ', reason: 'FOUND' }
    expect(adjustmentBody(up, 'LOT')).toMatchObject({ quantity: 3, lot: { number: 'L-9' }, lotId: undefined })
    expect(adjustmentBody(up, 'NONE')).toMatchObject({ quantity: 3, lot: undefined, lotId: undefined })
  })

  it('SERIAL: la cantidad es el número de series (escritas al subir, elegidas al bajar)', () => {
    expect(adjustmentBody({ ...values, direction: 'up', serialText: 'S1\nS2, S3' }, 'SERIAL')).toMatchObject({ quantity: 3, serialNumbers: ['S1', 'S2', 'S3'] })
    expect(adjustmentBody({ ...values, serials: ['S1'] }, 'SERIAL')).toMatchObject({ quantity: -1, serialNumbers: ['S1'] })
  })
})

const balances: BalanceDto[] = [
  { id: 1, binId: 10, productPublicId: 'P1', sku: 'TORN-01', productName: 'Tornillo', lotId: 7, lotNumber: 'L-7', qtyAvailable: 3, expiryDate: '2027-01-31' },
  { id: 2, binId: 10, productPublicId: 'P1', sku: 'TORN-01', productName: 'Tornillo', lotId: 8, lotNumber: 'L-8', qtyAvailable: 0 },
  { id: 3, binId: 10, productPublicId: 'P2', sku: 'TUER-02', productName: 'Tuerca', qtyAvailable: 5 },
]

describe('modales: disponible, lotes, ítems de la posición y series', () => {
  it('availableAt suma lo disponible (o el del lote)', () => {
    expect(availableAt(balances)).toBe(8)
    expect(availableAt(balances, 7)).toBe(3)
  })

  it('lotOptions: solo lotes con disponible', () => {
    expect(lotOptions(balances, (q) => `disp ${q}`)).toEqual([{ value: '7', label: 'L-7', hint: 'disp 3' }])
  })

  it('transferItemOptions: un ítem por producto y lote con disponible; la clave se lee de vuelta', () => {
    const opts = transferItemOptions(balances, { lot: (l) => `Lote ${l}`, available: (q) => `disp ${q}` })
    expect(opts).toEqual([
      { value: 'P1|7', label: 'TORN-01 · Tornillo · Lote L-7', hint: 'disp 3' },
      { value: 'P2|', label: 'TUER-02 · Tuerca', hint: 'disp 5' },
    ])
    expect(parseTransferItemKey('P1|7')).toEqual({ productPublicId: 'P1', lotId: 7 })
    expect(parseTransferItemKey('P2|')).toEqual({ productPublicId: 'P2', lotId: null })
    expect(parseTransferItemKey('')).toBeNull()
    expect(transferItemKey(balances[2])).toBe('P2|')
  })

  it('serialsAt: series de la posición (y del lote)', () => {
    const serials = [
      { serialNumber: 'S1', binId: 10, lotId: 7 },
      { serialNumber: 'S2', binId: 11, lotId: 7 },
      { serialNumber: 'S3', binId: 10, lotId: 8 },
    ]
    expect(serialsAt(serials, 10)).toEqual(['S1', 'S3'])
    expect(serialsAt(serials, 10, 7)).toEqual(['S1'])
    expect(serialsAt(serials, null)).toEqual([])
  })
})
