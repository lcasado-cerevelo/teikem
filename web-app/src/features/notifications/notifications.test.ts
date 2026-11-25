// Lote 52 — reglas puras de los avisos en la web: cuándo avisa Kemi, la campanita (contador, enlaces), el formulario de reglas
// (validación, request, variables) y el buscador de personas.
import { describe, expect, it } from 'vitest'
import { badgeText, internalLink } from './NotificationBell'
import { bubbleKey, decideBubble, readAsleep, writeAsleep } from './kemiState'
import {
  blockInSubject,
  buildRequest,
  defaultChannels,
  defaultContextRecipients,
  emptyForm,
  formOfRule,
  insertVariable,
  isValidEmail,
  isValidPhone,
  recipientKey,
  recipientsForEvent,
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

// Lote 243: destinatarios «Del evento», plantilla por canal y variables bloque
const ORDER_EVENT: NotificationEventDto = {
  ...EVENT,
  code: 'SALES_ORDER_REQUESTED',
  variables: [
    { name: 'orderNumber', label: 'Número', labelEn: 'Number', sample: 'OV-1' },
    { name: 'orderDetail', label: 'La orden completa (tabla)', labelEn: 'Full order', sample: '', isBlock: true },
  ],
  contextRecipients: [
    { key: 'SELLER', label: 'Vendedor de la orden', labelEn: "Order's sales rep", isDefault: true, audience: 'STAFF' },
    { key: 'CLIENT', label: 'Cliente de la orden', labelEn: "Order's customer", isDefault: false, audience: 'CUSTOMER' },
  ],
  channelDefaults: [{ channel: 'EMAIL', subject: 'Orden {orderNumber}', subjectEn: 'Order {orderNumber}', body: 'Detalle\n\n{orderDetail}', bodyEn: 'Detail\n\n{orderDetail}' }],
}

describe('Lote 243: avisos con destinatarios del evento y bloques', () => {
  it('cada canal usa su plantilla propia y los demás la del evento', () => {
    const channels = defaultChannels(ORDER_EVENT)
    expect(channels.find((c) => c.channel === 'EMAIL')).toMatchObject({ subjectEs: 'Orden {orderNumber}', bodyEn: 'Detail\n\n{orderDetail}' })
    expect(channels.find((c) => c.channel === 'APP')).toMatchObject({ subjectEs: 'Asunto {message}', bodyEs: 'Texto {message}' })
  })
  it('solo se prellenan los destinatarios marcados por omisión, con la etiqueta del idioma', () => {
    expect(defaultContextRecipients(ORDER_EVENT)).toEqual([{ kind: 'CONTEXT', value: 'SELLER', label: 'Vendedor de la orden' }])
    expect(defaultContextRecipients(ORDER_EVENT, 'en')[0].label).toBe("Order's sales rep")
    expect(defaultContextRecipients(EVENT)).toEqual([])
    expect(defaultContextRecipients(undefined)).toEqual([])
  })
  it('al cambiar a un evento sin esos destinatarios se quitan los «Del evento» y se conservan los demás', () => {
    const seller = defaultContextRecipients(ORDER_EVENT)
    const mail = { kind: 'EMAIL' as const, value: 'almacen@x.test', label: 'almacen@x.test' }
    // de «Orden de venta solicitada» a un evento sin contextRecipients: el vendedor de la orden se va (el servidor daría 400)
    expect(recipientsForEvent(seller, EVENT)).toEqual([])
    expect(recipientsForEvent([...seller, mail], EVENT)).toEqual([mail])
    // al volver al evento de la orden, sin destinatarios se prellenan los marcados; con un CONTEXT declarado se conserva tal cual
    expect(recipientsForEvent([], ORDER_EVENT)).toEqual(seller)
    expect(recipientsForEvent([mail], ORDER_EVENT)).toEqual([mail])
    const client = { kind: 'CONTEXT' as const, value: 'CLIENT', label: 'Cliente de la orden' }
    expect(recipientsForEvent([client], ORDER_EVENT)).toEqual([client])
  })
  it('la clave de un destinatario del evento es C{valor} y el request lo manda como CONTEXT', () => {
    expect(recipientKey({ kind: 'CONTEXT', value: 'SELLER' })).toBe('CSELLER')
    expect(recipientKey({ kind: 'CONTEXT', value: 'SELLER' })).not.toBe(recipientKey({ kind: 'CONTEXT', value: 'CLIENT' }))
    const form = { ...emptyForm(), eventCode: 'SALES_ORDER_REQUESTED', name: 'Correo', recipients: defaultContextRecipients(ORDER_EVENT) }
    expect(buildRequest(form, null).recipients).toEqual([{ kind: 'CONTEXT', roleId: undefined, userId: undefined, value: 'SELLER' }])
  })
  it('un bloque en el asunto se rechaza con el mismo mensaje del servidor', () => {
    const channels = defaultChannels(ORDER_EVENT).map((c) => (c.channel === 'EMAIL' ? { ...c, subjectEn: 'Order {orderDetail}' } : c))
    const form = { ...emptyForm(), eventCode: 'SALES_ORDER_REQUESTED', name: 'Correo', channels }
    expect(blockInSubject(form, ORDER_EVENT)).toBe('orderDetail')
    expect(validateForm(form, true, t, ORDER_EVENT)).toEqual({ channels: 'notifications.rules.errors.blockInSubject:{"name":"{orderDetail}"}' })
    // el bloque en el texto sí es válido, y sin evento no hay nada que comparar
    expect(validateForm({ ...form, channels: defaultChannels(ORDER_EVENT) }, true, t, ORDER_EVENT)).toEqual({})
    expect(blockInSubject(form, undefined)).toBeUndefined()
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
