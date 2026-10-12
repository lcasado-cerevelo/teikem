// «Olvidé mi contraseña» (2026-10-07): el enlace del login pide el correo (respuesta genérica) y el enlace del correo elige la contraseña nueva.
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearTokens, setMfaChallenge } from '../../kernel/auth/tokens'
import { setLang } from '../../kernel/i18n/i18n'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })

let calls: { path: string; body: unknown }[]
let resetReply: () => Response

async function renderAppAt(path: string) {
  window.history.pushState({}, '', path)
  vi.resetModules()
  const { default: App } = await import('../../app/App')
  return render(<App />)
}

describe('Recuperar la contraseña', () => {
  beforeEach(() => {
    setLang('es')
    clearTokens()
    setMfaChallenge(null)
    calls = []
    resetReply = () => new Response(null, { status: 204 })
    vi.stubEnv('VITE_API_URL', 'http://api.test')
    vi.stubGlobal(
      'fetch',
      vi.fn(async (req: Request) => {
        const path = new URL(req.url).pathname
        const text = await req.text()
        calls.push({ path, body: text ? JSON.parse(text) : undefined })
        if (path === '/api/v1/auth/forgot-password') return json({ message: 'Si el correo está registrado, le enviamos un enlace para restablecer su contraseña.', link: null })
        if (path === '/api/v1/auth/reset-password') return resetReply()
        return new Response(null, { status: 404 })
      }),
    )
  })
  afterEach(() => {
    clearTokens()
    setMfaChallenge(null)
    localStorage.setItem('teikem.lang', 'es')
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('el login ofrece «¿Olvidó su contraseña?» y lleva a pedir el enlace', async () => {
    await renderAppAt('/login')
    const user = userEvent.setup()
    await user.click(await screen.findByRole('link', { name: '¿Olvidó su contraseña?' }))
    expect(await screen.findByRole('heading', { name: 'Restablecer contraseña' })).toBeInTheDocument()
  })

  it('pide el enlace con el correo y muestra la respuesta genérica', async () => {
    await renderAppAt('/forgot-password')
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('Correo electrónico'), 'ana@empresa.com')
    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Si el correo está registrado')
    expect(calls.find((c) => c.path === '/api/v1/auth/forgot-password')?.body).toEqual({ email: 'ana@empresa.com' })
  })

  it('con el enlace del correo elige la contraseña nueva (valida largo y coincidencia) y manda correo, enlace y contraseña', async () => {
    await renderAppAt('/reset-password?email=ana%40empresa.com&token=abc123')
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('Contraseña nueva'), 'corta')
    await user.type(screen.getByLabelText('Repita la contraseña nueva'), 'corta')
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Use al menos 12 caracteres.')

    await user.clear(screen.getByLabelText('Contraseña nueva'))
    await user.clear(screen.getByLabelText('Repita la contraseña nueva'))
    await user.type(screen.getByLabelText('Contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.type(screen.getByLabelText('Repita la contraseña nueva'), 'Otra-Clave-Distinta')
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Las dos contraseñas no coinciden.')

    await user.clear(screen.getByLabelText('Repita la contraseña nueva'))
    await user.type(screen.getByLabelText('Repita la contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
    expect(await screen.findByText('Contraseña cambiada')).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/auth/reset-password')?.body).toEqual({ email: 'ana@empresa.com', token: 'abc123', newPassword: 'Mi-Clave-Propia-2026' })
    expect(screen.getByRole('link', { name: 'Ir a iniciar sesión' })).toBeInTheDocument()
  })

  it('un enlace vencido muestra el mensaje del API y deja pedir otro', async () => {
    resetReply = () => json({ title: 'El enlace no es válido o venció. Pida uno nuevo.', status: 400, code: 'validation' }, 400)
    await renderAppAt('/reset-password?email=ana%40empresa.com&token=viejo')
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('Contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.type(screen.getByLabelText('Repita la contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('El enlace no es válido o venció. Pida uno nuevo.')
    expect(screen.getByRole('link', { name: 'Pedir otro enlace' })).toBeInTheDocument()
  })

  it('con MFA inscrito: el servidor pide el código (mfa_required), la página muestra el campo y reenvía con mfaCode; el enlace no cambia', async () => {
    resetReply = () => {
      const body = calls.at(-1)?.body as { mfaCode?: string }
      if (!body.mfaCode) return json({ title: 'Escriba el código de verificación en dos pasos para cambiar la contraseña.', status: 400, code: 'mfa_required' }, 400)
      if (body.mfaCode !== '123456') return json({ title: 'Datos inválidos.', status: 400, code: 'validation', errors: { mfaCode: ['Código MFA inválido.'] } }, 400)
      return new Response(null, { status: 204 })
    }
    await renderAppAt('/reset-password?email=ana%40empresa.com&token=abc123')
    const user = userEvent.setup()
    expect(screen.queryByLabelText('Código de verificación')).not.toBeInTheDocument()
    await user.type(await screen.findByLabelText('Contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.type(screen.getByLabelText('Repita la contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
    // sin código: el servidor lo pide y aparece el campo
    const mfa = await screen.findByLabelText('Código de verificación')
    expect(screen.getByRole('alert')).toHaveTextContent('Escriba el código de verificación en dos pasos')
    // código malo: el error sale en la página y se puede corregir
    await user.type(mfa, '000000')
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
    expect(await screen.findByText('Código MFA inválido.')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Código de verificación'))
    await user.type(screen.getByLabelText('Código de verificación'), '123456')
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }))
    expect(await screen.findByText('Contraseña cambiada')).toBeInTheDocument()
    const sent = calls.filter((c) => c.path === '/api/v1/auth/reset-password').map((c) => c.body)
    expect(sent.at(-1)).toEqual({ email: 'ana@empresa.com', token: 'abc123', newPassword: 'Mi-Clave-Propia-2026', mfaCode: '123456' })
    expect(sent.every((b) => (b as { token: string }).token === 'abc123')).toBe(true)
  })

  it('sin correo o sin token en el enlace avisa y ofrece pedir otro', async () => {
    await renderAppAt('/reset-password')
    expect(await screen.findByRole('alert')).toHaveTextContent('El enlace no es válido o venció.')
    expect(screen.getByRole('link', { name: 'Pedir otro enlace' })).toBeInTheDocument()
  })
})
