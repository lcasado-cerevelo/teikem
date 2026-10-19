// Recorrido del Lote 136 (P11) contra el API real y un receptor HTTP de prueba: webhooks de salida. El cliente (con su llave y el alcance webhooks:manage) se suscribe a lo SUYO, el secreto de firma sale
// una sola vez, cada entrega va firmada con HMAC-SHA256 (marca de tiempo contra repetición), solo salen eventos de su cliente, una URL que falla se reintenta con espera creciente y tras seis intentos queda
// «no entregada» (bandeja de salidas); cinco seguidas suspenden el webhook (y no se puede reintentar hasta reactivarlo). Escritorio y móvil.
// Requiere el API con Integrations__Webhooks__AllowInsecure=true (el receptor de prueba está en 127.0.0.1) y E2E_SQL_DB (para adelantar la hora del siguiente intento, como hace el tiempo).
import { execFileSync } from 'node:child_process'
import { createHmac } from 'node:crypto'
import { createServer, type Server } from 'node:http'
import type { AddressInfo } from 'node:net'
import { expect, test, type APIRequestContext } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

type Received = { path: string; headers: Record<string, string | string[] | undefined>; body: string }
type Webhook = { publicId: string; url: string; events: string[]; secretLast4: string; isActive: boolean; consecutiveFailures: number; suspendedAtUtc: string | null; lastStatus: string | null }
type Created = { webhook: Webhook; secret: string }
type Delivery = { id: number; eventType: string; status: string; attemptCount: number; responseCode: number | null; error: string | null; payloadJson: string | null }
type DeliveryPage = { items: Delivery[]; total: number; undelivered: number }
type Integration = { publicId: string; name: string }

function startReceiver(): Promise<{ server: Server; url: string; received: Received[]; failing: { on: boolean } }> {
  const received: Received[] = []
  const failing = { on: true }
  const server = createServer((req, res) => {
    const chunks: Buffer[] = []
    req.on('data', (c: Buffer) => chunks.push(c))
    req.on('end', () => {
      const path = req.url ?? ''
      received.push({ path, headers: req.headers, body: Buffer.concat(chunks).toString('utf8') })
      res.statusCode = path.startsWith('/fail') && failing.on ? 500 : 200
      res.end('ok')
    })
  })
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve({ server, url: `http://127.0.0.1:${(server.address() as AddressInfo).port}`, received, failing })))
}

const sql = (query: string) =>
  execFileSync('sqlcmd', ['-S', process.env.E2E_SQL_SERVER ?? 'localhost,1433', '-E', '-C', '-I', '-d', process.env.E2E_SQL_DB!, '-Q', query])

/** Pide correr el trabajo de entrega y espera a que el trabajador (que lo recoge en su siguiente vuelta) TERMINE esa corrida. */
async function runJob(request: APIRequestContext, headers: Record<string, string>) {
  type Run = { id: number; finishedUtc: string | null }
  const runs = () => ok<Run[]>(request.get(`${API_URL}/api/v1/jobs/WEBHOOK_DELIVERY/runs?take=200`, { headers }), 'corridas')
  const lastId = Math.max(0, ...(await runs()).map((r) => r.id))
  // si ya hay una corrida en curso (409) se espera a que termine y se vuelve a pedir
  await expect
    .poll(async () => (await request.post(`${API_URL}/api/v1/jobs/WEBHOOK_DELIVERY/run`, { headers })).status(), { timeout: 120_000, intervals: [1000, 2000] })
    .toBe(200)
  await expect.poll(async () => (await runs()).some((r) => r.id > lastId && r.finishedUtc !== null), { timeout: 90_000, intervals: [500, 1000] }).toBe(true)
}

/** Corre el trabajo hasta que se cumpla la condición (el trabajador puede estar a medias con otra corrida cuando se pide la nuestra). */
async function runUntil(request: APIRequestContext, headers: Record<string, string>, done: () => Promise<boolean> | boolean) {
  for (let i = 0; i < 6; i++) {
    await runJob(request, headers)
    if (await done()) return
  }
  expect(await done(), 'el trabajo de entrega no dejó el resultado esperado').toBe(true)
}

