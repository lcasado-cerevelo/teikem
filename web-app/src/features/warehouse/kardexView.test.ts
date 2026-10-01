// Lote 14 — lógica pura del Kárdex: filtros compartidos → consultas de cada endpoint, qué aplica a cada pestaña, URL,
// tonos y textos de un movimiento, origen y ruta del documento.
import { describe, expect, it } from 'vitest'
import {
  balancesQuery,
  binFilterLabel,
  discrepancyQuery,
  documentLink,
  EMPTY_INVENTORY_FILTERS,
  filterApplies,
  filtersFromUrl,
  formatSignedQty,
  fromToText,
  inactiveFilters,
  kardexQuery,
  lotSerialText,
  movementOrigin,
  movementQtyView,
  OWN_OWNER,
  ownerQuery,
  qtyClass,
  rangeInverted,
  summaryItems,
  summaryQuery,
  tabFromParam,
  txnParam,
  txnTypeTone,
  type InventoryFilterState,
} from './kardexView'

const W = 'w-1'
const P = 'p-1'
const C = 'c-1'

const filters: InventoryFilterState = {
  ...EMPTY_INVENTORY_FILTERS,
  range: { from: '2026-09-01', to: '2026-09-30' },
  types: ['ADJUSTMENT'],
  warehousePublicIds: [W],
  bins: [{ id: 10, label: 'A-01 · PCK · ALM-01' }],
  products: [{ publicId: P, sku: 'TORN-01', label: 'TORN-01 · Tornillo' }],
  categoryIds: ['7'],
  owners: [OWN_OWNER, C],
  reasons: ['DAMAGE'],
  direction: 'OUT',
  manualOnly: true,
  lotNumber: ' L-1 ',
  serialNumber: '',
  includeZero: true,
  onlyAvailable: false,
}

describe('consultas desde el estado compartido', () => {
  it('kardexQuery lleva todos los filtros del Kárdex (Dueño Propio = includeOwn; lote recortado)', () => {
    expect(kardexQuery(filters)).toEqual({
      from: '2026-09-01',
      to: '2026-09-30',
      types: ['ADJUSTMENT'],
      warehousePublicIds: [W],
      binIds: [10],
      productPublicIds: [P],
      categoryIds: [7],
      ownerClientPublicIds: [C],
      includeOwn: true,
      reasons: ['DAMAGE'],
      direction: 'OUT',
      manualOnly: true,
      lotNumber: 'L-1',
      serialNumber: undefined,
      refEntity: undefined,
      refId: undefined,
    })
    // el resumen usa exactamente los mismos filtros
    expect(summaryQuery(filters)).toEqual(kardexQuery(filters))
  })

  it('sin filtros: todo undefined (el cliente no manda parámetros vacíos)', () => {
    expect(Object.values(kardexQuery(EMPTY_INVENTORY_FILTERS)).every((v) => v === undefined)).toBe(true)
  })

  it('balancesQuery solo lleva lo que el API de saldos acepta', () => {
    expect(balancesQuery(filters)).toEqual({
      warehousePublicIds: [W],
      binIds: [10],
      productPublicIds: [P],
      categoryIds: [7],
      lotNumber: 'L-1',
      includeZero: true,
      onlyAvailable: undefined,
    })
  })

  it('discrepancyQuery: estatus (por defecto Pendiente), ubicación, producto, categoría y fechas de detección', () => {
    expect(discrepancyQuery(filters)).toEqual({
      status: ['OPEN'],
      warehousePublicIds: [W],
      binIds: [10],
      productPublicIds: [P],
      categoryIds: [7],
      from: '2026-09-01',
      to: '2026-09-30',
    })
  })

  it('ownerQuery: solo Propio / solo clientes', () => {
    expect(ownerQuery([OWN_OWNER])).toEqual({ ownerClientPublicIds: undefined, includeOwn: true })
    expect(ownerQuery([C])).toEqual({ ownerClientPublicIds: [C], includeOwn: undefined })
  })

  it('rangeInverted', () => {
    expect(rangeInverted({ from: '2026-09-30', to: '2026-09-01' })).toBe(true)
    expect(rangeInverted({ from: '2026-09-01', to: '' })).toBe(false)
  })
})

