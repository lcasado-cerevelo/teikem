// Recorrido de Choferes y tarifas (Lote F-D1) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos, módulos CATALOG y CUSTOM_FIELDS encendidos).
//
// 1) Lista: fórmula de pago arriba, filas con código, nombre, chip y «zona · área», buscador y filtros.
// 2) Alta de un chofer (código repetido = 409 bajo el campo), edición con «Guardar cambios», Disponibilidad y casilla Activo.
// 3) Licencias con vencimiento (Vigente / Por vencer / Vencida), editar y quitar.
// 4) Tarifas: por entrega (alta, editar con vigencia futura, historial, cerrar), por intento (sin tarifa ≠ $0) y por viaje.
// 5) Fórmula de pago y vista previa del servidor; contactos.
// 6) Viaje pagado: registrar (monto congelado), cancelar con comentario y verlo con el filtro de estatus.
// 7) Eliminar chofer: efectos explicados, comentario, ficha de solo lectura y «Mostrar inactivos».
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fd1-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type DispatchZoneDto = components['schemas']['DispatchZoneDto']
type DriverPayPolicyDto = components['schemas']['DriverPayPolicyDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string, height = 1000, only?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  // arriba del todo y sin el puntero encima de un botón (que lo pintaría en su estado «hover»)
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–FC2). */
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
/** Panel por el título de su cabecera. */
const panel = (page: Page, name: string): Locator => page.getByRole('heading', { level: 2, name, exact: true }).locator('xpath=ancestor::section[1]')
/** Fila de una tabla (escritorio) o tarjeta (móvil) que contiene ese texto. */
const rowOf = (scope: Locator, text: string | RegExp): Locator => scope.locator('tbody tr, li.dt-card').filter({ hasText: text })

