import { BURST_GAP_MS, BURST_MIN_CHARS, NO_BURST, stepBurst, type BurstState } from './scanBurst'

describe('ráfaga del lector (teclas)', () => {
  it('un código que llega de golpe en el campo es una lectura', () => {
    expect(stepBurst(NO_BURST, '', '7501031311309', 1000).scanning).toBe(true)
    expect(stepBurst(NO_BURST, '', 'ABC', 1000).scanning).toBe(true)
  })

  it('caracteres rápidos seguidos (menos de BURST_GAP_MS entre sí) forman una lectura; el tercero la confirma', () => {
    let s: BurstState = NO_BURST
    let r = stepBurst(s, '', '7', 1000)
    expect(r.scanning).toBe(false)
    s = r.state
    r = stepBurst(s, '7', '75', 1000 + BURST_GAP_MS - 10)
    expect(r.scanning).toBe(false)
    s = r.state
    r = stepBurst(s, '75', '750', 1000 + 2 * (BURST_GAP_MS - 10))
    expect(r.scanning).toBe(true)
    expect(r.state.chars).toBe(BURST_MIN_CHARS)
  })

  it('una persona escribiendo (más lento que BURST_GAP_MS) nunca llega a ser lectura', () => {
    let s: BurstState = NO_BURST
    let text = ''
    for (let i = 0; i < 8; i++) {
      const next = text + String(i)
      const r = stepBurst(s, text, next, 1000 + i * (BURST_GAP_MS + 40))
      expect(r.scanning).toBe(false)
      s = r.state
      text = next
    }
  })

  it('borrar o reemplazar reinicia la ráfaga', () => {
    const s: BurstState = { chars: 5, lastAt: 1000 }
    expect(stepBurst(s, '12345', '1234', 1010)).toEqual({ state: NO_BURST, scanning: false })
    expect(stepBurst(s, '12345', '1234X', 1010).scanning).toBe(false)
  })
})
