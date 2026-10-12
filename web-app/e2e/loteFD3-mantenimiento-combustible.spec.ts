// Recorrido de Mantenimiento y combustible de Flota (Lote F-D3) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo, admin con todos los permisos, módulo CATALOG encendido).
//
// 1) Pantalla: las cuatro tarjetas resumen y las siete pestañas.
// 2) Mantenimiento: alta de un programa por vehículo (validaciones), vencimiento (Al día → Vencido al editar), programa por tipo (Sin
//    historial), filtros, desactivar y reactivar.
// 3) Órdenes de trabajo: crear desde el programa, ficha, tareas (alta, completar, quitar, costos calculados), cerrar con el 422 de las tareas
//    sin completar, Iniciar (el vehículo pasa a «En mantenimiento» y la disponibilidad lo refleja), Cerrar con odómetro (el vehículo vuelve a
//    «Disponible», sube su odómetro y avanza el programa) y Cancelar una orden con costos capturados en el encabezado.
// 4) Combustible: alta, validaciones, odómetro menor rechazado por el servidor, km/L y costo/km calculados, edición, quitar, resumen,
//    filtros, orden sobre todo lo filtrado y el efecto en el vencimiento.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fd3-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type DriverDetailDto = components['schemas']['DriverDetailDto']
type VehicleDetailDto = components['schemas']['VehicleDetailDto']
type WorkOrderListItemDto = components['schemas']['WorkOrderListItemDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(420_000)

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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–FD2). */
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

