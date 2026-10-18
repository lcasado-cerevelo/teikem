// Recorrido del Expediente de la orden (Lote F-C2) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos, módulos LTL_GROUND, CATALOG y CUSTOM_FIELDS encendidos).
//
// 1) Sometida: botones de la cabecera, edición dentro del Detalle (consignatario, paquetes, COD, comentario) con UN «Guardar cambios»
//    que manda solo lo cambiado; 409 de edición concurrente (otra persona guarda primero) con aviso, recarga y lo escrito intacto.
// 2) «Marcar como recogida» con crédito excedido: aviso → «Autorizar y marcar como recogida»; Cotización (vista previa); Re-cotizar con una tarifa nueva.
// 3) Estatus laterales: «Pasar a Intento fallido» y «Volver a Recogida» (solo ese regreso); campos personalizados y contactos.
// 4) Cancelar con razón obligatoria (queda en el historial).
// 5) Entrega especial: «Asignar chofer» (409 si es el mismo, chofer y viaje en el panel) y «Reasignar chofer».
// 6) Eliminar desde el expediente: vuelve a la lista.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fc2-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type OrderDetailDto = components['schemas']['OrderDetailDto']
type LocationDto = components['schemas']['LocationDto']
type DriverDetailDto = components['schemas']['DriverDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])
const CUSTOM_KEY = 'centro_costo_fc2'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string, height = 1000, only?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  // arriba del todo y sin el puntero encima de un botón (que lo pintaría en su estado «hover»)
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
  // ventana más alta solo para la captura: el expediente y los diálogos largos salen completos
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height })
  await (only ?? page).screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  if (size) await page.setViewportSize(size)
}

async function apiToken(request: APIRequestContext): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F18). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      if (el.parentElement?.closest('.dt-scroll')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      if (el.matches('.sw input')) return
      const r = el.getBoundingClientRect()
      if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')} [${Math.round(r.left)}, ${Math.round(r.right)}]`)
    })
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

async function ok<T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> {
  const res = await p
  expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
  return (await res.json()) as T
}

/** "Hoy" de la compañía demo (America/Puerto_Rico) y días después, 'YYYY-MM-DD'. */
function tenantDay(plus = 0): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Puerto_Rico', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  const d = new Date(`${today}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + plus)
  return d.toISOString().slice(0, 10)
}

/** Aviso (toast) con ese texto. */
const toast = (page: Page, text: string | RegExp): Locator => page.locator('.toast.on', { hasText: text })
/** Botón de la cabecera de la ficha (no los de los diálogos). */
const headBtn = (page: Page, name: string | RegExp): Locator => page.locator('.head .act').getByRole('button', { name, exact: typeof name === 'string' })

