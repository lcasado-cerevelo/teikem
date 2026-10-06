import { addDistBin, chunkAt, distSummary, isRestBin, maxBins, parsePerBin, sortTasksMineFirst, type DistBin, type PutawayTask } from './putawayLogic'

function task(id: number, assignedToUserId: number | null): PutawayTask {
  return { id, sku: `SKU-${id}`, productName: `P${id}`, quantity: 1, toBinCode: null, assignedToUserId }
}

describe('sortTasksMineFirst', () => {
  it('pone primero las tareas asignadas a mi usuario, en su orden original', () => {
    const tasks = [task(1, 5), task(2, 7), task(3, 7), task(4, null)]
    expect(sortTasksMineFirst(tasks, 7).map((t) => t.id)).toEqual([2, 3, 1, 4])
  })

  it('sin ninguna mía, conserva el orden original', () => {
    const tasks = [task(1, 5), task(2, 6)]
    expect(sortTasksMineFirst(tasks, 99).map((t) => t.id)).toEqual([1, 2])
  })
})

describe('reparto por posición', () => {
  const bin = (id: number): DistBin => ({ id, code: `A-${id}` })

  it('parsePerBin acepta coma y punto y rechaza vacío, cero y más de 3 decimales', () => {
    expect(parsePerBin('20')).toBe(20)
    expect(parsePerBin(' 2,5 ')).toBe(2.5)
    expect(parsePerBin('')).toBe(0)
    expect(parsePerBin('0')).toBe(0)
    expect(parsePerBin('1.2345')).toBe(0)
    expect(parsePerBin('abc')).toBe(0)
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
  })

  it('addDistBin acepta hasta 10 de 185 de 20 (la décima lleva el resto), rechaza la undécima y la repetida', () => {
    let bins: DistBin[] = []
    for (let i = 1; i <= 10; i++) {
      const r = addDistBin(bins, bin(i), 185, 20)
      expect(r.ok).toBe(true)
      if (r.ok) bins = r.bins
    }
    expect(addDistBin(bins, bin(3), 185, 20)).toEqual({ ok: false, reason: 'duplicate' })
    expect(addDistBin(bins, bin(11), 185, 20)).toEqual({ ok: false, reason: 'full' })
    expect(addDistBin([], bin(1), 185, 0)).toEqual({ ok: false, reason: 'perBin' })
  })

  it('distSummary: 9 posiciones de 20 de 185 → 180 repartidos, 5 pendientes; con la décima, todo', () => {
    expect(distSummary(185, 20, 9)).toEqual({ total: 180, left: 5 })
    expect(distSummary(185, 20, 10)).toEqual({ total: 185, left: 0 })
    expect(distSummary(5, 20, 1)).toEqual({ total: 5, left: 0 })
  })
})
