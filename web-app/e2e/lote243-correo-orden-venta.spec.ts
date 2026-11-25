// Lote 243 — correo con la orden de venta completa al solicitarla, contra el API real de pruebas (db-init hecho; API SIN correo: Brevo__ApiKey vacía, Auth__Onboarding__Enabled=false).
// 1) Sistema → Reglas de aviso: evento «Orden de venta solicitada» con la plantilla completa y «Vendedor de la orden» prellenados; se agregan «Cliente de la orden» y un buzón a mano.
//    «Vista previa» con el número de una orden real (personal, cliente y en inglés con tema oscuro): el correo se ve en un iframe, sin desplazamiento horizontal de página.
// 2) Clientes → Personas de contacto: casilla «Recibe avisos por correo» y el chip en la lista (la fila queda de alto normal).
// 3) Ajustes → Operación → Ventas: la línea de ayuda que lleva a la regla.
// 4) Registro de avisos: lo que se envió al solicitar otra orden (con la regla encendida por la campanita, único canal que el API de pruebas sin correo tiene configurado).
// Con MAILPIT_URL y el canal de correo LOCAL (scripts/api-para-pruebas.ps1 -Mailpit) además se enciende el correo y se captura el mensaje recibido en el buzón local.
// Capturas: l243-* en docs/manual/frontend/img/ (solo escritorio, salvo la móvil).
// Correr (web en :5174 apuntando al API de pruebas): WEB_URL=http://localhost:5174 API_URL=http://localhost:5100 npx playwright test lote243 --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync } from 'node:fs'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'

import { ADMIN, API_URL, IMG_DIR, LETTERS, apiHeaders, login, ok, openClientPanels, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const MAILPIT_URL = process.env.MAILPIT_URL

interface Seed {
  clientId: string
  clientName: string
  orderNumber: string
  orderId: string
  productId: string
  warehouseId: string
  rulePublicId?: string
}
let S: Seed

const PLATFORM = { email: process.env.TEIKEM_PLATFORM_EMAIL ?? 'teikem+support@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? ADMIN.password }

/** Enciende un módulo de la compañía con el usuario de la plataforma (el administrador de la compañía no puede). */
async function enableModule(request: APIRequestContext, key: string) {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: PLATFORM.email, password: PLATFORM.password } })
  expect(res.ok(), await res.text()).toBeTruthy()
  let body = (await res.json()) as { status?: string; tenants?: { tenantId?: number; isDefault?: boolean }[]; tokens?: { accessToken?: string } }
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((x) => x.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: PLATFORM.email, password: PLATFORM.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as typeof body
  }
  const headers = { Authorization: `Bearer ${body.tokens?.accessToken ?? ''}` }
  const status = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (status.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: status.currentVersion, lang: 'es' } })
  const reauth = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers, data: { password: PLATFORM.password } })
  expect(reauth.ok(), await reauth.text()).toBeTruthy()
  const token = ((await reauth.json()) as { accessToken: string }).accessToken
  const r = await request.put(`${API_URL}/api/v1/modules/${key}`, { headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() }, data: { isEnabled: true } })
  expect(r.ok(), await r.text()).toBeTruthy()
}

/** Solicita una orden nueva del cliente y devuelve su número. */
async function requestOrder(request: APIRequestContext, notes: string, clientId: string, warehouseId: string, productId: string): Promise<{ number: string; publicId: string }> {
  const headers = await apiHeaders(request)
  const order = await ok<{ publicId: string; number: string }>(
    request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { warehousePublicId: warehouseId, clientPublicId: clientId, notes, lines: [{ productPublicId: productId, quantity: 3 }], deliveryMode: 'PICKUP' } }),
    'orden en borrador',
  )
  await ok(request.post(`${API_URL}/api/v1/sales-orders/${order.publicId}/request`, { headers, data: { allowBackorder: true } }), 'solicitar')
  return order
}