test.describe('Choferes y tarifas (Lote F-D1)', () => {
  test('alta, edición, estatus, licencias, tarifas, fórmula, viaje pagado y eliminar', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const code = `FD${suffix}`
    const name1 = `Chofer FD1 ${suffix}`
    const name2 = `Chofer FD1 ${suffix} Rev`

    // ---- Datos: dos zonas y dos tipos de servicio especial (el catálogo de «Tipo de viaje») ----
    await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `ZA${suffix}`, name: `Toa Baja ${suffix}` } }), 'zona A')
    await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `ZB${suffix}`, name: `Caguas ${suffix}` } }), 'zona B')
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FD1 ${suffix}`, contract: { startDate: tenantDay(-30), title: 'Contrato FD1' } } }),
      'cliente',
    )
    await ok(request.post(`${API_URL}/api/v1/contracts/${client.currentContract?.publicId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    await ok(request.patch(`${API_URL}/api/v1/contracts/${client.currentContract?.publicId}/billing-model`, { headers, data: { billSpecialServices: true } }), 'modelo de facturación')
    const tripType = `Vagón FD1 ${suffix}`
    await ok(request.post(`${API_URL}/api/v1/clients/${client.publicId}/special-services`, { headers, data: { newTypeName: tripType, rate: 150 } }), 'servicio especial')
    const policy0 = await ok<DriverPayPolicyDto>(request.get(`${API_URL}/api/v1/driver-pay-policy`, { headers }), 'política inicial')

    await login(page)
    await page.goto('/catalog/drivers')
    await expect(page.getByRole('heading', { level: 1, name: 'Choferes y tarifas' })).toBeVisible()
    const roster = page.getByRole('list', { name: 'Choferes' })
    const formulaSelect = page.getByLabel('Cuando una entrega toma más de un intento, el chofer cobra…')

    // ---- 1. Lista y fórmula de pago ----
    await expect(formulaSelect).toBeVisible()
    await expect(page.getByText(/^Vista previa · ejemplo con 2 intentos/)).toBeVisible()
    await expect(page.getByRole('button', { name: /Nuevo chofer/ })).toBeVisible()
    await expect(page.getByRole('switch', { name: 'Mostrar inactivos' })).toBeVisible()
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 2. Alta ----
    await page.getByRole('button', { name: /Nuevo chofer/ }).click()
    let dialog = page.getByRole('dialog', { name: 'Nuevo chofer' })
    await dialog.getByLabel(/^Código/).fill(code.toLowerCase())
    await dialog.getByLabel(/^Nombre/).fill(name1)
    await dialog.getByLabel('Zona').selectOption({ label: `ZA${suffix} · Toa Baja ${suffix}` })
    await dialog.getByLabel('Tope de paradas').fill('12')
    await dialog.getByLabel('Fecha de alta').fill(tenantDay(-30))
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd1-alta', 760, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Chofer creado.')).toBeVisible()
    await expect(dialog).toBeHidden()
    const ficha = panel(page, 'Ficha del chofer')
    await expect(ficha.getByLabel(/^Nombre/)).toHaveValue(name1)
    await expect(ficha.getByLabel('Código')).toHaveValue(code) // en mayúsculas
    await expect(ficha.getByLabel('Código')).toBeDisabled()
    await expect(ficha.getByTestId('driver-area')).toHaveText(`Toa Baja ${suffix}`)
    await expect(roster.getByRole('button', { name: new RegExp(name1) })).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(name1) })).toContainText(`ZA${suffix} · Toa Baja ${suffix}`)

    // código repetido: 409 bajo el campo y el modal sigue abierto
    await page.getByRole('button', { name: /Nuevo chofer/ }).click()
    dialog = page.getByRole('dialog', { name: 'Nuevo chofer' })
    await dialog.getByLabel(/^Código/).fill(code)
    await dialog.getByLabel(/^Nombre/).fill('Otro nombre')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('Ya existe un chofer con ese código.')).toBeVisible()
    await shot(page, isMobile, 'fd1-codigo-repetido', 700, dialog)
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(dialog).toBeHidden()

    // validaciones de la ficha sin llamar al servidor
    await ficha.getByLabel(/^Nombre/).fill('')
    await ficha.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(ficha.getByText('El nombre del chofer es obligatorio.')).toBeVisible()

    // ---- 2b. Edición ----
    await ficha.getByLabel(/^Nombre/).fill(name2)
    await ficha.getByLabel('Zona').selectOption({ label: `ZB${suffix} · Caguas ${suffix}` })
    await expect(ficha.getByTestId('driver-area')).toHaveText(`Caguas ${suffix}`)
    await ficha.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toBeVisible()
    await expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()
    await shot(page, isMobile, 'fd1-ficha', 700, ficha)

    // ---- 2c. Disponibilidad y casilla Activo (dos cosas distintas) ----
    await ficha.getByRole('button', { name: 'No disponible' }).click()
    await expect(toast(page, 'El chofer quedó no disponible.')).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toContainText('No disponible')
    await ficha.getByRole('button', { name: 'Disponible', exact: true }).click()
    await expect(toast(page, 'El chofer está disponible.')).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toContainText('Activo')
    await ficha.getByRole('switch', { name: 'Activo' }).uncheck()
    await expect(toast(page, /Chofer desactivado/)).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toHaveCount(0)
    await expect(ficha.getByLabel(/^Nombre/)).toHaveValue(name2) // la ficha no salta a otro chofer
    await page.getByRole('switch', { name: 'Mostrar inactivos' }).check()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toContainText('Inactivo')
    await page.getByRole('switch', { name: 'Mostrar inactivos' }).uncheck()
    await ficha.getByRole('switch', { name: 'Activo' }).check()
    await expect(toast(page, 'Chofer activado.')).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toBeVisible()
    // buscador (va al servidor)
    await page.getByRole('searchbox').first().fill(`ZB${suffix}`.toLowerCase())
    await expect(roster.getByRole('button')).toHaveCount(1)
    await page.getByRole('searchbox').first().fill('')
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toBeVisible()

    // ---- 3. Licencias con vencimiento ----
    const docs = panel(page, 'Licencias y certificaciones')
    await docs.getByRole('button', { name: 'Agregar licencia' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar licencia' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('La clase de licencia es obligatoria.')).toBeVisible()
    await expect(dialog.getByText('El número de licencia es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Clase de licencia/).selectOption({ value: 'CDL_A' })
    await dialog.getByLabel(/^Número de licencia/).fill(`L-${suffix}-A`)
    await dialog.getByLabel('Emisión').fill(tenantDay(-300))
    await dialog.getByLabel('Vencimiento').fill(tenantDay(-400))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('La fecha de vencimiento no puede ser anterior a la de emisión.')).toBeVisible()
    await dialog.getByLabel('Vencimiento').fill(tenantDay(10))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Documento guardado.')).toBeVisible()
    await expect(rowOf(docs, `L-${suffix}-A`)).toContainText('Vence en 10 días')

    await docs.getByRole('button', { name: 'Agregar licencia' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar licencia' })
    await dialog.getByLabel(/^Clase de licencia/).selectOption({ value: 'REGULAR' })
    await dialog.getByLabel(/^Número de licencia/).fill(`L-${suffix}-V`)
    await dialog.getByLabel('Vencimiento').fill(tenantDay(-5))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `L-${suffix}-V`)).toContainText('Vencida hace 5 días')

    await docs.getByRole('button', { name: 'Agregar licencia' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar licencia' })
    await dialog.getByLabel(/^Clase de licencia/).selectOption({ value: 'CDL_B' })
    await dialog.getByLabel(/^Número de licencia/).fill(`L-${suffix}-S`)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `L-${suffix}-S`)).toContainText('Sin vencimiento')

    // una licencia más nueva del mismo tipo deja «Reemplazada» a la anterior
    await docs.getByRole('button', { name: 'Agregar licencia' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar licencia' })
    await dialog.getByLabel(/^Clase de licencia/).selectOption({ value: 'CDL_A' })
    await dialog.getByLabel(/^Número de licencia/).fill(`L-${suffix}-B`)
    await dialog.getByLabel('Vencimiento').fill(tenantDay(400))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `L-${suffix}-A`)).toContainText('Reemplazada')
    await expect(rowOf(docs, `L-${suffix}-B`)).toContainText('Vigente')
    await docs.getByRole('button', { name: 'Agregar certificación' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar certificación' })
    await dialog.getByLabel(/^Tipo de certificación/).selectOption({ value: 'HAZMAT' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(docs.getByRole('region', { name: 'Certificaciones' })).toContainText('HazMat')
    await shot(page, isMobile, 'fd1-licencias', 900, docs)

    // editar (cambia el vencimiento) y quitar
    await rowOf(docs, `L-${suffix}-V`).getByRole('button', { name: 'Editar' }).click()
    dialog = page.getByRole('dialog', { name: 'Editar licencia' })
    await dialog.getByLabel('Vencimiento').fill(tenantDay(60))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `L-${suffix}-V`)).toContainText('Vigente')
    await rowOf(docs, `L-${suffix}-S`).getByRole('button', { name: 'Quitar' }).click()
    dialog = page.getByRole('dialog', { name: 'Quitar licencia' })
    await dialog.getByRole('button', { name: 'Quitar' }).click()
    await expect(rowOf(docs, `L-${suffix}-S`)).toHaveCount(0)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 4. Tarifas por entrega ----
    const rates = panel(page, 'Tarifas por entrega e intento')
    const delivery = rates.getByRole('region', { name: 'Tarifas por entrega' })
    // el alta inline (los filtros, cuando hay tarifas, llevan las mismas etiquetas)
    const addSvc = delivery.locator('select[name="serviceType"]')
    const addPkg = delivery.locator('select[name="packageType"]')
    const addRate = delivery.locator('input[name="rate"]')
    await expect(rates.getByText('Sin tarifas por entrega configuradas')).toBeVisible()
    await delivery.getByRole('button', { name: /Agregar/ }).click()
    await expect(delivery.getByText('Indique el servicio y el tipo de paquete de la tarifa.').first()).toBeVisible()
    await expect(delivery.getByText('Indique la tarifa.')).toBeVisible()
    await addSvc.selectOption({ value: 'STANDARD' })
    await addPkg.selectOption({ value: 'BOX' })
    await addRate.fill('5')
    await delivery.getByRole('button', { name: /Agregar/ }).click()
    await expect(toast(page, 'Tarifa agregada.')).toBeVisible()
    await expect(rowOf(delivery, 'Estándar')).toContainText('$5.00')
    // el mismo par otra vez: 409 del servidor con su mensaje
    await addSvc.selectOption({ value: 'STANDARD' })
    await addPkg.selectOption({ value: 'BOX' })
    await addRate.fill('9')
    await delivery.getByRole('button', { name: /Agregar/ }).click()
    await expect(delivery.getByText(/El chofer ya tiene una tarifa vigente para Estándar \+ Caja/)).toBeVisible()
    await addRate.fill('')
    // segunda tarifa (Express · Sobre) para cerrarla después
    await addSvc.selectOption({ value: 'EXPRESS' })
    await addPkg.selectOption({ value: 'ENVELOPE' })
    await addRate.fill('7.5')
    await delivery.getByRole('button', { name: /Agregar/ }).click()
    await expect(rowOf(delivery, 'Express')).toContainText('$7.50')

    // editar con vigencia: hoy cambia la tarifa; una fecha futura deja la vigente y agenda la nueva
    await rowOf(delivery, 'Estándar').getByRole('button', { name: 'Editar' }).click()
    dialog = page.getByRole('dialog', { name: 'Editar tarifa' })
    await expect(dialog.getByLabel(/^Vigente desde/)).toHaveValue(tenantDay())
    await dialog.getByLabel(/^Vigente desde/).fill(tenantDay(-1))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.')).toBeVisible()
    await dialog.getByLabel(/^Tarifa/).fill('6')
    await dialog.getByLabel(/^Vigente desde/).fill(tenantDay())
    await shot(page, isMobile, 'fd1-editar-tarifa', 700, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Tarifa guardada.')).toBeVisible()
    await expect(rowOf(delivery, 'Estándar')).toContainText('$6.00')
    await rowOf(delivery, 'Estándar').getByRole('button', { name: 'Editar' }).click()
    dialog = page.getByRole('dialog', { name: 'Editar tarifa' })
    await dialog.getByLabel(/^Tarifa/).fill('6.5')
    await dialog.getByLabel(/^Vigente desde/).fill(tenantDay(10))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Tarifa guardada.')).toBeVisible()
    // hoy sigue rigiendo $6.00 (la fila quedó cerrada a futuro: ya no se edita)
    await expect(rowOf(delivery, 'Estándar')).toContainText('$6.00')
    await expect(rowOf(delivery, 'Estándar').getByRole('button', { name: 'Editar' })).toHaveCount(0)

    // historial
    await rates.getByRole('switch', { name: 'Ver historial' }).check()
    await expect(rowOf(delivery, 'Estándar')).toHaveCount(3)
    await expect(rowOf(delivery, 'Estándar').filter({ hasText: '$6.50' })).toHaveCount(1)
    await expect(rowOf(delivery, 'Estándar').filter({ hasText: '$5.00' })).toHaveCount(1)
    await shot(page, isMobile, 'fd1-historial', 1000, rates)
    await rates.getByRole('switch', { name: 'Ver historial' }).uncheck()

    // cerrar tarifa (nunca se borra)
    await rowOf(delivery, 'Express').getByRole('button', { name: 'Cerrar tarifa' }).click()
    dialog = page.getByRole('dialog', { name: 'Cerrar tarifa' })
    await shot(page, isMobile, 'fd1-cerrar-tarifa', 600, dialog)
    await dialog.getByRole('button', { name: 'Cerrar tarifa' }).click()
    await expect(toast(page, 'Tarifa cerrada.')).toBeVisible()
    await expect(rowOf(delivery, 'Express')).toHaveCount(0)
    await rates.getByRole('switch', { name: 'Ver historial' }).check()
    await expect(rowOf(delivery, 'Express')).toHaveCount(1)
    await rates.getByRole('switch', { name: 'Ver historial' }).uncheck()

    // filtros Servicio / Paquete (del cliente)
    await delivery.getByRole('group', { name: 'Filtros' }).getByRole('button', { name: 'Servicio' }).click()
    await page.getByRole('listbox').getByRole('option', { name: 'Estándar' }).click({ force: true })
    await page.keyboard.press('Escape')
    await expect(rowOf(delivery, 'Estándar')).toHaveCount(1)
    await delivery.getByRole('button', { name: 'Limpiar' }).click()

    // ---- 4b. Tarifas por intento: «sin tarifa» ≠ $0 ----
    const attempts = rates.getByRole('list', { name: 'Tarifas por intento' })
    await expect(attempts.locator('li', { hasText: 'Intento 1' })).toContainText('sin tarifa')
    const addLevel = rates.getByRole('button', { name: '+ Agregar intento' })
    const levelsBefore = await attempts.locator('li').count()
    if (await addLevel.isEnabled()) {
      await addLevel.click()
      await expect(toast(page, 'Se agregó un nivel de intento.')).toBeVisible()
      await expect(attempts.locator('li')).toHaveCount(levelsBefore + 1)
    }
    await attempts.locator('li', { hasText: 'Intento 1' }).getByRole('button', { name: 'Fijar tarifa' }).click()
    dialog = page.getByRole('dialog', { name: 'Fijar la tarifa del intento 1' })
    await dialog.getByLabel(/^Tarifa/).fill('0')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(attempts.locator('li', { hasText: 'Intento 1' })).toContainText('$0.00')
    await expect(attempts.locator('li', { hasText: 'Intento 1' })).not.toContainText('sin tarifa')
    await attempts.locator('li', { hasText: 'Intento 2' }).getByRole('button', { name: 'Fijar tarifa' }).click()
    dialog = page.getByRole('dialog', { name: 'Fijar la tarifa del intento 2' })
    await dialog.getByLabel(/^Tarifa/).fill('3')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(attempts.locator('li', { hasText: 'Intento 2' })).toContainText('$3.00')

    // ---- 4c. Tarifas por viaje ----
    const tripRates = panel(page, 'Tarifas por viaje')
    await expect(tripRates.getByText('Sin tarifas por viaje configuradas')).toBeVisible()
    await tripRates.getByLabel('Tipo de viaje', { exact: true }).selectOption({ label: tripType })
    await tripRates.getByLabel('Tarifa').fill('75')
    await tripRates.getByRole('button', { name: /Agregar/ }).click()
    await expect(toast(page, 'Tarifa agregada.')).toBeVisible()
    await expect(rowOf(tripRates, tripType)).toContainText('$75.00')
    await shot(page, isMobile, 'fd1-tarifas', 1100, rates)

    // ---- 5. Fórmula y vista previa (la calcula el servidor) ----
    const preview = page.locator('.drv-preview')
    await expect(preview).toContainText('Entrega')
    await expect(preview).toContainText('Intento 1')
    await expect(preview).toContainText(name2)
    const before = await preview.innerText()
    await formulaSelect.selectOption('FAILED_REPLACES_DELIVERY')
    await expect(toast(page, 'Fórmula de pago guardada.')).toBeVisible()
    await expect(formulaSelect).toHaveValue('FAILED_REPLACES_DELIVERY')
    await expect.poll(async () => preview.innerText()).not.toBe(before)
    await shot(page, isMobile, 'fd1-formula', 500, page.locator('.drv-formula').locator('xpath=ancestor::section[1]'))
    await formulaSelect.selectOption(policy0.payoutFormulaCode ?? 'DELIVERY_PLUS_ATTEMPTS')
    await expect(formulaSelect).toHaveValue(policy0.payoutFormulaCode ?? 'DELIVERY_PLUS_ATTEMPTS')

    // ---- contactos (entidad DRIVER) ----
    const contacts = panel(page, 'Teléfonos y correos')
    await contacts.getByRole('button', { name: 'Agregar teléfono' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar teléfono' })
    await dialog.getByLabel(/^Teléfono/).fill('7875550142')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(contacts.getByText('(787) 555-0142')).toBeVisible()

    // ---- 6. Viaje pagado ----
    const trips = panel(page, 'Viajes pagados')
    await expect(trips.getByText('No hay viajes pagados con estos filtros.')).toBeVisible()
    await trips.getByRole('button', { name: /Registrar viaje/ }).click()
    dialog = page.getByRole('dialog', { name: 'Registrar viaje pagado' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El tipo de viaje es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Tipo de viaje/).selectOption({ label: tripType })
    await dialog.getByLabel(/^Fecha/).fill(tenantDay(1))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('La fecha del viaje no puede ser futura.')).toBeVisible()
    await dialog.getByLabel(/^Fecha/).fill(tenantDay())
    await dialog.getByLabel('Notas').fill('Muelle 3')
    await shot(page, isMobile, 'fd1-registrar-viaje', 700, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Viaje registrado.')).toBeVisible()
    await expect(rowOf(trips, tripType)).toContainText('$75.00')
    await expect(rowOf(trips, tripType)).toContainText('Muelle 3')
    await shot(page, isMobile, 'fd1-viajes', 800, trips)

    await rowOf(trips, tripType).getByRole('button', { name: 'Cancelar viaje' }).click()
    dialog = page.getByRole('dialog', { name: 'Cancelar viaje' })
    await dialog.getByLabel('Comentario (opcional)').fill('Duplicado')
    await dialog.getByRole('button', { name: 'Cancelar viaje' }).click()
    await expect(toast(page, 'Viaje cancelado.')).toBeVisible()
    await expect(rowOf(trips, tripType)).toHaveCount(0)
    await trips.getByLabel('Estatus').selectOption({ label: 'Cancelado' })
    await expect(rowOf(trips, tripType)).toContainText('Cancelado')

    // ---- dispositivos e historial de estatus ----
    await expect(panel(page, 'Dispositivos')).toContainText('Este chofer no tiene dispositivos. Los registra la app del chofer.')
    const history = panel(page, 'Historial de estatus')
    await expect(history.getByRole('list', { name: 'Historial de estatus' })).toContainText('Activo')
    await expect(history.getByRole('list', { name: 'Historial de estatus' }).locator('li').first()).toContainText('No disponible')
    if (isMobile) await expectNoHorizontalScroll(page)

    await page.getByRole('searchbox').first().fill(`ZB${suffix}`.toLowerCase())
    await expect(roster.getByRole('button')).toHaveCount(1)
    await shot(page, isMobile, 'fd1-lista', 1300)
    await page.getByRole('searchbox').first().fill('')

    // ---- 7. Eliminar chofer ----
    await ficha.getByRole('button', { name: /Eliminar chofer/ }).click()
    dialog = page.getByRole('dialog', { name: `Eliminar al chofer ${name2}` })
    await expect(dialog).toContainText('sus tarifas abiertas se cierran hoy;')
    await dialog.getByLabel('Comentario (opcional)').fill('Renunció')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd1-eliminar', 800, dialog)
    await dialog.getByRole('button', { name: 'Eliminar chofer' }).click()
    await expect(toast(page, 'Chofer eliminado.')).toBeVisible()
    await expect(page.getByText('El chofer fue eliminado; solo se consulta su historial.').first()).toBeVisible()
    await expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toHaveCount(0)
    await expect(ficha.getByRole('button', { name: /Eliminar chofer/ })).toHaveCount(0)
    await expect(ficha.getByLabel(/^Nombre/)).toBeDisabled()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toHaveCount(0)
    // las tarifas abiertas se cerraron hoy: solo se consultan
    await expect(rates.getByText('El chofer fue eliminado; sus tarifas y viajes solo se consultan.')).toBeVisible()
    await expect(rowOf(delivery, 'Estándar')).toHaveCount(0)
    await page.getByRole('switch', { name: 'Mostrar inactivos' }).check()
    await expect(roster.getByRole('button', { name: new RegExp(name2) })).toContainText('Eliminado')
    await shot(page, isMobile, 'fd1-eliminado', 1000, ficha)
    if (isMobile) await expectNoHorizontalScroll(page)

    // la fórmula quedó como estaba
    const policy1 = await ok<DriverPayPolicyDto>(request.get(`${API_URL}/api/v1/driver-pay-policy`, { headers }), 'política final')
    expect(policy1.payoutFormulaCode).toBe(policy0.payoutFormulaCode)
  })
})
