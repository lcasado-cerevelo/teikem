import { describe, expect, it } from 'vitest'
import { captureError, periodEditable, periodError, type CaptureDraft, type PeriodDraft } from './taxRules'

const capture: CaptureDraft = { clientPublicId: 'c1', existingDocument: false, type: 'IVU_EXEMPTION_STATE', hasFile: true, number: '77', from: '2026-01-01', to: '2026-12-31', scope: 'COMPONENTS', components: ['STATE'] }

describe('captura única de la exención', () => {
  it('pide cliente, componentes, tipo, archivo, número y fechas', () => {
    expect(captureError({ ...capture, clientPublicId: null })).toBe('client')
    expect(captureError({ ...capture, components: [] })).toBe('components')
    expect(captureError({ ...capture, type: '' })).toBe('type')
    expect(captureError({ ...capture, hasFile: false })).toBe('file')
    expect(captureError({ ...capture, number: ' ' })).toBe('number')
    expect(captureError({ ...capture, from: '' })).toBe('from')
    expect(captureError({ ...capture, to: '' })).toBe('to')
    expect(captureError({ ...capture, to: '2025-01-01' })).toBe('dates')
    expect(captureError(capture)).toBeNull()
  })
  it('con un documento ya subido solo importan el cliente y el alcance', () => {
    expect(captureError({ ...capture, existingDocument: true, type: '', hasFile: false, number: '', from: '', to: '' })).toBeNull()
  })
})

describe('período por temporada', () => {
  const period: PeriodDraft = { name: 'Huracán', reason: 'Orden 2026-12', from: '2026-09-01', to: '2026-09-30', scope: 'SELECTED', productPublicIds: ['p1'], categoryIds: [] }
  it('revisa nombre, motivo, fechas y selección', () => {
    expect(periodError({ ...period, name: '' })).toBe('name')
    expect(periodError({ ...period, reason: '' })).toBe('reason')
    expect(periodError({ ...period, to: '2026-08-01' })).toBe('dates')
    expect(periodError({ ...period, productPublicIds: [] })).toBe('selection')
    expect(periodError({ ...period, scope: 'ALL', productPublicIds: [] })).toBeNull()
    expect(periodError(period)).toBeNull()
  })
  it('lo editable depende del estado', () => {
    expect(periodEditable('SCHEDULED')).toBe('ALL')
    expect(periodEditable('ACTIVE')).toBe('END_DATE')
    expect(periodEditable('ENDED')).toBe('NONE')
    expect(periodEditable('INACTIVE')).toBe('NONE')
  })
})
