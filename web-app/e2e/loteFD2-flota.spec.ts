// Recorrido de Flota y mantenimiento (Lote F-D2) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos, módulo CATALOG encendido).
//
// 1) Pantalla: cabecera, tarjetas resumen y las pestañas (F-D3 agregó Mantenimiento, Órdenes de trabajo y Combustible).
// 2) Alta de un vehículo (validaciones, código repetido = 409), ficha con «Guardar cambios», 409 de rowVersion y VIN repetido.
// 3) Estatus (Disponible / En mantenimiento), casilla Activo y filtros de la lista.
// 4) Documentos del vehículo: vencimientos (Vigente / Por vencer / Vencido), reemplazado, editar y quitar.
// 5) Documentos por vencer: filtros y clic que abre al vehículo.
// 6) Zonas de despacho: alta, código repetido, edición, inactivar con 409 (choferes asignados) y reactivar.
// 7) Disponibilidad: motivos que bloquean y avisos, «Solo disponibles».
// 8) Dar de baja definitiva: efectos, ficha de solo lectura y «Mostrar inactivos».
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fd2-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type DispatchZoneDto = components['schemas']['DispatchZoneDto']
type DriverDetailDto = components['schemas']['DriverDetailDto']
type VehicleDetailDto = components['schemas']['VehicleDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
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
  await page.evaluate(() =>
    document.querySelectorAll('*').forEach((el) => {
      if (el.scrollTop > 0) el.scrollTop = 0
    }),
  )
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height })
  const manualShot1 = await manualTarget((only ?? page))
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–FD1). */
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

/** Escribe una fecha y sale del campo con Tab: el calendario que se abre al escribir tapaba el botón Guardar. */
async function fillDate(input: Locator, iso: string): Promise<void> {
  await input.fill(iso)
  await input.press('Tab')
}

/** Aviso (toast) con ese texto. */
const toast = (page: Page, text: string | RegExp): Locator => page.locator('.toast.on', { hasText: text })
/** Panel por el título de su cabecera. */
const panel = (page: Page, name: string): Locator => page.getByRole('heading', { level: 2, name, exact: true }).locator('xpath=ancestor::section[1]')
/** Fila de una tabla (escritorio) o tarjeta (móvil) que contiene ese texto. */
const rowOf = (scope: Locator, text: string | RegExp): Locator => scope.locator('tbody tr, li.dt-card').filter({ hasText: text })

