// Kemi con vida: el temporizador parpadea con espera al azar, se apaga con «reducir movimiento» y limpia lo que puso al desmontarse.
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useKemiLife } from './useKemiLife'

function stubMatchMedia(reduced: boolean) {
  vi.stubGlobal('matchMedia', (q: string) => ({ matches: reduced && q.includes('reduce'), addEventListener() {}, removeEventListener() {} }))
}

function svgRef() {
  const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
  return { current: el as SVGSVGElement }
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('useKemiLife', () => {
  it('parpadea de vez en cuando y vuelve a abrir los ojos', () => {
    stubMatchMedia(false)
    vi.spyOn(Math, 'random').mockReturnValue(0)   // espera mínima: 2.5 s
    const ref = svgRef()
    renderHook(() => useKemiLife(ref, true))
    expect(ref.current.hasAttribute('data-blink')).toBe(false)
    act(() => vi.advanceTimersByTime(2600))
    expect(ref.current.getAttribute('data-blink')).toBe('1')
    act(() => vi.advanceTimersByTime(200))
    expect(ref.current.hasAttribute('data-blink')).toBe(false)
  })

  it('con el puntero encima se queda pendiente y al salir vuelve a su cara de siempre', () => {
    stubMatchMedia(false)
    const ref = svgRef()
    const { result } = renderHook(() => useKemiLife(ref, true))
    act(() => result.current.onPointerEnter({} as never))
    expect(ref.current.getAttribute('data-mood')).toBe('alert')
    act(() => result.current.onPointerLeave())
    expect(ref.current.hasAttribute('data-mood')).toBe(false)
  })

  it('con «reducir movimiento» no programa nada', () => {
    stubMatchMedia(true)
    vi.spyOn(Math, 'random').mockReturnValue(0)
    const ref = svgRef()
    renderHook(() => useKemiLife(ref, true))
    act(() => vi.advanceTimersByTime(60000))
    expect(ref.current.hasAttribute('data-blink')).toBe(false)
    expect(ref.current.hasAttribute('data-mood')).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('desactivada o desmontada no deja temporizadores ni atributos', () => {
    stubMatchMedia(false)
    const ref = svgRef()
    const { rerender, unmount } = renderHook(({ on }) => useKemiLife(ref, on), { initialProps: { on: true } })
    expect(vi.getTimerCount()).toBeGreaterThan(0)
    ref.current.setAttribute('data-mood', 'wink')
    rerender({ on: false })
    expect(vi.getTimerCount()).toBe(0)
    expect(ref.current.hasAttribute('data-mood')).toBe(false)
    rerender({ on: true })
    unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('sin poder preguntar por el movimiento reducido se queda quieta', () => {
    vi.stubGlobal('matchMedia', undefined)
    const ref = svgRef()
    renderHook(() => useKemiLife(ref, true))
    expect(vi.getTimerCount()).toBe(0)
  })
})
