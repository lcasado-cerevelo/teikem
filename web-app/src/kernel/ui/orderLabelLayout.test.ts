// Lote F-G1 — motor del label (puro): normalización, plan por renglón y celda, código de barras = número del paquete, COD y paquetes
// sin número.
import { describe, expect, it } from 'vitest'
import {
  BUILT_IN_LAYOUT,
  hasVisibleBarcode,
  LABEL_KEYS,
  normalizeLayout,
  planLabel,
  printableLabels,
  sameLayout,
  sampleLabelData,
  type LabelLayout,
  type LabelTexts,
} from './orderLabelLayout'

const TEXTS: LabelTexts = { from: 'De:', to: 'A:', waybill: 'Waybill', invoice: 'Factura', cod: 'COD', packageOf: 'Paquete {i} de {n}' }
const DATA = sampleLabelData('Advance Logistics', '$125.82')

describe('normalizeLayout', () => {
  it('lo que falta o viene raro toma el integrado; «como código» solo en waybill y factura', () => {
    expect(normalizeLayout(null)).toEqual(BUILT_IN_LAYOUT)
    expect(normalizeLayout('x')).toEqual(BUILT_IN_LAYOUT)
    const l = normalizeLayout({
      cod: { on: true, pos: 'BR', size: 'LG' },
      logo: { on: false, pos: 'zz', size: 'xl' },
      consignee: { on: true, pos: 'ml', size: 'md', asBarcode: true },
      invoice: { on: true, pos: 'bl', size: 'sm', asBarcode: true },
    })
    expect(l.cod).toEqual({ on: true, pos: 'br', size: 'lg', asBarcode: false })
    expect(l.logo).toEqual({ on: false, pos: 'tl', size: 'md', asBarcode: false })
    expect(l.consignee.asBarcode).toBe(false)
    expect(l.invoice.asBarcode).toBe(true)
    expect(Object.keys(l)).toEqual([...LABEL_KEYS])
    expect(LABEL_KEYS).toHaveLength(9) // los 9 elementos de la maqueta
  })

  it('hay código de barras visible: el del paquete, o waybill/factura como código', () => {
    expect(hasVisibleBarcode(BUILT_IN_LAYOUT)).toBe(true)
    const off: LabelLayout = { ...BUILT_IN_LAYOUT, barcode: { ...BUILT_IN_LAYOUT.barcode, on: false } }
    expect(hasVisibleBarcode(off)).toBe(false)
    expect(hasVisibleBarcode({ ...off, waybill: { on: true, pos: 'bl', size: 'sm', asBarcode: true } })).toBe(true)
    expect(sameLayout(BUILT_IN_LAYOUT, normalizeLayout({}))).toBe(true)
    expect(sameLayout(BUILT_IN_LAYOUT, off)).toBe(false)
  })
})

describe('planLabel', () => {
  it('el integrado: logo arriba izq., servicio arriba der., consignatario y dirección al medio izq., barras del PAQUETE en el medio, factura y COD abajo', () => {
    const [top, mid, bottom] = planLabel(BUILT_IN_LAYOUT, DATA, TEXTS)
    expect(top.cells.l.map((x) => x.lines)).toEqual([['Advance Logistics']])
    expect(top.cells.r.map((x) => x.lines)).toEqual([['NEXT DAY']])
    expect(top.cells.c).toEqual([]) // sender apagado
    expect(mid.cells.l.map((x) => x.key)).toEqual(['consignee', 'address'])
    expect(mid.cells.l[0].lines).toEqual(['A: Farmacia Las Marías'])
    expect(mid.cells.l[1].lines).toEqual(['Carr. 2 km 14.5', 'Toa Baja, PR 00949'])
    expect(mid.barcodes).toEqual([{ key: 'barcode', size: 'md', value: 'PQT-00012', caption: 'PQT-00012 · Paquete 1 de 3', encodable: true }])
    expect(bottom.cells.l.map((x) => x.lines)).toEqual([['Factura AX-48217']])
    expect(bottom.cells.r).toEqual([{ key: 'cod', size: 'md', lines: ['COD $125.82'], bold: true, inverse: true }])
  })

  it('mover y apagar elementos cambia el plan; waybill como código de barras; sin COD (0) no hay caja', () => {
    const l: LabelLayout = {
      ...BUILT_IN_LAYOUT,
      sender: { on: true, pos: 'tc', size: 'sm', asBarcode: false },
      cod: { on: true, pos: 'tl', size: 'lg', asBarcode: false },
      waybill: { on: true, pos: 'bc', size: 'sm', asBarcode: true },
    }
    const [top, , bottom] = planLabel(l, { ...DATA, cod: 0 }, TEXTS)
    expect(top.cells.c[0].lines).toEqual(['De: Advance Solutions'])
    expect(top.cells.l.map((x) => x.key)).toEqual(['logo']) // COD 0: no se pinta
    expect(bottom.barcodes.map((b) => [b.key, b.value, b.caption])).toEqual([['waybill', 'WB-88213', 'Waybill WB-88213']])
    const [withCod] = planLabel(l, DATA, TEXTS)
    expect(withCod.cells.l.map((x) => x.key)).toEqual(['logo', 'cod'])
  })

  it('un número con caracteres fuera de Code 128 sale solo en texto; sin número de paquete no hay barras', () => {
    const [, mid] = planLabel(BUILT_IN_LAYOUT, { ...DATA, packageNumber: 'PQT-Ñ1' }, TEXTS)
    expect(mid.barcodes[0].encodable).toBe(false)
    const [, none] = planLabel(BUILT_IN_LAYOUT, { ...DATA, packageNumber: ' ' }, TEXTS)
    expect(none.barcodes).toEqual([])
  })

  it('los paquetes sin número no llevan label y se cuentan para avisar', () => {
    const r = printableLabels([{ packageNumber: 'PQT-1' }, { packageNumber: null }, { packageNumber: '  ' }, { packageNumber: 'PQT-2' }])
    expect(r.printable.map((x) => x.packageNumber)).toEqual(['PQT-1', 'PQT-2'])
    expect(r.withoutNumber).toBe(2)
  })
})
