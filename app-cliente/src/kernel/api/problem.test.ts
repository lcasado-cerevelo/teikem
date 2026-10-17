import { ApiError, apiErrorMessage, isNetworkError, toApiError } from './problem'

describe('errores del API', () => {
  it('toma el título del servidor y deduce el código del estatus', () => {
    const e = toApiError({ title: 'Credenciales inválidas.', status: 401 }, { status: 401 } as Response)
    expect(e.title).toBe('Credenciales inválidas.')
    expect(e.code).toBe('unauthorized')
    expect(apiErrorMessage(e)).toBe('Credenciales inválidas.')
  })
  it('sin respuesta es un error de red; sin título usa el texto por omisión', () => {
    expect(isNetworkError(toApiError(null))).toBe(true)
    expect(toApiError(null).title).toContain('conexión')
    expect(new ApiError(403, null).title).toContain('permiso')
  })
  it('junta los mensajes por campo sin repetirlos', () => {
    const e = new ApiError(400, { title: 'Datos inválidos.', errors: { a: ['uno'], b: ['uno', 'dos'] } })
    expect(apiErrorMessage(e)).toBe('uno dos')
  })
})
