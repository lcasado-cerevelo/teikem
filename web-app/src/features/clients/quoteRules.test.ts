// F-A3 — reglas puras del Cotizador: esquema (espejo de RateService.QuoteAsync), request, renombrado de errores y desglose.
import { describe, expect, it } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import { buildQuoteRequest, emptyQuote, linesWithoutRate, lineSubtotal, quoteSchema, remapQuoteErrors, rethrowQuoteError, sourceOf, type QuoteValues } from './quoteRules'

const M = {
  serviceRequired: 'El tipo de servicio es obligatorio.',
  packageRequired: 'El tipo de paquete es obligatorio.',
  piecesMin: 'La cantidad de piezas debe ser al menos 1.',
  piecesInt: 'Enteras.',
  codMin: 'El monto COD no puede ser negativo.',
  repeated: 'El par servicio/tipo de paquete está repetido.',
}
const issues = (v: QuoteValues) => {
  const r = quoteSchema(M).safeParse(v)
  return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`)
}
const line = (serviceType = 'STANDARD', packageType = 'BOX', pieces: number | null = 3) => ({ serviceType, packageType, pieces })

describe('quoteSchema', () => {
  it('una línea completa es válida; la línea vacía pide servicio y paquete', () => {
    expect(issues({ lines: [line()], codAmount: null, asOf: '' })).toEqual([])
    expect(issues(emptyQuote())).toEqual(['lines.0.serviceType: El tipo de servicio es obligatorio.', 'lines.0.packageType: El tipo de paquete es obligatorio.'])
  })
  it('piezas: al menos 1 y enteras; vacías también se rechazan', () => {
    expect(issues({ lines: [line('STANDARD', 'BOX', 0)], codAmount: null, asOf: '' })).toEqual(['lines.0.pieces: La cantidad de piezas debe ser al menos 1.'])
    expect(issues({ lines: [line('STANDARD', 'BOX', 1.5)], codAmount: null, asOf: '' })).toEqual(['lines.0.pieces: Enteras.'])
    expect(issues({ lines: [line('STANDARD', 'BOX', null)], codAmount: null, asOf: '' })).toEqual(['lines.0.pieces: La cantidad de piezas debe ser al menos 1.'])
  })
  it('el COD no puede ser negativo (0 sí)', () => {
    expect(issues({ lines: [line()], codAmount: -1, asOf: '' })).toEqual(['codAmount: El monto COD no puede ser negativo.'])
    expect(issues({ lines: [line()], codAmount: 0, asOf: '' })).toEqual([])
  })
  it('un par servicio/paquete repetido se marca en la segunda línea', () => {
    expect(issues({ lines: [line(), line('STANDARD', 'ENVELOPE'), line()], codAmount: null, asOf: '' })).toEqual(['lines.2.packageType: El par servicio/tipo de paquete está repetido.'])
  })
})

describe('buildQuoteRequest', () => {
  it('manda el cliente, las líneas, el COD y asOf (vacío → null = hoy)', () => {
    expect(buildQuoteRequest('c-1', { lines: [line('EXPRESS', 'BOX', 8)], codAmount: 200, asOf: '' })).toEqual({
      clientPublicId: 'c-1',
      lines: [{ serviceType: 'EXPRESS', packageType: 'BOX', pieces: 8 }],
      codAmount: 200,
      asOf: null,
    })
    expect(buildQuoteRequest('c-1', { lines: [line()], codAmount: null, asOf: '2026-06-01' })).toMatchObject({ codAmount: null, asOf: '2026-06-01' })
  })
})

describe('errores del servidor', () => {
  it('`lines[i].campo` pasa a `lines.i.campo` y el par repetido `lines[i]` cae bajo el paquete', () => {
    expect(remapQuoteErrors({ 'lines[0].pieces': ['a'], 'lines[1]': ['b'], codAmount: ['c'], lines: ['d'] })).toEqual({ 'lines.0.pieces': ['a'], 'lines.1.packageType': ['b'], codAmount: ['c'], lines: ['d'] })
  })
  it('rethrowQuoteError conserva título, código y estatus', () => {
    const original = new ApiError(400, { title: 'La cantidad de piezas debe ser al menos 1.', code: 'validation', errors: { 'lines[0].pieces': ['x'] } })
    let caught: unknown
    try {
      rethrowQuoteError(original)
    } catch (e) {
      caught = e
    }
    expect(caught).toBeInstanceOf(ApiError)
    const err = caught as ApiError
    expect([err.status, err.code, err.title, err.errors]).toEqual([400, 'validation', 'La cantidad de piezas debe ser al menos 1.', { 'lines.0.pieces': ['x'] }])
    expect(() => rethrowQuoteError(new TypeError('red'))).toThrow(TypeError)
  })
})

describe('desglose', () => {
  it('el subtotal de la línea no incluye despacho ni COD (el servidor los suma a la primera línea)', () => {
    // ejemplo del manual: base 7 + 6.25 de piezas extra = 13.25 (el servidor devuelve lineTotal 21.25 con despacho 3 y COD 5)
    expect(lineSubtotal({ baseRate: 7, extraPieces: 6.25 })).toBe(13.25)
    expect(lineSubtotal({ baseRate: null, extraPieces: 0 })).toBe(0)
  })
  it('sourceOf normaliza el origen y las líneas sin tarifa base se detectan', () => {
    expect(['CONTRACT', 'generic', 'NONE', null, 'x'].map(sourceOf)).toEqual(['CONTRACT', 'GENERIC', 'NONE', 'NONE', 'NONE'])
    const lines = [
      { serviceType: 'A', packageType: 'B', pieces: 1, baseSource: 'NONE', extraPieces: 0, lineTotal: 0 },
      { serviceType: 'C', packageType: 'D', pieces: 1, baseSource: 'CONTRACT', extraPieces: 0, lineTotal: 1 },
    ]
    expect(linesWithoutRate({ lines })).toHaveLength(1)
  })
})