test.beforeAll(async ({ request }) => {
  await enableModule(request, 'SALES_ORDERS')
  const headers = await apiHeaders(request)
  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const warehouseId = warehouses[0].publicId
  const product = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: `L243-${LETTERS}`, name: `Producto lote 243 ${LETTERS}`, trackingType: 'NONE', salePrice: 12.5, purchaseCost: 2 } }),
    'producto',
  )
  await ok(request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: product.product.publicId, warehousePublicId: warehouseId, binId: 2, quantity: 400, reason: 'FOUND', notes: 'e2e lote 243' } }), 'existencia')
  const clientName = `Farmacia L243 ${LETTERS}`
  const client = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } }), 'cliente')
  // personas: la principal con correo (sin marcar), otra marcada con correo y otra marcada sin correo (la web lo señala)
  const mail = (value: string) => [{ contactType: 'EMAIL', value, isPrimary: true }]
  await ok(request.post(`${API_URL}/api/v1/clients/${client.publicId}/contacts`, { headers, data: { fullName: 'Rosa Principal', role: 'Compras', isPrimary: true, contactPoints: mail(`principal-${LETTERS}@example.test`) } }), 'contacto principal')
  await ok(request.post(`${API_URL}/api/v1/clients/${client.publicId}/contacts`, { headers, data: { fullName: 'Mario Marcado', role: 'Farmacéutico', receivesEmailNotifications: true, contactPoints: mail(`marcado-${LETTERS}@example.test`) } }), 'contacto marcado')
  await ok(request.post(`${API_URL}/api/v1/clients/${client.publicId}/contacts`, { headers, data: { fullName: 'Nora Sin Correo', role: 'Caja', receivesEmailNotifications: true } }), 'contacto sin correo')
  const order = await requestOrder(request, `Nota <b>prueba</b> ${LETTERS}`, client.publicId, warehouseId, product.product.publicId)
  S = { clientId: client.publicId, clientName, orderNumber: order.number, orderId: order.publicId, productId: product.product.publicId, warehouseId }
})

/** Todo el documento cabe en el ancho de la ventana. */
async function noHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
}

