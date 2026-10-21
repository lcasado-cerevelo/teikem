// Recorrido del Lote 137b (P11) contra el API real: integraciones de SALIDA. Las órdenes del cliente que llegan a un estatus pedido (p. ej. Entregada) se arman en un archivo (CSV, JSON o XML) con las columnas
// configuradas y se mandan por la API saliente genérica (HTTP: POST/PUT con autenticación y encabezados) o por SFTP/FTP, al instante (un envío por orden) o por horario (un envío con todas las pendientes).
// Cubre: validaciones del alta con el mensaje exacto, solo salen órdenes del cliente de la integración, vista previa, probar la API, el trabajo de salida, fallos con reintentos de espera creciente hasta «no
// entregado» (bandeja de salidas con Reintentar y Descartar), y la subida real a servidores SFTP/FTP de prueba (si hay: REMOTE_TEST_SFTP_ROOT y REMOTE_TEST_FTP_ROOT). Requiere el API con
// Integrations__Webhooks__AllowInsecure=true (el receptor de prueba está en 127.0.0.1) y E2E_SQL_DB (para adelantar los reintentos). Escritorio y móvil.
import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { createServer, type Server } from 'node:http'
import type { AddressInfo } from 'node:net'
import { join } from 'node:path'
import { expect, test, type APIRequestContext } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

const SFTP_ROOT = process.env.REMOTE_TEST_SFTP_ROOT
const FTP_ROOT = process.env.REMOTE_TEST_FTP_ROOT

type Received = { method: string; path: string; headers: Record<string, string | string[] | undefined>; body: string }
type Integration = { publicId: string; name: string; direction: string; connectorKind: string; scheduleKind: string; hasHttpSecret?: boolean; output?: { trigger: string; format: string; columns: { header: string; field: string }[]; http?: { url: string } } }
type Run = { status: string; filesRead: number; ordersCreated: number; rejected: number; message?: string }
type Item = { id: number; fileName: string; format: string; orderCount: number; status: string; attemptCount: number; responseCode?: number; error?: string; preview: string; integrationName: string }
type Page = { items: Item[]; total: number; undelivered: number }
type Preview = { fileName: string; format: string; content: string; orderCount: number; sample: boolean }

function startReceiver(): Promise<{ server: Server; url: string; received: Received[]; failing: { on: boolean } }> {
  const received: Received[] = []
  const failing = { on: true }
  const server = createServer((req, res) => {
    const chunks: Buffer[] = []
    req.on('data', (c: Buffer) => chunks.push(c))
    req.on('end', () => {
      const path = req.url ?? ''
      received.push({ method: req.method ?? '', path, headers: req.headers, body: Buffer.concat(chunks).toString('utf8') })
      res.statusCode = path.startsWith('/fail') && failing.on ? 500 : 200
      res.end('ok')
    })
  })
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve({ server, url: `http://127.0.0.1:${(server.address() as AddressInfo).port}`, received, failing })))
}

const sql = (query: string) => execFileSync('sqlcmd', ['-S', process.env.E2E_SQL_SERVER ?? 'localhost,1433', '-E', '-C', '-I', '-d', process.env.E2E_SQL_DB!, '-Q', query])

const COLUMNS = [
  { header: 'orderNumber', field: 'orderNumber' },
  { header: 'status', field: 'status' },
  { header: 'recipientName', field: 'recipientName' },
  { header: 'deliveredAt', field: 'deliveredAt' },
  { header: 'pieces', field: 'pieces' },
  { header: 'consignee', field: 'consigneeName' },
]

async function runJob(request: APIRequestContext, headers: Record<string, string>) {
  type JobRun = { id: number; finishedUtc: string | null }
  const runs = () => ok<JobRun[]>(request.get(`${API_URL}/api/v1/jobs/INTEGRATION_OUTPUT/runs?take=200`, { headers }), 'corridas')
  const lastId = Math.max(0, ...(await runs()).map((r) => r.id))
  await expect.poll(async () => (await request.post(`${API_URL}/api/v1/jobs/INTEGRATION_OUTPUT/run`, { headers })).status(), { timeout: 120_000, intervals: [1000, 2000] }).toBe(200)
  await expect.poll(async () => (await runs()).some((r) => r.id > lastId && r.finishedUtc !== null), { timeout: 90_000, intervals: [500, 1000] }).toBe(true)
}

