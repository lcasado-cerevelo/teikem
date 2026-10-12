import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

describe('EnvironmentRibbon', () => {
  beforeEach(() => {
    vi.resetModules()
    document.title = 'Teikem'
  })
  afterEach(() => vi.unstubAllGlobals())

  const mount = async (health: unknown) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => health }))
    const { EnvironmentRibbon } = await import('./EnvironmentRibbon')
    return render(<EnvironmentRibbon />)
  }

  it('con environment = STAGING muestra la cinta y antepone la etiqueta al título', async () => {
    await mount({ status: 'ok', environment: 'STAGING' })
    expect(await screen.findByRole('status')).toHaveTextContent('STAGING')
    await waitFor(() => expect(document.title).toBe('[STAGING] Teikem'))
  })

  it('en producción (environment null) no pinta nada ni toca el título', async () => {
    const { container } = await mount({ status: 'ok', environment: null })
    await new Promise((r) => setTimeout(r, 20))
    expect(container).toBeEmptyDOMElement()
    expect(document.title).toBe('Teikem')
  })

  it('si /health falla o no es JSON, no pinta nada', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('x')))
    const { EnvironmentRibbon } = await import('./EnvironmentRibbon')
    const { container } = render(<EnvironmentRibbon />)
    await new Promise((r) => setTimeout(r, 20))
    expect(container).toBeEmptyDOMElement()
  })
})