async function openRuleModal(page: Page): Promise<Locator> {
  await page.goto('/system/notifications')
  await expect(page.getByRole('heading', { level: 1, name: 'Reglas de aviso' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('button', { name: 'Nueva regla' }).click()
  const dialog = page.getByRole('dialog', { name: 'Nueva regla' })
  await dialog.getByLabel('Evento', { exact: true }).selectOption('SALES_ORDER_REQUESTED')
  return dialog
}

/** Alto del marco del correo y que el contenido quepa (tope 70vh con desplazamiento interno). */
async function measureFrame(page: Page, preview: Locator) {
  const frame = preview.locator('iframe.nr-prev-frame')
  await expect(frame).toBeVisible()
  await expect(frame.contentFrame().getByText(S.orderNumber).first()).toBeVisible({ timeout: 20_000 })
  await page.waitForTimeout(300)   // el alto se mide al cargar el contenido
  const box = (await frame.boundingBox())!
  const inner = await frame.evaluate((el) => Math.ceil((el as HTMLIFrameElement).contentDocument!.documentElement.getBoundingClientRect().height))
  const vh = await page.evaluate(() => window.innerHeight)
  expect(box.height).toBeGreaterThan(150)
  expect(box.height).toBeLessThanOrEqual(vh * 0.7 + 2)
  expect(box.height).toBeGreaterThanOrEqual(Math.min(inner, vh * 0.7) - 4)
  // sin hueco de sobra debajo del correo (el marco no es más alto que su contenido, salvo el margen)
  if (inner < vh * 0.7) expect(box.height).toBeLessThanOrEqual(inner + 12)
  return { box, inner }
}

test('Reglas de aviso: la plantilla y el vendedor vienen puestos y la vista previa muestra el correo de una orden real', async ({ page, isMobile }) => {
  await login(page)
  const dialog = await openRuleModal(page)
  // el vendedor de la orden ya viene en la lista y el correo trae la orden completa
  await expect(dialog.getByText('Vendedor de la orden')).toBeVisible()
  const emailChannel = dialog.locator('[data-channel="EMAIL"]')
  await expect(emailChannel.getByRole('button', { name: 'Vista previa' })).toBeEnabled()
  await expect(dialog.getByRole('button', { name: 'Insertar {orderDetail}' })).toContainText('bloque · solo en el texto del correo')
  await dialog.getByLabel('Nombre de la regla').fill(`Correo con la orden ${LETTERS}`)
  // «Cliente de la orden» (segundo selector) y un buzón a mano
  await dialog.getByLabel('Tipo', { exact: true }).selectOption('CONTEXT')
  await dialog.getByLabel('Del evento', { exact: true }).selectOption('CLIENT')
  await expect(dialog.locator('.nr-chip', { hasText: 'Cliente de la orden' })).toBeVisible()
  await dialog.getByLabel('Tipo', { exact: true }).selectOption('EMAIL')
  await dialog.getByPlaceholder('nombre@empresa.com').fill('almacen-e2e@example.test')
  await dialog.getByRole('button', { name: 'Agregar' }).click()
  await expect(dialog.locator('.nr-chip', { hasText: 'almacen-e2e@example.test' })).toBeVisible()

  // vista previa del personal con la orden real (aunque el canal de correo no esté configurado: no envía nada)
  await emailChannel.getByRole('button', { name: 'Vista previa' }).click()
  const preview = page.getByRole('dialog', { name: 'Vista previa del correo' })
  await expect(preview).toBeVisible()
  await expect(preview.locator('iframe.nr-prev-frame').contentFrame().getByText('Farmacia Central').first()).toBeVisible({ timeout: 20_000 })   // sin número: el ejemplo
  await preview.getByLabel('Número de orden (opcional; sin él se usa un ejemplo)').fill(S.orderNumber)
  await preview.getByRole('button', { name: 'Actualizar' }).click()
  await expect(preview.getByText(`Con los datos de la orden ${S.orderNumber}.`)).toBeVisible({ timeout: 20_000 })
  const personal = await measureFrame(page, preview)
  const body = preview.locator('iframe.nr-prev-frame').contentFrame()
  // el botón solo sale si el API tiene la dirección web (Notifications:WebBaseUrl); sin ella el correo sale sin botón, por diseño
  const hasLink = (await body.getByText('Abrir la orden', { exact: false }).count()) > 0
  console.log(`botón «Abrir la orden» en el correo del personal: ${hasLink ? 'sí' : 'no (sin Notifications:WebBaseUrl)'}`)
  await expect(body.getByText(S.clientName).first()).toBeVisible()
  await expect(body.getByText('<b>prueba</b>').first()).toBeVisible()   // la nota sale codificada, no como HTML
  await noHorizontalScroll(page)
  if (isMobile) {
    mkdirSync(IMG_DIR, { recursive: true })
    await page.screenshot({ path: `${IMG_DIR}l243-vista-previa-movil.png`, animations: 'disabled' })
  } else {
    await shot(page, isMobile, 'l243-vista-previa-correo', 1000)
  }
  console.log(`vista previa personal: marco ${Math.round(personal.box.height)} px de ${personal.inner} px de contenido`)

  // la copia al cliente: sin enlace ni notas
  await preview.getByLabel('Para quién').selectOption('CUSTOMER')
  await expect(preview.getByText(`Con los datos de la orden ${S.orderNumber}.`)).toBeVisible()
  await measureFrame(page, preview)
  await expect(body.getByText('Abrir la orden', { exact: false })).toHaveCount(0)
  await expect(body.getByText('prueba', { exact: false })).toHaveCount(0)
  await noHorizontalScroll(page)
  await shot(page, isMobile, 'l243-vista-previa-cliente', 1000)

  // inglés y tema oscuro: la ventana respeta el tema, el correo conserva su fondo claro
  await preview.getByLabel('Idioma').selectOption('en')
  await expect(preview.getByText(/^Sales order OV-/)).toBeVisible({ timeout: 20_000 })
  await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dark'))
  await measureFrame(page, preview)
  const frameBackground = await preview.locator('iframe.nr-prev-frame').evaluate((el) => getComputedStyle(el).backgroundColor)
  expect(frameBackground).toBe('rgb(255, 255, 255)')
  await noHorizontalScroll(page)
  await shot(page, isMobile, 'l243-vista-previa-oscuro-en', 1000)
  await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'light'))
  await preview.locator('footer').getByRole('button', { name: 'Cerrar' }).click()
  await expect(preview).toBeHidden()

  // se guarda la regla (apagada de entrada) y se enciende solo la campanita, único canal configurado sin correo
  await dialog.getByRole('switch', { name: 'Enviar por Campanita' }).check({ force: true })
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog).toBeHidden({ timeout: 20_000 })
  const headers = await apiHeaders(page.request)
  const rules = (await (await page.request.get(`${API_URL}/api/v1/notifications/rules`, { headers })).json()) as { publicId: string; name: string; recipients: { kind: string; value?: string }[] }[]
  const rule = rules.find((r) => r.name === `Correo con la orden ${LETTERS}`)!
  expect(rule.recipients.filter((r) => r.kind === 'CONTEXT').map((r) => r.value).sort()).toEqual(['CLIENT', 'SELLER'])
  S.rulePublicId = rule.publicId
})

