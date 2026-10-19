// Lote 137b — reglas puras de la salida: valores, validación en cliente y cuerpo del API; y su integración con el esquema del formulario y los cuerpos de alta y edición.
import { describe, expect, it } from 'vitest'
import { emptyEmail } from './emailRules'
import { emptyFormat } from './formatRules'
import { buildCreateRequest, buildPatchRequest, integrationSchema, type IntegrationValues } from './integrationRules'
import { buildHttpSecret, buildOutputConfig, emptyOutput, outputIssues, outputValuesOf, type OutputMessages, type OutputValues } from './outputRules'
import { emptyRemote } from './remoteRules'

const M: OutputMessages = {
  statuses: 'estatus',
  columnsRequired: 'columnas',
  columnsMax: 'máx',
  headerRequired: 'nombre',
  headerMax: 'largo',
  headerRepeated: (h) => `repetida ${h}`,
  headerName: (h) => `nombre ${h}`,
  fieldRequired: 'campo',
  url: 'url',
  authName: 'usuario',
  secret: 'secreto',
  headersMax: 'encabezados',
  headerPair: 'par',
}
const issues = (v: OutputValues, connector = 'HTTP', saved = false) => outputIssues(v, connector, saved, M).map((i) => `${i.path}:${i.message}`)
const withUrl = (over: Partial<OutputValues['http']> = {}): OutputValues => ({ ...emptyOutput(), format: 'JSON', http: { ...emptyOutput().http, url: 'https://api.cliente.com/x', ...over } })

describe('outputIssues', () => {
  it('una salida por omisión con URL pasa; sin estatus ni columnas pide los dos', () => {
    expect(issues(withUrl())).toEqual([])
    expect(issues({ ...withUrl(), statuses: [], columns: [] })).toEqual(['statuses:estatus', 'columns:columnas'])
  })

  it('las columnas: nombre obligatorio y único, campo elegido y, en JSON/XML, un nombre que sirva de campo', () => {
    const col = (header: string, field = 'status') => ({ header, field })
    expect(issues({ ...withUrl(), columns: [col(' ')] })).toEqual(['columns.0.header:nombre'])
    expect(issues({ ...withUrl(), columns: [col('A'), col('a')] })).toEqual(['columns.1.header:repetida a'])
    expect(issues({ ...withUrl(), columns: [col('Número de orden')] })).toEqual(['columns.0.header:nombre Número de orden'])
    expect(issues({ ...withUrl(), format: 'CSV', columns: [col('Número de orden')] })).toEqual([])
    expect(issues({ ...withUrl(), columns: [col('A', '')] })).toEqual(['columns.0.field:campo'])
    expect(issues({ ...withUrl(), columns: Array.from({ length: 41 }, (_, i) => col(`c${i}`)) })).toEqual(['columns:máx'])
  })

  it('la API saliente pide URL, usuario o encabezado y secreto (salvo que ya haya uno guardado)', () => {
    expect(issues(withUrl({ url: ' ' }))).toEqual(['http.url:url'])
    expect(issues(withUrl({ authKind: 'BEARER' }))).toEqual(['http.secret:secreto'])
    expect(issues(withUrl({ authKind: 'BEARER' }), 'HTTP', true)).toEqual([])
    expect(issues(withUrl({ authKind: 'BASIC', secret: 'x' }))).toEqual(['http.authName:usuario'])
    expect(issues(withUrl({ authKind: 'HEADER', authName: 'X-Api-Key', secret: 'x' }))).toEqual([])
    expect(issues(withUrl({ headers: Array.from({ length: 11 }, (_, i) => ({ name: `X-${i}`, value: 'v' })) }))).toEqual(['http.headers:encabezados'])
    expect(issues(withUrl({ headers: [{ name: '', value: 'v' }] }))).toEqual(['http.headers.0.name:par'])
    expect(issues(emptyOutput(), 'SFTP')).toEqual([])   // SFTP/FTP no llevan API
  })
})