test('Salida: API saliente HTTP (al instante y por horario), reintentos, bandeja de salidas y subida por SFTP/FTP', async ({ request }, testInfo) => {
  test.skip(!process.env.E2E_SQL_DB, 'E2E_SQL_DB no está definido (hace falta para adelantar los reintentos)')
  const s = `${testInfo.project.name.startsWith('movil') ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)
  sql(`UPDATE dbo.IntegrationOutput SET Status = 'DISCARDED' WHERE Status = 'PENDING'`)   // restos de corridas anteriores apuntan a receptores que ya no existen
  const receiver = await startReceiver()
  try {
    const a = await createClient(request, headers, `SalidaA ${s}`)
    const b = await createClient(request, headers, `SalidaB ${s}`)
    const consA = await createConsignee(request, headers, a.publicId, `Colmado A ${s}`)
    const consB = await createConsignee(request, headers, b.publicId, `Colmado B ${s}`)
    const deliver = async (clientId: string, consigneeId: string, who: string) => {
      const order = await createOrder(request, headers, clientId, consigneeId)
      await ok(request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers, data: { recipientName: who } }), 'entrega')
      return order
    }
    const post = (data: object) => request.post(`${API_URL}/api/v1/integrations`, { headers, data })
    const httpOut = (name: string, extra: object = {}, out: object = {}, http: object = {}) =>
      post({
        name: `0 ${name} ${s}`, direction: 'OUTBOUND', connectorKind: 'HTTP', clientPublicId: a.publicId, scopes: ['files.orders_export'], includeMe: true,
        output: { statuses: ['DELIVERED'], trigger: 'EVENT', format: 'JSON', columns: COLUMNS, http: { url: `${receiver.url}/ok`, method: 'POST', authKind: 'BEARER', headers: [{ name: 'X-Cliente', value: 'acme' }], ...http }, ...out },
        httpSecret: { value: 'tok-secreto-123' }, ...extra,
      })

    // ---- opciones: campos y estatus del formulario; las de salida piden su alcance y su cliente
    const options = await ok<{ outputFields: { code: string }[]; orderStatuses: string[]; outboundConnectors: string[]; connectors: string[]; scopes: { code: string; available: boolean; needsClient: boolean }[] }>(request.get(`${API_URL}/api/v1/integrations/options`, { headers }), 'opciones')
    expect(options.outputFields.map((f) => f.code)).toContain('recipientName')
    expect(options.orderStatuses).toContain('DELIVERED')
    expect(options.outboundConnectors).toEqual(['SFTP', 'FTP', 'HTTP'])
    expect(options.connectors).not.toContain('HTTP')
    expect(options.scopes.find((x) => x.code === 'files.orders_export')).toMatchObject({ available: true, needsClient: true })

    // ---- validaciones del alta con el mensaje exacto
    const bad = async (patch: object, text: string) => {
      const res = await httpOut(`Mala ${Date.now()}`, patch)
      expect(res.status()).toBe(400)
      expect(await res.text()).toContain(text)
    }
    await bad({ clientPublicId: undefined }, 'Indique el cliente de las órdenes de esta integración.')
    await bad({ scopes: ['files.orders_import'] }, 'Una integración de salida solo admite el alcance de salida de órdenes.')
    await bad({ connectorKind: 'EMAIL' }, 'Una integración de salida usa SFTP, FTP o HTTP.')
    await bad({ output: undefined }, 'Indique qué se manda (estatus, formato y columnas).')
    await bad({ output: { statuses: [], trigger: 'EVENT', format: 'JSON', columns: COLUMNS, http: { url: `${receiver.url}/ok`, method: 'POST', authKind: 'NONE' } } }, 'Escoja al menos un estatus que dispare la salida.')
    await bad({ output: { statuses: ['DELIVERED'], trigger: 'EVENT', format: 'JSON', columns: [], http: { url: `${receiver.url}/ok`, method: 'POST', authKind: 'NONE' } } }, 'Agregue al menos una columna a la salida.')
    await bad({ output: { statuses: ['DELIVERED'], trigger: 'EVENT', format: 'JSON', columns: [{ header: 'Número de envío', field: 'orderNumber' }], http: { url: `${receiver.url}/ok`, method: 'POST', authKind: 'NONE' } } }, 'debe empezar con una letra o guion bajo')
    await bad({ output: { statuses: ['DELIVERED'], trigger: 'EVENT', format: 'JSON', columns: COLUMNS, http: { url: 'ftp://x', method: 'POST', authKind: 'NONE' } } }, 'La URL no es válida')
    await bad({ output: { statuses: ['DELIVERED'], trigger: 'EVENT', format: 'JSON', columns: COLUMNS, http: { url: `${receiver.url}/ok`, method: 'DELETE', authKind: 'NONE' } } }, 'Método desconocido: DELETE. Use POST o PUT.')
    await bad({ httpSecret: undefined }, 'Indique el token, la contraseña o el valor del encabezado de la autenticación.')
    await bad({ output: { statuses: ['DELIVERED'], trigger: 'SCHEDULE', format: 'JSON', columns: COLUMNS, http: { url: `${receiver.url}/ok`, method: 'POST', authKind: 'NONE' } } }, 'La salida por horario necesita un intervalo (minutos).')

    // ---- API saliente al instante: una orden entregada del cliente A sale en JSON, con la autenticación y los encabezados
    const intEvent = await ok<Integration>(httpOut('Api al instante'), 'integración HTTP')
    expect(intEvent).toMatchObject({ direction: 'OUTBOUND', connectorKind: 'HTTP', scheduleKind: 'MANUAL', hasHttpSecret: true })
    expect(JSON.stringify(intEvent)).not.toContain('tok-secreto-123')
    const orderA = await deliver(a.publicId, consA.publicId, `Ana ${s}`)
    const orderB = await deliver(b.publicId, consB.publicId, `Beto ${s}`)   // otro cliente: no sale
    const first = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/run`, { headers }), 'enviar ahora')
    expect(first).toMatchObject({ status: 'OK', filesRead: 1, ordersCreated: 1, rejected: 0 })
    const sent = receiver.received.filter((r) => r.path === '/ok')
    expect(sent).toHaveLength(1)
    expect(sent[0].method).toBe('POST')
    expect(sent[0].headers.authorization).toBe('Bearer tok-secreto-123')
    expect(sent[0].headers['x-cliente']).toBe('acme')
    expect(String(sent[0].headers['content-type'])).toContain('application/json')
    expect(String(sent[0].headers['idempotency-key'])).toMatch(/^teikem-output-\d+$/)
    const body = JSON.parse(sent[0].body) as { orders: Record<string, unknown>[] }
    expect(body.orders).toHaveLength(1)
    expect(body.orders[0]).toMatchObject({ orderNumber: orderA.orderNumber, status: 'DELIVERED', recipientName: `Ana ${s}`, consignee: `Colmado A ${s}` })
    expect(typeof body.orders[0].pieces).toBe('number')
    expect(String(body.orders[0].deliveredAt)).toMatch(/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$/)
    expect(sent[0].body).not.toContain(`Beto ${s}`)   // el número de envío es consecutivo por cliente: se distingue por quien recibió
    const nothing = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/run`, { headers }), 'enviar otra vez')
    expect(nothing.message).toBe('No hay envíos pendientes de enviar.')

    // ---- vista previa y probar la API
    const preview = await ok<Preview>(request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/output/preview`, { headers }), 'vista previa')
    expect(preview).toMatchObject({ format: 'JSON', sample: true })
    expect(preview.fileName).toMatch(/^ordenes-\d{8}-\d{6}\.json$/)
    expect(preview.content).toContain(orderA.orderNumber)
    const test = await ok<{ ok: boolean; message: string }>(request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/test-connection`, { headers }), 'probar la API')
    expect(test).toMatchObject({ ok: true, message: 'La API respondió 200.' })

    // ---- el trabajo de salida manda solo lo que llega después (sin «enviar ahora»)
    const orderA2 = await deliver(a.publicId, consA.publicId, `Ana2 ${s}`)
    await runJob(request, headers)
    await expect.poll(() => receiver.received.filter((r) => r.path === '/ok').length).toBe(3)   // la entrega, la prueba de la API y esta
    expect(JSON.parse(receiver.received.filter((r) => r.path === '/ok').at(-1)!.body).orders[0].orderNumber).toBe(orderA2.orderNumber)

    // ---- por horario y en CSV: todas las pendientes salen en UN envío (con PUT, otra autenticación y su intervalo)
    const intSchedule = await ok<Integration>(
      post({
        name: `0 Api por horario ${s}`, direction: 'OUTBOUND', connectorKind: 'HTTP', clientPublicId: a.publicId, scopes: ['files.orders_export'], scheduleKind: 'INTERVAL', intervalSeconds: 3600,
        output: { statuses: ['DELIVERED'], trigger: 'SCHEDULE', format: 'CSV', delimiter: ';', columns: COLUMNS.slice(0, 3), http: { url: `${receiver.url}/lote`, method: 'PUT', authKind: 'HEADER', authName: 'X-Api-Key' } },
        httpSecret: { value: 'llave-9' },
      }),
      'integración por horario',
    )
    expect(intSchedule).toMatchObject({ scheduleKind: 'INTERVAL', output: { trigger: 'SCHEDULE', format: 'CSV' } })
    const batch = [await deliver(a.publicId, consA.publicId, `L1 ${s}`), await deliver(a.publicId, consA.publicId, `L2 ${s}`)]
    const batchRun = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${intSchedule.publicId}/run`, { headers }), 'enviar el lote')
    expect(batchRun).toMatchObject({ status: 'OK', filesRead: 1, ordersCreated: 2 })
    const lot = receiver.received.filter((r) => r.path === '/lote')
    expect(lot).toHaveLength(1)
    expect(lot[0].method).toBe('PUT')
    expect(lot[0].headers['x-api-key']).toBe('llave-9')
    expect(String(lot[0].headers['content-type'])).toContain('text/csv')
    const lines = lot[0].body.split('\r\n').filter(Boolean)
    expect(lines[0]).toBe('orderNumber;status;recipientName')
    expect(lines).toHaveLength(3)
    for (const o of batch) expect(lot[0].body).toContain(o.orderNumber)

    // ---- editar lo que se manda (otras columnas y formato); el secreto no se pide otra vez
    const edited = await ok<Integration>(request.patch(`${API_URL}/api/v1/integrations/${intEvent.publicId}`, { headers, data: { output: { statuses: ['DELIVERED', 'FAILED'], trigger: 'EVENT', format: 'XML', columns: [{ header: 'orden', field: 'orderNumber' }, { header: 'estatus', field: 'status' }], http: { url: `${receiver.url}/ok`, method: 'POST', authKind: 'BEARER' } } } }), 'editar la salida')
    expect(edited.output).toMatchObject({ format: 'XML', trigger: 'EVENT' })
    expect(edited.hasHttpSecret).toBe(true)
    const orderA3 = await deliver(a.publicId, consA.publicId, `Ana3 ${s}`)
    await ok(request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/run`, { headers }), 'enviar en XML')
    const xml = receiver.received.filter((r) => r.path === '/ok').at(-1)!
    expect(String(xml.headers['content-type'])).toContain('application/xml')
    expect(xml.body).toContain(`<orden>${orderA3.orderNumber}</orden>`)
    expect(xml.body).toContain('<estatus>DELIVERED</estatus>')

    // ---- fallos: un receptor que responde 500 → reintentos de espera creciente hasta «no entregado» y la bandeja de salidas
    const intFail = await ok<Integration>(httpOut('Api que falla', {}, {}, { url: `${receiver.url}/fail`, authKind: 'NONE' }), 'integración que falla')
    const failOrder = await deliver(a.publicId, consA.publicId, `Falla ${s}`)
    let run = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${intFail.publicId}/run`, { headers }), 'primer intento')
    expect(run).toMatchObject({ status: 'PARTIAL', filesRead: 1, ordersCreated: 1 })
    expect(run.message).toContain('0 enviado(s), 1 por reintentar, 0 no entregado(s)')
    const outputsOf = (id: string, status?: string) => ok<Page>(request.get(`${API_URL}/api/v1/integrations/outputs?integration=${id}${status ? `&status=${status}` : ''}`, { headers }), 'envíos')
    let page = await outputsOf(intFail.publicId)
    expect(page.items).toHaveLength(1)
    expect(page.items[0]).toMatchObject({ status: 'PENDING', attemptCount: 1, responseCode: 500, error: 'El servidor respondió 500.' })
    for (let attempt = 2; attempt <= 6; attempt++) {
      sql(`UPDATE dbo.IntegrationOutput SET NextAttemptUtc = DATEADD(DAY, -1, SYSUTCDATETIME()) WHERE Status = 'PENDING'`)
      run = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${intFail.publicId}/run`, { headers }), `intento ${attempt}`)
    }
    expect(run).toMatchObject({ status: 'FAILED', rejected: 1 })
    page = await outputsOf(intFail.publicId, 'UNDELIVERED')
    expect(page.items).toHaveLength(1)
    expect(page.items[0]).toMatchObject({ attemptCount: 6, status: 'UNDELIVERED', orderCount: 1 })
    expect(page.items[0].preview).toContain(failOrder.orderNumber)
    expect(page.undelivered).toBeGreaterThanOrEqual(1)

    // reintentar con el receptor ya bien: sale; descartar otro; lo ya enviado no se descarta ni se reintenta
    receiver.failing.on = false
    const retried = await ok<Item>(request.post(`${API_URL}/api/v1/integrations/outputs/${page.items[0].id}/retry`, { headers }), 'reintentar')
    expect(retried).toMatchObject({ status: 'PENDING', attemptCount: 0 })
    expect((await request.post(`${API_URL}/api/v1/integrations/outputs/${page.items[0].id}/retry`, { headers })).status()).toBe(409)
    run = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${intFail.publicId}/run`, { headers }), 'enviar el reintento')
    expect(run).toMatchObject({ status: 'OK' })
    const sentItems = (await outputsOf(intFail.publicId, 'SENT')).items
    expect(sentItems).toHaveLength(1)
    const noDiscard = await request.post(`${API_URL}/api/v1/integrations/outputs/${sentItems[0].id}/discard`, { headers })
    expect(noDiscard.status()).toBe(409)
    expect(await noDiscard.text()).toContain('El envío ya se entregó.')
    receiver.failing.on = true
    await deliver(a.publicId, consA.publicId, `Falla2 ${s}`)
    await ok(request.post(`${API_URL}/api/v1/integrations/${intFail.publicId}/run`, { headers }), 'otro fallo')
    const pending = (await outputsOf(intFail.publicId, 'PENDING')).items[0]
    const discarded = await ok<Item>(request.post(`${API_URL}/api/v1/integrations/outputs/${pending.id}/discard`, { headers }), 'descartar')
    expect(discarded.status).toBe('DISCARDED')
    expect((await request.post(`${API_URL}/api/v1/integrations/outputs/${pending.id}/discard`, { headers })).status()).toBe(409)
    expect((await request.post(`${API_URL}/api/v1/integrations/outputs/${pending.id}/retry`, { headers })).status()).toBe(409)

    // ---- una integración de salida no recibe archivos y, apagada, no manda
    const upload = await request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/upload`, { headers, multipart: { file: { name: 'x.csv', mimeType: 'text/csv', buffer: Buffer.from('a,b') } } })
    expect(upload.status()).toBe(400)
    expect(await upload.text()).toContain('Esta integración es de salida: no recibe archivos.')
    await ok(request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/deactivate`, { headers }), 'apagar')
    await deliver(a.publicId, consA.publicId, `Apagada ${s}`)
    const countOk = () => receiver.received.filter((r) => r.path === '/ok').length
    const before = countOk()
    await runJob(request, headers)
    expect(countOk()).toBe(before)
    expect((await request.post(`${API_URL}/api/v1/integrations/${intEvent.publicId}/run`, { headers })).status()).toBe(422)

    // ---- SFTP y FTP de verdad (si hay servidores de prueba): el archivo llega completo, con su nombre, sin «.part»
    if (SFTP_ROOT && FTP_ROOT) {
      const sftpDir = `salida-${s}`
      const sftpRemote = { host: '127.0.0.1', port: 2222, username: 'teikem', dir: `/${sftpDir}`, authKind: 'PASSWORD' }
      const out = { statuses: ['DELIVERED'], trigger: 'EVENT', format: 'CSV', delimiter: ',', fileName: 'entregas-{date}-{id}.{ext}', columns: COLUMNS.slice(0, 4) }
      const sftp = await ok<Integration>(post({ name: `0 Sftp salida ${s}`, direction: 'OUTBOUND', connectorKind: 'SFTP', clientPublicId: a.publicId, scopes: ['files.orders_export'], remote: sftpRemote, secret: { password: 'clave-sftp' }, output: out }), 'salida SFTP')
      const pin = await ok<{ fingerprint: string }>(request.post(`${API_URL}/api/v1/integrations/${sftp.publicId}/test-connection`, { headers }), 'huella')
      await ok(request.patch(`${API_URL}/api/v1/integrations/${sftp.publicId}`, { headers, data: { remote: { ...sftpRemote, hostFingerprint: pin.fingerprint } } }), 'fijar la huella')
      const ftpDir = `/salida-${s}`
      const ftp = await ok<Integration>(post({ name: `0 Ftp salida ${s}`, direction: 'OUTBOUND', connectorKind: 'FTP', clientPublicId: a.publicId, scopes: ['files.orders_export'], remote: { host: '127.0.0.1', port: 2121, username: 'teikem', dir: ftpDir, useFtps: false }, secret: { password: 'clave-ftp' }, output: { ...out, format: 'JSON' } }), 'salida FTP')
      const orderS = await deliver(a.publicId, consA.publicId, `Archivo ${s}`)
      await ok(request.post(`${API_URL}/api/v1/integrations/${sftp.publicId}/run`, { headers }), 'subir por SFTP')
      await ok(request.post(`${API_URL}/api/v1/integrations/${ftp.publicId}/run`, { headers }), 'subir por FTP')
      const sftpFiles = readdirSync(join(SFTP_ROOT, sftpDir))
      expect(sftpFiles).toHaveLength(1)
      expect(sftpFiles[0]).toMatch(/^entregas-\d{8}-\d+\.csv$/)
      const csv = readFileSync(join(SFTP_ROOT, sftpDir, sftpFiles[0]), 'utf8')
      expect(csv.split('\r\n')[0]).toBe('orderNumber,status,recipientName,deliveredAt')
      expect(csv).toContain(orderS.orderNumber)
      const ftpPath = join(FTP_ROOT, ftpDir)
      expect(existsSync(ftpPath)).toBe(true)
      const ftpFiles = readdirSync(ftpPath)
      expect(ftpFiles).toHaveLength(1)
      expect(ftpFiles[0]).toMatch(/^entregas-\d{8}-\d+\.json$/)
      expect(JSON.parse(readFileSync(join(ftpPath, ftpFiles[0]), 'utf8')).orders[0]).toMatchObject({ orderNumber: orderS.orderNumber, status: 'DELIVERED', recipientName: `Archivo ${s}` })
      expect(sftpFiles.concat(ftpFiles).some((f) => f.endsWith('.part'))).toBe(false)
    }
  } finally {
    receiver.server.close()
  }
})

