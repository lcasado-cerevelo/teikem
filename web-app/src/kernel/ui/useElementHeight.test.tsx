import { act, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useElementHeight } from './useElementWidth'

// ResizeObserver simulado: guarda la función para dispararla a mano.
let resize: (() => void) | null = null
class FakeResizeObserver {
  constructor(cb: () => void) {
    resize = cb
  }
  observe() {}
  disconnect() {
    resize = null
  }
}

function Probe({ show }: { show: boolean }) {
  const [el, setEl] = useState<HTMLDivElement | null>(null)
  const h = useElementHeight(el)
  return (
    <>
      {show && <div ref={setEl} data-testid="box" />}
      <output>{h}</output>
    </>
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('useElementHeight (Lote 15)', () => {
  it('mide el elemento que se monta DESPUÉS, sigue sus cambios y vuelve a 0 al quitarlo', () => {
    vi.stubGlobal('ResizeObserver', FakeResizeObserver)
    let height = 120
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(() => ({ height, width: 10 }) as DOMRect)
    const { rerender } = render(<Probe show={false} />)
    expect(screen.getByRole('status')).toHaveTextContent('0')
    rerender(<Probe show />)
    expect(screen.getByRole('status')).toHaveTextContent('120')
    height = 88.6
    act(() => resize?.())
    expect(screen.getByRole('status')).toHaveTextContent('89')
    rerender(<Probe show={false} />)
    expect(screen.getByRole('status')).toHaveTextContent('0')
  })
})
