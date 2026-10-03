// Lote F12 — lógica pura de la revisión del conteo por producto: estado de la lista "Por revisar", consulta, opciones de "Contó",
// qué cierra "Cerrar los que cuadran", textos de los omitidos del cierre en bloque, "solo las que fallan", evidencia de una línea
// y qué impide confirmar en la vista previa.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import type { CycleCountReviewItemDto } from './api'
import {
  adjustmentClass,
  bulkSummary,
  countTabFromParam,
  counterOptions,
  evidenceText,
  failingLines,
  idsToClose,
  keepSelection,
  lineEvidence,
  previewBlocker,
  previewByLine,
  previewLineFails,
  rememberCounters,
  reviewListQuery,
  reviewProduct,
  reviewState,
  signedQty,
  skipReasonText,
} from './countReview'

beforeAll(() => setLang('es'))

const item = (id: number, over: Partial<CycleCountReviewItemDto> = {}): CycleCountReviewItemDto => ({
  count: { id, number: `CC-0000${id}` },
  lines: 2,
  positions: 2,
  pendingLines: 0,
  differingLines: 0,
  errorLines: 0,
  movements: 0,
  correctedLines: 0,
  matches: true,
  ...over,
})

describe('estado de un conteo por revisar', () => {
  it('cuadra solo si el servidor dice matches; errores > faltan líneas > diferencia > cuadra', () => {
    expect(reviewState(item(1))).toBe('matches')
    expect(reviewState(item(1, { matches: false, differingLines: 1, movements: 1 }))).toBe('difference')
    // un movimiento de series sin "línea con diferencia" también es diferencia
    expect(reviewState(item(1, { matches: false, movements: 2 }))).toBe('difference')
    expect(reviewState(item(1, { matches: false, pendingLines: 1, differingLines: 1 }))).toBe('pending')
    expect(reviewState(item(1, { matches: false, errorLines: 1, pendingLines: 1, differingLines: 1 }))).toBe('errors')
    // sin líneas o sin causa conocida: nunca "Cuadra" si el servidor no lo dijo
    expect(reviewState(item(1, { matches: false, lines: 0 }))).toBe('pending')
    expect(reviewState(item(1, { matches: false }))).toBe('pending')
  })

  it('producto: el primero y "cuántos más"', () => {
    expect(reviewProduct({ firstProductSku: 'P-1', firstProductName: 'Tornillo', otherProducts: 2 })).toEqual({ label: 'P-1 · Tornillo', more: 2 })
    expect(reviewProduct({ firstProductSku: null, firstProductName: null, otherProducts: -1 })).toEqual({ label: '', more: 0 })
  })

  it('consulta al API: almacén, quién contó (entero), búsqueda recortada y página', () => {
    expect(reviewListQuery({ warehousePublicId: 'W', countedByUserId: '7', search: ' cc-1 ' }, 3, 25)).toEqual({
      warehousePublicId: 'W',
      countedByUserId: 7,
      search: 'cc-1',
      skip: 50,
      take: 25,
    })
    expect(reviewListQuery({ warehousePublicId: '', countedByUserId: 'x', search: '' }, 0, 50)).toEqual({
      warehousePublicId: undefined,
      countedByUserId: undefined,
      search: undefined,
      skip: 0,
      take: 50,
    })
  })

  it('"Contó": quienes aparecen en las páginas vistas más los usuarios activos, sin repetir y por nombre', () => {
    let seen: ReadonlyMap<number, string> = new Map()
    seen = rememberCounters(seen, [item(1, { countedByUserId: 9, countedByName: 'Zoe' }), item(2, { countedByUserId: null, countedByName: null })])
    const same = rememberCounters(seen, [item(3, { countedByUserId: 9, countedByName: 'Zoe' })])
    expect(same).toBe(seen)
    const opts = counterOptions(seen, [
      { id: 9, fullName: 'Zoe Otra' },
      { id: 4, fullName: 'Ana' },
      { id: 5, fullName: 'Baja', isActive: false },
    ])
    expect(opts).toEqual([
      { value: '4', label: 'Ana' },
      { value: '9', label: 'Zoe' },
    ])
  })

  it('pestaña: ?tab=review solo con warehouse.count', () => {
    expect(countTabFromParam('review', true)).toBe('review')
    expect(countTabFromParam('review', false)).toBe('counts')
    expect(countTabFromParam('otra', true)).toBe('counts')
    expect(countTabFromParam(null, true)).toBe('counts')
  })
})

