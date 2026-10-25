import { describe, expect, it } from 'vitest'
import { applyProblemDetails, ApiError } from '../../kernel/api/problem'
import { loginErrorText } from './loginErrors'

const t = (k: string) => `«${k}»`

describe('mensajes del inicio de sesión', () => {
  it('credenciales malas dicen qué revisar (y no revelan si el correo existe)', () => {
    const p = applyProblemDetails(new ApiError(401, { title: 'Credenciales inválidas.' }))
    expect(loginErrorText(p, t)).toBe('«auth.errors.invalidCredentials»')
  })
  it('demasiados intentos (429) y cuenta bloqueada tienen su texto', () => {
    expect(loginErrorText(applyProblemDetails(new ApiError(429, { title: 'Demasiados intentos; espere un minuto e intente de nuevo.' })), t)).toBe('«auth.errors.tooManyRequests»')
  })
  it('un fallo del servidor se explica y la red caída conserva su mensaje', () => {
    expect(loginErrorText(applyProblemDetails(new ApiError(500, { title: 'Error interno.' })), t)).toBe('«auth.errors.serverDown»')
    expect(loginErrorText({ title: 'sin conexión', code: 'network' }, t)).toBe('sin conexión')
  })
  it('lo demás conserva el mensaje del servidor', () => {
    expect(loginErrorText({ title: 'Confirme que no es un robot para continuar.', code: 'bad_request' }, t)).toBe('Confirme que no es un robot para continuar.')
  })
})
