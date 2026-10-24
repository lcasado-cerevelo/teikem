// Lote 133 — reglas puras del formulario de correo: validación con los mismos mensajes del servidor, bloques «6=1, 7=2» y el cuerpo del API (sin la contraseña en la configuración).
import { describe, expect, it } from 'vitest'
import { buildEmailConfig, emailIssues, emailValuesOf, emptyBodyField, emptyCondition, emptyEmail, emptyReader, legacyToReaders, parseBlockColumns, parseSenders, parseTypes, type EmailMessages, type EmailValues } from './emailRules'
import { emptyFormat } from './formatRules'
import { emptyOutput } from './outputRules'
import { emptyRemote } from './remoteRules'
import { buildCreateRequest, buildPatchRequest, integrationSchema, type IntegrationValues } from './integrationRules'

const M: EmailMessages = {
  tenantId: 'tenant', clientId: 'cliente', secret: 'secreto', host: 'host', port: 'puerto', username: 'usuario', password: 'contraseña', mailbox: 'carpeta', noFields: 'sin campos', position: 'columna', label: 'etiqueta', pattern: 'regex', block: 'bloque',
  blockColumns: 'columnas del bloque', regex: 'regex inválida', repeatStart: 'repetir necesita inicio', readerName: 'nombre del lector', readerNameDuplicate: 'nombre repetido', readerFields: 'lector sin campos', conditionValue: 'condición sin texto', conditionScope: 'condición no aplica', nameFilter: 'nombre', senders: 'remitentes',
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
    expect(issues({ readPdf: true })).toEqual(['bodyFields:sin campos'])
    const f = emptyBodyField()
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '0' }] })).toEqual(['bodyFields.0.position:columna', 'bodyFields.0.label:etiqueta'])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'REGEX' }] })).toEqual(['bodyFields.0.pattern:regex'])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'BLOCK', start: 'Order Summary' }] })).toEqual(['bodyFields.0.start:bloque', 'bodyFields.0.columns:columnas del bloque'])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'BLOCK', start: 'a', end: 'b', columns: '6=1, 7=2' }] })).toEqual([])
    // 2026-10-22: con una expresión por línea no hacen falta el inicio ni el fin; repetir sí necesita el inicio
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'BLOCK', lineRegex: '^(\\d+)$', columns: '2=1' }] })).toEqual([])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'BLOCK', lineRegex: '([', columns: '2=1' }] })).toEqual(['bodyFields.0.lineRegex:regex inválida'])
    expect(issues({ readBody: true, bodyFields: [{ ...f, position: '2', kind: 'BLOCK', lineRegex: '^(\\d+)$', repeat: true, columns: '2=1' }] })).toEqual(['bodyFields.0.start:repetir necesita inicio'])
  })
})

