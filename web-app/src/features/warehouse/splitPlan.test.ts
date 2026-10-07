import { describe, expect, it } from 'vitest'
import { addSplitBin, chunkAt, exceedsCapacity, freeQtyOf, isRestBin, maxBins, parsePerBin, splitSummary, type SplitBin } from './splitPlan'

const bin = (id: number): SplitBin => ({ id, code: `A-${id}` })

describe('splitPlan', () => {
  it('parsePerBin acepta coma y punto y rechaza vacío, cero y más de 3 decimales', () => {
    expect(parsePerBin('20')).toBe(20)
    expect(parsePerBin(' 2,5 ')).toBe(2.5)
    for (const bad of ['', '0', '1.2345', 'abc', '-3']) expect(parsePerBin(bad)).toBe(0)
  })

  it('maxBins: las llenas y una más con el resto (185 de 20 → 10; 180 de 20 → 9; 5 de 20 → 1)', () => {
    expect(maxBins(185, 20)).toBe(10)
    expect(maxBins(180, 20)).toBe(9)
    expect(maxBins(5, 20)).toBe(1)
    expect(maxBins(0, 20)).toBe(0)
    expect(chunkAt(185, 20, 8)).toBe(20)
    expect(chunkAt(185, 20, 9)).toBe(5)
    expect(chunkAt(5, 20, 0)).toBe(5)
    expect(isRestBin(185, 20, 9)).toBe(true)
    expect(isRestBin(185, 20, 8)).toBe(false)
    expect(isRestBin(180, 20, 8)).toBe(false)
  })

  it('addSplitBin acepta hasta 10 de 185 de 20, rechaza la undécima y la repetida', () => {
    let bins: SplitBin[] = []
    for (let i = 1; i <= 10; i++) {
      const r = addSplitBin(bins, bin(i), 185, 20)
      expect(r.ok).toBe(true)
      if (r.ok) bins = r.bins
    }
    expect(addSplitBin(bins, bin(3), 185, 20)).toEqual({ ok: false, reason: 'duplicate' })
    expect(addSplitBin(bins, bin(11), 185, 20)).toEqual({ ok: false, reason: 'full' })
    expect(addSplitBin([], bin(1), 185, 0)).toEqual({ ok: false, reason: 'perBin' })
  })

  it('splitSummary y cupo', () => {
    expect(splitSummary(185, 20, 9)).toEqual({ total: 180, left: 5 })
    expect(splitSummary(185, 20, 10)).toEqual({ total: 185, left: 0 })
    expect(freeQtyOf(25, 10)).toBe(15)
    expect(freeQtyOf(25, 30)).toBe(0)
    expect(freeQtyOf(null, 10)).toBeNull()
    expect(exceedsCapacity(15, 20)).toBe(true)
    expect(exceedsCapacity(20, 20)).toBe(false)
    expect(exceedsCapacity(null, 999)).toBe(false)
  })
})
