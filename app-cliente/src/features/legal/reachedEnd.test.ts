import { reachedEnd } from './LegalScreen'

describe('documento legal: llegar al final', () => {
  const m = (y: number, view: number, content: number) => ({ contentOffset: { y }, layoutMeasurement: { height: view }, contentSize: { height: content } })
  it('solo cuenta cuando se vio hasta el final (con un margen)', () => {
    expect(reachedEnd(m(0, 400, 3000))).toBe(false)
    expect(reachedEnd(m(1200, 400, 3000))).toBe(false)
    expect(reachedEnd(m(2590, 400, 3000))).toBe(true)
    expect(reachedEnd(m(0, 500, 500))).toBe(true)
  })
})