describe('qué aplica a cada pestaña', () => {
  it('Saldos no filtra por tipo, fechas, dueño, motivo ni dirección; Conciliación sí por fechas y estatus', () => {
    expect(filterApplies('balances', 'types')).toBe(false)
    expect(filterApplies('balances', 'bins')).toBe(true)
    expect(filterApplies('reconciliation', 'range')).toBe(true)
    expect(filterApplies('reconciliation', 'owners')).toBe(false)
    expect(filterApplies('kardex', 'manualOnly')).toBe(true)
  })

  it('inactiveFilters nombra solo los filtros CON valor que la pestaña no aplica', () => {
    expect(inactiveFilters('balances', filters)).toEqual(['range', 'types', 'owners', 'reasons', 'direction', 'manualOnly'])
    expect(inactiveFilters('kardex', filters)).toEqual(['includeZero'])
    expect(inactiveFilters('balances', EMPTY_INVENTORY_FILTERS)).toEqual([])
  })
})

describe('URL', () => {
  it('filtersFromUrl lee los filtros de las tres pestañas (repetibles o separados por comas)', () => {
    const f = filtersFromUrl(
      new URLSearchParams('warehousePublicIds=W1,W2&product=P1&product=P2&categoryIds=7,x&types=ADJUSTMENT&reasons=DAMAGE&direction=out&manualOnly=true&from=2026-09-01&to=mal&refEntity=cycle_count&refId=3&status=OPEN,RESOLVED'),
    )
    expect(f.warehousePublicIds).toEqual(['W1', 'W2'])
    expect(f.products.map((p) => p.publicId)).toEqual(['P1', 'P2'])
    expect(f.categoryIds).toEqual(['7'])
    expect(f.types).toEqual(['ADJUSTMENT'])
    expect(f.reasons).toEqual(['DAMAGE'])
    expect(f.direction).toBe('OUT')
    expect(f.manualOnly).toBe(true)
    expect(f.range).toEqual({ from: '2026-09-01', to: '' })
    expect(f.refEntity).toBe('CYCLE_COUNT')
    expect(f.refId).toBe(3)
    expect(f.discrepancyStatus).toEqual(['OPEN', 'RESOLVED'])
  })

  it('refId inválido descarta el documento; sin status = Pendiente', () => {
    const f = filtersFromUrl(new URLSearchParams('refEntity=RECEIPT&refId=abc'))
    expect(f.refEntity).toBe('')
    expect(f.refId).toBeNull()
    expect(f.discrepancyStatus).toEqual(['OPEN'])
  })

  it('tabFromParam y txnParam', () => {
    expect(tabFromParam('balances')).toBe('balances')
    expect(tabFromParam('kardex')).toBe('kardex')
    expect(tabFromParam('otra')).toBe('kardex')
    expect(txnParam(new URLSearchParams('txn=12'))).toBe(12)
    expect(txnParam(new URLSearchParams('txn=x'))).toBeNull()
  })
})

