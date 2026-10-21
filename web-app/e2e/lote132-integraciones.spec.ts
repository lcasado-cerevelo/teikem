// Recorrido del Lote 132 (P11) contra el API real: integraciones de entrada con su cuenta de servicio, carpeta de entrada y subida manual, historial, hash de archivos procesados, y la bandeja
// (corregir, reprocesar, descartar, descargar). Por API: alta con alcances (400/403/409), la cuenta de servicio (no inicia sesión, rol con los permisos de los alcances), corrida de carpeta
// (órdenes creadas con la plantilla de varias líneas del Lote 131, lo rechazado a la bandeja, archivo movido a «procesados»), el mismo archivo otra vez (duplicado), corregir y reprocesar una orden
// de la bandeja, descartar, apagar y prender. En pantalla: Integraciones (lista, alta y detalle) y Bandeja. Escritorio y móvil. Capturas: docs/manual/frontend/img/l132-<paso>.png.
import { mkdirSync, readdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { expect, test } from '@playwright/test'

import { contractData, withRates } from './rates'
import { API_URL, ADMIN, apiHeaders, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

const FOLDER_ROOT = resolve(process.env.INTEGRATIONS_ROOT ?? 'test-results/integrations')
const TENANT_ID = process.env.TEIKEM_TENANT_ID ?? '1'

type Integration = { id: number; publicId: string; name: string; scopes: string[]; serviceUserName?: string; notifyUsers: { id: number }[]; isActive: boolean; pendingInbox: number; lastStatus: string; folder?: string }
type Run = { id: number; status: string; filesRead: number; ordersCreated: number; rejected: number; message?: string; files: { fileName: string; result: string }[] }
type Item = { id: number; status: string; reason: string; referenceKey?: string; hasData: boolean; header?: string[]; rows?: { row: number; cells: string[] }[]; orderPublicId?: string }

test('Integraciones: cuenta de servicio, carpeta de entrada, hash, historial y bandeja (corregir, reprocesar, descartar)', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)

  // cliente que numera sus órdenes y la plantilla de varias líneas por orden (Lote 131)
  const client = await ok<{ publicId: string; name: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Integra ${s}`, contract: contractData() } }), 'cliente')
  await withRates(request, headers, client as never)
  await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { clientAssignsOrderNumber: true, clientAssignsInvoiceNumber: true } }), 'numeración')
  const columns = [
    { position: 1, field: 'orderNumber' }, { position: 3, field: 'pieceNumber' }, { position: 4, field: 'description' }, { position: 5, field: 'consigneeName' },
    { position: 6, field: 'line1' }, { position: 7, field: 'city' }, { position: 8, field: 'weightKg' },
  ]
  const rules = [{ kind: 'INVOICE', position: 2, operator: 'EQUALS', value: 'Invoice' }, { kind: 'DESCRIPTION', position: 2, operator: 'IN', value: 'Drum | Item' }]
  const template = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/import-templates`, {
    headers, data: { name: `Plantilla Integra ${s}`, columns, defaults: { serviceType: 'STANDARD', packageType: 'BOX' }, clientPublicId: client.publicId, multiLine: true, lineRules: rules },
  }), 'plantilla')

  // ---- opciones y validaciones del alta
  const options = await ok<{ scopes: { code: string; available: boolean }[]; connectors: string[]; users: { id: number }[] }>(request.get(`${API_URL}/api/v1/integrations/options`, { headers }), 'opciones')
  expect(options.scopes.find((x) => x.code === 'files.orders_import')?.available).toBe(true)
  expect(options.connectors).toEqual(['FOLDER', 'MANUAL'])
  const folder = `f${s.toLowerCase()}`
  const base = { name: `Integración ${s}`, description: 'Prueba', clientPublicId: client.publicId, connectorKind: 'FOLDER', folder, templatePublicId: template.publicId, scopes: ['files.orders_import'], includeMe: true }
  const bad = async (patch: object, status: number, text: string) => {
    const res = await request.post(`${API_URL}/api/v1/integrations`, { headers, data: { ...base, ...patch } })
    expect(res.status(), await res.text()).toBe(status)
    expect(await res.text()).toContain(text)
  }
  await bad({ scopes: [] }, 400, 'Escoja al menos un alcance')
  await bad({ scopes: ['nada.nada'] }, 400, "El alcance 'nada.nada' no existe.")
  await bad({ connectorKind: 'EMAIL' }, 400, 'todavía no está disponible')
  await bad({ connectorKind: 'FOLDER', folder: '../fuera' }, 400, 'solo admite letras')
  await bad({ connectorKind: 'FOLDER', folder: '' }, 400, 'Indique el nombre de la carpeta')
  await bad({ connectorKind: 'MANUAL', folder: undefined, scheduleKind: 'INTERVAL', intervalSeconds: 120 }, 400, 'subida manual no se programa')
  await bad({ scheduleKind: 'INTERVAL', intervalSeconds: 10 }, 400, 'intervalo debe estar entre')
  await bad({ retentionDays: 0 }, 400, 'retención')
  await bad({ notifyUserIds: [999999] }, 400, 'usuarios a avisar')

  // ---- alta: cuenta de servicio y avisos a quien se defina (con «incluirme»)
  const created = await ok<Integration>(request.post(`${API_URL}/api/v1/integrations`, { headers, data: base }), 'alta')
  expect(created.scopes).toEqual(['files.orders_import'])
  expect(created.serviceUserName).toContain(`Integración Integración ${s}`.slice(0, 12))
  expect(created.notifyUsers).toHaveLength(1)
  expect(created.isActive).toBe(true)
  await bad({}, 409, 'Ya existe una integración con ese nombre')

  // la cuenta de servicio no inicia sesión (ni siquiera con una contraseña adivinada) y su rol solo trae los permisos del alcance
  const serviceLogin = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: `integration-${created.publicId.replace(/-/g, '')}@service.teikem.local`, password: 'Cualquiera_123!' } })
  expect(serviceLogin.status()).toBe(401)
  const roles = await ok<{ name: string; permissions: string[] }[]>(request.get(`${API_URL}/api/v1/roles`, { headers }), 'roles')
  const role = roles.find((r) => r.name === `Integración: Integración ${s}`)
  expect(role?.permissions.sort()).toEqual(['clients.read', 'locations.create', 'locations.read', 'orders.create', 'orders.view'])

  // ---- carpeta de entrada: un archivo con 2 órdenes buenas y 2 malas (sin pieza; pieza repetida en el archivo)
  const dir = join(FOLDER_ROOT, TENANT_ID, folder)
  mkdirSync(dir, { recursive: true })
  const p = (n: number) => `PCS-${s}-${n}`
  const csv = [
    'orden,tipo,label,descripcion,consignatario,direccion,pueblo,peso',
    `A-${s},Box,${p(1)},,Colmado Uno ${s},Calle 1,Ponce,2.5`,
    `A-${s},Box,${p(2)},,Colmado Uno ${s},Calle 1,Ponce,3`,
    `A-${s},Drum,,Dron de reparto,,,,`,
    `A-${s},Invoice,INV-${s},,,,,`,
    `B-${s},Box,${p(3)},,Colmado Dos ${s},Calle 2,Ponce,1`,
    `C-${s},Drum,,Solo descripcion,Colmado Tres ${s},Calle 3,Ponce,`,
    `D-${s},Box,${p(1)},,Colmado Cuatro ${s},Calle 4,Ponce,1`,
  ].join('\n')
  writeFileSync(join(dir, 'ordenes.csv'), csv, 'utf-8')
  const run = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/run`, { headers }), 'correr')
  expect(run).toMatchObject({ status: 'PARTIAL', filesRead: 1, ordersCreated: 2, rejected: 2 })
  expect(run.files[0]).toMatchObject({ fileName: 'ordenes.csv', result: 'PROCESSED' })
  expect(readdirSync(join(dir, 'procesados')).some((f) => f.endsWith('ordenes.csv'))).toBe(true)   // nunca se borra: se mueve
  expect(readdirSync(dir).filter((f) => f.endsWith('.csv'))).toEqual([])
  const orderA = await ok<{ items: { orderNumber: string }[] }>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&take=50`, { headers }), 'órdenes')
  expect(orderA.items.map((o) => o.orderNumber).sort()).toEqual([`A-${s}`, `B-${s}`])

  // el acuse es un aviso del sistema a quien se definió en la integración (aquí quien pidió «incluirme»)
  await expect
    .poll(async () => JSON.stringify(await (await request.get(`${API_URL}/api/v1/inbox?take=30`, { headers })).json()), { timeout: 90_000 })
    .toContain(`«Integración ${s}»`)

  // ---- el mismo contenido otra vez: ya se procesó (hash) y no se duplica nada
  writeFileSync(join(dir, 'copia.csv'), csv, 'utf-8')
  const again = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/run`, { headers }), 'correr otra vez')
  expect(again.files[0]).toMatchObject({ fileName: 'copia.csv', result: 'DUPLICATE' })
  expect(again.ordersCreated).toBe(0)
  const none = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/run`, { headers }), 'sin archivos')
  expect(none).toMatchObject({ status: 'OK', filesRead: 0 })
  expect(none.message).toBe('Sin archivos nuevos.')

  // ---- la bandeja: las dos órdenes rechazadas con su motivo exacto y los datos tal como llegaron
  const inbox = await ok<{ items: Item[]; pending: number }>(request.get(`${API_URL}/api/v1/integrations/inbox?integration=${created.publicId}&status=PENDING`, { headers }), 'bandeja')
  expect(inbox.pending).toBe(2)
  const byKey = Object.fromEntries(inbox.items.map((i) => [i.referenceKey, i]))
  expect(byKey[`C-${s}`].reason).toContain('no trae ninguna pieza')
  expect(byKey[`D-${s}`].reason).toContain(`El número de pieza ${p(1)} se repite`)
  const itemD = await ok<Item>(request.get(`${API_URL}/api/v1/integrations/inbox/${byKey[`D-${s}`].id}`, { headers }), 'ítem D')
  expect(itemD.hasData).toBe(true)
  expect(itemD.header?.[2]).toBe('label')
  expect(itemD.rows).toHaveLength(1)

  // corregir: la fila D trae un label nuevo; reprocesar crea la orden y el ítem queda REPROCESSED
  const stillBad = await ok<{ reprocessed: number; stillPending: number; items: Item[] }>(request.post(`${API_URL}/api/v1/integrations/inbox/${itemD.id}/reprocess`, { headers }), 'reprocesar sin corregir')
  expect(stillBad).toMatchObject({ reprocessed: 0, stillPending: 1 })
  const wrongRows = await request.patch(`${API_URL}/api/v1/integrations/inbox/${itemD.id}`, { headers, data: { rows: [{ row: 99, cells: ['x'] }] } })
  expect(wrongRows.status()).toBe(400)
  const fixedCells = [...itemD.rows![0].cells]
  fixedCells[2] = p(9)
  await ok(request.patch(`${API_URL}/api/v1/integrations/inbox/${itemD.id}`, { headers, data: { rows: [{ row: itemD.rows![0].row, cells: fixedCells }] } }), 'corregir')
  const fixed = await ok<{ reprocessed: number; stillPending: number; items: Item[] }>(request.post(`${API_URL}/api/v1/integrations/inbox/${itemD.id}/reprocess`, { headers }), 'reprocesar')
  expect(fixed).toMatchObject({ reprocessed: 1, stillPending: 0 })
  expect(fixed.items[0].status).toBe('REPROCESSED')
  expect(fixed.items[0].orderPublicId).toBeTruthy()
  const resolvedTwice = await request.post(`${API_URL}/api/v1/integrations/inbox/${itemD.id}/reprocess`, { headers })
  expect(resolvedTwice.status()).toBe(409)

  // descartar el otro con nota; el CSV de pendientes y el estado de la integración
  const discarded = await ok<Item & { note?: string }>(request.post(`${API_URL}/api/v1/integrations/inbox/${byKey[`C-${s}`].id}/discard`, { headers, data: { note: 'El cliente la manda de nuevo' } }), 'descartar')
  expect(discarded).toMatchObject({ status: 'DISCARDED', note: 'El cliente la manda de nuevo' })
  const csvOut = await request.get(`${API_URL}/api/v1/integrations/inbox/export`, { headers })
  expect(csvOut.ok()).toBe(true)
  const detail = await ok<Integration>(request.get(`${API_URL}/api/v1/integrations/${created.publicId}`, { headers }), 'integración')
  expect(detail.pendingInbox).toBe(0)

  // ---- apagar y prender: apagada no corre (422) y su cuenta de servicio también se apaga
  await ok(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/deactivate`, { headers }), 'apagar')
  const off = await request.post(`${API_URL}/api/v1/integrations/${created.publicId}/run`, { headers })
  expect(off.status()).toBe(422)
  expect(await off.text()).toContain('apagada')
  await ok(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/reactivate`, { headers }), 'prender')

  // ---- sin sesión: 401
  const anonymous = await request.get(`${API_URL}/api/v1/integrations`)
  expect(anonymous.status()).toBe(401)
  expect(ADMIN.email).toBeTruthy()

  // ---- en pantalla: lista, alta por formulario, subida de un archivo, historial y bandeja
  const rowOf = (name: string) => page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()
  await login(page)
  await page.goto('/system/integrations')
  await expect(page.locator('h1', { hasText: 'Integraciones' })).toBeVisible({ timeout: 30_000 })
  await expect(rowOf(`Integración ${s}`)).toBeVisible()
  await expect(rowOf(`Integración ${s}`)).toContainText('Carpeta de entrada')
  await shot(page, isMobile, 'l132-lista', 900)

  // alta por el formulario (subida manual): valida y crea
  await page.getByRole('button', { name: 'Nueva integración' }).click()
  const dialog = page.getByRole('dialog', { name: 'Nueva integración' })
  await dialog.getByRole('button', { name: 'Crear integración' }).click()
  await expect(dialog.getByText('El nombre de la integración es obligatorio.')).toBeVisible()
  await expect(dialog.getByText('Escoja al menos un alcance para la integración.')).toBeVisible()
  const manualName = `Manual ${s}`
  await dialog.getByLabel(/^Nombre/).fill(manualName)
  await dialog.getByLabel(/^Cliente/).selectOption({ label: new RegExp(client.name) as never }).catch(async () => {
    const value = await dialog.locator('select option', { hasText: client.name }).first().getAttribute('value')
    await dialog.getByLabel(/^Cliente/).selectOption(value ?? '')
  })
  await expect(dialog.getByLabel(/^Plantilla de importación/)).toContainText(`Plantilla Integra ${s}`, { timeout: 30_000 })
  const tplValue = await dialog.locator('select option', { hasText: `Plantilla Integra ${s}` }).first().getAttribute('value')
  await dialog.getByLabel(/^Plantilla de importación/).selectOption(tplValue ?? '')
  await dialog.getByLabel(/^Conector/).selectOption('MANUAL')
  await dialog.getByRole('switch', { name: 'Entrada de envíos por archivo' }).check()
  await expect(dialog.getByRole('switch', { name: 'Incluirme' }).or(dialog.getByLabel('Incluirme'))).toBeVisible()
  await shot(page, isMobile, 'l132-alta', 1500)
  await dialog.getByRole('button', { name: 'Crear integración' }).click()
  await expect(dialog).toBeHidden({ timeout: 30_000 })
  await expect(rowOf(manualName)).toContainText('Subida manual')

  // subir un archivo a la integración manual: una orden buena y una sin pieza
  const upCsv = ['orden,tipo,label,descripcion,consignatario,direccion,pueblo,peso', `U-${s},Box,${p(21)},,Colmado Cinco ${s},Calle 5,Ponce,1`, `V-${s},Drum,,Solo descripcion,Colmado Seis ${s},Calle 6,Ponce,`].join('\n')
  const [chooser] = await Promise.all([page.waitForEvent('filechooser'), rowOf(manualName).getByRole('button', { name: 'Subir archivo' }).click()])
  await chooser.setFiles({ name: 'subida.csv', mimeType: 'text/csv', buffer: Buffer.from(upCsv, 'utf-8') })
  await expect(page.locator('.toast.on').first()).toContainText('Se subió el archivo', { timeout: 30_000 })
  await expect(rowOf(manualName)).toContainText('Con pendientes', { timeout: 30_000 })
  await rowOf(manualName).getByRole('button', { name: 'Historial' }).click()
  const history = page.getByRole('dialog', { name: /Historial de/ })
  await expect(history.getByText('subida.csv')).toBeVisible()
  await shot(page, isMobile, 'l132-historial', 1100)
  await history.getByRole('button', { name: 'Cerrar' }).first().click()

  // bandeja: el pendiente con su motivo; corregir y reprocesar desde la pantalla
  await page.getByRole('tab', { name: /Bandeja/ }).click()
  await expect(page.getByText(/no trae ninguna pieza/).first()).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l132-bandeja', 900)
  await page.locator('tr, li.dt-card, .dt-cards > li').filter({ hasText: `V-${s}` }).first().getByRole('button', { name: 'Corregir' }).click()
  const fix = page.getByRole('dialog', { name: /Corregir/ })
  await expect(fix.getByLabel(/Fila \d+, columna tipo/)).toBeVisible()
  await fix.getByLabel(/Fila \d+, columna tipo/).fill('Box')
  await fix.getByLabel(/Fila \d+, columna label/).fill(p(22))
  await shot(page, isMobile, 'l132-corregir', 1000)
  await fix.getByRole('button', { name: 'Guardar y reprocesar' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('se creó el envío', { timeout: 30_000 })
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