describe('cuerpo del API', () => {
  it('HTTP manda la API sin el secreto y los demás conectores nada de API ni de delimitador fuera de CSV', () => {
    const v = withUrl({ method: 'PUT', authKind: 'HEADER', authName: ' X-Api-Key ', secret: 'llave', headers: [{ name: ' X-A ', value: '1' }, { name: '', value: '' }] })
    const c = buildOutputConfig(v, 'HTTP')
    expect(c.http).toEqual({ url: 'https://api.cliente.com/x', method: 'PUT', authKind: 'HEADER', authName: 'X-Api-Key', headers: [{ name: 'X-A', value: '1' }] })
    expect(c.delimiter).toBeNull()                       // JSON
    expect(c.fileName).toBeNull()                        // HTTP no lleva nombre de archivo
    expect(buildHttpSecret(v, 'HTTP')).toEqual({ value: 'llave' })
    expect(buildHttpSecret({ ...v, http: { ...v.http, secret: '' } }, 'HTTP')).toBeUndefined()   // al editar sin escribir otro, el servidor conserva el guardado
    expect(buildHttpSecret({ ...v, http: { ...v.http, authKind: 'NONE' } }, 'HTTP')).toBeUndefined()
    const file = buildOutputConfig({ ...emptyOutput(), delimiter: ';', fileName: ' entregas-{date}.{ext} ' }, 'SFTP')
    expect(file).toMatchObject({ format: 'CSV', delimiter: ';', fileName: 'entregas-{date}.{ext}', http: null })
    expect(buildHttpSecret(emptyOutput(), 'SFTP')).toBeUndefined()
  })

  it('parte de lo guardado (sin el secreto) y la salida por omisión es CSV al instante de las entregadas', () => {
    const d = emptyOutput()
    expect(d).toMatchObject({ statuses: ['DELIVERED'], trigger: 'EVENT', format: 'CSV', delimiter: ',' })
    expect(d.columns.map((c) => c.field)).toEqual(['orderNumber', 'status', 'deliveredAt', 'recipientName'])
    expect(outputValuesOf(null)).toEqual(d)
    const saved = outputValuesOf({ statuses: ['FAILED'], trigger: 'SCHEDULE', format: 'XML', columns: [{ header: 'orden', field: 'orderNumber' }], http: { url: 'https://x.example.com', method: 'PUT', authKind: 'BEARER', headers: [{ name: 'X-A', value: '1' }] } })
    expect(saved).toMatchObject({ statuses: ['FAILED'], trigger: 'SCHEDULE', format: 'XML', http: { url: 'https://x.example.com', method: 'PUT', authKind: 'BEARER', secret: '', headers: [{ name: 'X-A', value: '1' }] } })
  })
})

describe('formulario de una integración de salida', () => {
  const T = { nameRequired: 'n', nameMax: 'm', clientRequired: 'cliente', templateRequired: 'plantilla', folderRequired: 'f', folderInvalid: 'fi', interval: 'intervalo', retention: 'r', scopes: 'alcances' }
  const base: IntegrationValues = {
    name: ' Entregas al ERP ', description: '', clientPublicId: 'c1', templatePublicId: '', connectorKind: 'HTTP', folder: '', scheduleKind: 'MANUAL', intervalMinutes: '5', scopes: ['files.orders_export'], notifyUserIds: [], includeMe: true,
    retentionDays: '30', email: emptyEmail(), remote: emptyRemote('SFTP'), format: emptyFormat(), direction: 'OUTBOUND', output: withUrl({ authKind: 'BEARER', secret: 'tok' }),
  }
  const check = (v: Partial<IntegrationValues>) => {
    const r = integrationSchema(T, false, undefined, undefined, undefined, undefined, { messages: M, hasSavedSecret: false }).safeParse({ ...base, ...v })
    return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}:${i.message}`)
  }

  it('una salida no pide plantilla ni los datos de entrada, y valida su salida', () => {
    expect(check({})).toEqual([])
    expect(check({ clientPublicId: '' })).toEqual(['clientPublicId:cliente'])
    expect(check({ output: { ...base.output, statuses: [] } })).toEqual(['output.statuses:estatus'])
    expect(check({ output: withUrl({ url: '' }) })).toEqual(['output.http.url:url'])
  })

  it('por horario pide el intervalo (minutos) y al instante no', () => {
    expect(check({ output: { ...base.output, trigger: 'SCHEDULE' }, intervalMinutes: '0' })).toEqual(['intervalMinutes:intervalo'])
    expect(check({ output: { ...base.output, trigger: 'SCHEDULE' }, intervalMinutes: '60' })).toEqual([])
    expect(check({ intervalMinutes: 'x' })).toEqual([])
  })

  it('el alta manda dirección, salida y secreto, sin plantilla ni formato; el horario sale del disparador', () => {
    const req = buildCreateRequest(base)
    expect(req).toMatchObject({ direction: 'OUTBOUND', connectorKind: 'HTTP', templatePublicId: null, scheduleKind: 'MANUAL', httpSecret: { value: 'tok' }, scopes: ['files.orders_export'] })
    expect(req.output?.http?.url).toBe('https://api.cliente.com/x')
    expect(req).not.toHaveProperty('format')
    const timed = buildCreateRequest({ ...base, output: { ...base.output, trigger: 'SCHEDULE' }, intervalMinutes: '15' })
    expect(timed).toMatchObject({ scheduleKind: 'INTERVAL', intervalSeconds: 900 })
  })

  it('la edición manda la salida y solo manda el secreto si se escribió otro', () => {
    const keep = buildPatchRequest({ ...base, output: withUrl({ authKind: 'BEARER', secret: '' }) }, { connectorKind: 'HTTP', direction: 'OUTBOUND' })
    expect(keep.output?.http?.authKind).toBe('BEARER')
    expect(keep.httpSecret).toBeUndefined()
    expect(keep).not.toHaveProperty('templatePublicId')
    expect(keep).not.toHaveProperty('format')
    expect(buildPatchRequest(base, { connectorKind: 'HTTP', direction: 'OUTBOUND' }).httpSecret).toEqual({ value: 'tok' })
  })
})