test.describe('Órdenes — expediente (Lote F-C2)', () => {
  test('edición, 409, crédito, re-cotizar, laterales, campos, contactos, cancelar, chofer y eliminar', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Cliente FC2 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const get = async <T,>(path: string) => ok<T>(request.get(`${API_URL}${path}`, { headers }), `GET ${path}`)

    // ---- Datos: cliente con contrato activo, tarifa y servicio especial; dos consignatarios; un chofer con tarifa por viaje ----
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, creditLimit: 10, contract: { startDate: tenantDay(-30), title: 'Contrato FC2' } } }),
      'cliente',
    )
    const contractId = client.currentContract?.publicId ?? ''
    const rate = await ok<{ id: number }>(
      request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 100, effectiveFrom: tenantDay(-30) } }),
      'tarifa',
    )
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'ENVELOPE', rate: 60, effectiveFrom: tenantDay(-30) } }), 'tarifa sobre')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    await ok(request.patch(`${API_URL}/api/v1/contracts/${contractId}/billing-model`, { headers, data: { billSpecialServices: true } }), 'modelo de facturación')
    const special = await ok<{ id: number; typeId: number }>(
      request.post(`${API_URL}/api/v1/clients/${client.publicId}/special-services`, { headers, data: { newTypeName: `Vagón FC2 ${suffix}`, rate: 150 } }),
      'servicio especial',
    )
    const place = (name: string, line1: string, city: string, zip: string) =>
      ok<LocationDto>(
        request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name, line1, city, postalCode: zip, country: 'PR' } }),
        `consignatario ${name}`,
      )
    const cons1 = await place(`Farmacia Sol FC2 ${suffix}`, 'Calle Sol 123', 'Ponce', '00716')
    await place(`Colmado Luna FC2 ${suffix}`, 'Calle Luna 45', 'Bayamón', '00959')
    const driver = await ok<DriverDetailDto>(request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code: `F2${suffix}`.slice(0, 12), fullName: `Chofer FC2 ${suffix}` } }), 'chofer')
    await ok(request.post(`${API_URL}/api/v1/drivers/${driver.publicId}/licenses`, { headers, data: { licenseClass: 'CDL_A', licenseNumber: `L-${suffix}`, expiryDate: tenantDay(365) } }), 'licencia')
    await ok(request.post(`${API_URL}/api/v1/drivers/${driver.publicId}/trip-rates`, { headers, data: { specialServiceTypeId: special.typeId, rate: 75 } }), 'tarifa por viaje')
    // un campo personalizado para las órdenes (una vez; si ya existe, queda)
    await request.post(`${API_URL}/api/v1/custom-fields/definitions/TRANSPORT_ORDER`, {
      headers,
      data: { fieldKey: CUSTOM_KEY, labels: { es: 'Centro de costo', en: 'Cost center' }, dataType: 'TEXT', isRequired: false, isUnique: false, showInList: false },
    })

    const newOrder = (extra: Record<string, unknown>) =>
      ok<OrderDetailDto>(request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: client.publicId, consigneeLocationPublicId: cons1.publicId, serviceType: 'STANDARD', overrideCredit: true, ...extra } }), 'orden')
    // al crear ya se cotiza y se mide el crédito (límite 10): el administrador autoriza el exceso por API
    const draft = await newOrder({ packages: [{ packageType: 'BOX', pieces: 2, description: 'Ropa' }], codAmount: 50, notes: 'Llamar antes' })
    const orderPath = `/orders/${draft.publicId}`

    await login(page)
    await page.goto(orderPath)
    await expect(page.getByRole('heading', { level: 1 })).toContainText(draft.orderNumber ?? '')
    await expect(page.getByRole('heading', { level: 1 })).toContainText(`Farmacia Sol FC2 ${suffix}`)

    // ---- 1. Sometida: cabecera, panel Detalle editable, paneles de la derecha ----
    for (const name of ['Marcar como recogida', 'Cancelar la orden', 'Eliminar la orden']) await expect(headBtn(page, name)).toBeVisible()
    await expect(headBtn(page, 'Re-cotizar')).toBeVisible() // Lote 106: Sometida ya nace cotizada y se puede re-cotizar
    await expect(headBtn(page, /chofer/i)).toHaveCount(0)
    await expect(headBtn(page, 'Imprimir label')).toBeVisible() // F-G2
    await expect(headBtn(page, /pod/i)).toHaveCount(0)
    await expect(page.getByText('Esta orden está en Sometida: puede corregir sus datos y sus paquetes.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()
    for (const title of ['Detalle', 'Historial', 'COD', 'Cotización']) await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Campos personalizados' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Teléfonos y correos' })).toBeVisible()
    await shot(page, isMobile, 'fc2-expediente', 1500)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- Edición: consignatario, paquete nuevo (sobre), COD y comentario; un solo «Guardar cambios» ----
    const consBox = page.getByRole('combobox', { name: /^Consignatario/ })
    await consBox.click()
    await page.getByRole('option', { name: new RegExp(`Colmado Luna FC2 ${suffix}`) }).click()
    await expect(page.getByText('Bayamón', { exact: true })).toBeVisible()
    await expect(page.getByText('00959', { exact: true })).toBeVisible()
    await page.getByLabel(/^Piezas/).first().fill('3')
    await page.getByRole('button', { name: /Añadir paquete/ }).click()
    await page.getByRole('combobox', { name: /^Tipo de paquete/ }).nth(1).selectOption('ENVELOPE')
    await page.getByLabel(/^Piezas/).nth(1).fill('1')
    await page.getByLabel('Descripción').nth(1).fill('Documentos')
    await page.getByLabel('Monto COD').fill('80')
    await page.getByLabel('Comentario', { exact: true }).fill('Dejar con el encargado')
    await expect(page.getByRole('button', { name: 'Guardar cambios' })).toBeEnabled()
    await shot(page, isMobile, 'fc2-edicion', 1500)
    if (isMobile) await expectNoHorizontalScroll(page)
    await page.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()
    let saved = await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)
    expect(saved.delivery?.name).toBe(`Colmado Luna FC2 ${suffix}`)
    expect(saved.codAmount).toBe(80)
    expect(saved.notes).toBe('Dejar con el encargado')
    expect(saved.totalPieces).toBe(4)
    expect(saved.packages?.map((p) => `${p.packageType}×${p.pieces}`).sort()).toEqual(['BOX×3', 'ENVELOPE×1'])
    expect(saved.clientInvoiceNumber).toBe(draft.clientInvoiceNumber) // lo que no se tocó, no cambia

    // validación sin llamar al servidor: piezas en cero
    await page.getByLabel(/^Piezas/).first().fill('0')
    await page.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(page.getByText('La cantidad de piezas debe ser al menos 1.')).toBeVisible()
    await page.getByLabel(/^Piezas/).first().fill('3')

    // ---- 409: otra persona guarda primero (misma ficha, rowVersion nuevo) ----
    await ok(request.patch(`${API_URL}/api/v1/orders/${draft.publicId}`, { headers, data: { notes: 'Cambio de otra persona', rowVersion: saved.rowVersion } }), 'cambio concurrente')
    await page.getByLabel('Comentario', { exact: true }).fill('Mi cambio')
    await page.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(page.locator('.form-alert').first()).toContainText('El registro fue modificado por otro usuario; recargue e intente de nuevo.')
    await shot(page, isMobile, 'fc2-conflicto', 1500)
    await expect(page.getByLabel('Comentario', { exact: true })).toHaveValue('Mi cambio') // lo escrito se conserva
    await page.getByRole('button', { name: 'Guardar cambios' }).click() // ya con el rowVersion nuevo
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    saved = await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)
    expect(saved.notes).toBe('Mi cambio')

    // ---- Re-cotizar con una tarifa nueva (Lote 106: solo mientras la orden está Sometida; al marcarla recogida el precio queda congelado) ----
    expect((await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)).quotedAmount).toBe(160) // ya nació cotizada: caja 100 + sobre 60
    const utcToday = tenantDay(0) // el día de la compañía (no el UTC: pasadas las 8 p. m. en Puerto Rico el UTC ya es mañana y la tarifa quedaría programada)
    await ok(request.patch(`${API_URL}/api/v1/contracts/${contractId}/rate-components/${rate.id}`, { headers, data: { rate: 120, effectiveFrom: utcToday } }), 'tarifa nueva')
    await headBtn(page, 'Re-cotizar').click()
    const requote = page.getByRole('dialog')
    await shot(page, isMobile, 'fc2-recotizar')
    await requote.getByRole('button', { name: 'Re-cotizar', exact: true }).click()
    await expect(toast(page, `Orden ${draft.orderNumber} re-cotizada.`)).toBeVisible()
    expect((await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)).quotedAmount).toBe(180) // caja 120 + sobre 60

    // ---- 2. Marcar como recogida con crédito excedido ----
    await headBtn(page, 'Marcar como recogida').click()
    let dialog = page.getByRole('dialog')
    await expect(dialog).toContainText(`Marcar como recogida la orden ${draft.orderNumber}`)
    await shot(page, isMobile, 'fc2-confirmar')
    await dialog.getByRole('button', { name: 'Marcar como recogida', exact: true }).click()
    const credit = page.getByRole('dialog', { name: 'El cliente excede su límite de crédito' })
    await expect(credit).toContainText('límite 10.00')
    await shot(page, isMobile, 'fc2-credito')
    if (isMobile) await expectNoHorizontalScroll(page)
    expect((await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)).status).toBe('SUBMITTED')
    await credit.getByRole('button', { name: 'Autorizar y marcar como recogida' }).click()
    await expect(toast(page, `Orden ${draft.orderNumber} marcada como recogida.`)).toBeVisible()
    await expect(credit).toBeHidden()
    saved = await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)
    expect(saved.status).toBe('PICKUP')
    expect(saved.quotedAmount).toBe(180) // el precio que tenía al marcarla recogida (caja 120 + sobre 60) queda congelado
    // recogida: los datos pasan a referencia, ya no se edita ni se re-cotiza (precio congelado), y queda Cancelar
    await expect(page.getByText('Esta orden ya avanzó de Sometida', { exact: false })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Guardar cambios' })).toHaveCount(0)
    await expect(headBtn(page, 'Marcar como recogida')).toHaveCount(0)
    await expect(headBtn(page, 'Eliminar la orden')).toHaveCount(0)
    await expect(headBtn(page, 'Re-cotizar')).toHaveCount(0)
    await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/profile`, { headers, data: { creditLimit: 100000 } }), 'subir el límite')

    // ---- Cotización (vista previa) ----
    const quotePanel = page.getByRole('heading', { name: 'Cotización', exact: true }).locator('xpath=ancestor::section[1]')
    await expect(quotePanel).toContainText('$180.00')
    await quotePanel.getByRole('button', { name: 'Cotizar' }).click()
    await expect(quotePanel).toContainText('Vista previa con las tarifas vigentes')
    await expect(quotePanel).toContainText('Estándar · Caja ×3')
    await expect(quotePanel).toContainText('Dentro del límite')
    await shot(page, isMobile, 'fc2-cotizacion', 1500)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- Campos personalizados y contactos ----
    const customPanel = page.getByRole('heading', { name: 'Campos personalizados' }).locator('xpath=ancestor::section[1]')
    await customPanel.getByLabel('Centro de costo').fill('CC-FARMA-02')
    await customPanel.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Campos personalizados guardados.')).toBeVisible()
    const values = await get<{ fieldKey: string; value: unknown }[]>(`/api/v1/custom-fields/values/TRANSPORT_ORDER/${draft.id}`)
    expect(values.find((v) => v.fieldKey === CUSTOM_KEY)?.value).toBe('CC-FARMA-02')

    const contactsPanel = page.getByRole('heading', { name: 'Teléfonos y correos' }).locator('xpath=ancestor::section[1]')
    await expect(contactsPanel).toContainText('Esta orden no tiene contactos')
    await contactsPanel.getByRole('button', { name: 'Agregar teléfono' }).click()
    dialog = page.getByRole('dialog')
    await dialog.getByLabel(/^Teléfono/).fill('7875550142')
    await dialog.getByLabel('Etiqueta').fill('Recibe: Carmen Ortiz')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Medio de contacto guardado.')).toBeVisible()
    await expect(contactsPanel).toContainText('(787) 555-0142')
    await expect(contactsPanel).toContainText('Recibe: Carmen Ortiz')
    const points = await get<{ value: string; label: string }[]>(`/api/v1/contacts/TRANSPORT_ORDER/${draft.id}`)
    expect(points.map((p) => p.label)).toContain('Recibe: Carmen Ortiz')
    await shot(page, isMobile, 'fc2-paneles', 1500, page.locator('.od-side'))

    // ---- 3. Laterales: Intento fallido y regreso exacto ----
    await expect(page.getByRole('button', { name: /Avanzar a|Pasar a Cancelada|Pasar a Entregada/ })).toHaveCount(0)
    await page.getByRole('button', { name: 'Pasar a Intento fallido' }).click()
    dialog = page.getByRole('dialog')
    await dialog.getByLabel('Comentario (opcional)').fill('El cliente no está')
    await dialog.getByRole('button', { name: 'Cambiar estatus' }).click()
    await expect(toast(page, `La orden ${draft.orderNumber} pasó a Intento fallido.`)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Volver a Recogida' })).toBeVisible()
    // desde un lateral solo se vuelve a la etapa de la que salió (no a la siguiente, ni se cancela por aquí)
    await expect(page.getByRole('button', { name: /Volver a (?!Recogida)|Avanzar a|Pasar a Cancelada/ })).toHaveCount(0)
    expect((await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)).status).toBe('FAILED')
    await expect(page.getByText('El cliente no está')).toBeVisible() // el historial real lo muestra
    await shot(page, isMobile, 'fc2-lateral', 1500)
    if (isMobile) await expectNoHorizontalScroll(page)
    await page.getByRole('button', { name: 'Volver a Recogida' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Cambiar estatus' }).click()
    await expect(toast(page, `La orden ${draft.orderNumber} pasó a Recogida.`)).toBeVisible()
    expect((await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)).status).toBe('PICKUP')

    // ---- 4. Cancelar con comentario ----
    await headBtn(page, 'Cancelar la orden').click()
    dialog = page.getByRole('dialog')
    await expect(dialog).toContainText(`Cancelar la orden ${draft.orderNumber}`)
    await dialog.getByLabel('Razón de la cancelación').fill('El cliente anuló el pedido')
    await shot(page, isMobile, 'fc2-cancelar')
    if (isMobile) await expectNoHorizontalScroll(page)
    await dialog.getByRole('button', { name: 'Cancelar la orden' }).click()
    await expect(toast(page, `Orden ${draft.orderNumber} cancelada.`)).toBeVisible()
    expect((await get<OrderDetailDto>(`/api/v1/orders/${draft.publicId}`)).status).toBe('CANCELLED')
    await expect(page.getByText('El cliente anuló el pedido')).toBeVisible()
    await expect(headBtn(page, 'Cancelar la orden')).toHaveCount(0)
    await expect(headBtn(page, 'Re-cotizar')).toHaveCount(0)
    await shot(page, isMobile, 'fc2-cancelada', 1500)

    // ---- 5. Entrega especial: asignar chofer ----
    const sp = await newOrder({ packages: null, isSpecialDelivery: true, specialServiceId: special.id })
    await page.goto(`/orders/${sp.publicId}`)
    await expect(page.getByRole('heading', { level: 1 })).toContainText(sp.orderNumber ?? '')
    await expect(page.getByRole('heading', { name: 'Chofer asignado' })).toBeVisible()
    await expect(page.getByText('Todavía no tiene chofer asignado.')).toBeVisible()
    await expect(page.getByLabel('Monto COD')).toHaveCount(0) // una entrega especial no edita paquetes ni COD
    await headBtn(page, 'Asignar chofer').click()
    dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Chofer' }).click()
    await dialog.getByRole('combobox', { name: 'Chofer' }).fill(`Chofer FC2 ${suffix}`)
    await page.getByRole('option', { name: new RegExp(`Chofer FC2 ${suffix}`) }).click()
    await dialog.getByLabel('Comentario (opcional)').fill('Urgente')
    await shot(page, isMobile, 'fc2-chofer')
    if (isMobile) await expectNoHorizontalScroll(page)
    await dialog.getByRole('button', { name: 'Asignar chofer' }).click()
    await expect(toast(page, `Chofer asignado a la orden ${sp.orderNumber}.`)).toBeVisible()
    const spSaved = await get<OrderDetailDto>(`/api/v1/orders/${sp.publicId}`)
    expect(spSaved.status).toBe('IN_TRANSIT')
    expect(spSaved.assignedDriverName).toBe(`Chofer FC2 ${suffix}`)
    const driverPanel = page.getByRole('heading', { name: 'Chofer asignado' }).locator('xpath=ancestor::section[1]')
    await expect(driverPanel).toContainText(`Chofer FC2 ${suffix}`)
    await shot(page, isMobile, 'fc2-chofer-asignado', 1500)
    // ya en tránsito: se puede reasignar, pero el mismo chofer da el 409 del servidor
    await expect(headBtn(page, 'Asignar chofer')).toHaveCount(0)
    await headBtn(page, 'Reasignar chofer').click()
    dialog = page.getByRole('dialog')
    await expect(dialog).toContainText(`Reasignar chofer de la orden ${sp.orderNumber}`)
    await dialog.getByRole('combobox', { name: 'Chofer' }).click()
    await dialog.getByRole('combobox', { name: 'Chofer' }).fill(`Chofer FC2 ${suffix}`)
    await page.getByRole('option', { name: new RegExp(`Chofer FC2 ${suffix}`) }).click()
    await dialog.getByRole('button', { name: 'Reasignar chofer' }).click()
    await expect(dialog.getByRole('alert')).toContainText('La orden ya está asignada a ese chofer.')
    await shot(page, isMobile, 'fc2-chofer-error')
    await dialog.getByRole('button', { name: 'Volver' }).click()
    await expect(dialog).toBeHidden()

    // ---- 6. Eliminar desde el expediente ----
    const toDelete = await newOrder({ packages: [{ packageType: 'BOX', pieces: 1 }] })
    await page.goto(`/orders/${toDelete.publicId}`)
    await headBtn(page, 'Eliminar la orden').click()
    dialog = page.getByRole('dialog')
    await expect(dialog).toContainText(toDelete.orderNumber ?? '')
    await dialog.getByRole('button', { name: 'Eliminar', exact: true }).click()
    await expect(toast(page, `Orden ${toDelete.orderNumber} eliminada.`)).toBeVisible()
    await expect(page).toHaveURL(/\/orders$/)
  })
})
