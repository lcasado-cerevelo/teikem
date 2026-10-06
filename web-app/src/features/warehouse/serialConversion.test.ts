// Lote F17 (Rentas F-R1) — "Convertir a serie": posiciones y bloqueos de los saldos, validación de lo capturado con los
// mensajes exactos del servidor (manual 06 §2.1) y el cuerpo de la solicitud.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { canConvertToSerial, conversionBody, conversionIssues, conversionPlan } from './serialConversion'

const es = (key: string, params?: Record<string, string | number>) => translate('es', `warehouse.convertSerial.errors.${key}`, params)
beforeAll(() => setLang('es'))

const bal = (binId: number | null, binCode: string | null, qtyOnHand: number, extra: Record<string, unknown> = {}) => ({
  binId,
  binCode,
  warehouseCode: 'ALM-01',
  zoneCode: 'RSV',
  qtyOnHand,
  qtyReserved: 0,
  ...extra,
})

describe('conversionPlan', () => {
  it('una posición por bin (ordenadas por código) con sus unidades en mano; sin existencia no cuenta', () => {
    const { positions, blockers } = conversionPlan('EQ-1', [bal(5, 'B-02', 1), bal(4, 'A-01', 2), bal(6, 'C-03', 0)])
    expect(blockers).toEqual([])
    expect(positions.map((p) => [p.binId, p.binCode, p.onHand])).toEqual([
      [4, 'A-01', 2],
      [5, 'B-02', 1],
    ])
  })

  it('bloqueos: reservado (409), con lote o sin posición (422), fraccionaria (422)', () => {
    const { blockers } = conversionPlan('EQ-1', [bal(4, 'A-01', 1.5, { qtyReserved: 1 }), bal(5, 'B-02', 1, { lotId: 9, lotNumber: 'L1' }), bal(null, null, 2)])
    expect(blockers.map((b) => es(b.code, b.params))).toEqual([
      'El producto EQ-1 tiene unidades reservadas; libérelas antes de convertirlo.',
      'La existencia de EQ-1 en B-02 no está en una posición sin lote; muévala o ajústela antes de convertirlo.',
      'La existencia de EQ-1 en ALM-01 no está en una posición sin lote; muévala o ajústela antes de convertirlo.',
      'La existencia de EQ-1 en A-01 es 1.5; ajústela a unidades enteras antes de convertirlo.',
    ])
  })
})

describe('conversionIssues', () => {
  const positions = conversionPlan('EQ-1', [bal(4, 'A-01', 2), bal(5, 'B-02', 1)]).positions

  it('conteo por posición con el mensaje del servidor ({n} en mano, {m} capturadas)', () => {
    const r = conversionIssues(positions, { 4: 'SN-1', 5: 'SN-3' })
    expect(r.total).toBe(1)
    expect(es(r.first!.code, r.first!.params)).toBe('Capture 2 número(s) de serie para A-01 (hay 1).')
    expect(r.byBin[5]).toBeUndefined()
    expect(r.captured).toBe(2)
  })

  it('pegar varias líneas (o con comas); repetidas entre posiciones sin distinguir mayúsculas; largas', () => {
    expect(conversionIssues(positions, { 4: 'SN-1\r\nSN-2\n', 5: 'SN-3' }).total).toBe(0)
    expect(conversionIssues(positions, { 4: 'SN-1, SN-2', 5: 'SN-3' }).total).toBe(0)
    const dup = conversionIssues(positions, { 4: 'SN-1\nSN-2', 5: 'sn-2' })
    expect(dup.byBin[5]!.map((i) => es(i.code, i.params))).toEqual(['El número de serie sn-2 está repetido.'])
    const long = 'X'.repeat(81)
    const r = conversionIssues(positions, { 4: `${long}\nSN-2`, 5: 'SN-3' })
    expect(es(r.first!.code, r.first!.params)).toBe(`El número de serie ${long} excede 80 caracteres.`)
  })

  it('una posición sin capturar también falla (hay 0)', () => {
    const r = conversionIssues(positions, { 4: 'SN-1\nSN-2' })
    expect(es(r.first!.code, r.first!.params)).toBe('Capture 1 número(s) de serie para B-02 (hay 0).')
  })
})

describe('cuerpo y botón', () => {
  it('una posición por renglón con sus series; nota vacía → null', () => {
    const positions = conversionPlan('EQ-1', [bal(4, 'A-01', 2), bal(5, 'B-02', 1)]).positions
    expect(conversionBody(positions, { 4: 'SN-1\nSN-2', 5: ' SN-3 ' }, '  ', 'AAA=')).toEqual({
      positions: [
        { binId: 4, serialNumbers: ['SN-1', 'SN-2'] },
        { binId: 5, serialNumbers: ['SN-3'] },
      ],
      notes: null,
      rowVersion: 'AAA=',
    })
  })

  it('el botón aplica a productos sin seguimiento con existencia', () => {
    expect(canConvertToSerial({ trackingTypeCode: 'NONE', qtyOnHand: 3 })).toBe(true)
    expect(canConvertToSerial({ trackingTypeCode: 'NONE', qtyOnHand: 0 })).toBe(false)
    expect(canConvertToSerial({ trackingTypeCode: 'SERIAL', qtyOnHand: 3 })).toBe(false)
    expect(canConvertToSerial({ trackingTypeCode: 'LOT', qtyOnHand: 3 })).toBe(false)
    expect(canConvertToSerial(null)).toBe(false)
  })
})