test('Reglas de aviso: un error del API (orden inexistente) se muestra tal cual en la vista previa', async ({ page }) => {
  await login(page)
  const dialog = await openRuleModal(page)
  await dialog.locator('[data-channel="EMAIL"]').getByRole('button', { name: 'Vista previa' }).click()
  const preview = page.getByRole('dialog', { name: 'Vista previa del correo' })
  await preview.getByLabel('Número de orden (opcional; sin él se usa un ejemplo)').fill('OV-99999')
  await preview.getByRole('button', { name: 'Actualizar' }).click()
  await expect(preview.getByRole('alert')).toHaveText("Orden de venta 'OV-99999' no encontrada.")
  await noHorizontalScroll(page)
})

test('Clientes → Personas: casilla «Recibe avisos por correo», chip en la lista y aviso si no hay correo', async ({ page, isMobile }) => {
  await login(page)
  await page.goto(`/catalog/clients?client=${S.clientId}`)
  await openClientPanels(page)
  const people = page.locator('section.panel', { has: page.getByRole('heading', { name: 'Personas de contacto', exact: true }) })
  await expect(people.getByText('Mario Marcado')).toBeVisible()
  const chipRow = isMobile ? people.locator('article, li, .dt-card').filter({ hasText: 'Mario Marcado' }).first() : people.locator('tr', { hasText: 'Mario Marcado' })
  await expect(chipRow.locator('.chip', { hasText: 'Avisos por correo' })).toBeVisible()
  // la fila no se infla: alto normal (una línea de texto con su chip), no los ~230 px de una clase global heredada
  const rowHeight = (await chipRow.boundingBox())!.height
  console.log(`fila de contacto: ${Math.round(rowHeight)} px`)
  if (!isMobile) expect(rowHeight).toBeLessThan(90)
  // la marcada sin correo se ve con la advertencia
  await expect(people.locator('.chip.s-warn', { hasText: 'Avisos por correo' })).toHaveCount(1)
  await noHorizontalScroll(page)
  // se abre «Nora Sin Correo» (marcada, sin correo): el aviso sale en la ventana
  const edit = people.locator(isMobile ? 'article, li, .dt-card' : 'tr', { hasText: 'Nora Sin Correo' }).first().getByRole('button', { name: 'Editar' })
  await edit.click()
  const dialog = page.getByRole('dialog', { name: 'Editar contacto' })
  await expect(dialog.getByRole('switch', { name: 'Recibe avisos por correo' })).toBeChecked()
  await expect(dialog.getByText('Marcado para avisos pero sin correo: agréguele un correo.')).toBeVisible()
  await noHorizontalScroll(page)
  await shot(page, isMobile, 'l243-contacto-avisos', 1000)
  await dialog.getByLabel('Correo', { exact: true }).fill(`nora-${LETTERS}@example.test`)
  await expect(dialog.getByText('Marcado para avisos pero sin correo: agréguele un correo.')).toHaveCount(0)
  await dialog.getByRole('button', { name: 'Cancelar' }).click()
})

