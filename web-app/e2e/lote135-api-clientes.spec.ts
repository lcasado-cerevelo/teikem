// Recorrido del Lote 135 (P11) contra el API real: la API de los clientes con llaves. Una integración de tipo API con alcances (sin escalada de privilegios y por familia), llaves cuyo secreto se
// muestra UNA vez (solo se guarda su hash), token de 15 minutos, y la lista CERRADA de rutas: la llave solo usa las rutas de sus alcances (todo lo demás 403, aunque el token sea válido), todo limitado al
// cliente de la integración (otro cliente no existe para ella), sin borrar ni dar de baja, con límite de uso (429 + Retry-After), revocación inmediata y bitácora. Escritorio y móvil.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(120_000)

type Key = { key: { keyId: string; secretLast4: string; rateLimitPerMinute: number; isActive: boolean }; secret: string }
type Integration = { publicId: string; name: string; scopes: string[]; clientName?: string }
type Page<T> = { items: T[] }

test('API de los clientes: llaves, token, alcances, aislamiento por cliente, límite de uso y revocación', async ({ request }, testInfo) => {
  const s = `${testInfo.project.name.startsWith('movil') ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  await enableModule(request, 'WMS_LOTSERIAL')
  const headers = await apiHeaders(request)

  // dos clientes con una orden cada uno
  const a = await createClient(request, headers, `ApiA ${s}`)
  const b = await createClient(request, headers, `ApiB ${s}`)
  const consA = await createConsignee(request, headers, a.publicId, `Colmado A ${s}`)
  const consB = await createConsignee(request, headers, b.publicId, `Colmado B ${s}`)
  const orderA = await createOrder(request, headers, a.publicId, consA.publicId)
  const orderB = await createOrder(request, headers, b.publicId, consB.publicId)

  // ---- opciones y validaciones del alta
  const options = await ok<{ scopes: { code: string; available: boolean; isApi: boolean; needsClient: boolean; family: string }[] }>(request.get(`${API_URL}/api/v1/integrations/options`, { headers }), 'opciones')
  const byCode = Object.fromEntries(options.scopes.map((x) => [x.code, x]))
  expect(byCode['orders:read']).toMatchObject({ available: true, isApi: true, needsClient: true, family: 'TRANSPORT' })
  expect(byCode['warehouse:read']).toMatchObject({ available: true, isApi: true, needsClient: false, family: 'WAREHOUSE' })
  expect(byCode['files.orders_import']).toMatchObject({ isApi: false })

  const scopesA = ['orders:read', 'orders:create', 'consignees:read', 'consignees:write', 'client:read', 'client:write']
  const post = (data: object) => request.post(`${API_URL}/api/v1/integrations`, { headers, data })
  const bad = async (patch: object, text: string) => {
    const res = await post({ name: `API ${s}`, connectorKind: 'API', clientPublicId: a.publicId, scopes: scopesA, ...patch })
    expect(res.status(), await res.text()).toBe(400)
    expect(await res.text()).toContain(text)
  }
  await bad({ scopes: [] }, 'Escoja al menos un alcance')
  await bad({ scopes: ['files.orders_import'] }, 'solo admite alcances de la API')
  await bad({ clientPublicId: undefined }, 'necesitan el cliente')
  await bad({ scopes: ['no:existe'] }, "El alcance 'no:existe' no existe.")
  const fileScope = await post({ name: `Carpeta ${s}`, connectorKind: 'FOLDER', folder: `f${s.toLowerCase()}`, clientPublicId: a.publicId, templatePublicId: undefined, scopes: ['orders:read'] })
  expect(fileScope.status()).toBe(400)

  // ---- altas: la del cliente A (transporte) y una de almacén sin cliente
  const intA = await ok<Integration>(post({ name: `API cliente A ${s}`, connectorKind: 'API', clientPublicId: a.publicId, scopes: scopesA, includeMe: true }), 'alta A')
  const intW = await ok<Integration>(post({ name: `API almacén ${s}`, connectorKind: 'API', scopes: ['warehouse:read'] }), 'alta almacén')
  expect(intA.scopes).toEqual(scopesA)
  expect((await post({ name: `API cliente A ${s}`, connectorKind: 'API', clientPublicId: a.publicId, scopes: scopesA })).status()).toBe(409)

  // ---- llaves: el secreto se muestra UNA vez
  const keyUrl = (id: string) => `${API_URL}/api/v1/integrations/${id}/api-keys`
  expect((await request.post(keyUrl(intA.publicId), { headers, data: { label: ' ' } })).status()).toBe(400)
  const badRate = await request.post(keyUrl(intA.publicId), { headers, data: { label: 'x', rateLimitPerMinute: 0 } })
  expect(badRate.status()).toBe(400)
  expect(await badRate.text()).toContain('entre 1 y 6000')
  const created = await ok<Key>(request.post(keyUrl(intA.publicId), { headers, data: { label: 'Producción', rateLimitPerMinute: 120 } }), 'crear llave')
  expect(created.secret).toMatch(/^tks_/)
  expect(created.key).toMatchObject({ keyId: expect.stringMatching(/^tk_[0-9a-f]{20}$/), rateLimitPerMinute: 120, isActive: true, secretLast4: created.secret.slice(-4) })
  const list = await request.get(keyUrl(intA.publicId), { headers })
  expect(await list.text()).not.toContain(created.secret)                       // nunca vuelve
  expect((await ok<{ keyId: string }[]>(Promise.resolve(list), 'llaves')).map((k) => k.keyId)).toEqual([created.key.keyId])

  // ---- token: «Credenciales inválidas.» sin decir cuál falló
  const token = async (clientId: string, clientSecret: string) => request.post(`${API_URL}/api/v1/api-auth/token`, { data: { clientId, clientSecret } })
  const bad1 = await token(created.key.keyId, 'tks_incorrecto')
  const bad2 = await token('tk_noexiste0000000000000', created.secret)
  expect([bad1.status(), bad2.status()]).toEqual([401, 401])
  expect((await bad1.json()).title).toBe((await bad2.json()).title)
  const issued = await ok<{ accessToken: string; tokenType: string; expiresIn: number; scopes: string[]; clientPublicId: string }>(token(created.key.keyId, created.secret), 'token')
  expect(issued).toMatchObject({ tokenType: 'Bearer', scopes: scopesA, clientPublicId: a.publicId })
  expect(issued.expiresIn).toBeGreaterThan(800)
  const api = { Authorization: `Bearer ${issued.accessToken}` }
  const me = await ok<{ scopes: string[]; clientName: string; rateLimitPerMinute: number }>(request.get(`${API_URL}/api/v1/api-auth/me`, { headers: api }), 'me')
  expect(me).toMatchObject({ scopes: scopesA, rateLimitPerMinute: 120 })
  expect((await request.get(`${API_URL}/api/v1/api-auth/me`, { headers })).status()).toBe(403)   // una persona no es una llave

  // ---- lo que sí: órdenes del cliente A (la de B no existe para esta llave)
  const orders = await ok<Page<{ publicId: string; orderNumber: string }>>(request.get(`${API_URL}/api/v1/orders?take=50`, { headers: api }), 'órdenes')
  expect(orders.items.map((o) => o.publicId)).toEqual([orderA.publicId])
  expect((await request.get(`${API_URL}/api/v1/orders/${orderA.publicId}`, { headers: api })).status()).toBe(200)
  expect((await request.get(`${API_URL}/api/v1/orders/${orderB.publicId}`, { headers: api })).status()).toBe(404)
  const clients = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/clients`, { headers: api }), 'clientes')
  expect(clients.map((c) => c.publicId)).toEqual([a.publicId])
  expect((await request.get(`${API_URL}/api/v1/clients/${b.publicId}`, { headers: api })).status()).toBe(404)
  const catalogs = await request.get(`${API_URL}/api/v1/catalogs/ServiceType`, { headers: api })
  expect(catalogs.status()).toBe(200)                                            // los catálogos los puede leer cualquier llave

  // crear una orden (con Idempotency-Key) para su cliente; para otro cliente no se puede
  const body = { clientPublicId: a.publicId, consigneeLocationPublicId: consA.publicId, packages: [{ pieces: 1, description: 'Por API' }] }
  const idem = `idem-${s}`
  const made = await ok<{ publicId: string; orderNumber: string }>(request.post(`${API_URL}/api/v1/orders`, { headers: { ...api, 'Idempotency-Key': idem }, data: body }), 'crear orden')
  const again = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/orders`, { headers: { ...api, 'Idempotency-Key': idem }, data: body }), 'repetir')
  expect(again.publicId).toBe(made.publicId)                                     // la misma orden: idempotente
  const other = await request.post(`${API_URL}/api/v1/orders`, { headers: api, data: { ...body, clientPublicId: b.publicId, consigneeLocationPublicId: consB.publicId } })
  expect(other.ok()).toBe(false)
  const afterOrders = await ok<Page<{ publicId: string }>>(request.get(`${API_URL}/api/v1/orders?take=50`, { headers: api }), 'órdenes otra vez')
  expect(afterOrders.items.map((o) => o.publicId).sort()).toEqual([made.publicId, orderA.publicId].sort())

  // consignatarios: agrega y edita los propios; no se borran (ni se dan de baja)
  const loc = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/locations`, { headers: api, data: { clientPublicId: a.publicId, name: `Colmado API ${s}`, locationType: 'DELIVERY', line1: 'Calle API 1', city: 'Ponce', postalCode: '00716', country: 'PR' } }), 'consignatario')
  expect((await request.patch(`${API_URL}/api/v1/locations/${loc.publicId}`, { headers: api, data: { name: `Colmado API editado ${s}` } })).status()).toBe(200)
  expect((await request.patch(`${API_URL}/api/v1/locations/${consB.publicId}`, { headers: api, data: { name: 'ajeno' } })).status()).toBe(404)
  const deactivate = await request.post(`${API_URL}/api/v1/locations/${loc.publicId}/deactivate`, { headers: api })
  expect(deactivate.status()).toBe(403)
  expect((await deactivate.json()).code).toBe('api_scope')
  expect((await request.delete(`${API_URL}/api/v1/locations/${loc.publicId}`, { headers: api })).status()).toBe(403)
  // el propio cliente: leer y editar su perfil
  expect((await request.patch(`${API_URL}/api/v1/clients/${a.publicId}/profile`, { headers: api, data: { name: `ApiA editado ${s}` } })).status()).toBe(200)

  // ---- lo que no: fuera de sus alcances, aunque el token sea válido y el usuario de servicio tenga permisos
  for (const [method, path] of [['GET', '/api/v1/products'], ['GET', '/api/v1/billing/invoices'], ['GET', '/api/v1/integrations'], ['GET', '/api/v1/users'], ['POST', '/api/v1/clients'], ['GET', '/api/v1/me']] as const) {
    const res = await request.fetch(`${API_URL}${path}`, { method, headers: api, data: method === 'POST' ? {} : undefined })
    expect(res.status(), `${method} ${path}`).toBe(403)
  }
  const needs = await request.get(`${API_URL}/api/v1/products`, { headers: api })
  expect((await needs.json()).title).toContain('warehouse:read')

  // ---- la llave de almacén (sin cliente): solo almacén
  const keyW = await ok<Key>(request.post(keyUrl(intW.publicId), { headers, data: { label: 'Almacén' } }), 'llave almacén')
  const tokenW = await ok<{ accessToken: string; clientPublicId?: string }>(token(keyW.key.keyId, keyW.secret), 'token almacén')
  expect(tokenW.clientPublicId ?? null).toBeNull()
  const apiW = { Authorization: `Bearer ${tokenW.accessToken}` }
  expect((await request.get(`${API_URL}/api/v1/products?take=5`, { headers: apiW })).status()).toBe(200)
  const noOrders = await request.get(`${API_URL}/api/v1/orders`, { headers: apiW })
  expect(noOrders.status()).toBe(403)
  expect((await noOrders.json()).title).toContain('orders:read')

  // ---- límite de uso: 3 por minuto → la cuarta es 429 con Retry-After
  const slow = await ok<Key>(request.post(keyUrl(intW.publicId), { headers, data: { label: 'Lenta', rateLimitPerMinute: 4 } }), 'llave lenta')
  const slowToken = await ok<{ accessToken: string }>(token(slow.key.keyId, slow.secret), 'token lento')   // el token también cuenta (1 de 4)
  const slowApi = { Authorization: `Bearer ${slowToken.accessToken}` }
  const statuses: number[] = []
  let retry = ''
  for (let i = 0; i < 5; i++) {
    const res = await request.get(`${API_URL}/api/v1/products?take=1`, { headers: slowApi })
    statuses.push(res.status())
    if (res.status() === 429) retry = res.headers()['retry-after'] ?? ''
  }
  expect(statuses).toEqual([200, 200, 200, 429, 429])
  expect(Number(retry)).toBeGreaterThan(0)

  // ---- revocar: deja de servir de inmediato (aunque el token siga vivo) y no se puede pedir otro
  const revoked = await ok<{ isActive: boolean }>(request.post(`${keyUrl(intA.publicId)}/${created.key.keyId}/revoke`, { headers }), 'revocar')
  expect(revoked.isActive).toBe(false)
  expect((await request.get(`${API_URL}/api/v1/orders`, { headers: api })).status()).toBe(401)
  expect((await token(created.key.keyId, created.secret)).status()).toBe(401)
  expect((await request.post(`${keyUrl(intA.publicId)}/${created.key.keyId}/revoke`, { headers })).status()).toBe(409)

  // ---- bitácora de la llave
  const log = await ok<{ method: string; path: string; statusCode: number }[]>(request.get(`${keyUrl(intA.publicId)}/${created.key.keyId}/log?take=100`, { headers }), 'bitácora')
  expect(log.some((l) => l.method === 'GET' && l.path === '/api/v1/orders' && l.statusCode === 200)).toBe(true)
  expect(log.some((l) => l.statusCode === 403)).toBe(true)
  expect(log.some((l) => l.statusCode === 401)).toBe(true)

  // ---- apagar la integración apaga su cuenta de servicio: los tokens vivos dejan de servir
  await ok(request.post(`${API_URL}/api/v1/integrations/${intW.publicId}/deactivate`, { headers }), 'apagar')
  expect((await token(keyW.key.keyId, keyW.secret)).status()).toBe(401)
})

