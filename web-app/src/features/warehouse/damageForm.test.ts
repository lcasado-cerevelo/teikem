import { describe, expect, it } from 'vitest'
import { damageBody, defaultCause, type DamageFormValues } from './damageForm'

const BASE: DamageFormValues = {
  origin: 'WAREHOUSE',
  warehousePublicId: 'wh-1',
  receiptPublicId: '',
  productPublicId: 'p-1',
  fromBinId: '12',
  lotId: '',
  lotNumber: '',
  quantity: 4,
  cause: 'WAREHOUSE_ACCIDENT',
  disposition: 'QUARANTINE',
  notes: '  se cayó  ',
}

describe('damageForm', () => {
  it('propone la causa según el origen', () => {
    expect(defaultCause('RECEIPT')).toBe('ARRIVED_DAMAGED')
    expect(defaultCause('WAREHOUSE')).toBe('WAREHOUSE_ACCIDENT')
  })

  it('un daño del almacén manda la posición de origen y no el recibo', () => {
    expect(damageBody({ ...BASE, receiptPublicId: 'r-1' }, 'NONE')).toEqual({
      origin: 'WAREHOUSE',
      warehousePublicId: 'wh-1',
      productPublicId: 'p-1',
      receiptPublicId: null,
      fromBinId: 12,
      quantity: 4,
      cause: 'WAREHOUSE_ACCIDENT',
      disposition: 'QUARANTINE',
      lotId: null,
      lot: null,
      notes: 'se cayó',
    })
  })

  it('un daño de recibo manda el recibo y no la posición', () => {
    const body = damageBody({ ...BASE, origin: 'RECEIPT', receiptPublicId: 'r-1', cause: 'ARRIVED_DAMAGED', disposition: 'DISCARD', notes: '' }, 'NONE')
    expect(body).toMatchObject({ origin: 'RECEIPT', receiptPublicId: 'r-1', fromBinId: null, disposition: 'DISCARD', notes: null })
  })

  it('con lote usa el lote elegido; un lote nuevo solo vale al recibir lo dañado', () => {
    expect(damageBody({ ...BASE, lotId: '7' }, 'LOT')).toMatchObject({ lotId: 7, lot: null })
    expect(damageBody({ ...BASE, origin: 'RECEIPT', receiptPublicId: 'r-1', lotNumber: ' L-9 ' }, 'LOT')).toMatchObject({ lotId: null, lot: { number: 'L-9' } })
    expect(damageBody({ ...BASE, lotNumber: 'L-9' }, 'LOT')).toMatchObject({ lotId: null, lot: null })
    expect(damageBody({ ...BASE, lotId: '7' }, 'NONE')).toMatchObject({ lotId: null, lot: null })
  })
})
