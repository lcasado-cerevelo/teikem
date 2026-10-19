// Lote 133 — reglas puras del formulario de correo: validación con los mismos mensajes del servidor, bloques «6=1, 7=2» y el cuerpo del API (sin la contraseña en la configuración).
import { describe, expect, it } from 'vitest'
import { buildEmailConfig, emailIssues, emailValuesOf, emptyBodyField, emptyEmail, parseBlockColumns, parseSenders, parseTypes, type EmailMessages, type EmailValues } from './emailRules'
import { emptyFormat } from './formatRules'
import { emptyOutput } from './outputRules'
import { emptyRemote } from './remoteRules'
import { buildCreateRequest, buildPatchRequest, integrationSchema, type IntegrationValues } from './integrationRules'

const M: EmailMessages = {
  host: 'host', port: 'puerto', username: 'usuario', password: 'contraseña', mailbox: 'carpeta', noFields: 'sin campos', position: 'columna', label: 'etiqueta', pattern: 'regex', block: 'bloque',
  blockColumns: 'columnas del bloque', nameFilter: 'nombre', senders: 'remitentes',
}
const ok = (): EmailValues => ({ ...emptyEmail(), host: ' imap.x.com ', username: ' u@x.com ', password: 'secreto' })
const issues = (e: Partial<EmailValues>, saved = false) => emailIssues({ ...ok(), ...e }, saved, M).map((i) => `${i.path}:${i.message}`)

describe('emailIssues', () => {
  it('acepta lo mínimo y pide servidor, puerto, usuario, contraseña (solo si no hay guardada) y carpeta', () => {
    expect(issues({})).toEqual([])
    expect(issues({ host: '', port: '0', username: '', password: '', mailbox: ' ' })).toEqual(['host:host', 'port:puerto', 'username:usuario', 'password:contraseña', 'mailbox:carpeta'])
    expect(issues({ password: '' }, true)).toEqual([])
  })

  it('el modo «por nombre» exige la condición y los remitentes deben ser correos o dominios', () => {
    expect(issues({ attachmentMode: 'BY_NAME', attachmentNameValue: ' ' })).toEqual(['attachmentNameValue:nombre'])
    expect(issues({ allowedSenders: 'truss.com, rep@truss.com\n@mail.truss.com' })).toEqual([])
    expect(issues({ allowedSenders: 'no es correo' })).toEqual(['allowedSenders:remitentes'])
  })

  it('leer el cuerpo exige al menos un campo y cada tipo sus datos', () => {
    expect(issues({ readBody: true })).toEqual(['bodyFields:sin campos'])
    const f = emptyBodyField()
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '0' }] })).toEqual(['bodyFields.0.position:columna', 'bodyFields.0.label:etiqueta'])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'REGEX' }] })).toEqual(['bodyFields.0.pattern:regex'])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'BLOCK', start: 'Order Summary' }] })).toEqual(['bodyFields.0.start:bloque', 'bodyFields.0.columns:columnas del bloque'])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'BLOCK', start: 'a', end: 'b', columns: '6=1, 7=2' }] })).toEqual([])
  })
})

describe('texto → listas', () => {
  it('las columnas del bloque son «posición=columna»', () => {
    expect(parseBlockColumns('6=1, 7=2;8 = 3')).toEqual([{ position: 6, index: 1 }, { position: 7, index: 2 }, { position: 8, index: 3 }])
    expect(parseBlockColumns('6')).toBeNull()
    expect(parseBlockColumns('')).toBeNull()
    expect(parseBlockColumns('a=1')).toBeNull()
  })

  it('remitentes y tipos se normalizan sin repetidos', () => {
    expect(parseSenders('Truss.com, REP@truss.com\ntruss.com')).toEqual(['truss.com', 'rep@truss.com'])
    expect(parseTypes('.CSV, txt; csv')).toEqual(['csv', 'txt'])
  })
})