test('Ajustes → Operación → Ventas: la ayuda del correo de la orden enlaza a la regla', async ({ page, isMobile }) => {
  await login(page)
  await page.goto('/system/settings?tab=ops')
  const help = page.getByTestId('sales-email-help')
  await expect(help).toBeVisible({ timeout: 30_000 })
  await expect(help).toContainText('El correo con la orden al solicitarla se enciende en Sistema → Reglas de aviso (evento «Orden de venta solicitada»).')
  await help.scrollIntoViewIfNeeded()
  await noHorizontalScroll(page)
  await shot(page, isMobile, 'l243-ajustes-ventas-ayuda', 1000, page.locator('section.panel', { has: help }))
  await help.getByRole('link', { name: 'Abrir Reglas de aviso' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Reglas de aviso' })).toBeVisible()
})

// La fila omitida de diagnóstico («(Vendedor de la orden)» con su motivo) no se puede provocar aquí sin carreras: el API de pruebas solo tiene la campanita
// (el canal EMAIL sin configurar no se deja encender) y el vendedor es el administrador que solicita, que siempre tiene usuario. La cubren xunit
// (NotificationContentSqlTests, SalesOrderEmailSqlTests) y la prueba de pantalla NotificationRulesScreen.test.tsx (la tabla pinta el destinatario y el motivo).
test('Registro de avisos: al solicitar otra orden la regla avisa por la campanita y el registro lo muestra', async ({ page, request, isMobile }) => {
  // la regla se enciende por API (la campanita), se solicita otra orden y se espera el aviso del vendedor
  const headers = await apiHeaders(request)
  const rule = await ok<{ rowVersion: string }>(request.get(`${API_URL}/api/v1/notifications/rules/${S.rulePublicId}`, { headers }), 'regla')
  await ok(request.patch(`${API_URL}/api/v1/notifications/rules/${S.rulePublicId}`, { headers, data: { isActive: true, rowVersion: rule.rowVersion } }), 'activar la regla')
  const second = await requestOrder(request, `Segunda orden ${LETTERS}`, S.clientId, S.warehouseId, S.productId)
  type Row = { eventCode?: string; channel?: string; recipient?: string; status?: string; error?: string; ruleName?: string }
  let rows: Row[] = []
  await expect
    .poll(
      async () => {
        const res = await ok<{ items: Row[] }>(request.get(`${API_URL}/api/v1/notifications/log?eventCode=SALES_ORDER_REQUESTED&take=50`, { headers }), 'registro')
        rows = res.items.filter((r) => r.ruleName === `Correo con la orden ${LETTERS}`)
        return rows.some((r) => r.channel === 'APP' && r.status === 'SENT')
      },
      { timeout: 60_000, intervals: [1_000, 2_000, 3_000] },
    )
    .toBe(true)
  console.log(`registro: ${rows.map((r) => `${r.channel} ${r.recipient} ${r.status}${r.error ? ` (${r.error})` : ''}`).join(' | ')}`)

  await login(page)
  await page.goto('/system/notifications')
  await page.getByRole('tab', { name: 'Registro' }).click()
  await expect(page.getByText(`Correo con la orden ${LETTERS}`).first()).toBeVisible({ timeout: 30_000 })
  await noHorizontalScroll(page)
  await shot(page, isMobile, 'l243-registro', 1000)
  expect(second.number).toMatch(/^OV-/)
})

test('Con el buzón local (Mailpit): se enciende el correo, se solicita una orden y el mensaje llega con la tabla', async ({ page, request, isMobile }) => {
  test.skip(!MAILPIT_URL, 'solo con MAILPIT_URL y el API en Development con Email__Smtp__Host (scripts/api-para-pruebas.ps1 -Mailpit)')
  const headers = await apiHeaders(request)
  const channels = await ok<{ channel: string; isConfigured: boolean; isLocal?: boolean }[]>(request.get(`${API_URL}/api/v1/notifications/channels`, { headers }), 'canales')
  const email = channels.find((c) => c.channel === 'EMAIL')
  test.skip(!email?.isConfigured || !email.isLocal, 'el canal de correo no es el buzón local')
  const rule = await ok<{ rowVersion: string; channels: { channel: string; subjectEs: string; subjectEn: string; bodyEs: string; bodyEn: string }[] }>(request.get(`${API_URL}/api/v1/notifications/rules/${S.rulePublicId}`, { headers }), 'regla')
  const channelsOn = rule.channels.map((c) => ({ ...c, isEnabled: c.channel === 'EMAIL' || c.channel === 'APP' }))
  await ok(request.patch(`${API_URL}/api/v1/notifications/rules/${S.rulePublicId}`, { headers, data: { channels: channelsOn, rowVersion: rule.rowVersion } }), 'encender el correo')
  const order = await requestOrder(request, `Correo local ${LETTERS}`, S.clientId, S.warehouseId, S.productId)
  await expect
    .poll(async () => ((await (await request.get(`${MAILPIT_URL}/api/v1/search?query=${encodeURIComponent(`subject:"${order.number}"`)}`)).json()) as { messages_count?: number }).messages_count ?? 0, { timeout: 60_000, intervals: [1_000, 2_000] })
    .toBeGreaterThan(0)
  const found = (await (await request.get(`${MAILPIT_URL}/api/v1/search?query=${encodeURIComponent(`subject:"${order.number}"`)}`)).json()) as { messages: { ID: string; To: { Address: string }[] }[] }
  const toStaff = found.messages.find((m) => m.To.some((a) => a.Address === 'almacen-e2e@example.test'))
  expect(toStaff, 'llegó al buzón de almacén').toBeTruthy()
  const html = ((await (await request.get(`${MAILPIT_URL}/api/v1/message/${toStaff!.ID}`)).json()) as { HTML: string }).HTML
  expect(html).toContain(order.number)
  expect(html).toContain('Abrir la orden')
  const toCustomer = found.messages.find((m) => m.To.some((a) => a.Address === `principal-${LETTERS}@example.test`))
  if (toCustomer) {
    const customerHtml = ((await (await request.get(`${MAILPIT_URL}/api/v1/message/${toCustomer.ID}`)).json()) as { HTML: string }).HTML
    expect(customerHtml).not.toContain('Abrir la orden')
  }
  await page.goto(`${MAILPIT_URL}/view/${toStaff!.ID}`)
  await shot(page, isMobile, 'l243-correo-recibido', 1000)
})
