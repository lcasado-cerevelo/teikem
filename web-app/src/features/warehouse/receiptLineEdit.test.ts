// Lote 13 — rejilla de líneas del recibo: copia recibido → esperado mientras se teclea (solo sin documento y si el esperado
// estaba vacío o en 0), fila vacía al final, qué se manda al API por fila, puesta al día con el servidor y motivos para no
// confirmar. Lote 16: posición destino de un recibo directo (se manda al guardar, bloqueo 'noTarget').
import { describe, expect, it } from 'vitest'
import type { components } from '../../kernel/api/schema'
import {
  confirmBlockers,
  emptyRow,
  ensureTrailingEmpty,
  isDirty,
  isEmptyRow,
  linePayload,
  mergeSaved,
  missingTargets,
  newLineFrom,
  onExpectedInput,
  onProductPicked,
  onReceivedInput,
  onTargetPicked,
  parseQtyText,
  reconcileRows,
  rowErrorsFromProblem,
  rowFromLine,
  rowNeedsTarget,
  rowsFromDetail,
  rowVariance,
  MAX_RECEIPT_LINES,
} from './receiptLineEdit'

type LineDto = components['schemas']['ReceiptLineDto']

let seq = 0
const key = () => `k${++seq}`
const line = (over: Partial<LineDto> = {}): LineDto => ({
  id: 1,
  asnLineId: null,
  productPublicId: 'P1',
  sku: 'A-1',
  productName: 'Tornillo',
  trackingTypeCode: 'NONE',
  expectedQty: null,
  receivedQty: 5,
  varianceQty: 0,
  serialNumbers: [],
  allocatedToCrossDock: 0,
  ...over,
})
const PRODUCT = { publicId: 'P2', sku: 'B-2', name: 'Tuerca', trackingTypeCode: 'NONE' }

describe('copia recibido → esperado mientras se teclea', () => {
  it('fila nueva (esperado vacío): el esperado sigue a lo recibido tecla por tecla', () => {
    let r = onProductPicked(emptyRow('n'), PRODUCT)
    r = onReceivedInput(r, '1')
    expect(r.expected).toBe('1')
    r = onReceivedInput(r, '12')
    expect(r).toMatchObject({ received: '12', expected: '12' })
  })

  it('esperado en 0 también copia; con esperado distinto de 0 no', () => {
    const zero = rowFromLine(line({ expectedQty: 0, receivedQty: 0 }))
    expect(zero.mirror).toBe(true)
    expect(onReceivedInput(zero, '7').expected).toBe('7')
    const ten = rowFromLine(line({ expectedQty: 10, receivedQty: 10 }))
    expect(ten.mirror).toBe(false)
    expect(onReceivedInput(ten, '8')).toMatchObject({ received: '8', expected: '10' })
  })

  it('teclear el esperado recalcula si copia: con un valor deja de copiar; vacío o 0 vuelve a copiar', () => {
    let r = onExpectedInput(emptyRow('n'), '5')
    expect(r.mirror).toBe(false)
    expect(onReceivedInput(r, '3')).toMatchObject({ received: '3', expected: '5' })
    r = onExpectedInput(r, '')
    expect(r.mirror).toBe(true)
    r = onExpectedInput(r, '0')
    expect(onReceivedInput(r, '4').expected).toBe('4')
  })

  it('con documento (aviso u OC) nunca copia: el esperado es del documento', () => {
    const extra = rowFromLine(line({ expectedQty: null }))
    expect(onReceivedInput(extra, '9', false)).toMatchObject({ received: '9', expected: '' })
  })
})