describe('"Cerrar los que cuadran"', () => {
  const items = [item(1), item(2, { matches: false, differingLines: 1, movements: 1 }), item(3), item(4, { matches: false, errorLines: 1 })]

  it('sin elegir: todos los que cuadran de la página; con elegidos: solo esos (y solo si cuadran)', () => {
    expect(idsToClose(items, new Set())).toEqual([
      { id: 1, number: 'CC-00001' },
      { id: 3, number: 'CC-00003' },
    ])
    expect(idsToClose(items, new Set([3, 2]))).toEqual([{ id: 3, number: 'CC-00003' }])
    expect(idsToClose([items[1]], new Set())).toEqual([])
  })

  it('al recargar, la selección pierde lo que ya no cuadra o ya no está (misma instancia si no cambia)', () => {
    const sel = new Set([1, 3])
    expect(keepSelection(sel, items)).toBe(sel)
    expect([...keepSelection(sel, [item(1), item(3, { matches: false, differingLines: 1 })])]).toEqual([1])
  })

  it('motivos de omisión en español: cuenta con {n}, detalle del servidor en Errors/Failed y código desconocido tal cual', () => {
    expect(skipReasonText({ reasonCode: 'WouldPost', reason: 'Asentaría 2 movimiento(s); revíselo.', count: 2 }, t)).toBe('Asentaría 2 movimiento(s); revíselo.')
    expect(skipReasonText({ reasonCode: 'Pending', count: 3 }, t)).toBe('Faltan 3 línea(s) por contar.')
    expect(skipReasonText({ reasonCode: 'Stale' }, t)).toBe('La existencia cambió mientras se cerraba; revíselo.')
    expect(skipReasonText({ reasonCode: 'NotCounted' }, t)).toBe('Todavía no se termina de contar.')
    expect(skipReasonText({ reasonCode: 'AlreadyReconciled' }, t)).toBe('Ya estaba confirmado.')
    expect(skipReasonText({ reasonCode: 'NotFound' }, t)).toBe('El conteo no existe.')
    expect(skipReasonText({ reasonCode: 'NoLines' }, t)).toBe('El conteo no tiene líneas.')
    const reserved = 'El conteo de P-1 en A-01 (1) es menor que lo reservado (2); libere la reserva antes de reconciliar.'
    expect(skipReasonText({ reasonCode: 'Errors', reason: reserved, count: 1 }, t)).toBe(`Tiene 1 línea(s) con error: ${reserved}`)
    expect(skipReasonText({ reasonCode: 'Failed', reason: 'El conteo fue modificado por otro usuario.' }, t)).toBe('No se pudo cerrar: El conteo fue modificado por otro usuario.')
    expect(skipReasonText({ reasonCode: 'Nuevo', reason: 'Motivo nuevo del servidor.' }, t)).toBe('Motivo nuevo del servidor.')
  })

  it('resumen: cerrados y omitidos con su texto; examined y truncated', () => {
    const s = bulkSummary(
      {
        examined: 3,
        closed: [{ id: 1, number: 'CC-00001', statusCode: 'RECONCILED', lines: 2 }],
        skipped: [
          { id: 2, number: 'CC-00002', reasonCode: 'WouldPost', reason: 'x', count: 1 },
          { id: 99, number: null, reasonCode: 'NotFound', reason: 'El conteo no existe.' },
        ],
        truncated: true,
      },
      t,
    )
    expect(s.closed).toEqual([{ id: 1, number: 'CC-00001', lines: 2 }])
    expect(s.skipped).toEqual([
      { id: 2, number: 'CC-00002', code: 'WouldPost', text: 'Asentaría 1 movimiento(s); revíselo.' },
      { id: 99, number: '#99', code: 'NotFound', text: 'El conteo no existe.' },
    ])
    expect(s.examined).toBe(3)
    expect(s.truncated).toBe(true)
  })
})