test.describe('Mantenimiento y combustible (Lote F-D3)', () => {
  test('programas, vencimiento, órdenes de trabajo (tareas, iniciar, cerrar, cancelar) y bitácora de combustible', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const plateA = `MA${suffix}`
    const plateB = `MB${suffix}`
    const driverName = `Chofer FD3 ${suffix}`

    // ---- Datos: dos vehículos de tipo Van (odómetro 1000) y un chofer ----
    const vehicleA = await ok<VehicleDetailDto>(
      request.post(`${API_URL}/api/v1/vehicles`, { headers, data: { code: `F3A${suffix}`, plateNumber: plateA, vehicleType: 'VAN', currentOdometerKm: 1000 } }),
      'vehículo A',
    )
    const vehicleB = await ok<VehicleDetailDto>(request.post(`${API_URL}/api/v1/vehicles`, { headers, data: { code: `F3B${suffix}`, plateNumber: plateB, vehicleType: 'VAN' } }), 'vehículo B')
    await ok<DriverDetailDto>(request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code: `F3D${suffix}`, fullName: driverName } }), 'chofer')
    const vehicleStatus = async (id: string) => (await ok<VehicleDetailDto>(request.get(`${API_URL}/api/v1/vehicles/${id}`, { headers }), 'vehículo')).statusCode
    const vehicleOdometer = async (id: string) => (await ok<VehicleDetailDto>(request.get(`${API_URL}/api/v1/vehicles/${id}`, { headers }), 'vehículo')).currentOdometerKm

    await login(page)
    await page.goto('/catalog/fleet')
    await expect(page.getByRole('heading', { level: 1, name: 'Flota y mantenimiento' })).toBeVisible()

    // ---- 1. Pantalla: tarjetas y pestañas ----
    await expect(page.getByRole('button', { name: /Vehículos activos/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Documentos por vencer \(≤ 30 d\)/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Mantenimiento preventivo \(vencidos\)/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Órdenes de trabajo \(abiertas\)/ })).toBeVisible()
    await expect(page.getByRole('tablist', { name: 'Secciones de flota' }).getByRole('tab')).toHaveText([
      'Vehículos',
      'Mantenimiento',
      'Órdenes de trabajo',
      'Combustible',
      'Documentos por vencer',
      'Zonas de despacho',
      'Disponibilidad',
    ])
    await shot(page, isMobile, 'fd3-pantalla', 900, page.locator('.flt-river'))
    if (isMobile) await expectNoHorizontalScroll(page)
    // las tarjetas llevan a su pestaña
    await page.getByRole('button', { name: /Órdenes de trabajo \(abiertas\)/ }).click()
    await expect(page.getByRole('tab', { name: 'Órdenes de trabajo' })).toHaveAttribute('aria-selected', 'true')
    await page.getByRole('button', { name: /Mantenimiento preventivo \(vencidos\)/ }).click()
    await expect(page.getByRole('tab', { name: 'Mantenimiento' })).toHaveAttribute('aria-selected', 'true')

    // ---- 2. Mantenimiento: programa por vehículo ----
    const due = panel(page, 'Mantenimiento preventivo')
    const programs = panel(page, 'Programas de mantenimiento')
    await programs.getByRole('button', { name: /Nuevo programa/ }).click()
    let dialog = page.getByRole('dialog', { name: 'Nuevo programa' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El nombre del programa es obligatorio.')).toBeVisible()
    await expect(dialog.getByText('Indique el vehículo o el tipo de vehículo del programa, no ambos.')).toBeVisible()
    await expect(dialog.getByText('Un programa por kilometraje exige un intervalo en km mayor que 0.')).toBeVisible()
    await dialog.getByLabel(/^Nombre/).fill(`Aceite ${suffix}`)
    await dialog.getByLabel(/^Vehículo/).selectOption({ label: `F3A${suffix} · ${plateA}` })
    await dialog.getByLabel('Cada (km)').fill('5000')
    await dialog.getByLabel('Último servicio (km)').fill('500')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd3-programa-alta', 900, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Programa creado.')).toBeVisible()
    await expect(dialog).toBeHidden()

    await due.getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Al día')
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('5,500 km') // 500 + 5000
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Faltan 4,500 km')
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText(plateA)

    // editar: con intervalo de 500 km y último servicio en 400, el vehículo (1000 km) está vencido
    await programs.getByRole('searchbox').fill(suffix.toLowerCase())
    await rowOf(programs, `Aceite ${suffix}`).getByRole('button', { name: 'Editar' }).click()
    dialog = page.getByRole('dialog', { name: 'Editar programa' })
    await dialog.getByLabel('Cada (km)').fill('500')
    await dialog.getByLabel('Último servicio (km)').fill('400')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Programa guardado.')).toBeVisible()
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Vencido')
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Pasado por 100 km')

    // programa por tipo: sin historial para los vehículos de ese tipo
    await programs.getByRole('button', { name: /Nuevo programa/ }).click()
    dialog = page.getByRole('dialog', { name: 'Nuevo programa' })
    await dialog.getByLabel(/^Nombre/).fill(`Revisión ${suffix}`)
    await dialog.getByLabel(/^Aplica a/).selectOption({ label: 'Un tipo de vehículo' })
    await expect(dialog.getByLabel('Último servicio (km)')).toHaveCount(0)
    await dialog.getByLabel(/^Tipo de vehículo/).selectOption({ label: 'Van' })
    await dialog.getByLabel(/^Disparador/).selectOption({ label: 'Tiempo' })
    await dialog.getByLabel('Cada (días)').fill('36501')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El intervalo en días admite como máximo 36500.')).toBeVisible()
    await dialog.getByLabel('Cada (días)').fill('90')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Programa creado.')).toBeVisible()
    // una fila por cada Van de la compañía (la base de pruebas trae otras, en varias páginas): se busca por la placa
    await due.getByRole('searchbox').fill(plateA.toLowerCase())
    await expect(rowOf(due, `Revisión ${suffix}`)).toHaveCount(1)
    await due.getByRole('searchbox').fill(plateB.toLowerCase())
    await expect(rowOf(due, `Revisión ${suffix}`)).toContainText('Sin historial')
    await expect(rowOf(due, `Revisión ${suffix}`)).toContainText('Tiempo')
    await due.getByRole('searchbox').fill(suffix.toLowerCase())
    // la captura deja a la vista solo el vehículo de este recorrido
    await due.getByLabel('Vehículo', { exact: true }).selectOption({ label: `F3A${suffix} · ${plateA}` })
    await expect(rowOf(due, `Aceite ${suffix}`)).toBeVisible()
    await due.getByRole('searchbox').fill('')
    await expect(rowOf(due, `Revisión ${suffix}`)).toHaveCount(1)
    await shot(page, isMobile, 'fd3-mantenimiento', 1100, due)
    if (isMobile) await expectNoHorizontalScroll(page)
    await due.getByRole('button', { name: 'Limpiar' }).click()
    await due.getByRole('searchbox').fill(suffix.toLowerCase())

    // filtros del panel: Estado y Vehículo (van al servidor)
    await due.getByLabel('Estado').selectOption({ label: 'Vencido' })
    await expect(rowOf(due, `Aceite ${suffix}`)).toHaveCount(1)
    await expect(rowOf(due, `Revisión ${suffix}`)).toHaveCount(0)
    await due.getByLabel('Estado').selectOption({ label: 'Todos' })
    await due.getByLabel('Vehículo', { exact: true }).selectOption({ label: `F3B${suffix} · ${plateB}` })
    await expect(rowOf(due, `Revisión ${suffix}`)).toHaveCount(1)
    await expect(rowOf(due, `Aceite ${suffix}`)).toHaveCount(0)
    await due.getByRole('button', { name: 'Limpiar' }).click()
    await due.getByRole('searchbox').fill(suffix.toLowerCase())

    // desactivar el programa por tipo (con confirmación) y reactivarlo
    await programs.getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(programs, `Revisión ${suffix}`)).toContainText(/Tipo: Van/)
    await rowOf(programs, `Revisión ${suffix}`).getByRole('button', { name: 'Desactivar' }).click()
    dialog = page.getByRole('dialog', { name: 'Desactivar programa' })
    await shot(page, isMobile, 'fd3-programa-desactivar', 500, dialog)
    await dialog.getByRole('button', { name: 'Desactivar' }).click()
    await expect(toast(page, 'Programa desactivado.')).toBeVisible()
    await expect(rowOf(programs, `Revisión ${suffix}`)).toHaveCount(0)
    await expect(rowOf(due, `Revisión ${suffix}`)).toHaveCount(0)
    await programs.getByRole('switch', { name: 'Mostrar inactivos' }).check()
    await expect(rowOf(programs, `Revisión ${suffix}`)).toContainText('Inactivo')
    await rowOf(programs, `Revisión ${suffix}`).getByRole('button', { name: 'Reactivar' }).click()
    await expect(toast(page, 'Programa reactivado.')).toBeVisible()
    await programs.getByRole('switch', { name: 'Mostrar inactivos' }).uncheck()
    await shot(page, isMobile, 'fd3-programas', 1000, programs)

    // ---- 3. Órdenes de trabajo: crear desde el programa ----
    await rowOf(due, `Aceite ${suffix}`).getByRole('button', { name: 'Crear orden de trabajo' }).click()
    dialog = page.getByRole('dialog', { name: 'Nueva orden de trabajo' })
    await expect(dialog.getByLabel(/^Vehículo/)).toHaveValue(vehicleA.publicId ?? '')
    await expect(dialog.getByLabel('Programa preventivo')).not.toHaveValue('')
    await expect(dialog.getByLabel(/^Tipo de mantenimiento/)).toHaveValue('PREVENTIVE')
    await dialog.getByLabel('Proveedor').fill('Taller Díaz')
    await dialog.getByLabel('Fecha programada').fill(tenantDay())
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd3-orden-alta', 1100, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    const created = toast(page, /Orden OT-\d+ creada\./)
    await expect(created).toBeVisible()
    const number = /OT-\d+/.exec((await created.textContent()) ?? '')?.[0] ?? ''
    expect(number).not.toBe('')

    await page.getByRole('tab', { name: 'Órdenes de trabajo' }).click()
    const orders = panel(page, 'Órdenes de trabajo')
    await orders.getByRole('searchbox').fill(plateA.toLowerCase())
    await expect(rowOf(orders, number)).toContainText('Abierta')
    await expect(rowOf(orders, number)).toContainText('Preventivo')
    await expect(rowOf(orders, number)).toContainText('Taller Díaz')
    await rowOf(orders, number).click()
    const ficha = panel(page, `Orden ${number}`)
    await expect(ficha).toBeVisible()
    await expect(ficha.getByLabel('Proveedor')).toHaveValue('Taller Díaz')
    await expect(ficha.getByLabel('Labor')).toBeEnabled() // sin tareas se capturan en el encabezado
    const tasks = panel(page, 'Tareas')
    await expect(tasks.getByText('Esta orden no tiene tareas.')).toBeVisible()

    // tareas: validación, alta de dos y los costos se calculan
    await tasks.getByRole('button', { name: /Agregar tarea/ }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar tarea' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('La descripción de la tarea es obligatoria.')).toBeVisible()
    await dialog.getByLabel(/^Descripción/).fill('Cambio de aceite')
    await dialog.getByLabel('Partes').fill('40')
    await dialog.getByLabel('Labor').fill('20')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Tarea guardada.')).toBeVisible()
    await tasks.getByRole('button', { name: /Agregar tarea/ }).click()
    dialog = page.getByRole('dialog', { name: 'Agregar tarea' })
    await dialog.getByLabel(/^Descripción/).fill('Rotación de gomas')
    await dialog.getByLabel('Partes').fill('0')
    await dialog.getByLabel('Labor').fill('35.5')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(rowOf(tasks, 'Rotación de gomas')).toBeVisible()
    await expect(tasks.getByText('Totales').locator('xpath=..')).toContainText('Labor $55.50')
    await expect(tasks.getByText('Totales').locator('xpath=..')).toContainText('Total $95.50')
    await expect(ficha.getByLabel('Total')).toHaveValue('$95.50')
    await expect(ficha.getByLabel('Labor')).toBeDisabled() // con tareas, los costos son la suma
    await expect(tasks.getByText('2 sin completar')).toBeVisible()
    await shot(page, isMobile, 'fd3-tareas', 1000, tasks)

    // cerrar con tareas sin completar: el 422 del servidor, con su mensaje, dentro del diálogo
    await ficha.getByRole('button', { name: 'Cerrar orden' }).click()
    dialog = page.getByRole('dialog', { name: `Cerrar la orden ${number}` })
    await dialog.getByLabel(/^Odómetro al cerrar/).fill('1200')
    await dialog.getByRole('button', { name: 'Cerrar orden' }).click()
    await expect(dialog.getByText('La orden tiene 2 tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla.')).toBeVisible()
    await shot(page, isMobile, 'fd3-cerrar-422', 800, dialog)
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(dialog).toBeHidden()

    // quitar una tarea y completar la otra
    await rowOf(tasks, 'Rotación de gomas').getByRole('button', { name: 'Quitar' }).click()
    dialog = page.getByRole('dialog', { name: 'Quitar tarea' })
    await dialog.getByRole('button', { name: 'Quitar' }).click()
    await expect(toast(page, 'Tarea quitada.')).toBeVisible()
    await expect(rowOf(tasks, 'Rotación de gomas')).toHaveCount(0)
    await expect(ficha.getByLabel('Total')).toHaveValue('$60.00')
    await rowOf(tasks, 'Cambio de aceite').getByRole('checkbox', { name: 'Completada: Cambio de aceite' }).check()
    await expect(tasks.getByText(/sin completar/)).toHaveCount(0)

    // Iniciar: el vehículo pasa a «En mantenimiento» y la disponibilidad lo refleja
    await ficha.getByRole('button', { name: 'Iniciar' }).click()
    dialog = page.getByRole('dialog', { name: `Iniciar la orden ${number}` })
    await shot(page, isMobile, 'fd3-iniciar', 600, dialog)
    await dialog.getByRole('button', { name: 'Iniciar' }).click()
    await expect(toast(page, 'Orden iniciada.')).toBeVisible()
    await expect(ficha.getByText('En proceso')).toBeVisible()
    await expect(ficha.getByRole('button', { name: 'Iniciar' })).toHaveCount(0)
    expect(await vehicleStatus(vehicleA.publicId ?? '')).toBe('MAINTENANCE')
    await shot(page, isMobile, 'fd3-ficha', 1400, page.locator('.flt-wo'))
    if (isMobile) await expectNoHorizontalScroll(page)

    await page.getByRole('tab', { name: 'Disponibilidad' }).click()
    await panel(page, 'Disponibilidad para despacho').getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(panel(page, 'Vehículos'), plateA)).toContainText('No disponible')
    await expect(rowOf(panel(page, 'Vehículos'), plateA)).toContainText(`Tiene la orden de trabajo ${number} en proceso.`)
    await shot(page, isMobile, 'fd3-disponibilidad-orden', 900, panel(page, 'Vehículos'))
    await page.getByRole('tab', { name: 'Vehículos' }).click()
    await page.getByRole('list', { name: 'Vehículos' }).waitFor()
    await panel(page, 'Vehículos').getByRole('searchbox').fill(plateA.toLowerCase())
    await expect(page.getByRole('list', { name: 'Vehículos' }).getByRole('button', { name: new RegExp(plateA) })).toContainText('En mantenimiento')

    // ---- Cerrar con odómetro: el vehículo vuelve a «Disponible», sube su odómetro y avanza el programa ----
    await page.getByRole('tab', { name: 'Órdenes de trabajo' }).click()
    await panel(page, 'Órdenes de trabajo').getByRole('searchbox').fill(plateA.toLowerCase())
    await rowOf(panel(page, 'Órdenes de trabajo'), number).click()
    await panel(page, `Orden ${number}`).getByRole('button', { name: 'Cerrar orden' }).click()
    dialog = page.getByRole('dialog', { name: `Cerrar la orden ${number}` })
    await expect(dialog.getByText('Obligatorio: el programa de esta orden es por kilometraje.')).toBeVisible()
    await dialog.getByRole('button', { name: 'Cerrar orden' }).click()
    await expect(dialog.getByText('Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.')).toBeVisible()
    await dialog.getByLabel('Fecha de cierre').fill(tenantDay(1))
    await dialog.getByLabel(/^Odómetro al cerrar/).fill('1200')
    await dialog.getByRole('button', { name: 'Cerrar orden' }).click()
    await expect(dialog.getByText('La fecha de cierre no puede ser futura.')).toBeVisible()
    await dialog.getByLabel('Fecha de cierre').fill(tenantDay())
    await dialog.getByLabel('Comentario (opcional)').fill('Servicio completo')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd3-cerrar', 900, dialog)
    await dialog.getByRole('button', { name: 'Cerrar orden' }).click()
    await expect(toast(page, 'Orden cerrada.')).toBeVisible()
    const closed = panel(page, `Orden ${number}`)
    await expect(closed.getByText('Esta orden está en estatus «Cerrada» y solo se consulta.')).toBeVisible()
    await expect(closed.getByLabel('Proveedor')).toBeDisabled()
    for (const name of ['Iniciar', 'Cerrar orden', 'Cancelar orden', 'Guardar cambios']) await expect(closed.getByRole('button', { name })).toHaveCount(0)
    await expect(panel(page, 'Tareas').getByRole('button', { name: /Agregar tarea/ })).toHaveCount(0)
    await expect(panel(page, 'Historial de estatus').getByRole('list', { name: 'Historial de estatus' }).locator('li').first()).toContainText('Cerrada')
    expect(await vehicleStatus(vehicleA.publicId ?? '')).toBe('ACTIVE')
    expect(await vehicleOdometer(vehicleA.publicId ?? '')).toBe(1200)
    // el servidor también niega editar una orden cerrada (EDIT_WORK_ORDER)
    const orderA = await ok<WorkOrderListItemDto[]>(request.get(`${API_URL}/api/v1/maintenance-work-orders?vehiclePublicId=${vehicleA.publicId}`, { headers }), 'órdenes del vehículo A')
    const closedEdit = await request.patch(`${API_URL}/api/v1/maintenance-work-orders/${orderA[0].publicId}`, { headers, data: { vendor: 'NO-DEBE' } })
    expect(closedEdit.status()).toBe(422)
    expect(((await closedEdit.json()) as { title: string }).title).toBe("El estatus actual no permite la acción 'EDIT_WORK_ORDER'.")
    await shot(page, isMobile, 'fd3-orden-cerrada', 1400, page.locator('.flt-wo'))
    await panel(page, `Orden ${number}`).getByRole('button', { name: 'Cerrar ficha' }).click()
    await expect(panel(page, `Orden ${number}`)).toHaveCount(0)

    // el programa avanzó: último servicio en 1200 → próximo 1700
    await page.getByRole('tab', { name: 'Mantenimiento' }).click()
    await due.getByRole('searchbox').fill(`aceite ${suffix.toLowerCase()}`)
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('1,700 km')
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Al día')
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Faltan 500 km')

    // ---- Cancelar una orden con costos capturados en el encabezado (sin tareas) ----
    await page.getByRole('tab', { name: 'Órdenes de trabajo' }).click()
    await orders.getByRole('button', { name: /Nueva orden de trabajo/ }).click()
    dialog = page.getByRole('dialog', { name: 'Nueva orden de trabajo' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El vehículo de la orden de trabajo es obligatorio.')).toBeVisible()
    await expect(dialog.getByText('El tipo de mantenimiento es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Vehículo/).selectOption({ label: `F3B${suffix} · ${plateB}` })
    await dialog.getByLabel(/^Tipo de mantenimiento/).selectOption({ label: 'Correctivo' })
    await dialog.getByLabel('Proveedor').fill('AutoZone PR')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    const created2 = toast(page, /Orden OT-\d+ creada\./)
    await expect(created2).toBeVisible()
    const number2 = /OT-\d+/.exec((await created2.textContent()) ?? '')?.[0] ?? ''
    const ficha2 = panel(page, `Orden ${number2}`)
    await expect(ficha2).toBeVisible() // la orden nueva se abre sola
    await ficha2.getByLabel('Labor').fill('100')
    await ficha2.getByLabel('Partes').fill('50')
    await ficha2.getByLabel('Odómetro (km)').fill('900')
    await ficha2.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    await expect(ficha2.getByLabel('Total')).toHaveValue('$150.00')
    // con tareas, el servidor rechaza costos en el encabezado
    await ok(request.post(`${API_URL}/api/v1/maintenance-work-orders/${await publicIdOf(request, headers, number2)}/tasks`, { headers, data: { description: 'Diagnóstico', laborCost: 10 } }), 'tarea por API')
    const costsWithTasks = await request.patch(`${API_URL}/api/v1/maintenance-work-orders/${await publicIdOf(request, headers, number2)}`, { headers, data: { laborCost: 5 } })
    expect(costsWithTasks.status()).toBe(400)
    expect(((await costsWithTasks.json()) as { title: string }).title).toBe('La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas.')
    await page.reload()
    await expect(ficha2).toBeVisible()
    await expect(ficha2.getByLabel('Labor')).toBeDisabled()
    await ficha2.getByRole('button', { name: 'Cancelar orden' }).click()
    dialog = page.getByRole('dialog', { name: `Cancelar la orden ${number2}` })
    await dialog.getByLabel('Comentario (opcional)').fill('Ya no hace falta')
    await shot(page, isMobile, 'fd3-cancelar', 700, dialog)
    await dialog.getByRole('button', { name: 'Cancelar orden' }).click()
    await expect(toast(page, 'Orden cancelada.')).toBeVisible()
    await expect(ficha2.getByText('Esta orden está en estatus «Cancelada» y solo se consulta.')).toBeVisible()
    expect(await vehicleStatus(vehicleB.publicId ?? '')).toBe('ACTIVE')

    // lista: filtros Estado y Vehículo, y orden sobre todo lo filtrado
    await ficha2.getByRole('button', { name: 'Cerrar ficha' }).click()
    await orders.getByRole('searchbox').fill(suffix.toLowerCase())
    await expect(rowOf(orders, number)).toBeVisible()
    await expect(rowOf(orders, number2)).toContainText('Cancelada')
    // al agregar la primera tarea el servidor reemplaza los costos del encabezado (100 + 50) por la suma de las tareas (10)
    await expect(rowOf(orders, number2)).toContainText('$10.00')
    await orders.getByLabel('Vehículo', { exact: true }).selectOption({ label: `F3A${suffix} · ${plateA}` })
    await expect(rowOf(orders, number)).toBeVisible()
    await expect(rowOf(orders, number2)).toHaveCount(0)
    await orders.getByRole('button', { name: 'Limpiar' }).click()
    await orders.getByRole('searchbox').fill(suffix.toLowerCase())
    await shot(page, isMobile, 'fd3-ordenes', 1100, orders)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 4. Combustible ----
    await page.getByRole('tab', { name: 'Combustible' }).click()
    const fuel = panel(page, 'Bitácora de combustible')
    await fuel.getByLabel('Vehículo', { exact: true }).selectOption({ label: `F3A${suffix} · ${plateA}` })
    await expect(fuel.getByText('Sin resultados')).toBeVisible()
    await fuel.getByRole('button', { name: /Registrar carga/ }).click()
    dialog = page.getByRole('dialog', { name: 'Registrar carga' })
    await expect(dialog.getByLabel(/^Vehículo/)).toHaveValue(vehicleA.publicId ?? '') // el vehículo filtrado
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('Los litros deben ser mayores que 0.')).toBeVisible()
    await expect(dialog.getByText('Indique el costo total de la carga.')).toBeVisible()
    await dialog.getByLabel(/^Fecha/).fill(tenantDay(2))
    await dialog.getByLabel(/^Litros/).fill('40')
    await dialog.getByLabel(/^Costo total/).fill('60')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('La fecha de la carga no puede ser futura.')).toBeVisible()
    await dialog.getByLabel(/^Fecha/).fill(tenantDay(-2))
    await dialog.getByLabel(/^Hora/).fill('10:00')
    await dialog.getByLabel(/^Odómetro/).fill('1300')
    await dialog.getByLabel('Chofer').selectOption({ label: `F3D${suffix} · ${driverName}` })
    await dialog.getByLabel('Estación').fill('Puma Bayamón')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fd3-carga-alta', 1100, dialog)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Carga registrada.')).toBeVisible()
    await expect(rowOf(fuel, 'Puma Bayamón')).toContainText('40')
    await expect(rowOf(fuel, 'Puma Bayamón')).toContainText(driverName)

    // segunda carga (más reciente): 400 km con 50 L = 8.00 km/L y $80 / 400 km = $0.20/km
    const addFill = async (date: string, time: string, liters: string, cost: string, odometer: string, station: string) => {
      await fuel.getByRole('button', { name: /Registrar carga/ }).click()
      const d = page.getByRole('dialog', { name: 'Registrar carga' })
      await d.getByLabel(/^Fecha/).fill(date)
      await d.getByLabel(/^Hora/).fill(time)
      await d.getByLabel(/^Litros/).fill(liters)
      await d.getByLabel(/^Costo total/).fill(cost)
      await d.getByLabel(/^Odómetro/).fill(odometer)
      await d.getByLabel('Estación').fill(station)
      return d
    }
    dialog = await addFill(tenantDay(-1), '10:00', '50', '80', '1700', 'Esso Caguas')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Carga registrada.')).toBeVisible()
    const second = rowOf(fuel, 'Esso Caguas')
    await expect(second).toContainText('400')
    await expect(second).toContainText('8.00')
    await expect(second).toContainText('$0.20')

    // odómetro menor que el de una carga anterior: lo rechaza el servidor, con su mensaje bajo el campo
    dialog = await addFill(tenantDay(-1), '20:00', '25', '40', '1500', 'Gulf Ponce')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText(`La lectura de odómetro (1,500 km) es menor que la de una carga anterior del mismo vehículo (1,700 km el ${tenantDay(-1)}).`)).toBeVisible()
    await shot(page, isMobile, 'fd3-odometro-menor', 1100, dialog)
    await dialog.getByLabel(/^Odómetro/).fill('1750')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Carga registrada.')).toBeVisible()
    await expect(fuel.getByText(/de 3$|^3$/).first()).toBeVisible()
    const third = rowOf(fuel, 'Gulf Ponce')
    await expect(third).toContainText('50') // 1750 - 1700
    await expect(third).toContainText('2.00') // 50 km / 25 L
    await expect(third).toContainText('$0.80') // $40 / 50 km
    expect(await vehicleOdometer(vehicleA.publicId ?? '')).toBe(1750)

    // resumen por vehículo de todo el filtro
    const summary = panel(page, 'Resumen por vehículo')
    await expect(rowOf(summary, plateA)).toContainText('3')
    await expect(rowOf(summary, plateA)).toContainText('115')
    await expect(rowOf(summary, plateA)).toContainText('$180.00')
    await shot(page, isMobile, 'fd3-resumen', 700, summary)

    // editar: el vehículo no se cambia y solo viaja lo cambiado
    await rowOf(fuel, 'Gulf Ponce').getByRole('button', { name: 'Editar' }).click()
    dialog = page.getByRole('dialog', { name: 'Editar carga' })
    await expect(dialog.getByLabel(/^Vehículo/)).toBeDisabled()
    await expect(dialog.getByText('El vehículo de una carga no se cambia; quítela y registre otra.')).toBeVisible()
    await dialog.getByLabel(/^Litros/).fill('30')
    await dialog.getByLabel('Estación').fill('Shell')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Carga guardada.')).toBeVisible()
    await expect(rowOf(fuel, 'Shell')).toContainText('1.67') // 50 km / 30 L
    await shot(page, isMobile, 'fd3-combustible', 1300)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ordenar lee todo el filtro: la de menos litros primero, la de más litros al revés
    if (!isMobile) {
      await fuel.getByRole('button', { name: 'Litros' }).click()
      await expect(fuel.locator('tbody tr').first()).toContainText('Shell')
      await fuel.getByRole('button', { name: 'Litros' }).click()
      await expect(fuel.locator('tbody tr').first()).toContainText('Esso Caguas')
    }

    // quitar: pide confirmación y el odómetro del vehículo no baja
    await rowOf(fuel, 'Shell').getByRole('button', { name: 'Quitar' }).click()
    dialog = page.getByRole('dialog', { name: 'Quitar carga' })
    await shot(page, isMobile, 'fd3-carga-quitar', 600, dialog)
    await dialog.getByRole('button', { name: 'Quitar' }).click()
    await expect(toast(page, 'Carga quitada.')).toBeVisible()
    await expect(rowOf(fuel, 'Shell')).toHaveCount(0)
    await expect(rowOf(summary, plateA)).toContainText('2')
    expect(await vehicleOdometer(vehicleA.publicId ?? '')).toBe(1750)

    // filtros: chofer y fechas
    await fuel.getByLabel('Chofer', { exact: true }).selectOption({ label: `F3D${suffix} · ${driverName}` })
    await expect(rowOf(fuel, 'Puma Bayamón')).toHaveCount(1)
    await expect(fuel.locator('tbody tr, li.dt-card')).toHaveCount(1)
    await fuel.getByRole('button', { name: 'Limpiar' }).click()
    await fuel.getByLabel('Vehículo', { exact: true }).selectOption({ label: `F3A${suffix} · ${plateA}` })
    await fuel.getByLabel('Desde').fill(tenantDay(-1))
    await fuel.getByLabel('Hasta').fill(tenantDay(-1))
    await expect(fuel.locator('tbody tr, li.dt-card')).toHaveCount(1) // solo la del día -1 (la del -2 queda fuera, la del día -1 entra completa)

    // la lectura del combustible adelantó el vencimiento: el odómetro (1750) pasó el próximo servicio (1700)
    await page.getByRole('tab', { name: 'Mantenimiento' }).click()
    await due.getByRole('searchbox').fill(`aceite ${suffix.toLowerCase()}`)
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Vencido')
    await expect(rowOf(due, `Aceite ${suffix}`)).toContainText('Pasado por 50 km')
    if (isMobile) await expectNoHorizontalScroll(page)

    // limpieza: el programa por tipo alcanza a todas las Van de la compañía; se desactiva para no ensuciar otros recorridos
    const all = await ok<{ id: number; name: string }[]>(request.get(`${API_URL}/api/v1/maintenance-schedules`, { headers }), 'programas')
    for (const sc of all.filter((x) => x.name === `Revisión ${suffix}`)) await request.post(`${API_URL}/api/v1/maintenance-schedules/${sc.id}/deactivate`, { headers })
  })
})

/** publicId de una orden por su número (la lista de órdenes del API no se filtra por número). */
async function publicIdOf(request: APIRequestContext, headers: Record<string, string>, number: string): Promise<string> {
  const list = await ok<WorkOrderListItemDto[]>(request.get(`${API_URL}/api/v1/maintenance-work-orders?take=200`, { headers }), 'lista de órdenes')
  const found = list.find((o) => o.number === number)
  expect(found, `orden ${number}`).toBeTruthy()
  return found?.publicId ?? ''
}