/** Adelanta la hora del siguiente intento de lo pendiente (el trabajo real espera 1 min, 5, 30, 2 h, 12 h). */
const dueNow = () => sql(`UPDATE dbo.IntegrationWebhookDelivery SET NextAttemptUtc = DATEADD(DAY, -1, SYSUTCDATETIME()) WHERE Status = 'PENDING'`)

test('webhooks: suscripción por API, firma HMAC, solo lo del cliente, reintentos, no entregadas y suspensión', async ({ request }, testInfo) => {
  test.skip(!process.env.E2E_SQL_DB, 'E2E_SQL_DB no está definido (hace falta para adelantar los reintentos)')
  const s = `${testInfo.project.name.startsWith('movil') ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)
  // restos de corridas anteriores apuntan a receptores que ya no existen: no estorban a esta
  sql(`UPDATE dbo.IntegrationWebhookDelivery SET Status = 'DISCARDED' WHERE Status = 'PENDING'`)
  const receiver = await startReceiver()
  try {
    const a = await createClient(request, headers, `HookA ${s}`)
    const b = await createClient(request, headers, `HookB ${s}`)
    const consA = await createConsignee(request, headers, a.publicId, `Colmado A ${s}`)
    const consB = await createConsignee(request, headers, b.publicId, `Colmado B ${s}`)
    const deliver = async (clientId: string, consigneeId: string, who: string) => {
      const order = await createOrder(request, headers, clientId, consigneeId)
      await ok(request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers, data: { recipientName: who } }), 'entrega')
      return order
    }

    // ---- integraciones: una con el alcance de webhooks (cliente A) y otra sin él
    const post = (data: object) => request.post(`${API_URL}/api/v1/integrations`, { headers, data })
    const intHook = await ok<Integration>(post({ name: `Hooks ${s}`, connectorKind: 'API', clientPublicId: a.publicId, scopes: ['webhooks:manage', 'orders:read'], includeMe: true }), 'integración con webhooks')
    const intNo = await ok<Integration>(post({ name: `Sin hooks ${s}`, connectorKind: 'API', clientPublicId: a.publicId, scopes: ['orders:read'] }), 'integración sin webhooks')
    const keyOf = async (integration: Integration) => {
      const k = await ok<{ key: { keyId: string }; secret: string }>(request.post(`${API_URL}/api/v1/integrations/${integration.publicId}/api-keys`, { headers, data: { label: 'Prueba' } }), 'llave')
      const t = await ok<{ accessToken: string }>(request.post(`${API_URL}/api/v1/api-auth/token`, { data: { clientId: k.key.keyId, clientSecret: k.secret } }), 'token')
      return { Authorization: `Bearer ${t.accessToken}` }
    }
    const api = await keyOf(intHook)
    const apiNo = await keyOf(intNo)

    // ---- el alcance manda: sin webhooks:manage 403; una persona (no llave) también
    expect((await request.get(`${API_URL}/api/v1/webhooks`, { headers: apiNo })).status()).toBe(403)
    const noScope = await request.post(`${API_URL}/api/v1/integrations/${intNo.publicId}/webhooks`, { headers, data: { url: `${receiver.url}/ok`, events: ['order.delivered'] } })
    expect(noScope.status()).toBe(400)
    expect(await noScope.text()).toContain('La integración no tiene el alcance «webhooks:manage».')
    const person = await request.get(`${API_URL}/api/v1/webhooks`, { headers })
    expect(person.status()).toBe(403)
    expect(await person.text()).toContain('Esta ruta es para llaves de API')

    // ---- validaciones con el mensaje exacto
    const create = (data: object, as = api) => request.post(`${API_URL}/api/v1/webhooks`, { headers: as, data })
    const bad = async (data: object, text: string) => {
      const res = await create(data)
      expect(res.status()).toBe(400)
      expect(await res.text()).toContain(text)
    }
    await bad({ url: '', events: ['order.delivered'] }, 'Indique la URL a la que se enviarán los avisos.')
    await bad({ url: 'no es una url', events: ['order.delivered'] }, 'La URL no es válida: debe ser una dirección completa (https://…) sin usuario ni contraseña.')
    await bad({ url: `${receiver.url}/ok`, events: [] }, 'Escoja al menos un evento.')
    await bad({ url: `${receiver.url}/ok`, events: ['order.created'] }, 'Evento desconocido: order.created. Use order.status_changed, order.delivered, invoice.issued.')

    // ---- crear: el secreto sale una vez
    const w1 = await ok<Created>(create({ url: `${receiver.url}/ok`, events: ['order.status_changed', 'order.delivered'] }), 'crear webhook')
    expect(w1.secret).toMatch(/^whsec_/)
    expect(w1.webhook).toMatchObject({ isActive: true, consecutiveFailures: 0, secretLast4: w1.secret.slice(-4) })
    expect(w1.webhook.suspendedAtUtc ?? null).toBeNull()
    const listText = await (await request.get(`${API_URL}/api/v1/webhooks`, { headers: api })).text()
    expect(listText).not.toContain(w1.secret)
    expect((await request.get(`${API_URL}/api/v1/integrations/${intHook.publicId}/webhooks`, { headers })).status()).toBe(200)

    // ---- probar: llega un webhook.ping firmado y vigente
    const test1 = await ok<{ ok: boolean; responseCode: number }>(request.post(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}/test`, { headers: api }), 'probar')
    expect(test1).toMatchObject({ ok: true, responseCode: 200 })
    const ping = receiver.received.at(-1)!
    expect(ping.headers['x-teikem-event']).toBe('webhook.ping')
    const verify = (r: Received, secret: string) => {
      const ts = String(r.headers['x-teikem-timestamp'])
      return r.headers['x-teikem-signature'] === `sha256=${createHmac('sha256', secret).update(`${ts}.${r.body}`).digest('hex')}` && Math.abs(Date.now() / 1000 - Number(ts)) < 300
    }
    expect(verify(ping, w1.secret)).toBe(true)
    expect(verify(ping, 'whsec_otro')).toBe(false)
    expect(JSON.parse(ping.body).type).toBe('webhook.ping')

    // ---- una entrega del cliente A sale (cambio de estatus + entregada); una del cliente B no
    const orderA = await deliver(a.publicId, consA.publicId, `Ana ${s}`)
    const orderB = await deliver(b.publicId, consB.publicId, `Beto ${s}`)
    await runUntil(request, headers, () => receiver.received.some((r) => r.path === '/ok' && r.headers['x-teikem-event'] === 'order.delivered'))
    const sent = receiver.received.filter((r) => r.path === '/ok' && r.headers['x-teikem-event'] !== 'webhook.ping')
    // la entrega de una orden recién sometida recorre varios estatus: un «order.status_changed» por cada uno y un solo «order.delivered»
    const types = sent.map((r) => r.headers['x-teikem-event'])
    expect(types.filter((t) => t === 'order.delivered')).toHaveLength(1)
    expect(types.filter((t) => t === 'order.status_changed').length).toBeGreaterThanOrEqual(2)
    const bodies = sent.map((r) => JSON.parse(r.body) as { id: string; type: string; data: { orderId: string; orderNumber: string; status: string; previousStatus?: string } })
    for (const [i, r] of sent.entries()) {
      expect(verify(r, w1.secret)).toBe(true)
      expect(bodies[i].data).toMatchObject({ orderId: orderA.publicId, orderNumber: orderA.orderNumber })
      expect(r.headers['x-teikem-delivery']).toBe(bodies[i].id)
    }
    expect(bodies.filter((b) => b.type === 'order.delivered')[0].data.status).toBe('DELIVERED')
    expect(bodies.filter((b) => b.type === 'order.status_changed').at(-1)!.data).toMatchObject({ status: 'DELIVERED' })
    expect(JSON.stringify(receiver.received)).not.toContain(orderB.publicId)
    const page1 = await ok<DeliveryPage>(request.get(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}/deliveries?take=50`, { headers: api }), 'salidas')
    expect(page1.items).toHaveLength(sent.length)
    expect(page1.items.every((d) => d.status === 'DELIVERED')).toBe(true)
    expect(page1.items.every((d) => d.attemptCount === 1 && d.responseCode === 200)).toBe(true)
    expect(page1.items[0].payloadJson).toContain(orderA.orderNumber)

    // ---- aislamiento entre webhooks e integraciones
    expect((await request.get(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}`, { headers: apiNo })).status()).toBe(403)

    // ---- rotar el secreto: el viejo deja de servir
    const rotated = await ok<Created>(request.post(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}/rotate-secret`, { headers: api }), 'rotar')
    expect(rotated.secret).not.toBe(w1.secret)
    await ok(request.post(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}/test`, { headers: api }), 'probar')
    const ping2 = receiver.received.at(-1)!
    expect(verify(ping2, rotated.secret)).toBe(true)
    expect(verify(ping2, w1.secret)).toBe(false)

    // ---- reintentos: un webhook a una URL que responde 500 (cinco entregas seguidas lo suspenden)
    const w2 = await ok<Created>(create({ url: `${receiver.url}/fail`, events: ['order.delivered'] }), 'webhook que falla')
    for (let i = 0; i < 5; i++) await deliver(a.publicId, consA.publicId, `Falla ${i} ${s}`)
    const w2Deliveries = async () => (await ok<DeliveryPage>(request.get(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}/deliveries?take=50`, { headers: api }), 'salidas del webhook que falla')).items
    await runUntil(request, headers, async () => (await w2Deliveries()).filter((d) => d.attemptCount >= 1).length === 5)
    let outbox = await ok<DeliveryPage>(request.get(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}/deliveries?take=50`, { headers: api }), 'salidas fallidas')
    expect(outbox.items).toHaveLength(5)
    expect(outbox.items.every((d) => d.status === 'PENDING' && d.attemptCount === 1 && d.responseCode === 500 && d.error === 'El servidor respondió 500.')).toBe(true)
    for (let attempt = 2; attempt <= 6; attempt++) {
      dueNow()
      await runUntil(request, headers, async () => (await w2Deliveries()).every((d) => d.attemptCount >= attempt || d.status === 'UNDELIVERED'))
    }
    outbox = await ok<DeliveryPage>(request.get(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}/deliveries?take=50`, { headers: api }), 'salidas no entregadas')
    expect(outbox.items.every((d) => d.status === 'UNDELIVERED' && d.attemptCount === 6)).toBe(true)
    const suspended = await ok<Webhook>(request.get(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}`, { headers: api }), 'suspendido')
    expect(suspended).toMatchObject({ consecutiveFailures: 5, lastStatus: 'UNDELIVERED' })
    expect(suspended.suspendedAtUtc).not.toBeNull()

    // ---- la bandeja del personal: no se reintenta mientras esté suspendido; reactivar, arreglar y reintentar
    const staffOutbox = await ok<DeliveryPage>(request.get(`${API_URL}/api/v1/integrations/outbox?integration=${intHook.publicId}&status=UNDELIVERED`, { headers }), 'bandeja de salidas')
    expect(staffOutbox.items).toHaveLength(5)
    expect(staffOutbox.undelivered).toBeGreaterThanOrEqual(5)
    const retryBlocked = await request.post(`${API_URL}/api/v1/integrations/outbox/${staffOutbox.items[0].id}/retry`, { headers })
    expect(retryBlocked.status()).toBe(409)
    expect(await retryBlocked.text()).toContain('El webhook está suspendido: reactívelo antes de reintentar.')
    expect((await request.post(`${API_URL}/api/v1/integrations/${intHook.publicId}/webhooks/${w1.webhook.publicId}/resume`, { headers })).status()).toBe(409)   // no estaba suspendido
    await ok(request.post(`${API_URL}/api/v1/integrations/${intHook.publicId}/webhooks/${w2.webhook.publicId}/resume`, { headers }), 'reactivar')
    receiver.failing.on = false
    const retried = await ok<Delivery>(request.post(`${API_URL}/api/v1/integrations/outbox/${staffOutbox.items[0].id}/retry`, { headers }), 'reintentar')
    expect(retried).toMatchObject({ status: 'PENDING', attemptCount: 0 })
    expect((await request.post(`${API_URL}/api/v1/integrations/outbox/${staffOutbox.items[0].id}/retry`, { headers })).status()).toBe(409)   // ya no está no entregada
    const discarded = await ok<Delivery>(request.post(`${API_URL}/api/v1/integrations/outbox/${staffOutbox.items[1].id}/discard`, { headers }), 'descartar')
    expect(discarded.status).toBe('DISCARDED')
    expect((await request.post(`${API_URL}/api/v1/integrations/outbox/${staffOutbox.items[1].id}/discard`, { headers })).status()).toBe(409)
    await runUntil(request, headers, async () => (await w2Deliveries()).some((d) => d.id === staffOutbox.items[0].id && d.status === 'DELIVERED'))
    outbox = await ok<DeliveryPage>(request.get(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}/deliveries?take=50`, { headers: api }), 'salidas')
    const byId = Object.fromEntries(outbox.items.map((d) => [d.id, d.status]))
    expect(byId[staffOutbox.items[0].id]).toBe('DELIVERED')
    expect(byId[staffOutbox.items[1].id]).toBe('DISCARDED')
    expect(await ok<Webhook>(request.get(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}`, { headers: api }), 'reactivado')).toMatchObject({ consecutiveFailures: 0 })

    // ---- editar y quitar
    const edited = await ok<Webhook>(request.patch(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}`, { headers: api, data: { events: ['order.delivered'] } }), 'editar')
    expect(edited.events).toEqual(['order.delivered'])
    const editBad = await request.patch(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}`, { headers: api, data: { url: 'ftp://x.example.com' } })
    expect(editBad.status()).toBe(400)
    expect((await request.delete(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}`, { headers: api })).status()).toBe(204)
    expect((await request.get(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}`, { headers: api })).status()).toBe(404)
    expect((await ok<Webhook[]>(request.get(`${API_URL}/api/v1/webhooks`, { headers: api }), 'lista')).map((w) => w.publicId)).toEqual([w1.webhook.publicId])
    expect((await request.delete(`${API_URL}/api/v1/webhooks/${w2.webhook.publicId}`, { headers: api })).status()).toBe(404)
    // y la llave sin el alcance no puede usar el DELETE ni nada de /webhooks
    expect((await request.delete(`${API_URL}/api/v1/webhooks/${w1.webhook.publicId}`, { headers: apiNo })).status()).toBe(403)
  } finally {
    receiver.server.close()
  }
})