describe('cómo se pinta un movimiento', () => {
  it('tono por tipo (MOV_TYPES de la maqueta)', () => {
    expect(txnTypeTone('RECEIPT')).toBe('deliv')
    expect(txnTypeTone('ISSUE')).toBe('route')
    expect(txnTypeTone('TRANSFER')).toBe('disp')
    expect(txnTypeTone('ADJUSTMENT')).toBe('fail')
    expect(txnTypeTone('CROSSDOCK')).toBe('cod')
    expect(txnTypeTone('OTRO')).toBe('neutral')
  })

  it('cantidad con signo y color', () => {
    expect(formatSignedQty(2, 'es')).toBe('+2')
    expect(formatSignedQty(-1500, 'es')).toBe('−1,500')
    expect(formatSignedQty(0, 'es')).toBe('0')
    expect(qtyClass(3)).toBe('qty-in')
    expect(qtyClass(-3)).toBe('qty-out')
    expect(qtyClass(0)).toBe('qty-zero')
  })

  it('transferencia interna (signedQuantity 0 sin filtro de ubicación): se muestra lo movido, sin signo', () => {
    expect(movementQtyView({ signedQuantity: 0, quantity: 4 }, 'es')).toEqual({ text: '4', className: 'qty-zero', value: 4 })
    expect(movementQtyView({ signedQuantity: -4, quantity: 4 }, 'es')).toEqual({ text: '−4', className: 'qty-out', value: -4 })
    expect(movementQtyView({ signedQuantity: 2, quantity: 2 }, 'es')).toEqual({ text: '+2', className: 'qty-in', value: 2 })
  })

  it('de → a, lote/serie y etiqueta de una posición', () => {
    expect(fromToText({ fromWarehouseCode: 'ALM-01', fromBinCode: 'A-01', toWarehouseCode: 'ALM-01', toBinCode: 'A-02' })).toBe('ALM-01/A-01 → ALM-01/A-02')
    expect(fromToText({ toWarehouseCode: 'ALM-01', toBinCode: 'A-02' })).toBe('ALM-01/A-02')
    expect(lotSerialText({ lotNumber: 'L-1', serialNumber: 'S1' })).toBe('L-1 · S1')
    expect(binFilterLabel({ code: 'A-01', zoneCode: 'PCK', warehouseCode: 'ALM-01' })).toBe('A-01 · PCK · ALM-01')
  })

  it('origen del movimiento por su documento', () => {
    expect(movementOrigin(null)).toBe('manual')
    expect(movementOrigin('CYCLE_COUNT')).toBe('count')
    expect(movementOrigin('WAREHOUSE_TASK')).toBe('task')
    expect(movementOrigin('PRODUCT')).toBe('replenish')
    expect(movementOrigin('CROSSDOCK_ALLOCATION')).toBe('crossdock')
  })

  it('documentLink: ruta, permiso y módulo de la pantalla que abre el documento; la tarea abre su padre', () => {
    expect(documentLink({ entityCode: 'RECEIPT', id: 1, publicId: 'R' })).toEqual({ to: '/warehouse/receipts?receipt=R', perm: 'inventory.view', module: 'WMS_LOTSERIAL' })
    expect(documentLink({ entityCode: 'CYCLE_COUNT', id: 5 })?.to).toBe('/warehouse/cycle-counts?count=5')
    expect(documentLink({ entityCode: 'PURCHASE_ORDER', id: 2, publicId: 'PO' })).toEqual({ to: '/warehouse/purchase-orders/PO', perm: 'purchasing.view', module: 'PURCHASING' })
    expect(documentLink({ entityCode: 'WAREHOUSE_TASK', id: 9, parent: { entityCode: 'RECEIPT', id: 1, publicId: 'R' } })?.to).toBe('/warehouse/receipts?receipt=R')
    expect(documentLink({ entityCode: 'CROSSDOCK_ALLOCATION', id: 3 })).toBeNull()
    expect(documentLink(null)).toBeNull()
  })

  it('summaryItems: cifras del resumen; En mano y Disponible solo en Saldos (D13)', () => {
    const t = (k: string) => k.split('.').pop() ?? k
    const s = { movements: 7, inCount: 5, inQty: 12, outCount: 2, outQty: 3, internalCount: 1 }
    const items = summaryItems(s, t, 'es')
    expect(items.map((i) => [i.key, i.value])).toEqual([
      ['movements', '7'],
      ['inCount', '5'],
      ['inQty', '+12'],
      ['outCount', '2'],
      ['outQty', '−3'],
      ['internal', '1'],
    ])
    const withBalances = summaryItems(s, t, 'es', { onHand: 40, available: 35 })
    expect(withBalances.slice(-2).map((i) => [i.key, i.value])).toEqual([
      ['onHand', '40'],
      ['available', '35'],
    ])
    expect(summaryItems(undefined, t, 'es')[0].value).toBe('—')
  })
})
