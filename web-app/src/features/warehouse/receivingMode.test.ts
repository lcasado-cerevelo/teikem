// Lote 16 — modo de recepción (lógica pura, espejo de ReceivingModeRules.cs): normalización, modo efectivo, cambio de modo,
// PATCH de la sección "Recepción" del almacén (D12), cupo excedido (D4), qué líneas necesitan destino y la sugerida.
import { describe, expect, it } from 'vitest'
import {
  effectiveReceivingMode,
  exceedsCapacity,
  inSentence,
  isDirectMode,
  needsTarget,
  normalizeReceivingMode,
  receivingModeChanged,
  receivingModeLabel,
  topSuggestion,
  warehouseReceivingPatch,
} from './receivingMode'

describe('modo de recepción', () => {
  it('normaliza: DIRECT sin distinguir mayúsculas; vacío o desconocido = PUTAWAY', () => {
    expect(normalizeReceivingMode('direct')).toBe('DIRECT')
    expect(normalizeReceivingMode(' DIRECT ')).toBe('DIRECT')
    expect(normalizeReceivingMode(null)).toBe('PUTAWAY')
    expect(normalizeReceivingMode('HALF')).toBe('PUTAWAY')
    expect(isDirectMode('DIRECT')).toBe(true)
    expect(isDirectMode(undefined)).toBe(false)
  })

  it('modo efectivo: el del recibo; sin él, el del almacén', () => {
    expect(effectiveReceivingMode('PUTAWAY', 'DIRECT')).toBe('PUTAWAY')
    expect(effectiveReceivingMode(null, 'DIRECT')).toBe('DIRECT')
    expect(effectiveReceivingMode('', null)).toBe('PUTAWAY')
  })

  it('etiqueta del catálogo (o el código) y dentro de una oración en minúscula', () => {
    const opts = [
      { code: 'PUTAWAY', label: 'Con acomodo' },
      { code: 'DIRECT', label: 'Directo a posición' },
    ]
    expect(receivingModeLabel('direct', opts)).toBe('Directo a posición')
    expect(receivingModeLabel('DIRECT', [])).toBe('DIRECT')
    expect(inSentence('Directo a posición')).toBe('directo a posición')
    expect(inSentence('With put-away')).toBe('with put-away')
  })

  it('cambio de modo: compara normalizado (sin valor = PUTAWAY)', () => {
    expect(receivingModeChanged(null, 'PUTAWAY')).toBe(false)
    expect(receivingModeChanged('PUTAWAY', 'DIRECT')).toBe(true)
  })
})

describe('warehouseReceivingPatch (sección Recepción del almacén)', () => {
  const w = { receivingModeCode: 'PUTAWAY', defaultReceivingBinId: 20 }
  it('sin cambios no manda nada', () => {
    expect(warehouseReceivingPatch(w, { receivingMode: 'PUTAWAY', defaultReceivingBinId: '20' })).toEqual({})
  })
  it('modo nuevo y posición nueva', () => {
    expect(warehouseReceivingPatch(w, { receivingMode: 'DIRECT', defaultReceivingBinId: '21' })).toEqual({ receivingMode: 'DIRECT', defaultReceivingBinId: 21 })
  })
  it('vaciar la posición manda clearDefaultReceivingBin (D12)', () => {
    expect(warehouseReceivingPatch(w, { receivingMode: 'PUTAWAY', defaultReceivingBinId: '' })).toEqual({ clearDefaultReceivingBin: true })
    expect(warehouseReceivingPatch({ receivingModeCode: null, defaultReceivingBinId: null }, { receivingMode: 'PUTAWAY', defaultReceivingBinId: '' })).toEqual({})
  })
})

describe('cupo, necesidad de destino y sugerida', () => {
  it('excede el cupo solo si hay cupo y la cantidad es mayor que lo libre (D4)', () => {
    expect(exceedsCapacity(5, 8)).toBe(true)
    expect(exceedsCapacity(5, 5)).toBe(false)
    expect(exceedsCapacity(null, 1000)).toBe(false)
    expect(exceedsCapacity(0, null)).toBe(false)
  })

  it('necesita destino: recibido > 0, salvo lote sin lote o con cruce de muelle (D11)', () => {
    expect(needsTarget({ received: 3 })).toBe(true)
    expect(needsTarget({ received: 0 })).toBe(false)
    expect(needsTarget({ received: null })).toBe(false)
    expect(needsTarget({ received: 3, trackingTypeCode: 'LOT' })).toBe(false)
    expect(needsTarget({ received: 3, trackingTypeCode: 'lot', lotNumber: 'L1' })).toBe(true)
    expect(needsTarget({ received: 3, allocatedToCrossDock: 1 })).toBe(false)
  })

  it('la sugerida es la primera que cabe; si ninguna cabe, la primera', () => {
    expect(topSuggestion([{ binId: 1, fits: false }, { binId: 2, fits: true }])?.binId).toBe(2)
    expect(topSuggestion([{ binId: 1, fits: false }])?.binId).toBe(1)
    expect(topSuggestion([])).toBeNull()
    expect(topSuggestion(undefined)).toBeNull()
  })
})
