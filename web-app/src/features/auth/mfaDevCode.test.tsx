// Código de MFA a la vista (2026-10-22, Luis): solo cuando el API (en Development) manda `devCode`; con un botón para usarlo.
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearTokens, setMfaChallenge } from '../../kernel/auth/tokens'
import { setLang } from '../../kernel/i18n/i18n'

async function renderMfa() {
  window.history.pushState({}, '', '/mfa')
  vi.resetModules()
  const { default: App } = await import('../../app/App')
  return render(<App />)
}

describe('Paso del MFA con el código de desarrollo', () => {
  beforeEach(() => {
    setLang('es')
    clearTokens()
    vi.stubEnv('VITE_API_URL', 'http://api.test')
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 404 })))
  })
  afterEach(() => {
    clearTokens()
    setMfaChallenge(null)
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('muestra el código y «Usar este código» lo escribe en el campo', async () => {
    setMfaChallenge({ token: 't', enrollmentRequired: false, devCode: '123456' })
    await renderMfa()
    expect(await screen.findByTestId('mfa-dev-code')).toHaveTextContent('123456')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Usar este código' }))
    expect(screen.getByLabelText('Código de verificación')).toHaveValue('123456')
  })

  it('sin devCode (staging y producción) no hay nada que mostrar', async () => {
    setMfaChallenge({ token: 't', enrollmentRequired: false })
    await renderMfa()
    expect(await screen.findByLabelText('Código de verificación')).toBeInTheDocument()
    expect(screen.queryByTestId('mfa-dev-code')).toBeNull()
  })
})
