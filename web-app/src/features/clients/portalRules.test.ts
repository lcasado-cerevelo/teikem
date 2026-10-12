// F-A3 — reglas puras de «Usuarios de portal»: esquema de la invitación (espejo del servidor), request, acciones por estatus y orden.
import { describe, expect, it } from 'vitest'
import {
  buildInviteRequest,
  emptyInvite,
  inviteSchema,
  PORTAL_EMAIL_MAX,
  PORTAL_NAME_MAX,
  portalActions,
  reinviteValues,
  sortPortalUsers,
  wasAddedWithoutLink,
  type PortalUser,
} from './portalRules'

const M = { emailRequired: 'El correo es obligatorio.', emailInvalid: 'Correo inválido.', emailMax: 'Máx correo', nameMax: 'Máx nombre', roleRequired: 'El rol de portal es obligatorio.' }
const messages = (v: unknown) => {
  const r = inviteSchema(M).safeParse(v)
  return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`)
}
const user = (status: string, email = 'a@x.com'): PortalUser => ({ id: 1, clientPublicId: 'c', email, status, isActive: true, hasUser: true, hasPassword: false, clientsCount: 1 })

describe('inviteSchema', () => {
  it('pide correo, correo válido y rol (mensajes del servidor)', () => {
    expect(messages(emptyInvite())).toEqual(['email: El correo es obligatorio.', 'role: El rol de portal es obligatorio.'])
    expect(messages({ email: 'xx', fullName: '', role: 'CLIENT_ADMIN' })).toEqual(['email: Correo inválido.'])
    expect(messages({ email: 'ana@acme.com', fullName: '', role: 'CLIENT_ADMIN' })).toEqual([])
  })
  it('respeta los máximos de 150 del correo y del nombre', () => {
    const longMail = `${'a'.repeat(PORTAL_EMAIL_MAX)}@x.com`
    expect(messages({ email: longMail, fullName: '', role: 'R' })).toContain('email: Máx correo')
    expect(messages({ email: 'a@x.com', fullName: 'n'.repeat(PORTAL_NAME_MAX + 1), role: 'R' })).toEqual(['fullName: Máx nombre'])
    expect(messages({ email: 'a@x.com', fullName: 'n'.repeat(PORTAL_NAME_MAX), role: 'R' })).toEqual([])
  })
})

describe('buildInviteRequest', () => {
  it('recorta, pone el correo en minúsculas y manda null si no hay nombre', () => {
    expect(buildInviteRequest({ email: '  Ana@Acme.COM ', fullName: '  ', role: 'CLIENT_ADMIN' })).toEqual({ email: 'ana@acme.com', fullName: null, role: 'CLIENT_ADMIN' })
    expect(buildInviteRequest({ email: 'a@x.com', fullName: ' Ana Paz ', role: 'CLIENT_READONLY' }).fullName).toBe('Ana Paz')
  })
  it('«volver a invitar» precarga correo, nombre y rol', () => {
    expect(reinviteValues({ email: 'a@x.com', fullName: null, role: 'CLIENT_OPERATOR' })).toEqual({ email: 'a@x.com', fullName: '', role: 'CLIENT_OPERATOR' })
  })
})

describe('portalActions', () => {
  it('INVITED reenvía y se da de baja; ACTIVE suspende; SUSPENDED reactiva; DISABLED solo se vuelve a invitar', () => {
    expect(portalActions(user('INVITED'), true)).toEqual({ resend: true, suspend: false, reactivate: false, remove: true, reinvite: false })
    expect(portalActions(user('ACTIVE'), true)).toEqual({ resend: false, suspend: true, reactivate: false, remove: true, reinvite: false })
    expect(portalActions(user('SUSPENDED'), true)).toEqual({ resend: false, suspend: false, reactivate: true, remove: true, reinvite: false })
    expect(portalActions(user('DISABLED'), true)).toEqual({ resend: false, suspend: false, reactivate: false, remove: false, reinvite: true })
  })
  it('con el cliente de baja no se reenvía ni se vuelve a invitar (el servidor lo rechaza con 409), pero sí se suspende y se da de baja', () => {
    expect(portalActions(user('INVITED'), false).resend).toBe(false)
    expect(portalActions(user('DISABLED'), false).reinvite).toBe(false)
    expect(portalActions(user('ACTIVE'), false).suspend).toBe(true)
    expect(portalActions(user('ACTIVE'), false).remove).toBe(true)
  })
  it('sin estatus no ofrece nada', () => {
    expect(portalActions({ status: '' }, true)).toEqual({ resend: false, suspend: false, reactivate: false, remove: false, reinvite: false })
  })
})

describe('sortPortalUsers / wasAddedWithoutLink', () => {
  it('los dados de baja van al final y el resto por correo', () => {
    const sorted = sortPortalUsers([user('DISABLED', 'a@x.com'), user('ACTIVE', 'z@x.com'), user('INVITED', 'm@x.com')])
    expect(sorted.map((u) => u.email)).toEqual(['m@x.com', 'z@x.com', 'a@x.com'])
  })
  it('una fila que nace ACTIVE sin vencimiento es una cuenta que ya existía (sin enlace)', () => {
    expect(wasAddedWithoutLink({ user: user('ACTIVE'), expiresAtUtc: null })).toBe(true)
    expect(wasAddedWithoutLink({ user: user('INVITED'), expiresAtUtc: '2026-10-14T00:00:00Z' })).toBe(false)
  })
})
