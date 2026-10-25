import { setLang } from '../i18n/i18n'
import { translate } from '../i18n/i18n'
import { loginErrorMessage } from './loginErrors'
import { ApiError } from './problem'

const t = (k: string) => translate('es', k)
beforeEach(() => setLang('es'))

describe('loginErrorMessage', () => {
  it('cada fallo dice qué revisar', () => {
    expect(loginErrorMessage(new ApiError(401, { title: 'Credenciales inválidas.' }), t)).toMatch(/no son correctos.*mayúsculas.*ojito/)
    expect(loginErrorMessage(new ApiError(429, null), t)).toMatch(/Demasiados intentos/)
    expect(loginErrorMessage(new ApiError(503, null), t)).toMatch(/servidor tuvo un problema/)
    expect(loginErrorMessage(new ApiError(0, null), t)).toMatch(/No hay conexión/)
  })
  it('lo demás conserva el mensaje del servidor', () => {
    expect(loginErrorMessage(new ApiError(400, { title: 'Confirme que no es un robot para continuar.' }), t)).toBe('Confirme que no es un robot para continuar.')
    expect(loginErrorMessage(new Error('x'), t)).toBe('Ocurrió un error. Intente de nuevo.')
  })
})
