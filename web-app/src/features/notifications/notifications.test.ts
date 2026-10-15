// Lote 52 — reglas puras de los avisos en la web: cuándo avisa Kemi, la campanita (contador, enlaces), el formulario de reglas
// (validación, request, variables) y el buscador de personas.
import { describe, expect, it } from 'vitest'
import { badgeText, internalLink } from './NotificationBell'
import { bubbleKey, decideBubble, readAsleep, writeAsleep } from './kemiState'
import {
  buildRequest,
  defaultChannels,
  emptyForm,
  formOfRule,
  insertVariable,
  isValidEmail,
  isValidPhone,
  recipientKey,
  validateForm,
} from './ruleForm'
import { filterPeople, matchingPeople, MAX_SUGGESTIONS, type PersonOption } from './UserPicker'
import type { NotificationEventDto, NotificationRuleDto } from './notificationsApi'

const t = (key: string, params?: Record<string, string | number>) => (params ? `${key}:${JSON.stringify(params)}` : key)

describe('decideBubble (cuándo avisa Kemi)', () => {
  it('la primera lectura con avisos saluda una sola vez por sesión', () => {
    expect(decideBubble(null, 3, false, false)).toEqual({ bubble: 3, seen: 3 })
    expect(decideBubble(null, 3, true, false)).toEqual({ bubble: null, seen: 3 })
    expect(decideBubble(null, 0, false, false)).toEqual({ bubble: null, seen: 0 })
  })
  it('después solo avisa cuando el contador SUBE, con la diferencia', () => {
    expect(decideBubble(2, 3, true, false)).toEqual({ bubble: 1, seen: 3 })
    expect(decideBubble(2, 2, true, false)).toEqual({ bubble: null, seen: 2 })
    expect(decideBubble(5, 1, true, false)).toEqual({ bubble: null, seen: 1 })   // se leyeron: sin globo
    expect(decideBubble(1, 6, true, false)).toEqual({ bubble: 5, seen: 6 })
  })
  it('dormida nunca muestra globo, pero lleva la cuenta', () => {
    expect(decideBubble(null, 4, false, true)).toEqual({ bubble: null, seen: 4 })
    expect(decideBubble(1, 4, true, true)).toEqual({ bubble: null, seen: 4 })
  })
  it('el globo dice «un aviso» o «varios»', () => {
    expect(bubbleKey(1)).toBe('one')
    expect(bubbleKey(2)).toBe('many')
  })
})

describe('Kemi dormida se recuerda por usuario', () => {
  it('guarda y quita la marca, separada por usuario', () => {
    writeAsleep(7, true)
    expect(readAsleep(7)).toBe(true)
    expect(readAsleep(8)).toBe(false)
    writeAsleep(7, false)
    expect(readAsleep(7)).toBe(false)
  })
})

describe('campanita', () => {
  it('el contador pasa de 99', () => {
    expect(badgeText(5)).toBe('5')
    expect(badgeText(99)).toBe('99')
    expect(badgeText(100)).toBe('99+')
  })
  it('solo se navega a enlaces de la web', () => {
    expect(internalLink('/orders/123')).toBe('/orders/123')
    expect(internalLink('  /system/audit ')).toBe('/system/audit')
    expect(internalLink('//evil.com')).toBeNull()
    expect(internalLink('https://evil.com')).toBeNull()
    expect(internalLink('javascript:alert(1)')).toBeNull()
    expect(internalLink(null)).toBeNull()
  })
})

const EVENT: NotificationEventDto = {
  code: 'TEST', moduleKey: null, name: 'Aviso de prueba', nameEn: 'Test', isOptional: true,
  variables: [{ name: 'message', label: 'Mensaje', labelEn: 'Message', sample: 'x' }],
  defaultSubject: 'Asunto {message}', defaultSubjectEn: 'Subject {message}', defaultBody: 'Texto {message}', defaultBodyEn: 'Body {message}',
}

