import { beforeEach, describe, expect, it } from 'vitest'
import { readEntryMode, wizardPath, writeEntryMode } from './entryMode'

describe('modo de captura de la orden de venta', () => {
  beforeEach(() => window.localStorage.clear())

  it('se recuerda por persona', () => {
    writeEntryMode(1, 'wizard')
    writeEntryMode(2, 'full')
    expect(readEntryMode(1)).toBe('wizard')
    expect(readEntryMode(2)).toBe('full')
  })

  it('sin preferencia usa el modo por omisión (formulario en escritorio)', () => {
    expect(readEntryMode(99)).toBe('full')
    expect(readEntryMode(undefined)).toBe('full')
  })

  it('un valor guardado inválido se ignora', () => {
    window.localStorage.setItem('teikem.salesEntryMode.1', 'otro')
    expect(readEntryMode(1)).toBe('full')
  })

  it('la dirección del asistente retoma el borrador por su id', () => {
    expect(wizardPath()).toBe('/sales-orders/new-order')
    expect(wizardPath('abc 1')).toBe('/sales-orders/new-order?order=abc%201')
  })
})
