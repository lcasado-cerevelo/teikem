import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../db/database'
import { __resetLangForTests, setLang } from '../i18n/i18n'
import { ApiError, apiErrorMessage, toApiError } from './problem'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetLangForTests()
})

const FORBIDDEN_ES = 'No tienes permiso para esta acción. Pide a tu administrador que te lo asigne.'

describe('ProblemDetails — 403 sin cuerpo (2026-10-11)', () => {
  it('un 403 sin cuerpo (política de permiso del API) dice «sin permiso», no «Ocurrió un error»', () => {
    const err = new ApiError(403, null)
    expect(err.title).toBe(FORBIDDEN_ES)
    expect(err.message).toBe(FORBIDDEN_ES)
    expect(err.code).toBe('forbidden')
    expect(apiErrorMessage(err)).toBe(FORBIDDEN_ES)
  })

  it('lo que openapi-fetch entrega de un 403 vacío (undefined o cadena vacía) también', () => {
    const response = new Response(null, { status: 403 })
    expect(toApiError(undefined, response).title).toBe(FORBIDDEN_ES)
    expect(toApiError('', response).title).toBe(FORBIDDEN_ES)
    expect(toApiError('   ', response).title).toBe(FORBIDDEN_ES)
  })

  it('un 403 con ProblemDetails conserva el mensaje y el código del servidor', () => {
    const err = toApiError(
      { title: "El módulo 'WMS_LOTSERIAL' no está habilitado para esta compañía.", code: 'module_disabled', status: 403 },
      new Response(null, { status: 403 }),
    )
    expect(err.title).toBe("El módulo 'WMS_LOTSERIAL' no está habilitado para esta compañía.")
    expect(err.code).toBe('module_disabled')
    // un 403 con cuerpo de texto también conserva su texto
    expect(toApiError('Acceso denegado.', new Response(null, { status: 403 })).title).toBe('Acceso denegado.')
  })

  it('un 403 con ProblemDetails sin título usa el mensaje de «sin permiso» y conserva el código', () => {
    const err = toApiError({ code: 'forbidden', status: 403 }, new Response(null, { status: 403 }))
    expect(err.title).toBe(FORBIDDEN_ES)
    expect(err.code).toBe('forbidden')
  })

  it('en inglés', () => {
    setLang('en')
    expect(new ApiError(403, null).title).toBe('You do not have permission for this action. Ask your administrator to grant it.')
  })

  it('los demás estatus sin cuerpo siguen con el mensaje genérico', () => {
    expect(new ApiError(500, null).title).toBe('Ocurrió un error. Intente de nuevo.')
    expect(new ApiError(404, null).title).toBe('Ocurrió un error. Intente de nuevo.')
    expect(new ApiError(0, null).code).toBe('network')
  })
})