test('pantalla: webhooks de una integración (secreto una sola vez, probar, rotar) y bandeja de salidas no entregadas', async ({ page, request }, testInfo) => {
  test.skip(!process.env.E2E_SQL_DB, 'E2E_SQL_DB no está definido')
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)
  const receiver = await startReceiver()
  try {
    const client = await createClient(request, headers, `HookUi ${s}`)
    const integration = await ok<Integration>(request.post(`${API_URL}/api/v1/integrations`, { headers, data: { name: `0 Hooks UI ${s}`, connectorKind: 'API', clientPublicId: client.publicId, scopes: ['webhooks:manage', 'orders:read'], includeMe: true } }), 'integración')
    await ok<Integration>(request.post(`${API_URL}/api/v1/integrations`, { headers, data: { name: `0 Sin hooks UI ${s}`, connectorKind: 'API', clientPublicId: client.publicId, scopes: ['orders:read'] } }), 'integración sin webhooks')

    await login(page)
    await page.goto('/system/integrations')
    const rowOf = (name: string) => page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()
    await expect(rowOf(`0 Hooks UI ${s}`)).toBeVisible({ timeout: 30_000 })
    await expect(rowOf(`0 Sin hooks UI ${s}`).getByRole('button', { name: 'Webhooks' })).toHaveCount(0)    // sin el alcance no hay acción
    await rowOf(`0 Hooks UI ${s}`).getByRole('button', { name: 'Webhooks' }).click()
    const dialog = page.getByRole('dialog', { name: /Webhooks de/ })

    // validaciones con el mensaje exacto
    await dialog.getByRole('button', { name: 'Crear webhook' }).click()
    await expect(dialog.getByText('Indique la URL a la que se enviarán los avisos.')).toBeVisible()
    await dialog.getByLabel(/^URL del cliente/).fill('no es una url')
    await dialog.getByRole('button', { name: 'Crear webhook' }).click()
    await expect(dialog.getByText('La URL no es válida: debe ser una dirección completa (https://…) sin usuario ni contraseña.')).toBeVisible()

    // crear: el secreto se muestra una sola vez
    await dialog.getByLabel(/^URL del cliente/).fill(`${receiver.url}/ok`)
    await dialog.getByRole('button', { name: 'Crear webhook' }).click()
    const secret = (await dialog.getByTestId('webhook-secret').textContent()) ?? ''
    expect(secret).toMatch(/^whsec_/)
    await shot(page, isMobile, 'l136-webhooks', 1300)
    await dialog.getByRole('button', { name: 'Ya lo guardé' }).click()
    await expect(dialog.getByText(secret)).toHaveCount(0)
    await expect(dialog.getByText(`${receiver.url}/ok`)).toBeVisible()
    await expect(dialog.getByText('Activo', { exact: true })).toBeVisible()

    // probar: llega un webhook.ping
    await dialog.getByRole('button', { name: 'Probar' }).first().click()
    await expect(page.locator('.toast.on').first()).toContainText('Respondió 200', { timeout: 15_000 })
    expect(receiver.received.some((r) => r.headers['x-teikem-event'] === 'webhook.ping')).toBe(true)

    // rotar
    page.once('dialog', (d) => void d.accept())
    await dialog.getByRole('button', { name: 'Rotar secreto' }).first().click()
    const rotated = (await dialog.getByTestId('webhook-secret').textContent()) ?? ''
    expect(rotated).toMatch(/^whsec_/)
    expect(rotated).not.toBe(secret)
    await dialog.getByRole('button', { name: 'Cerrar' }).first().click()

    // bandeja de salidas: dos salidas no entregadas (se siembran como las dejaría el trabajo tras los 6 intentos)
    const hook = (await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/integrations/${integration.publicId}/webhooks`, { headers }), 'webhooks'))[0]
    const payload = (n: number) => `{#id#:#00000000-0000-0000-0000-00000000000${n}#,#type#:#order.delivered#,#data#:{#orderNumber#:#UI-${s}-${n}#,#status#:#DELIVERED#}}`
    const seed = (n: number) =>
      sql(
        `INSERT dbo.IntegrationWebhookDelivery (TenantId, IntegrationWebhookId, EventId, EventType, PayloadJson, Status, AttemptCount, NextAttemptUtc, LastAttemptUtc, ResponseCode, Error) ` +
          `SELECT w.TenantId, w.IntegrationWebhookId, NEWID(), 'order.delivered', REPLACE(N'${payload(n)}', '#', CHAR(34)), 'UNDELIVERED', 6, SYSUTCDATETIME(), SYSUTCDATETIME(), 500, N'El servidor respondi' + NCHAR(243) + N' 500.' ` +
          `FROM dbo.IntegrationWebhook w WHERE w.PublicId = '${hook.publicId}'`,
      )
    seed(1)
    seed(2)
    await page.goto(`/system/integrations?tab=outbox&integration=${integration.publicId}`)
    await expect(page.getByRole('tab', { name: /Salidas/ })).toBeVisible({ timeout: 30_000 })
    const outboxRows = () => page.locator('tr, li.dt-card, .dt-cards > li').filter({ hasText: 'El servidor respondió 500.' })
    await expect(outboxRows().first()).toBeVisible({ timeout: 30_000 })
    await shot(page, isMobile, 'l136-salidas', 900)
    await outboxRows().first().getByRole('button', { name: 'Ver cuerpo' }).click()
    const body = page.getByRole('dialog', { name: /Aviso/ })
    await expect(body.getByTestId('outbox-payload')).toContainText('order.delivered')
    await shot(page, isMobile, 'l136-cuerpo', 800)
    await body.getByRole('button', { name: 'Cerrar' }).first().click()

    // reintentar una (vuelve a pendiente) y descartar la otra
    const undelivered = async () => (await ok<{ undelivered: number }>(request.get(`${API_URL}/api/v1/integrations/outbox?integration=${integration.publicId}`, { headers }), 'bandeja')).undelivered
    expect(await undelivered()).toBe(2)
    await outboxRows().first().getByRole('button', { name: 'Reintentar' }).click()
    await expect.poll(undelivered).toBe(1)
    await outboxRows().first().getByRole('button', { name: 'Descartar' }).click()
    await expect.poll(undelivered).toBe(0)
    if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  } finally {
    receiver.server.close()
  }
})