test('pantalla: crear una integración de salida (API saliente), probar, enviar ahora, vista previa, editar y la bandeja de salidas', async ({ page, request }, testInfo) => {
  test.skip(!process.env.E2E_SQL_DB, 'E2E_SQL_DB no está definido')
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)
  sql(`UPDATE dbo.IntegrationOutput SET Status = 'DISCARDED' WHERE Status = 'PENDING'`)
  sql(`UPDATE dbo.Integration SET IsActive = 0, NextRunUtc = NULL WHERE IsActive = 1`)   // la lista (25 por página) solo muestra lo encendido: así se ve lo de esta prueba
  const receiver = await startReceiver()
  try {
    const client = await createClient(request, headers, `SalidaUi ${s}`)
    const cons = await createConsignee(request, headers, client.publicId, `Colmado UI ${s}`)
    const name = `0 Salida UI ${s}`

    await login(page)
    await page.goto('/system/integrations')
    await page.getByRole('button', { name: 'Nueva integración' }).click()
    const form = page.getByRole('dialog', { name: 'Nueva integración' })
    await form.getByLabel(/^Dirección/).selectOption('OUTBOUND')
    await expect(form.getByLabel(/^Conector/)).toHaveValue('HTTP')
    await expect(form.getByLabel(/^Plantilla de importación/)).toHaveCount(0)                  // una salida no usa plantilla
    await expect(form.locator('fieldset', { hasText: 'Formato del archivo' })).toHaveCount(0)
    await expect(form.getByRole('switch', { name: 'Salida de envíos por archivo o API' })).toBeChecked()
    await expect(form.getByRole('switch', { name: 'Entrada de envíos por archivo' })).toHaveCount(0)
    await form.getByLabel(/^Nombre/).first().fill(name)
    await form.getByLabel(/^Cliente/).selectOption(client.publicId)
    await form.getByLabel(/^Autenticación/).selectOption('BEARER')

    // validaciones del formulario con su mensaje
    await form.getByRole('button', { name: 'Crear integración' }).click()
    await expect(form.getByText('Indique la URL a la que se enviarán los avisos.')).toBeVisible()
    await expect(form.getByText('Indique el token, la contraseña o el valor del encabezado de la autenticación.')).toBeVisible()
    await form.getByLabel(/^URL/).fill(`${receiver.url}/ok`)
    await form.getByLabel(/^Token/).fill('tok-ui-123')
    await form.getByRole('button', { name: 'Agregar encabezado' }).click()
    await form.getByLabel('Encabezado', { exact: true }).fill('X-Cliente')
    await form.getByLabel('Valor', { exact: true }).fill('acme')
    // al instante, entregadas, JSON y las columnas por omisión
    await form.getByLabel(/^Formato$/).selectOption('JSON')
    await expect(form.getByLabel('Nombre', { exact: true })).toHaveCount(4)   // las 4 columnas por omisión
    await shot(page, isMobile, 'l137b-formulario', 2200, form)
    await form.getByRole('button', { name: 'Crear integración' }).click()
    const row = page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()
    await expect(row).toBeVisible({ timeout: 30_000 })
    await expect(row).toContainText('API saliente (HTTP)')
    await expect(row.getByRole('button', { name: 'Subir archivo' })).toHaveCount(0)

    // probar la API
    await row.getByRole('button', { name: 'Probar conexión' }).click()
    await expect(page.locator('.toast.on').first()).toContainText('La API respondió 200.', { timeout: 15_000 })
    expect(receiver.received.some((r) => r.headers.authorization === 'Bearer tok-ui-123' && r.headers['x-cliente'] === 'acme')).toBe(true)

    // una entrega del cliente y «Enviar ahora»
    const order = await createOrder(request, headers, client.publicId, cons.publicId)
    await ok(request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers, data: { recipientName: `Recibe ${s}` } }), 'entrega')
    await row.getByRole('button', { name: 'Enviar ahora' }).click()
    await expect.poll(() => receiver.received.filter((r) => r.body.includes(`Recibe ${s}`)).length, { timeout: 20_000 }).toBe(1)
    await expect(row).toContainText('1 envío(s) armado(s) con 1 envío(es): 1 enviado(s)', { timeout: 20_000 })

    // vista previa
    await row.getByRole('button', { name: 'Vista previa' }).click()
    const preview = page.getByRole('dialog', { name: /Vista previa de/ })
    await expect(preview.getByTestId('output-preview')).toContainText(`Recibe ${s}`, { timeout: 15_000 })
    await expect(preview.getByTestId('output-preview')).toContainText('"orders"')
    await shot(page, isMobile, 'l137b-vista-previa', 800)
    await preview.getByRole('button', { name: 'Cerrar' }).first().click()

    // editar: lo guardado vuelve (sin el secreto) y se puede cambiar el formato
    await row.getByRole('button', { name: 'Editar' }).click()
    const edit = page.getByRole('dialog', { name: /Editar/ })
    await expect(edit.getByLabel(/^URL/)).toHaveValue(`${receiver.url}/ok`)
    await expect(edit.getByText('Déjelo vacío para conservar el guardado.')).toBeVisible()
    await expect(edit.getByLabel(/^Dirección/)).toHaveCount(0)                                  // la dirección no se cambia
    await edit.getByLabel(/^Formato$/).selectOption('CSV')
    await edit.getByRole('button', { name: 'Guardar' }).click()
    await expect(page.locator('.toast.on').first()).toContainText('Se guardó', { timeout: 15_000 })

    // bandeja de salidas: un envío que agotó los reintentos, su contenido, reintentar y descartar
    const fail = await ok<Integration>(request.post(`${API_URL}/api/v1/integrations`, {
      headers, data: { name: `0 Salida falla UI ${s}`, direction: 'OUTBOUND', connectorKind: 'HTTP', clientPublicId: client.publicId, scopes: ['files.orders_export'], output: { statuses: ['DELIVERED'], trigger: 'EVENT', format: 'JSON', columns: COLUMNS, http: { url: `${receiver.url}/fail`, method: 'POST', authKind: 'NONE' } } },
    }), 'integración que falla')
    const failOrder = await createOrder(request, headers, client.publicId, cons.publicId)
    await ok(request.post(`${API_URL}/api/v1/orders/${failOrder.publicId}/delivery`, { headers, data: { recipientName: `Falla ${s}` } }), 'entrega')
    for (let attempt = 1; attempt <= 6; attempt++) {
      sql(`UPDATE dbo.IntegrationOutput SET NextAttemptUtc = DATEADD(DAY, -1, SYSUTCDATETIME()) WHERE Status = 'PENDING'`)
      await ok(request.post(`${API_URL}/api/v1/integrations/${fail.publicId}/run`, { headers }), `intento ${attempt}`)
    }
    await page.goto(`/system/integrations?tab=outbox&integration=${fail.publicId}`)
    await expect(page.getByRole('tab', { name: /Salidas \(\d+\)/ })).toBeVisible({ timeout: 30_000 })
    const outRow = page.locator('tr, li.dt-card, .dt-cards > li').filter({ hasText: 'El servidor respondió 500.' }).filter({ hasText: fail.name })
    await expect(outRow.first()).toBeVisible({ timeout: 30_000 })
    await expect(outRow.first()).toContainText('No entregado')
    await shot(page, isMobile, 'l137b-salidas', 1300)
    await outRow.first().getByRole('button', { name: 'Ver contenido' }).click()
    const view = page.getByRole('dialog', { name: /Envío/ })
    await expect(view.getByTestId('output-content')).toContainText(`Falla ${s}`)
    await view.getByRole('button', { name: 'Cerrar' }).first().click()
    receiver.failing.on = false
    await outRow.first().getByRole('button', { name: 'Reintentar' }).click()
    await expect(page.locator('.toast.on').first()).toContainText('Envío puesto a reintento.', { timeout: 15_000 })
    await ok(request.post(`${API_URL}/api/v1/integrations/${fail.publicId}/run`, { headers }), 'enviar el reintento')
    await expect.poll(() => receiver.received.filter((r) => r.path === '/fail' && r.body.includes(`Falla ${s}`)).length, { timeout: 20_000 }).toBeGreaterThanOrEqual(7)
    if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  } finally {
    receiver.server.close()
  }
})