describe('filas', () => {
  it('rowsFromDetail: líneas por id y, sin documento y editable, una fila vacía al final', () => {
    const rows = rowsFromDetail([line({ id: 2 }), line({ id: 1 })], { manual: true, editable: true }, key)
    expect(rows.map((r) => r.lineId)).toEqual([1, 2, null])
    expect(isEmptyRow(rows[2])).toBe(true)
    expect(rowsFromDetail([line()], { manual: false, editable: true }, key)).toHaveLength(1)
    expect(rowsFromDetail([line()], { manual: true, editable: false }, key)).toHaveLength(1)
  })

  it('ensureTrailingEmpty: agrega una fila vacía si la última ya tiene algo; no pasa de 200', () => {
    const one = [onProductPicked(emptyRow('a'), PRODUCT)]
    expect(ensureTrailingEmpty(one, key)).toHaveLength(2)
    const withEmpty = [emptyRow('a')]
    expect(ensureTrailingEmpty(withEmpty, key)).toHaveLength(1)
    const full = Array.from({ length: MAX_RECEIPT_LINES }, (_, i) => rowFromLine(line({ id: i + 1 })))
    expect(ensureTrailingEmpty(full, key)).toHaveLength(MAX_RECEIPT_LINES)
  })

  it('parseQtyText: vacío = null, coma decimal, texto inválido = NaN', () => {
    expect(parseQtyText('')).toBeNull()
    expect(parseQtyText(' 2,5 ')).toBe(2.5)
    expect(parseQtyText('abc')).toBeNaN()
  })

  it('rowVariance como el servidor: ciego sin esperado = 0; extra de un documento espera 0', () => {
    expect(rowVariance(rowFromLine(line({ expectedQty: null, receivedQty: 5 })), true)).toBe(0)
    expect(rowVariance(rowFromLine(line({ expectedQty: null, receivedQty: 5 })), false)).toBe(5)
    expect(rowVariance(rowFromLine(line({ expectedQty: 10, receivedQty: 7 })), true)).toBe(-3)
    expect(rowVariance(emptyRow('n'), true)).toBeNull()
  })
})

describe('linePayload', () => {
  it('fila nueva: POST con producto, recibido y esperado (sin documento); incompleta sin producto o sin recibido', () => {
    const r = onReceivedInput(onProductPicked(emptyRow('n'), PRODUCT), '12')
    expect(linePayload(r, true)).toEqual({ kind: 'add', body: { productPublicId: 'P2', receivedQty: 12, expectedQty: 12 } })
    expect(linePayload(onProductPicked(emptyRow('n'), PRODUCT), true)).toEqual({ kind: 'incomplete' })
    expect(linePayload(onReceivedInput(emptyRow('n'), '3'), true)).toEqual({ kind: 'incomplete' })
    expect(linePayload(emptyRow('n'), true)).toEqual({ kind: 'none' })
  })

  it('fila guardada: PUT solo con lo que cambió; esperado vacío = clearExpected; producto nuevo', () => {
    const saved = rowFromLine(line({ expectedQty: 10, receivedQty: 10 }))
    expect(linePayload(saved, true)).toEqual({ kind: 'none' })
    expect(linePayload(onReceivedInput(saved, '8'), true)).toEqual({ kind: 'update', lineId: 1, body: { receivedQty: 8 } })
    expect(linePayload(onExpectedInput(saved, ''), true)).toEqual({ kind: 'update', lineId: 1, body: { clearExpected: true } })
    expect(linePayload(onProductPicked(saved, PRODUCT), true)).toEqual({ kind: 'update', lineId: 1, body: { productPublicId: 'P2' } })
  })

  it('con documento solo se manda lo recibido (nunca esperado ni producto)', () => {
    const docLine = rowFromLine(line({ asnLineId: 7, expectedQty: 10, receivedQty: 10 }))
    const edited = { ...onReceivedInput(docLine, '9', false), expected: '99' }
    expect(linePayload(edited, false)).toEqual({ kind: 'update', lineId: 1, body: { receivedQty: 9 } })
  })

  it('valores inválidos no se mandan: mensajes del manual (claves i18n)', () => {
    const saved = rowFromLine(line({ expectedQty: 1, receivedQty: 1 }))
    expect(linePayload(onReceivedInput(saved, ''), true)).toEqual({ kind: 'invalid', issues: { received: { key: 'warehouse.lineRules.receivedQtyRequired' } } })
    expect(linePayload(onReceivedInput(saved, '-1'), true)).toMatchObject({ kind: 'invalid', issues: { received: { key: 'warehouse.lineRules.receivedQtyNegative' } } })
    expect(linePayload(onExpectedInput(saved, '-2'), true)).toMatchObject({ kind: 'invalid', issues: { expected: { key: 'warehouse.receipts.errors.expectedQtyNegative' } } })
    expect(linePayload(onReceivedInput(saved, '1.2345'), true)).toMatchObject({ kind: 'invalid', issues: { received: { key: 'warehouse.lineRules.qtyDecimals' } } })
    expect(linePayload(onReceivedInput(saved, 'x'), true)).toMatchObject({ kind: 'invalid', issues: { received: { key: 'warehouse.receipts.lines.invalidNumber' } } })
    expect(linePayload(onProductPicked(saved, null), true)).toMatchObject({ kind: 'invalid', issues: { product: { key: 'warehouse.receipts.errors.productRequired' } } })
  })
})

