import { describe, expect, it, vi } from 'vitest'
import { ApiError, applyProblemDetails, normalizeField, toApiError } from './problem'

describe('applyProblemDetails', () => {
  it('pone los errores por campo en el formulario y devuelve title/code', () => {
    const setError = vi.fn()
    const error = new ApiError(400, {
      title: 'Datos inválidos.',
      status: 400,
      code: 'validation',
      errors: { name: ['El nombre es obligatorio.'], Code: ['Código duplicado.', 'Máximo 20.'] },
    })
    const result = applyProblemDetails(error, { setError })
    expect(result.title).toBe('Datos inválidos.')
    expect(result.code).toBe('validation')
    expect(result.errors).toEqual({ name: ['El nombre es obligatorio.'], code: ['Código duplicado.', 'Máximo 20.'] })
    expect(setError).toHaveBeenCalledWith('name', { type: 'server', message: 'El nombre es obligatorio.' })
    expect(setError).toHaveBeenCalledWith('code', { type: 'server', message: 'Código duplicado. Máximo 20.' })
  })

  it('acepta el cuerpo crudo de openapi-fetch y deduce el código por estatus si falta', () => {
    const result = applyProblemDetails({ title: 'One or more validation errors occurred.', status: 400, errors: { '$.email': ['x'] } })
    expect(result.code).toBe('validation')
    expect(result.errors).toEqual({ email: ['x'] })
  })

  it('conserva códigos del API como aal2_required y module_disabled', () => {
    const e = toApiError({ title: 'Esta acción requiere reautenticación reciente (AAL2).', code: 'aal2_required' }, new Response(null, { status: 403 }))
    expect(e.status).toBe(403)
    expect(applyProblemDetails(e).code).toBe('aal2_required')
  })

  it('traduce un fallo de red a code=network', () => {
    const result = applyProblemDetails(new TypeError('Failed to fetch'))
    expect(result.code).toBe('network')
    expect(result.title).toMatch(/servidor|server/)
  })

  it('sin formulario no falla y devuelve un título genérico para errores desconocidos', () => {
    expect(applyProblemDetails(undefined).code).toBe('error')
  })

  it('normaliza nombres de campo del servidor', () => {
    expect(normalizeField('$.ClientPublicId')).toBe('clientPublicId')
    expect(normalizeField('lines[0].qty')).toBe('lines[0].qty')
  })
})
