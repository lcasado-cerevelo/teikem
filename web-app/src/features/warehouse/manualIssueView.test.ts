// Despacho manual (2026-10-11) — lógica pura: cuerpo del POST, llave de idempotencia, estatus «Despachado», filtro por motivo.
import { describe, expect, it } from 'vitest'
import { z } from 'zod'
import { collectSchema, EMPTY_COLLECT_LINE, MANUAL_NOTE_MAX, type CollectFormValues } from './collectForm'
import { buildManualIssueBody, filterByReason, isManualCancelled, manualIssueKardexLink, manualStatusLabel, nextIdempotencyKey, reasonSearchTerm, shortNote } from './manualIssueView'

const line = { ...EMPTY_COLLECT_LINE, productPublicId: 'p1', sku: 'A', trackingTypeCode: 'NONE', owner: 'OWN', quantity: 2 }

describe('buildManualIssueBody', () => {
  it('lleva solo las líneas con datos, el motivo y la nota recortada', () => {
    const { body, indexMap } = buildManualIssueBody({ warehousePublicId: 'w', lines: [line, { ...EMPTY_COLLECT_LINE }], reasonCode: ' SAMPLE ', note: '  Feria  ' })
    expect(body).toEqual({
      warehousePublicId: 'w',
      lines: [{ productPublicId: 'p1', quantity: 2, binId: null, lotId: null, serialNumbers: null }],
      reasonCode: 'SAMPLE',
      note: 'Feria',
    })
    expect(indexMap).toEqual([0])
  })
  it('nota vacía = null y sin motivo = null (el servidor responde su mensaje)', () => {
    const { body } = buildManualIssueBody({ warehousePublicId: 'w', lines: [line], reasonCode: '', note: '   ' })
    expect(body.note).toBeNull()
    expect(body.reasonCode).toBeNull()
  })
})

describe('nextIdempotencyKey', () => {
  it('mismo cuerpo reusa la llave; otro cuerpo, una nueva', () => {
    let n = 0
    const id = () => `k${++n}`
    const a = nextIdempotencyKey(null, { x: 1 }, id)
    expect(a.key).toBe('k1')
    expect(nextIdempotencyKey(a, { x: 1 }, id).key).toBe('k1')
    expect(nextIdempotencyKey(a, { x: 2 }, id).key).toBe('k2')
  })
})

describe('estatus y enlaces', () => {
  it('COLLECTED se muestra Despachado y CANCELLED Cancelado', () => {
    expect(manualStatusLabel({ statusCode: 'COLLECTED', status: 'Recolectada', isActive: true }, 'Despachado', 'Cancelado')).toBe('Despachado')
    expect(manualStatusLabel({ statusCode: 'CANCELLED', status: 'Cancelada', isActive: false }, 'Despachado', 'Cancelado')).toBe('Cancelado')
    expect(isManualCancelled({ statusCode: 'COLLECTED', isActive: false })).toBe(true)
  })
  it('enlace al Kárdex por documento', () => {
    expect(manualIssueKardexLink({ id: 1002 })).toBe('/warehouse/kardex?refEntity=PICK_BATCH&refId=1002')
  })
  it('shortNote recorta y junta espacios', () => {
    expect(shortNote('a   b\nc')).toBe('a b c')
    expect(shortNote('x'.repeat(100), 10)).toHaveLength(10)
    expect(shortNote(null)).toBe('')
  })
})

describe('filtro por motivo', () => {
  it('la búsqueda escrita manda sobre el motivo; el motivo se usa como búsqueda si no hay texto', () => {
    expect(reasonSearchTerm('', 'Muestra')).toBe('Muestra')
    expect(reasonSearchTerm(' feria ', 'Muestra')).toBe('feria')
    expect(reasonSearchTerm('', null)).toBeUndefined()
  })
  it('descarta los que coincidieron por otro lado', () => {
    const rows = [{ reasonCode: 'SAMPLE' }, { reasonCode: 'SALE' }, { reasonCode: 'sample' }]
    expect(filterByReason(rows, 'SAMPLE')).toHaveLength(2)
    expect(filterByReason(rows, '')).toHaveLength(3)
  })
})

describe('collectSchema en modo manual', () => {
  const t = (k: string) => k
  const schema = collectSchema(t, (i) => i.code, { manual: true })
  const values = (over: Partial<CollectFormValues>): CollectFormValues => ({ warehousePublicId: 'w', lines: [line], reasonCode: 'SAMPLE', note: '', ...over })
  const messages = (v: CollectFormValues) => {
    const r = schema.safeParse(v)
    return r.success ? [] : (r.error as z.ZodError).issues.map((i) => i.message)
  }
  it('motivo obligatorio', () => {
    expect(messages(values({ reasonCode: '' }))).toEqual(['warehouse.manualIssues.errors.reasonRequired'])
  })
  it('nota de más de 500', () => {
    expect(messages(values({ note: 'x'.repeat(MANUAL_NOTE_MAX + 1) }))).toEqual(['warehouse.manualIssues.errors.noteTooLong'])
    expect(messages(values({ note: 'x'.repeat(MANUAL_NOTE_MAX) }))).toEqual([])
  })
  it('la recolección normal no pide motivo', () => {
    const plain = collectSchema(t, (i) => i.code)
    expect(plain.safeParse(values({ reasonCode: '' })).success).toBe(true)
  })
})