test.describe('Flota y mantenimiento (Lote F-D2)', () => {
  test('alta de vehículo, ficha, estatus, documentos, documentos por vencer, zonas, disponibilidad y baja definitiva', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const code = `FD2${suffix}`
    const plate = `PL${suffix}`
    const plate2 = `PM${suffix}`
    const vin1 = `VIN1${suffix}`
    const vin2 = `VIN2${suffix}`

    // ---- Datos: dos zonas, dos choferes (uno con licencia por vencer y zona; otro sin licencia) y otro vehículo con VIN propio ----
    const zoneA = await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `FA${suffix}`, name: `Flota Toa Baja ${suffix}` } }), 'zona A')
    await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `FB${suffix}`, name: `Flota Caguas ${suffix}` } }), 'zona B')
    const driverA = await ok<DriverDetailDto>(
      request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code: `F2A${suffix}`, fullName: `Chofer FD2 ${suffix} A`, dispatchZoneId: zoneA.id } }),
      'chofer A',
    )
    await ok(request.post(`${API_URL}/api/v1/drivers/${driverA.publicId}/licenses`, { headers, data: { licenseClass: 'CDL_A', licenseNumber: `L-${suffix}`, expiryDate: tenantDay(12) } }), 'licencia')
    await ok<DriverDetailDto>(request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code: `F2B${suffix}`, fullName: `Chofer FD2 ${suffix} B` } }), 'chofer B')
    const other = await ok<VehicleDetailDto>(request.post(`${API_URL}/api/v1/vehicles`, { headers, data: { code: `FO${suffix}`, plateNumber: plate2, vin: vin2 } }), 'otro vehículo')

    await login(page)
    await page.goto('/catalog/fleet')
    await expect(page.getByRole('heading', { level: 1, name: 'Flota y mantenimiento' })).toBeVisible()

    // ---- 1. Pantalla: tarjetas y pestañas ----
    await expect(page.getByRole('button', { name: /Vehículos activos/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Documentos por vencer \(≤ 30 d\)/ })).toBeVisible()
    await expect(page.getByRole('tablist', { name: 'Secciones de flota' }).getByRole('tab')).toHaveText([
      'Vehículos',
      'Mantenimiento',
      'Órdenes de trabajo',
      'Combustible',
      'Documentos por vencer',
      'Zonas de despacho',
      'Disponibilidad',
    ])
    await expect(page.getByRole('button', { name: /Nuevo vehículo/ })).toBeVisible()
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 2. Alta ----
    await page.getByRole('button', { name: /Nuevo vehículo/ }).click()
    let dialog = page.getByRole('dialog', { name: 'Nuevo vehículo' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El código del vehículo es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Código/).fill(code.toLowerCase())
    await dialog.getByLabel('Año del modelo').fill('1850')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText(/^El año del modelo debe estar entre 1900 y \d{4}\.$/)).toBeVisible()
    await dialog.getByLabel('Año del modelo').fill('2022')
    await dialog.getByLabel('Placa').fill(plate)
    await dialog.getByLabel('Tipo').selectOption({ label: 'Van' })
    await dialog.getByLabel('Propiedad').selectOption({ label: 'Propio' })
    await dialog.getByLabel('Combustible').selectOption({ label: 'Diésel' })
    await dialog.getByLabel('Marca').fill('Ford')
    await dialog.getByLabel('Modelo', { exact: true }).fill('Transit')
    await dialog.getByLabel('VIN').fill(vin1.toLowerCase())
    await dialog.getByLabel('Odómetro (km)').fill('1000')
    await dialog.getByLabel('Tope de paradas').fill('40')
    await dialog.getByLabel('Peso máx (kg)').fill('1200')
    await dialog.getByLabel('Volumen máx (m³)').fill('9')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd2-alta', 900, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Vehículo creado.')).toBeVisible()
    await expect(dialog).toBeHidden()
    const ficha = panel(page, 'Ficha del vehículo')
    await expect(ficha.getByLabel('Código')).toHaveValue(code) // en mayúsculas
    await expect(ficha.getByLabel('Código')).toBeDisabled()
    await expect(ficha.getByLabel('VIN')).toHaveValue(vin1) // sin espacios y en mayúsculas
    const roster = page.getByRole('list', { name: 'Vehículos' })
    await expect(roster.getByRole('button', { name: new RegExp(plate) })).toContainText('Disponible')

    // código repetido: 409 y el modal sigue abierto; un VIN repetido entre activos también
    await page.getByRole('button', { name: /Nuevo vehículo/ }).click()
    dialog = page.getByRole('dialog', { name: 'Nuevo vehículo' })
    await dialog.getByLabel(/^Código/).fill(code)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('Ya existe un vehículo con ese código.')).toBeVisible()
    await shot(page, isMobile, 'fd2-codigo-repetido', 700, dialog)
    await dialog.getByLabel(/^Código/).fill(`${code}X`)
    await dialog.getByLabel('VIN').fill(vin2)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('Ya existe un vehículo activo con ese VIN.')).toBeVisible()
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(dialog).toBeHidden()

    // ---- 2b. Ficha: validaciones, edición, 409 de rowVersion y VIN repetido ----
    await ficha.getByLabel('Peso máx (kg)').fill('-4')
    await ficha.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(ficha.getByText('La capacidad no puede ser negativa.')).toBeVisible()
    await ficha.getByLabel('Peso máx (kg)').fill('1300')
    await ficha.getByLabel('Placa').fill(`${plate}B`)
    await ficha.getByLabel('Odómetro (km)').fill('1500')
    await ficha.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    await expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()
    await expect(roster.getByRole('button', { name: new RegExp(`${plate}B`) })).toBeVisible()
    await shot(page, isMobile, 'fd2-ficha', 1100, ficha)

    // alguien más guarda antes (por API): el siguiente guardado es 409 con el mensaje exacto y conserva lo escrito
    const mine = await ok<VehicleDetailDto>(request.get(`${API_URL}/api/v1/vehicles/${(await currentPublicId(page))}`, { headers }), 'mi vehículo')
    await ok(request.patch(`${API_URL}/api/v1/vehicles/${mine.publicId}`, { headers, data: { make: 'Ford (otro usuario)' } }), 'cambio ajeno')
    await ficha.getByLabel('Modelo', { exact: true }).fill('Transit 350')
    await ficha.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(ficha.getByText('El registro fue modificado por otro usuario; recargue e intente de nuevo.')).toBeVisible()
    await shot(page, isMobile, 'fd2-409-version', 1100, ficha)
    await expect(ficha.getByLabel('Modelo', { exact: true })).toHaveValue('Transit 350')
    await ficha.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    await expect(ficha.getByLabel('Marca')).toHaveValue('Ford (otro usuario)')
    await expect(ficha.getByLabel('Modelo', { exact: true })).toHaveValue('Transit 350')

    // VIN repetido entre vehículos activos
    await ficha.getByLabel('VIN').fill(vin2)
    await ficha.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(ficha.getByText('Ya existe un vehículo activo con ese VIN.')).toBeVisible()
    await ficha.getByLabel('VIN').fill(vin1)
    await expect(ficha.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()

    // ---- 3. Estatus, casilla Activo (dos cosas distintas) y filtros ----
    await ficha.getByRole('button', { name: 'En mantenimiento' }).click()
    await expect(toast(page, 'El vehículo quedó en mantenimiento.')).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(`${plate}B`) })).toContainText('En mantenimiento')
    await ficha.getByRole('button', { name: 'Disponible', exact: true }).click()
    await expect(toast(page, 'El vehículo está disponible.')).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(`${plate}B`) })).toContainText('Disponible')
    await ficha.getByRole('switch', { name: 'Activo' }).uncheck()
    await expect(toast(page, /Vehículo desactivado/)).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(`${plate}B`) })).toHaveCount(0)
    await expect(ficha.getByLabel('Código')).toHaveValue(code) // la ficha no salta a otro vehículo
    await page.getByRole('switch', { name: 'Mostrar inactivos' }).check()
    await expect(roster.getByRole('button', { name: new RegExp(`${plate}B`) })).toContainText('Inactivo')
    await page.getByRole('switch', { name: 'Mostrar inactivos' }).uncheck()
    await ficha.getByRole('switch', { name: 'Activo' }).check()
    await expect(toast(page, 'Vehículo activado.')).toBeVisible()
    await expect(roster.getByRole('button', { name: new RegExp(`${plate}B`) })).toBeVisible()
    // buscador y filtros (van al servidor)
    const listPanel = panel(page, 'Vehículos')
    await listPanel.getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(roster.getByRole('button')).toHaveCount(2)
    await listPanel.getByLabel('Tipo').selectOption({ label: 'Van' })
    await listPanel.getByLabel('Combustible').selectOption({ label: 'Diésel' })
    await expect(roster.getByRole('button')).toHaveCount(1)
    await listPanel.getByLabel('Combustible').selectOption({ label: 'Todos' })
    await listPanel.getByLabel('Tipo').selectOption({ label: 'Todos' })
    await expect(roster.getByRole('button')).toHaveCount(2)
    await listPanel.getByLabel('Estatus').selectOption({ label: 'En mantenimiento' })
    await expect(roster.getByRole('button')).toHaveCount(0)
    await expect(listPanel.getByText('Sin resultados')).toBeVisible()
    await listPanel.getByLabel('Estatus').selectOption({ label: 'Todos' })
    await expect(roster.getByRole('button', { name: new RegExp(`${plate}B`) })).toBeVisible()
    await roster.getByRole('button', { name: new RegExp(`${plate}B`) }).click()
    await expect(ficha.getByLabel('Código')).toHaveValue(code)
    await shot(page, isMobile, 'fd2-lista', 1300)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 4. Documentos del vehículo ----
    const docs = panel(page, 'Documentos del vehículo')
    await expect(docs.getByText('Sin documentos registrados.')).toBeVisible()
    await docs.getByRole('button', { name: 'Agregar documento' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar documento' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El tipo de documento es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Documento/).selectOption({ label: 'Seguro' })
    await dialog.getByLabel('Número').fill(`S-${suffix}-A`)
    await fillDate(dialog.getByLabel('Emisión'), tenantDay(-300))
    await fillDate(dialog.getByLabel('Vencimiento'), tenantDay(-400))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('La fecha de vencimiento no puede ser anterior a la de emisión.')).toBeVisible()
    await fillDate(dialog.getByLabel('Vencimiento'), tenantDay(10))
    await shot(page, isMobile, 'fd2-documento-alta', 700, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Documento guardado.')).toBeVisible()
    await expect(rowOf(docs, `S-${suffix}-A`)).toContainText('Vence en 10 días')

    await docs.getByRole('button', { name: 'Agregar documento' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar documento' })
    await dialog.getByLabel(/^Documento/).selectOption({ label: 'Registro' })
    await dialog.getByLabel('Número').fill(`R-${suffix}`)
    await fillDate(dialog.getByLabel('Vencimiento'), tenantDay(-5))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `R-${suffix}`)).toContainText('Vencida hace 5 días')

    await docs.getByRole('button', { name: 'Agregar documento' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar documento' })
    await dialog.getByLabel(/^Documento/).selectOption({ label: 'Inspección' })
    await dialog.getByLabel('Número').fill(`I-${suffix}`)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `I-${suffix}`)).toContainText('Sin vencimiento')

    // un seguro más nuevo deja «Reemplazada» al anterior
    await docs.getByRole('button', { name: 'Agregar documento' }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar documento' })
    await dialog.getByLabel(/^Documento/).selectOption({ label: 'Seguro' })
    await dialog.getByLabel('Número').fill(`S-${suffix}-B`)
    await fillDate(dialog.getByLabel('Vencimiento'), tenantDay(400))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `S-${suffix}-A`)).toContainText('Reemplazada')
    await expect(rowOf(docs, `S-${suffix}-B`)).toContainText('Vigente')
    await shot(page, isMobile, 'fd2-documentos', 1500, docs)

    // editar (nuevo vencimiento por vencer) y quitar
    await rowOf(docs, `I-${suffix}`).getByRole('button', { name: 'Editar' }).click()
    dialog = page.getByRole('dialog', { name: 'Editar documento' })
    await fillDate(dialog.getByLabel('Vencimiento'), tenantDay(20))
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(docs, `I-${suffix}`)).toContainText('Vence en 20 días')
    await rowOf(docs, `S-${suffix}-A`).getByRole('button', { name: 'Quitar' }).click()
    dialog = page.getByRole('dialog', { name: 'Quitar documento' })
    await shot(page, isMobile, 'fd2-documento-quitar', 500, dialog)
    await dialog.getByRole('button', { name: 'Quitar' }).click()
    await expect(toast(page, 'Documento quitado.')).toBeVisible()
    await expect(rowOf(docs, `S-${suffix}-A`)).toHaveCount(0)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 5. Documentos por vencer ----
    await page.getByRole('tab', { name: 'Documentos por vencer' }).click()
    const due = panel(page, 'Documentos por vencer')
    await expect(rowOf(due, code)).toHaveCount(2) // registro vencido + inspección a 20 días (el seguro nuevo vence en 400)
    await expect(rowOf(due, code).filter({ hasText: 'Registro' })).toContainText('Vencido')
    await expect(rowOf(due, code).filter({ hasText: 'Inspección' })).toContainText('20 días')
    // el buscador deja a la vista solo lo de este recorrido (la base de pruebas trae documentos de otros)
    await due.getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(due, new RegExp(suffix))).toHaveCount(3) // registro, inspección y la licencia del chofer A (el seguro vence en 400 días)
    await shot(page, isMobile, 'fd2-por-vencer', 900, due)
    await due.getByRole('searchbox').fill('')
    // filtros Documento y Entidad (multi), «Dentro de» e «Incluir vencidos»
    await due.getByLabel('Entidad', { exact: true }).click()
    await page.getByRole('option', { name: 'Chofer' }).click()
    await page.keyboard.press('Escape')
    await expect(rowOf(due, code)).toHaveCount(0)
    await expect(rowOf(due, `Chofer FD2 ${suffix} A`)).toHaveCount(1) // su licencia vence en 12 días
    await due.getByRole('button', { name: 'Limpiar' }).click()
    await due.getByLabel('Documento', { exact: true }).click()
    await page.getByRole('option', { name: 'Registro' }).click()
    await page.keyboard.press('Escape')
    await expect(rowOf(due, code)).toHaveCount(1)
    await due.getByRole('switch', { name: 'Incluir vencidos' }).uncheck()
    await expect(rowOf(due, code)).toHaveCount(0)
    await due.getByRole('button', { name: 'Limpiar' }).click()
    await due.getByLabel('Dentro de (días)').fill('5')
    await expect(rowOf(due, code)).toHaveCount(1) // solo el vencido
    await due.getByLabel('Dentro de (días)').fill('400')
    await expect(due.getByText('Escriba un número entero entre 0 y 365.')).toBeVisible()
    await due.getByLabel('Dentro de (días)').fill('365')
    await expect(rowOf(due, code)).toHaveCount(2)
    await due.getByRole('button', { name: 'Limpiar' }).click()
    await expect(due.getByLabel('Dentro de (días)')).toHaveValue('30')
    if (isMobile) await expectNoHorizontalScroll(page)
    // un clic abre al vehículo
    await rowOf(due, code).filter({ hasText: 'Inspección' }).click()
    await expect(page.getByRole('tab', { name: 'Vehículos' })).toHaveAttribute('aria-selected', 'true')
    await expect(panel(page, 'Ficha del vehículo').getByLabel('Código')).toHaveValue(code)

    // ---- 6. Zonas de despacho ----
    await page.getByRole('tab', { name: 'Zonas de despacho' }).click()
    const zones = panel(page, 'Zonas de despacho')
    await zones.getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(zones, `FA${suffix}`)).toContainText('1') // el chofer A
    await expect(rowOf(zones, `FB${suffix}`)).toBeVisible()
    await zones.getByRole('button', { name: /Nueva zona/ }).click()
    dialog = page.getByRole('dialog', { name: 'Nueva zona' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El código de la zona es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Código/).fill(`FA${suffix}`)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('Ya existe una zona de despacho con ese código.')).toBeVisible()
    await shot(page, isMobile, 'fd2-zona-repetida', 600, dialog)
    await dialog.getByLabel(/^Código/).fill(`fc${suffix}`)
    await dialog.getByLabel('Nombre').fill(`Flota Norte ${suffix}`)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Zona creada.')).toBeVisible()
    await expect(rowOf(zones, `FC${suffix}`)).toContainText(`Flota Norte ${suffix}`)
    await rowOf(zones, `FC${suffix}`).getByRole('button', { name: 'Editar' }).click()
    dialog = page.getByRole('dialog', { name: 'Editar zona' })
    await expect(dialog.getByLabel('Código')).toBeDisabled()
    await dialog.getByLabel('Nombre').fill(`Flota Norte ${suffix} (editada)`)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Zona guardada.')).toBeVisible()
    await expect(rowOf(zones, `FC${suffix}`)).toContainText('(editada)')
    // inactivar una zona con un chofer asignado: 409 visible; la que no tiene choferes sí se inactiva
    await rowOf(zones, `FA${suffix}`).getByRole('button', { name: 'Inactivar' }).click()
    dialog = page.getByRole('dialog', { name: 'Inactivar zona' })
    await dialog.getByRole('button', { name: 'Inactivar' }).click()
    await expect(dialog.getByText('La zona tiene choferes asignados; reasígnelos antes de inactivarla.')).toBeVisible()
    await shot(page, isMobile, 'fd2-zona-409', 600, dialog)
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(dialog).toBeHidden()
    await shot(page, isMobile, 'fd2-zonas', 900, zones)
    await rowOf(zones, `FC${suffix}`).getByRole('button', { name: 'Inactivar' }).click()
    dialog = page.getByRole('dialog', { name: 'Inactivar zona' })
    await dialog.getByRole('button', { name: 'Inactivar' }).click()
    await expect(toast(page, 'Zona inactivada.')).toBeVisible()
    await expect(rowOf(zones, `FC${suffix}`)).toHaveCount(0)
    await zones.getByRole('switch', { name: 'Mostrar inactivas' }).check()
    await expect(rowOf(zones, `FC${suffix}`)).toContainText('Inactivo')
    await rowOf(zones, `FC${suffix}`).getByRole('button', { name: 'Reactivar' }).click()
    await expect(toast(page, 'Zona reactivada.')).toBeVisible()
    await expect(rowOf(zones, `FC${suffix}`)).toContainText('Activo')
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 7. Disponibilidad ----
    await page.getByRole('tab', { name: 'Disponibilidad' }).click()
    const avail = panel(page, 'Disponibilidad para despacho')
    await avail.getByRole('searchbox').fill(suffix.toLowerCase())
    const drivers = panel(page, 'Choferes')
    const vehicles = panel(page, 'Vehículos')
    // chofer A: licencia que vence en 12 días = solo aviso, sigue disponible
    await expect(rowOf(drivers, `F2A${suffix}`)).toContainText('Disponible')
    await expect(rowOf(drivers, `F2A${suffix}`)).toContainText('Aviso')
    await expect(rowOf(drivers, `F2A${suffix}`)).toContainText(`Licencia CDL-A vence el`)
    // chofer B: sin licencia = bloquea
    await expect(rowOf(drivers, `F2B${suffix}`)).toContainText('No disponible')
    await expect(rowOf(drivers, `F2B${suffix}`)).toContainText('Sin licencia vigente.')
    await expect(rowOf(drivers, `F2B${suffix}`)).toContainText('Bloquea')
    // mi vehículo: registro vencido = bloquea; el otro: sin documentos = aviso
    await expect(rowOf(vehicles, code)).toContainText('No disponible')
    await expect(rowOf(vehicles, code)).toContainText('Registro vencido el')
    await expect(rowOf(vehicles, `FO${suffix}`)).toContainText('Disponible')
    await expect(rowOf(vehicles, `FO${suffix}`)).toContainText('El vehículo no tiene documentos registrados.')
    await shot(page, isMobile, 'fd2-disponibilidad', 1100, page.locator('.flt-stack'))
    if (isMobile) await expectNoHorizontalScroll(page)
    await avail.getByRole('switch', { name: 'Solo disponibles' }).check()
    await expect(rowOf(drivers, `F2B${suffix}`)).toHaveCount(0)
    await expect(rowOf(vehicles, code)).toHaveCount(0)
    await expect(rowOf(drivers, `F2A${suffix}`)).toHaveCount(1)
    await avail.getByRole('switch', { name: 'Solo disponibles' }).uncheck()
    // otra fecha: dentro de 15 días la licencia de A ya venció = bloquea
    await avail.getByLabel('Fecha').fill(tenantDay(15))
    await expect(rowOf(drivers, `F2A${suffix}`)).toContainText('No disponible')
    await expect(rowOf(drivers, `F2A${suffix}`)).toContainText('Su licencia venció el')
    await avail.getByLabel('Fecha').fill(tenantDay())
    // un vehículo en mantenimiento no está disponible
    await ok(request.post(`${API_URL}/api/v1/vehicles/${other.publicId}/status`, { headers, data: { toCode: 'MAINTENANCE' } }), 'mantenimiento por API')
    await page.reload()
    await page.getByRole('tab', { name: 'Disponibilidad' }).click()
    await avail.getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(vehicles, `FO${suffix}`)).toContainText('El vehículo está en estatus')

    // ---- 8. Dar de baja definitiva ----
    await page.goto(`/catalog/fleet?vehicle=${mine.publicId}`)
    const ficha2 = panel(page, 'Ficha del vehículo')
    await expect(ficha2.getByLabel('Código')).toHaveValue(code)
    await ficha2.getByRole('button', { name: /Dar de baja definitiva/ }).click()
    dialog = page.getByRole('dialog', { name: `Dar de baja al vehículo ${code}` })
    await expect(dialog).toContainText('sale de la disponibilidad para despacho;')
    await dialog.getByLabel('Comentario (opcional)').fill('Vendido')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd2-baja', 800, dialog)
    await dialog.getByRole('button', { name: 'Dar de baja definitiva' }).click()
    await expect(toast(page, 'Vehículo dado de baja.')).toBeVisible()
    await expect(page.getByText('El vehículo fue dado de baja; solo se consulta su historial.').first()).toBeVisible()
    await expect(ficha2.getByRole('button', { name: 'Guardar cambios' })).toHaveCount(0)
    await expect(ficha2.getByRole('button', { name: /Dar de baja definitiva/ })).toHaveCount(0)
    await expect(ficha2.getByLabel('Placa')).toBeDisabled()
    // el servidor también lo impide (antes un vehículo dado de baja seguía editable por la API)
    const retiredEdit = await request.patch(`${API_URL}/api/v1/vehicles/${mine.publicId}`, { headers, data: { plateNumber: 'NO-DEBE' } })
    expect(retiredEdit.status()).toBe(409)
    expect(((await retiredEdit.json()) as { title: string }).title).toBe('El vehículo está dado de baja definitiva; solo se consulta su historial.')
    await expect(ficha2.getByRole('switch', { name: 'Activo' })).toBeDisabled()
    await expect(panel(page, 'Documentos del vehículo').getByRole('button', { name: 'Agregar documento' })).toHaveCount(0)
    await expect(panel(page, 'Historial de estatus').getByRole('list', { name: 'Historial de estatus' }).locator('li').first()).toContainText('Inactivo')
    await page.getByRole('switch', { name: 'Mostrar inactivos' }).check()
    await panel(page, 'Vehículos').getByRole('searchbox').fill(suffix.toLowerCase())
    const rosterAll = page.getByRole('list', { name: 'Vehículos' })
    await expect(rosterAll.getByRole('button', { name: new RegExp(`${plate}B`) })).toContainText('Baja definitiva')
    await shot(page, isMobile, 'fd2-baja-ficha', 1100, page.locator('.flt-detail'))
    // la baja ya no cuenta en la disponibilidad ni en documentos por vencer
    await page.getByRole('tab', { name: 'Disponibilidad' }).click()
    await panel(page, 'Disponibilidad para despacho').getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(panel(page, 'Vehículos'), code)).toHaveCount(0)
    await page.getByRole('tab', { name: 'Documentos por vencer' }).click()
    await expect(rowOf(panel(page, 'Documentos por vencer'), code)).toHaveCount(0)
    if (isMobile) await expectNoHorizontalScroll(page)
  })
})

/** El publicId del vehículo elegido, leído de la dirección (`?vehicle=`) o de la ficha cargada. */
async function currentPublicId(page: Page): Promise<string> {
  await expect.poll(() => new URL(page.url()).searchParams.get('vehicle')).not.toBeNull()
  return new URL(page.url()).searchParams.get('vehicle') ?? ''
}