describe('guardado y puesta al día', () => {
  it('newLineFrom: la línea nueva es la de id más alto que no estaba', () => {
    expect(newLineFrom([line({ id: 1 }), line({ id: 5 }), line({ id: 3 })], new Set([1]))?.id).toBe(5)
    expect(newLineFrom([line({ id: 1 })], new Set([1]))).toBeNull()
  })

  it('mergeSaved: sin teclear más queda como el servidor (y vuelve a decidir si copia); si se siguió tecleando conserva lo tecleado', () => {
    const sent = onReceivedInput(onProductPicked(emptyRow('n'), PRODUCT), '4')
    const server = line({ id: 9, productPublicId: 'P2', expectedQty: 4, receivedQty: 4 })
    const merged = mergeSaved(sent, sent, server)
    expect(merged).toMatchObject({ key: 'n', lineId: 9, expected: '4', received: '4', mirror: false })
    expect(isDirty(merged)).toBe(false)
    const typedMore = onReceivedInput(sent, '45')
    const kept = mergeSaved(typedMore, sent, server)
    expect(kept).toMatchObject({ key: 'n', lineId: 9, received: '45' })
    expect(isDirty(kept)).toBe(true)
  })

  it('reconcileRows: toma lo guardado en filas sin cambios, conserva las tecleadas, quita las borradas y agrega las nuevas antes de la fila vacía', () => {
    const rows = rowsFromDetail([line({ id: 1 }), line({ id: 2 }), line({ id: 3 })], { manual: true, editable: true }, key)
    const typed = rows.map((r) => (r.lineId === 2 ? onReceivedInput(r, '99') : r))
    const out = reconcileRows(typed, [line({ id: 1, receivedQty: 6 }), line({ id: 2 }), line({ id: 4 })], { manual: true, editable: true }, key)
    expect(out.map((r) => r.lineId)).toEqual([1, 2, 4, null])
    expect(out[0].received).toBe('6')
    expect(out[1].received).toBe('99')
    expect(out[0].key).toBe(rows[0].key)
  })

  it('reconcileRows: mientras viaja un alta no agrega la línea nueva (la tomará la fila que la mandó)', () => {
    const pending = { ...onReceivedInput(onProductPicked(emptyRow('n'), PRODUCT), '2'), saving: true }
    const out = reconcileRows([pending], [line({ id: 7 })], { manual: true, editable: true }, key)
    expect(out.map((r) => r.lineId)).toEqual([null, null])
    expect(out[0].key).toBe('n')
  })

  it('reconcileRows: confirmado (no editable) quita la fila vacía', () => {
    const rows = rowsFromDetail([line()], { manual: true, editable: true }, key)
    expect(reconcileRows(rows, [line()], { manual: true, editable: false }, key)).toHaveLength(1)
  })

  it('rowErrorsFromProblem: cada error del API bajo su campo; sin campo, el título', () => {
    expect(
      rowErrorsFromProblem({
        title: 'Datos inválidos',
        errors: { 'line.receivedQty': ['La cantidad recibida no puede ser negativa.'], expectedQty: ['La cantidad esperada no puede ser negativa.'] },
      }),
    ).toEqual({ received: 'La cantidad recibida no puede ser negativa.', expected: 'La cantidad esperada no puede ser negativa.' })
    expect(rowErrorsFromProblem({ title: 'El recibo REC-1 ya fue confirmado; no se puede modificar.', errors: {} })).toEqual({
      row: 'El recibo REC-1 ya fue confirmado; no se puede modificar.',
    })
    expect(rowErrorsFromProblem({ title: 'x', errors: { line: ['El producto A-1 no se controla por lote; no indique lote.'] } })).toEqual({
      row: 'El producto A-1 no se controla por lote; no indique lote.',
    })
  })
})