describe('cuerpo del API', () => {
  const e: EmailValues = {
    ...ok(), subjectValue: ' PR Orden ', allowedSenders: 'truss.com', requireAttachment: true, attachmentMode: 'BY_NAME', attachmentNameValue: 'ordenes-*', readBody: true,
    bodyFields: [
      { ...emptyBodyField(), position: '1', label: ' Reference # ', required: true, transform: 'UPPER' },
      { ...emptyBodyField(), position: '6', kind: 'BLOCK', start: 'Order Summary', end: 'Total', columns: '6=1, 7=2' },
    ],
  }

  it('arma la configuración sin contraseña y con cada tipo de campo solo con sus datos', () => {
    const c = buildEmailConfig(e)
    expect(c).not.toHaveProperty('password')
    expect(c).toMatchObject({
      host: 'imap.x.com', port: 993, useSsl: true, username: 'u@x.com', mailbox: 'INBOX', subject: { operator: 'CONTAINS', value: 'PR Orden' }, allowedSenders: ['truss.com'],
      requireAttachment: true, attachmentMode: 'BY_NAME', attachmentName: { operator: 'WILDCARD', value: 'ordenes-*' }, attachmentTypes: ['csv', 'txt'], readBody: true,
    })
    expect(c.bodyFields?.[0]).toMatchObject({ position: 1, kind: 'LABEL', label: 'Reference #', pattern: null, required: true, transform: 'UPPER', columns: null })
    expect(c.bodyFields?.[1]).toMatchObject({ position: 6, kind: 'BLOCK', label: null, start: 'Order Summary', end: 'Total', columns: [{ position: 6, index: 1 }, { position: 7, index: 2 }] })
    expect(buildEmailConfig({ ...e, readBody: false }).bodyFields).toEqual([])
    expect(buildEmailConfig({ ...e, attachmentMode: 'ALL' }).attachmentName).toBeNull()
    expect(buildEmailConfig({ ...e, subjectValue: ' ' }).subject).toBeNull()
  })

  it('al editar parte de lo guardado (la contraseña vuelve vacía)', () => {
    const back = emailValuesOf(buildEmailConfig(e))
    expect(back).toMatchObject({ host: 'imap.x.com', password: '', allowedSenders: 'truss.com', attachmentMode: 'BY_NAME', readBody: true })
    expect(back.bodyFields[1]).toMatchObject({ kind: 'BLOCK', columns: '6=1, 7=2' })
    expect(emailValuesOf(null)).toEqual(emptyEmail())
  })
})

describe('formulario de integración con correo', () => {
  const base: IntegrationValues = {
    name: 'Truss', description: '', clientPublicId: 'c1', templatePublicId: 't1', connectorKind: 'EMAIL', folder: '', scheduleKind: 'INTERVAL', intervalMinutes: '5', scopes: ['files.orders_import'], notifyUserIds: [], includeMe: true,
    retentionDays: '30', email: ok(), remote: emptyRemote('SFTP'), format: emptyFormat(), direction: 'INBOUND', output: emptyOutput(),
  }
  const em = { hasSavedPassword: false, messages: M }

  it('el alta con correo valida el buzón con la ruta email.* y no pide carpeta', () => {
    const issuesOf = (v: IntegrationValues, saved = false) => {
      const r = integrationSchema({ nameRequired: 'n', nameMax: 'm', clientRequired: 'c', templateRequired: 't', folderRequired: 'f', folderInvalid: 'fi', interval: 'i', retention: 'r', scopes: 's' }, false, { hasSavedPassword: saved, messages: M }).safeParse(v)
      return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}:${i.message}`)
    }
    expect(issuesOf(base)).toEqual([])
    expect(issuesOf({ ...base, email: { ...ok(), host: '' } })).toEqual(['email.host:host'])
    expect(em.messages.host).toBe('host')
  })

  it('el alta manda la configuración y la contraseña; la edición solo manda la contraseña si se escribió una nueva', () => {
    const create = buildCreateRequest(base)
    expect(create).toMatchObject({ connectorKind: 'EMAIL', folder: null, mailPassword: 'secreto', email: { host: 'imap.x.com' } })
    const keep = buildPatchRequest({ ...base, email: { ...ok(), password: '' } }, { connectorKind: 'EMAIL' })
    expect(keep.mailPassword).toBeUndefined()
    expect(keep).not.toHaveProperty('folder')
    expect(buildPatchRequest(base, { connectorKind: 'EMAIL' }).mailPassword).toBe('secreto')
    expect(buildCreateRequest({ ...base, connectorKind: 'FOLDER', folder: 'x' })).not.toHaveProperty('email')
  })
})