describe('detalle para revisar', () => {
  const lines = [{ id: 1 }, { id: 2 }, { id: 3 }, { id: 4 }]
  const preview = previewByLine({
    lines: [
      { lineId: 1, adjustmentQty: 0, movements: 0, error: null },
      { lineId: 2, adjustmentQty: -1, movements: 1, error: null },
      { lineId: 3, adjustmentQty: 0, movements: 0, error: 'El conteo de P en A (1) es menor que lo reservado (2); libere la reserva antes de reconciliar.' },
    ],
  })

  it('una línea falla si asentaría algo o tiene error; pendiente no es falla', () => {
    expect(previewLineFails({ adjustmentQty: 0, movements: 0, error: null })).toBe(false)
    expect(previewLineFails({ adjustmentQty: 2, movements: 1 })).toBe(true)
    expect(previewLineFails({ adjustmentQty: 0, movements: 2 })).toBe(true)
    expect(previewLineFails({ adjustmentQty: 0, error: 'x' })).toBe(true)
  })

  it('"solo las que fallan": con la vista previa manda ella; sin ella, la varianza contra la foto; las fijadas se quedan', () => {
    const variance = (l: { id: number }) => (l.id === 4 ? 3 : 0)
    expect(failingLines(lines, preview, variance).map((l) => l.id)).toEqual([2, 3, 4])
    expect(failingLines(lines, null, variance).map((l) => l.id)).toEqual([4])
    expect(failingLines(lines, preview, variance, new Set([1])).map((l) => l.id)).toEqual([1, 2, 3, 4])
    expect(failingLines(lines, null, () => null).map((l) => l.id)).toEqual([])
  })

  it('evidencia: Contó X (quién, cuándo) · Corregido a Y (quién, cuándo); sin captura, nada', () => {
    expect(lineEvidence({ countedQty: null })).toEqual({ counted: null, corrected: null })
    const plain = lineEvidence({ countedQty: 3, capturedQty: 3, capturedByName: 'Ana', capturedAtUtc: '2026-10-02T13:30:00' })
    expect(plain).toEqual({ counted: { qty: 3, by: 'Ana', at: '2026-10-02T13:30:00' }, corrected: null })
    const fixed = lineEvidence({
      countedQty: 5,
      capturedQty: 3,
      capturedByName: 'Ana',
      capturedAtUtc: '2026-10-02T13:30:00',
      correctedByName: 'Beto',
      correctedAtUtc: '2026-10-02T15:00:00',
      wasCorrected: true,
    })
    expect(fixed.corrected).toEqual({ qty: 5, by: 'Beto', at: '2026-10-02T15:00:00' })
    const text = evidenceText(fixed, t, (n) => String(n), (iso) => (iso ?? '').slice(11, 16))
    expect(text).toBe('Contó 3 (Ana, 13:30) · Corregido a 5 (Beto, 15:00)')
    // sin nombre ni fecha (series sin capturedQty): sin paréntesis y "—" si no hay cantidad
    expect(evidenceText(lineEvidence({ countedQty: 1, wasCorrected: true, capturedQty: null, capturedAtUtc: null, capturedByName: 'Ana' }), t, String, () => '')).toBe(
      'Contó — (Ana) · Corregido a 1',
    )
  })

  it('vista previa: qué impide confirmar, en el orden del servidor', () => {
    expect(previewBlocker(null)).toBeNull()
    expect(previewBlocker({ blockingError: 'La serie S1 está en dos líneas.', totals: { errorLines: 1 } })).toEqual({ key: 'blocking', message: 'La serie S1 está en dos líneas.' })
    expect(previewBlocker({ totals: { lines: 2, errorLines: 1, pendingLines: 1 } })).toEqual({ key: 'errors', params: { n: 1 } })
    expect(previewBlocker({ totals: { lines: 2, pendingLines: 1 } })).toEqual({ key: 'pending', params: { n: 1 } })
    expect(previewBlocker({ totals: { lines: 0 } })).toEqual({ key: 'noLines' })
    expect(previewBlocker({ totals: { lines: 2, movements: 1 } })).toBeNull()
  })

  it('ajuste con signo y color de semáforo', () => {
    const num = (n: number) => String(n)
    expect(signedQty(2, num)).toBe('+2')
    expect(signedQty(-1.5, num)).toBe('-1.5')
    expect(signedQty(0, num)).toBe('0')
    expect(signedQty(null, num)).toBe('—')
    expect(adjustmentClass(2)).toBe('qty-in')
    expect(adjustmentClass(-1)).toBe('qty-out')
    expect(adjustmentClass(0)).toBe('qty-zero')
  })
})