describe('confirmBlockers', () => {
  const saved = rowFromLine(line())
  it('confirmado, sin líneas, guardando, sin guardar o con error', () => {
    expect(confirmBlockers([saved], false)).toBe('confirmed')
    expect(confirmBlockers([emptyRow('n')], true)).toBe('noLines')
    expect(confirmBlockers([{ ...saved, saving: true }], true)).toBe('saving')
    expect(confirmBlockers([onReceivedInput(saved, '9')], true)).toBe('unsaved')
    expect(confirmBlockers([{ ...saved, errors: { row: 'x' } }], true)).toBe('errors')
    expect(confirmBlockers([saved, emptyRow('n')], true)).toBeNull()
  })

  it('con documento un esperado distinto no cuenta como "sin guardar"', () => {
    const doc = { ...rowFromLine(line({ asnLineId: 3, expectedQty: 5, receivedQty: 5 })), expected: '6' }
    expect(confirmBlockers([doc], true, false)).toBeNull()
  })
})

describe('Lote 16: posición destino (recibo directo a posición)', () => {
  const BIN = { id: 40, code: 'RSV-A-01', zoneTypeCode: 'RESERVE' }

  it('la fila toma el destino del servidor (código, tipo de zona y espacio libre) y lo guarda como original', () => {
    const r = rowFromLine(line({ targetBinId: 40, targetBinCode: 'RSV-A-01', targetZoneTypeCode: 'RESERVE', targetFreeQty: 3 }))
    expect(r).toMatchObject({ targetBinId: 40, targetBinCode: 'RSV-A-01', targetZoneTypeCode: 'RESERVE', targetFreeQty: 3 })
    expect(r.original?.targetBinId).toBe(40)
    expect(isDirty(r)).toBe(false)
  })

  it('elegir el destino ensucia la fila y el PUT manda targetBinId; quitarlo manda clearTargetBin', () => {
    const saved = rowFromLine(line())
    const picked = onTargetPicked({ ...saved, errors: { target: 'x', row: 'y' } }, BIN)
    expect(picked).toMatchObject({ targetBinId: 40, targetBinCode: 'RSV-A-01', targetFreeQty: null, errors: {} })
    expect(isDirty(picked)).toBe(true)
    expect(linePayload(picked, true)).toEqual({ kind: 'update', lineId: 1, body: { targetBinId: 40 } })

    const withTarget = rowFromLine(line({ targetBinId: 40, targetBinCode: 'RSV-A-01' }))
    expect(linePayload(onTargetPicked(withTarget, null), true)).toEqual({ kind: 'update', lineId: 1, body: { clearTargetBin: true } })
  })

  it('fila nueva: el destino elegido va en su POST cuando está completa', () => {
    let r = onProductPicked(emptyRow('n'), PRODUCT)
    r = onTargetPicked(r, BIN)
    expect(linePayload(r, true)).toEqual({ kind: 'incomplete' })
    r = onReceivedInput(r, '8')
    expect(linePayload(r, true)).toEqual({ kind: 'add', body: { productPublicId: 'P2', receivedQty: 8, expectedQty: 8, targetBinId: 40 } })
  })

  it('mergeSaved: si se cambió el destino mientras viajaba, se conserva lo elegido (queda sin guardar)', () => {
    const saved = rowFromLine(line())
    const sent = onTargetPicked(saved, BIN)
    const current = onTargetPicked(sent, { id: 41, code: 'RSV-A-02' })
    const merged = mergeSaved(current, sent, line({ targetBinId: 40, targetBinCode: 'RSV-A-01' }))
    expect(merged.targetBinId).toBe(41)
    expect(merged.original?.targetBinId).toBe(40)
    expect(isDirty(merged)).toBe(true)
    // sin cambios mientras viajaba: queda como la devolvió el servidor
    expect(mergeSaved(sent, sent, line({ targetBinId: 40, targetBinCode: 'RSV-A-01', targetFreeQty: 2 })).targetFreeQty).toBe(2)
  })

  it('reconcileRows: una fila con cantidad tecleada recibe el destino que puso el servidor ("Usar posiciones sugeridas")', () => {
    const typing = onReceivedInput(rowFromLine(line()), '9')
    const [out] = reconcileRows([typing], [line({ targetBinId: 40, targetBinCode: 'RSV-A-01', targetFreeQty: 1 })], { manual: false, editable: true }, key)
    expect(out).toMatchObject({ received: '9', targetBinId: 40, targetBinCode: 'RSV-A-01', targetFreeQty: 1 })
    expect(out.original?.targetBinId).toBe(40)
    // si el destino se estaba cambiando en la fila, se conserva el de la fila
    const changing = onTargetPicked(rowFromLine(line()), { id: 41, code: 'RSV-A-02' })
    const [kept] = reconcileRows([changing], [line({ targetBinId: 40, targetBinCode: 'RSV-A-01' })], { manual: false, editable: true }, key)
    expect(kept.targetBinId).toBe(41)
  })

  it('errores del API de la posición destino van bajo su campo', () => {
    expect(
      rowErrorsFromProblem({ title: 'x', errors: { targetBinId: ['La posición X-01 está en una zona CROSSDOCK; la posición destino debe ser de guardado.'] } }),
    ).toEqual({ target: 'La posición X-01 está en una zona CROSSDOCK; la posición destino debe ser de guardado.' })
    expect(rowErrorsFromProblem({ title: 'x', errors: { 'line.targetBinCode': ['La posición ZZ no existe en el almacén del recibo.'] } })).toEqual({
      target: 'La posición ZZ no existe en el almacén del recibo.',
    })
  })

  it('qué líneas necesitan destino: recibido > 0, salvo lote sin lote o con cruce de muelle; las nuevas no cuentan', () => {
    expect(rowNeedsTarget(rowFromLine(line({ receivedQty: 5 })))).toBe(true)
    expect(rowNeedsTarget(rowFromLine(line({ receivedQty: 0 })))).toBe(false)
    expect(rowNeedsTarget(rowFromLine(line({ trackingTypeCode: 'LOT', lotNumber: null })))).toBe(false)
    expect(rowNeedsTarget(rowFromLine(line({ trackingTypeCode: 'LOT', lotNumber: 'L-1' })))).toBe(true)
    expect(rowNeedsTarget(rowFromLine(line({ allocatedToCrossDock: 2 })))).toBe(false)
    expect(rowNeedsTarget(onReceivedInput(onProductPicked(emptyRow('n'), PRODUCT), '3'))).toBe(false)
    const rows = [rowFromLine(line({ id: 1 })), rowFromLine(line({ id: 2, targetBinId: 40 })), rowFromLine(line({ id: 3, receivedQty: 0 }))]
    expect(missingTargets(rows)).toBe(1)
  })

  it("confirmBlockers: en directo, sin destino → 'noTarget' (después de los demás motivos); con acomodo no aplica", () => {
    const noTarget = rowFromLine(line())
    expect(confirmBlockers([noTarget], true, true, true)).toBe('noTarget')
    expect(confirmBlockers([noTarget], true, true, false)).toBeNull()
    expect(confirmBlockers([{ ...noTarget, errors: { row: 'x' } }], true, true, true)).toBe('errors')
    expect(confirmBlockers([rowFromLine(line({ targetBinId: 40 }))], true, true, true)).toBeNull()
    expect(confirmBlockers([noTarget], false, true, true)).toBe('confirmed')
  })
})
