import { capMarks, markRecommended, marksComplete, marksToRows, remainingQty, sortRecommendedFirst, sumMarks, toggleMark, type MarkOption } from './binMarks'

const opt = (key: string, capacity: number | null, recommended = false): MarkOption => ({ key, binCode: key, capacity, recommended, detail: '' })
const A = opt('A-01', 20, true)
const B = opt('B-02', 40, true)
const C = opt('C-03', 100)
const all = [A, B, C]

describe('binMarks', () => {
  it('al marcar toma lo que falta hasta el tope de la posición, sin pasar del total', () => {
    let m = toggleMark(all, {}, 'A-01', 50)
    expect(m).toEqual({ 'A-01': 20 })
    m = toggleMark(all, m, 'B-02', 50)
    expect(m).toEqual({ 'A-01': 20, 'B-02': 30 })
    expect(marksComplete(m, 50)).toBe(true)
    // ya no falta nada: otra marca no agrega
    expect(toggleMark(all, m, 'C-03', 50)).toEqual(m)
  })

  it('desmarcar libera su cantidad', () => {
    const m = toggleMark(all, { 'A-01': 20, 'B-02': 30 }, 'A-01', 50)
    expect(m).toEqual({ 'B-02': 30 })
    expect(remainingQty(50, m)).toBe(20)
  })

  it('si la cantidad baja, las marcas se recortan por el final del listado', () => {
    expect(capMarks(all, { 'A-01': 20, 'B-02': 30 }, 35)).toEqual({ 'A-01': 20, 'B-02': 15 })
    expect(capMarks(all, { 'A-01': 20, 'B-02': 30 }, 10)).toEqual({ 'A-01': 10 })
    expect(sumMarks(capMarks(all, { 'A-01': 20 }, 0))).toBe(0)
  })

  it('sin tope conocido toma todo lo que falta', () => {
    expect(toggleMark([opt('X', null)], {}, 'X', 7.5)).toEqual({ X: 7.5 })
  })

  it('markRecommended marca las recomendadas y deja lo marcado a mano', () => {
    expect(markRecommended(all, {}, 50)).toEqual({ 'A-01': 20, 'B-02': 30 })
    expect(markRecommended(all, { 'C-03': 50 }, 50)).toEqual({ 'C-03': 50 })
  })

  it('las recomendadas van primero y los renglones siguen el orden del listado', () => {
    expect(sortRecommendedFirst([C, A, B]).map((o) => o.key)).toEqual(['A-01', 'B-02', 'C-03'])
    expect(marksToRows(all, { 'B-02': 30, 'A-01': 20 })).toEqual([
      { key: 'A-01', binCode: 'A-01', qty: 20 },
      { key: 'B-02', binCode: 'B-02', qty: 30 },
    ])
  })
})