test('pantalla: conector API, llaves con el secreto una sola vez, bitácora y revocación', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'INTEGRATIONS')
  await enableModule(request, 'WMS_LOTSERIAL')
  const headers = await apiHeaders(request)
  const integration = await ok<Integration>(request.post(`${API_URL}/api/v1/integrations`, { headers, data: { name: `0 API almacén ${s}`, connectorKind: 'API', scopes: ['warehouse:read'], includeMe: true } }), 'alta API')

  await login(page)
  await page.goto('/system/integrations')
  const rowOf = (name: string) => page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()
  await expect(rowOf(`0 API almacén ${s}`)).toContainText('API para el cliente', { timeout: 30_000 })
  await expect(rowOf(`0 API almacén ${s}`).getByRole('button', { name: 'Correr ahora' })).toHaveCount(0)
  await shot(page, isMobile, 'l135-lista', 900)

  // formulario: con el conector API no hay plantilla ni carpeta ni horario; el cliente solo se pide con alcances de transporte
  await page.getByRole('button', { name: 'Nueva integración' }).click()
  const form = page.getByRole('dialog', { name: 'Nueva integración' })
  await form.getByLabel(/^Conector/).selectOption('API')
  await expect(form.getByLabel(/^Plantilla de importación/)).toHaveCount(0)
  await expect(form.getByLabel(/^Horario/)).toHaveCount(0)
  await form.getByLabel(/^Nombre/).fill(`1 API ${s}`)
  await form.getByRole('switch', { name: 'API: leer envíos, su seguimiento y el comprobante de entrega' }).check()
  await form.getByRole('button', { name: 'Crear integración' }).click()
  await expect(form.getByText('Escoja el cliente: algún alcance elegido es de envíos de transporte.')).toBeVisible()
  await shot(page, isMobile, 'l135-formulario', 1500)
  await form.getByRole('button', { name: 'Cancelar' }).click()

  // llaves
  await rowOf(`0 API almacén ${s}`).getByRole('button', { name: 'Llaves de API' }).click()
  const keys = page.getByRole('dialog', { name: /Llaves de API de/ })
  await keys.getByRole('button', { name: 'Crear llave' }).click()
  await expect(keys.getByText('Escriba el nombre de la llave.')).toBeVisible()
  await keys.getByLabel(/^Nombre de la llave/).fill('Producción')
  await keys.getByLabel(/^Límite por minuto/).fill('7000')
  await keys.getByRole('button', { name: 'Crear llave' }).click()
  await expect(keys.getByText('El límite debe ser un número entero entre 1 y 6000.')).toBeVisible()
  await keys.getByLabel(/^Límite por minuto/).fill('90')
  await keys.getByRole('button', { name: 'Crear llave' }).click()
  const secret = (await keys.getByTestId('api-secret').textContent()) ?? ''
  expect(secret).toMatch(/^tks_/)
  await shot(page, isMobile, 'l135-llaves', 1200)
  await keys.getByRole('button', { name: 'Ya lo guardé' }).click()
  await expect(keys.getByText(secret)).toHaveCount(0)                       // no vuelve a mostrarse
  await expect(keys.getByText('Producción', { exact: true })).toBeVisible()

  // la llave sirve: token + una llamada que queda en la bitácora
  const keyId = (await ok<{ keyId: string }[]>(request.get(`${API_URL}/api/v1/integrations/${integration.publicId}/api-keys`, { headers }), 'llaves'))[0].keyId
  const tk = await ok<{ accessToken: string }>(request.post(`${API_URL}/api/v1/api-auth/token`, { data: { clientId: keyId, clientSecret: secret } }), 'token')
  expect((await request.get(`${API_URL}/api/v1/products?take=1`, { headers: { Authorization: `Bearer ${tk.accessToken}` } })).status()).toBe(200)
  await keys.getByRole('button', { name: 'Bitácora' }).first().click()
  await expect(keys.getByText('/api/v1/products').first()).toBeVisible({ timeout: 15_000 })
  await shot(page, isMobile, 'l135-bitacora', 1400)

  // revocar
  page.once('dialog', (d) => void d.accept())
  await keys.getByRole('button', { name: 'Revocar' }).first().click()
  await expect(keys.getByText('Revocada').first()).toBeVisible()
  expect((await request.get(`${API_URL}/api/v1/products?take=1`, { headers: { Authorization: `Bearer ${tk.accessToken}` } })).status()).toBe(401)
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