describe('texto → listas', () => {
  it('las columnas del bloque son «posición=columna»', () => {
    expect(parseBlockColumns('6=1, 7=2;8 = 3')).toEqual([{ position: 6, index: 1 }, { position: 7, index: 2 }, { position: 8, index: 3 }])
    expect(parseBlockColumns('2=2, 3=4/3, 4=4')).toEqual([{ position: 2, index: 2 }, { position: 3, index: 4, divide: 3 }, { position: 4, index: 4 }])   // la cantidad = grupo 4 ÷ grupo 3 (importe ÷ precio unitario)
    expect(parseBlockColumns('3=4/')).toBeNull()
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

// 2026-10-22 — lectores: qué hace la integración con cada documento (cuerpo, PDF, archivo de datos), con sus condiciones, campos y destino.
describe('lectores', () => {
  const field = { ...emptyBodyField(), position: '1', kind: 'LABEL', label: 'Reference #' }
  const reader = (over: object = {}) => ({ ...emptyReader(), name: 'Pedido', source: 'BODY', fields: [field], ...over })

  it('un lector necesita nombre único, campos (cuerpo y PDF) y condiciones con texto que apliquen a lo que lee', () => {
    expect(issues({ readers: [reader()] })).toEqual([])
    expect(issues({ readers: [reader({ name: ' ' })] })).toEqual(['readers.0.name:nombre del lector'])
    expect(issues({ readers: [reader(), reader()] })).toEqual(['readers.1.name:nombre repetido'])
    expect(issues({ readers: [reader({ fields: [] })] })).toEqual(['readers.0.fields:lector sin campos'])
    expect(issues({ readers: [reader({ source: 'FILE', fields: [] })] })).toEqual([])   // un archivo de datos no lleva campos
    expect(issues({ readers: [reader({ conditions: [{ ...emptyCondition('BODY'), value: ' ' }] })] })).toEqual(['readers.0.conditions.0.value:condición sin texto'])
    // un cuerpo no tiene nombre de adjunto
    expect(issues({ readers: [reader({ conditions: [{ scope: 'ATTACHMENT_NAME', operator: 'CONTAINS', value: 'x' }] })] })).toEqual(['readers.0.conditions.0.scope:condición no aplica'])
  })

  it('los campos de un lector se validan como los de siempre, con su ruta', () => {
    const bad = { ...emptyBodyField(), position: '0', kind: 'REGEX', pattern: '' }
    expect(issues({ readers: [reader({ fields: [bad] })] })).toEqual(['readers.0.fields.0.position:columna', 'readers.0.fields.0.pattern:regex'])
  })

  it('los lectores viajan en la configuración del API y vuelven iguales', () => {
    const r = reader({
      source: 'PDF', target: 'ORDER', firstOnly: true, templatePublicId: 't-1', clientPublicId: 'c-1', recordStart: ' Order #: ', minConfidence: '85',
      checks: [{ kind: 'SUM', pattern: ' Total\\s*([\\d,.]+) ', position: '4', distinct: true, tolerance: '0.5', name: ' Importes ', scope: 'RECORD' }, { kind: 'MATCH_COUNT', pattern: 'CASES:\\s*(\\d+)', position: '1', distinct: true, tolerance: '', name: '', scope: 'HEADER' }],
      conditions: [{ scope: 'TEXT', operator: 'CONTAINS', value: ' packing list ' }],
      fields: [
        { ...emptyBodyField(), position: '2', kind: 'BLOCK', start: 'Product', end: 'Page', lineRegex: '^(T-\\w+)$', repeat: true, columns: '2=1' },
        { ...emptyBodyField(), position: '9', kind: 'REGEX', pattern: 'N: (.+)', fromHeader: true, appendWith: ' | ' },
      ],
    })
    const cfg = buildEmailConfig({ ...ok(), readers: [r] })
    expect(cfg.readers).toEqual([{
      name: 'Pedido', source: 'PDF', target: 'ORDER', firstOnly: true, templatePublicId: 't-1', clientPublicId: 'c-1', recordStart: 'Order #:', mode: 'SUGGEST', toStatus: null, warehousePublicId: null, minConfidence: 85,
      checks: [
        { kind: 'SUM', pattern: 'Total\\s*([\\d,.]+)', position: 4, distinct: false, tolerance: 0.5, name: 'Importes', scope: null },   // «distinct» solo cuenta fuera de SUM; el ámbito RECORD es el de siempre (no se manda)
        { kind: 'MATCH_COUNT', pattern: 'CASES:\\s*(\\d+)', position: 1, distinct: true, tolerance: null, name: null, scope: 'HEADER' },
      ],
      when: [{ scope: 'TEXT', operator: 'CONTAINS', value: 'packing list' }],
      fields: [
        expect.objectContaining({ position: 2, kind: 'BLOCK', start: 'Product', end: 'Page', lineRegex: '^(T-\\w+)$', repeat: true, columns: [{ position: 2, index: 1 }] }),
        expect.objectContaining({ position: 9, kind: 'REGEX', pattern: 'N: (.+)', fromHeader: true, appendWith: ' | ' }),
      ],
    }])
    const back = emailValuesOf(cfg).readers[0]
    expect(back).toMatchObject({ name: 'Pedido', source: 'PDF', firstOnly: true, templatePublicId: 't-1', clientPublicId: 'c-1', conditions: [{ scope: 'TEXT', operator: 'CONTAINS', value: 'packing list' }] })
    expect(back.fields[0]).toMatchObject({ kind: 'BLOCK', repeat: true, lineRegex: '^(T-\\w+)$', columns: '2=1' })
    expect(back.recordStart).toBe('Order #:')
    expect(back.minConfidence).toBe('85')
    expect(back.checks).toEqual([
      { kind: 'SUM', pattern: 'Total\\s*([\\d,.]+)', position: '4', distinct: false, tolerance: '0.5', name: 'Importes', scope: 'RECORD' },
      { kind: 'MATCH_COUNT', pattern: 'CASES:\\s*(\\d+)', position: '1', distinct: true, tolerance: '', name: '', scope: 'HEADER' },
    ])
    expect(back.fields[1]).toMatchObject({ fromHeader: true, appendWith: ' | ' })
  })

  it('«convertir lo de arriba en lectores» crea los equivalentes a los ajustes de siempre', () => {
    const e: EmailValues = { ...ok(), attachmentTypes: 'csv, xlsx, pdf', attachmentMode: 'BY_NAME', attachmentNameOperator: 'WILDCARD', attachmentNameValue: 'ordenes-*', readBody: true, readPdf: true, bodyFields: [field] }
    const readers = legacyToReaders(e)
    expect(readers.map((r) => [r.name, r.source])).toEqual([['Archivos de datos', 'FILE'], ['Cuerpo del correo', 'BODY'], ['PDF adjuntos', 'PDF']])
    expect(readers[0].conditions.map((c) => `${c.scope} ${c.operator} ${c.value}`)).toEqual(['ATTACHMENT_NAME REGEX \\.(csv|xlsx)$', 'ATTACHMENT_NAME WILDCARD ordenes-*'])
    expect(readers[2].conditions[0].value).toBe('\\.pdf$')
    expect(readers[1].fields).toEqual([field])
    expect(legacyToReaders({ ...ok(), attachmentTypes: 'pdf' })).toEqual([])
    expect(issues({ readers })).toEqual([])
  })
})

// 2026-10-24 — buzones de Microsoft 365 (aplicación de Entra ID): sin servidor ni puerto, con Tenant ID, Client ID y el secreto en lugar de la contraseña.
describe('buzón de Microsoft 365', () => {
  const m365 = (over: Partial<EmailValues> = {}): EmailValues => ({ ...emptyEmail(), provider: 'MICROSOFT365', tenantId: ' contoso.onmicrosoft.com ', clientId: ' app-1 ', username: ' avisos@contoso.com ', password: 'secreto', ...over })

  it('pide tenant, cliente, buzón y secreto, y no pide servidor ni puerto', () => {
    expect(emailIssues(m365(), false, M)).toEqual([])
    expect(emailIssues(m365({ tenantId: ' ', clientId: '', username: '', password: '' }), false, M).map((i) => `${i.path}:${i.message}`)).toEqual(['tenantId:tenant', 'clientId:cliente', 'username:usuario', 'password:secreto'])
    expect(emailIssues(m365({ password: '' }), true, M)).toEqual([])   // ya hay un secreto guardado
    // IMAP sigue pidiendo servidor y contraseña
    expect(emailIssues({ ...ok(), host: '', password: '' }, false, M).map((i) => i.message)).toEqual(['host', 'contraseña'])
  })

  it('viaja al API con el tipo, el tenant y el cliente, sin servidor, y vuelve igual', () => {
    const cfg = buildEmailConfig(m365())
    expect(cfg).toMatchObject({ provider: 'MICROSOFT365', tenantId: 'contoso.onmicrosoft.com', clientId: 'app-1', username: 'avisos@contoso.com', host: '' })
    expect(cfg).not.toHaveProperty('password')
    expect(emailValuesOf(cfg)).toMatchObject({ provider: 'MICROSOFT365', tenantId: 'contoso.onmicrosoft.com', clientId: 'app-1', password: '' })
    // un IMAP guardado sin «provider» (los de antes) se abre como IMAP
    expect(emailValuesOf({ ...buildEmailConfig(ok()), provider: undefined }).provider).toBe('IMAP')
    expect(buildEmailConfig(ok())).toMatchObject({ provider: 'IMAP', tenantId: null, clientId: null, host: 'imap.x.com' })
  })
})