describe('formulario de reglas', () => {
  it('los canales nacen apagados con los textos del evento', () => {
    const channels = defaultChannels(EVENT)
    expect(channels.map((c) => c.channel)).toEqual(['EMAIL', 'SMS', 'APP', 'PUSH'])
    expect(channels.every((c) => !c.enabled && c.subjectEs === 'Asunto {message}' && c.bodyEn === 'Body {message}')).toBe(true)
  })
  it('valida nombre y evento con los mensajes del servidor', () => {
    expect(validateForm({ ...emptyForm(), name: '  ' }, true, t)).toEqual({ name: 'notifications.rules.errors.nameRequired', eventCode: 'notifications.rules.errors.eventRequired' })
    expect(validateForm({ ...emptyForm(), name: 'x'.repeat(101), eventCode: 'TEST' }, true, t)).toEqual({ name: 'notifications.rules.errors.nameMax' })
    expect(validateForm({ ...emptyForm(), name: 'Ok' }, false, t)).toEqual({})   // al editar el evento no se pide
  })
  it('el alta manda el evento; la edición manda la lista completa y el rowVersion', () => {
    const form = { ...emptyForm(), eventCode: 'TEST', name: ' Mi regla ', isActive: true, recipients: [{ kind: 'EMAIL' as const, value: 'a@x.com', label: 'a@x.com' }] }
    const create = buildRequest(form, null)
    expect(create).toMatchObject({ eventCode: 'TEST', name: 'Mi regla', isActive: true, recipients: [{ kind: 'EMAIL', value: 'a@x.com' }] })
    expect(create.channels).toHaveLength(4)
    const rule: NotificationRuleDto = { publicId: 'p', eventCode: 'TEST', name: 'Mi regla', isActive: true, channels: [], recipients: [], rowVersion: 'AAA=' }
    const edit = buildRequest(formOfRule(rule), rule)
    expect(edit).toMatchObject({ rowVersion: 'AAA=' })
    expect(edit).not.toHaveProperty('eventCode')
  })
  it('correos y teléfonos con las reglas del servidor', () => {
    expect(isValidEmail('ana@correo.com')).toBe(true)
    expect(isValidEmail('ana@correo')).toBe(false)
    expect(isValidPhone('787-555-0101')).toBe(true)
    expect(isValidPhone('12345')).toBe(false)
    expect(isValidPhone('787-555-ABCD')).toBe(false)
  })
  it('un destinatario no se repite (los correos sin importar mayúsculas)', () => {
    expect(recipientKey({ kind: 'EMAIL', value: 'A@X.com' })).toBe(recipientKey({ kind: 'EMAIL', value: ' a@x.com ' }))
    expect(recipientKey({ kind: 'ROLE', roleId: 3 })).not.toBe(recipientKey({ kind: 'USER', userId: 3 }))
  })
  it('inserta la variable en el cursor, reemplazando lo seleccionado', () => {
    expect(insertVariable('Hola ', 'user', null, null)).toEqual({ text: 'Hola {user}', caret: 11 })
    expect(insertVariable('Hola XX fin', 'user', 5, 7)).toEqual({ text: 'Hola {user} fin', caret: 11 })
    expect(insertVariable('ab', 'm', 1, 1)).toEqual({ text: 'a{m}b', caret: 4 })
  })
})

describe('buscador de personas', () => {
  const people: PersonOption[] = [
    { id: 1, name: 'Ana Rivera', email: 'ana@advance.com' },
    { id: 2, name: 'Luis Casado', email: 'lcasadopr@gmail.com' },
    { id: 3, name: 'Pedro Pérez', email: 'pedro@advance.com' },
  ]
  it('busca por nombre o por correo, sin importar mayúsculas ni tildes', () => {
    expect(filterPeople(people, 'ana').map((p) => p.id)).toEqual([1])
    expect(filterPeople(people, 'PEREZ').map((p) => p.id)).toEqual([3])
    expect(filterPeople(people, 'gmail').map((p) => p.id)).toEqual([2])
    expect(filterPeople(people, 'advance').map((p) => p.id)).toEqual([1, 3])
    expect(filterPeople(people, '').map((p) => p.id)).toEqual([1, 2, 3])
  })
  it('«Seleccionar todo» escoge TODAS las que coinciden con lo escrito, no solo las que caben en la lista', () => {
    const many = Array.from({ length: 30 }, (_, i) => ({ id: i + 1, name: i < 20 ? `Operador ${i}` : `Gerente ${i}`, email: `p${i}@x.com` }))
    expect(filterPeople(many, 'operador')).toHaveLength(MAX_SUGGESTIONS)        // la lista se corta en 8
    expect(matchingPeople(many, 'operador')).toHaveLength(20)                    // pero «Seleccionar todo» son 20
    expect(matchingPeople(many, 'gerente').every((p) => p.name.startsWith('Gerente'))).toBe(true)   // solo lo filtrado
    expect(matchingPeople(many, 'operador', new Set([1, 2]))).toHaveLength(18)   // sin los ya escogidos
    expect(matchingPeople(many, 'zzz')).toHaveLength(0)
    expect(matchingPeople(many, '')).toHaveLength(30)
  })
  it('no ofrece a los ya agregados y limita la lista', () => {
    expect(filterPeople(people, 'advance', new Set([1])).map((p) => p.id)).toEqual([3])
    const many = Array.from({ length: 30 }, (_, i) => ({ id: i, name: `Persona ${i}`, email: `p${i}@x.com` }))
    expect(filterPeople(many, 'persona')).toHaveLength(MAX_SUGGESTIONS)
  })
})
