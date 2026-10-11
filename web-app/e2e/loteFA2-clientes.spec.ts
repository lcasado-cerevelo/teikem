// Recorridos de Clientes y contratos (F-A1 pendiente + F-A2) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo, admin con todos los permisos, módulo CATALOG encendido).
//
// 1) F-A1 «Clientes: lista, alta y ficha»: alta por la pantalla (con contrato inicial) → perfil (razón social) → teléfono →
//    persona de contacto → numeración con «Ejemplo» en vivo → baja y reactivación → el buscador de la lista.
// 2) F-A2 «Contratos del cliente»: siembra por API un cliente con su contrato inicial; en la ficha, pestaña Contrato (datos
//    generales, los 5 checks, despacho y COD, transición de estatus), SLA (borrador y PUT), Tarifas (por servicio: alta, nueva
//    versión, quitar; pieza extra: componente y tramos), Servicios especiales (tipo nuevo, tarifa, versión nueva, quitar) y
//    «Nuevo contrato». Con mensajes del servidor tal cual (409 de tarifa repetida).
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fa1-<paso>.png y fa2-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type ContractDetailDto = components['schemas']['ContractDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(150_000)

/** "Hoy" de la compañía demo (America/Puerto_Rico) y días después, 'YYYY-MM-DD'. */
function tenantDay(plus = 0): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Puerto_Rico', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  const d = new Date(`${today}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + plus)
  return d.toISOString().slice(0, 10)
}

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Panel de la ficha por su título (h2). */
const panel = (page: Page, name: string): Locator => page.locator('section.panel', { has: page.getByRole('heading', { name, exact: true }) })

/** Aviso de éxito (toast) con ese texto. */
const toast = (page: Page, text: string): Locator => page.locator('.toast.on', { hasText: text })

test.describe('Clientes y contratos (F-A1 + F-A2)', () => {
  test('F-A1: alta, perfil, teléfono, persona de contacto, numeración, baja y reactivación', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const name = `Cliente FA1 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    await login(page)
    await page.goto('/catalog/clients')
    await expect(page.getByRole('heading', { name: 'Clientes y contratos' })).toBeVisible()
    await expect(page.getByRole('list', { name: 'Clientes' })).toBeVisible()
    await shot(page, isMobile, 'fa1-lista')

    // ---- Alta con contrato inicial por omisión ----
    await page.getByRole('button', { name: 'Nuevo cliente' }).click()
    const dialog = page.getByRole('dialog', { name: 'Nuevo cliente' })
    await expect(dialog.getByRole('switch', { name: /Crear contrato inicial/ })).toBeChecked()
    // sin nombre el servidor no se llama: la validación sale en pantalla
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El nombre es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Nombre/).fill(name)
    await shot(page, isMobile, 'fa1-alta')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog).toBeHidden()
    const detail = panel(page, name)
    await expect(detail).toBeVisible()
    await expect(page).toHaveURL(/client=/)
    const found = await ok<{ publicId: string; name: string }[]>(request.get(`${API_URL}/api/v1/clients?search=${encodeURIComponent(name)}`, { headers }), 'buscar')
    expect(found).toHaveLength(1)
    const clientId = found[0].publicId

    // ---- Perfil ----
    const profile = panel(page, 'Perfil del cliente')
    await profile.getByLabel('Razón social').fill(`Razón ${suffix} LLC`)
    await profile.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    const saved = await ok<ClientDetailDto>(request.get(`${API_URL}/api/v1/clients/${clientId}`, { headers }), 'ficha')
    expect(saved.legalName).toBe(`Razón ${suffix} LLC`)

    // ---- Teléfono ----
    const points = panel(page, 'Teléfonos y correos')
    await points.getByRole('button', { name: 'Agregar teléfono' }).click()
    const phoneDialog = page.getByRole('dialog', { name: 'Agregar teléfono' })
    await phoneDialog.getByLabel(/^Teléfono/).fill('7875551234')
    await phoneDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(points.getByText('(787) 555-1234')).toBeVisible()

    // ---- Persona de contacto ----
    const people = panel(page, 'Personas de contacto')
    await people.getByRole('button', { name: 'Agregar contacto' }).click()
    const personDialog = page.getByRole('dialog', { name: 'Agregar contacto' })
    await personDialog.getByLabel(/^Nombre/).fill('Ana Pérez')
    await personDialog.getByLabel('Puesto').fill('Compras')
    await personDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(people.getByText('Ana Pérez')).toBeVisible()
    await expect(people.getByText('Compras')).toBeVisible()

    // ---- Numeración con ejemplo en vivo ----
    const numbering = panel(page, 'Numeración')
    await numbering.getByLabel('Número de orden', { exact: true }).fill('AX-#####')
    await expect(numbering.getByText('AX-00001', { exact: true })).toBeVisible()
    await numbering.getByRole('button', { name: 'Guardar' }).click()
    await expect(toast(page, 'Cambios guardados.')).toBeVisible()
    const numbered = await ok<ClientDetailDto>(request.get(`${API_URL}/api/v1/clients/${clientId}`, { headers }), 'ficha')
    expect(numbered.numberSettings?.orderNumberFormat).toBe('AX-#####')
    await shot(page, isMobile, 'fa1-ficha')

    // ---- Contratos: el inicial ya está (el detalle va en el recorrido de F-A2) ----
    await expect(panel(page, 'Contratos').getByRole('tab', { name: 'Contrato', exact: true })).toBeVisible()

    // ---- Baja y reactivación ----
    await detail.getByRole('button', { name: /Dar de baja/ }).click()
    await page.getByRole('dialog', { name: 'Dar de baja el cliente' }).getByRole('button', { name: 'Dar de baja' }).click()
    await expect(detail.getByText('Este cliente está dado de baja')).toBeVisible()
    await detail.getByRole('button', { name: /Reactivar/ }).click()
    await page.getByRole('dialog', { name: 'Reactivar el cliente' }).getByRole('button', { name: 'Reactivar' }).click()
    await expect(detail.getByText('Este cliente está dado de baja')).toBeHidden()

    // ---- Buscador de la lista ----
    await page.getByRole('searchbox').fill(suffix)
    await expect(page.getByRole('list', { name: 'Clientes' }).getByText(name)).toBeVisible()
    await expectNoHorizontalScroll(page)
  })

  test('F-A2: contrato (datos, facturación, estatus), SLA, tarifas, servicios especiales y nuevo contrato', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const name = `Cliente FA2 ${suffix}`
    // las tablas internas pasan a tarjetas (li.dt-card) cuando el panel es angosto
    const ROW = isMobile ? 'li.dt-card' : 'tbody tr'
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name, contract: { startDate: tenantDay(-30), title: 'Contrato marco' } } }),
      'cliente',
    )
    const clientId = client.publicId!
    const contractId = client.currentContract!.publicId!
    const getContract = () => ok<ContractDetailDto>(request.get(`${API_URL}/api/v1/contracts/${contractId}`, { headers }), 'contrato')

    await login(page)
    await page.goto(`/catalog/clients?client=${clientId}`)
    const section = panel(page, 'Contratos')
    await expect(section).toBeVisible()
    await expect(section.getByRole('tab')).toHaveText(['Contrato', 'Tarifas', 'SLA', 'Servicios especiales'])
    await expect(section.getByLabel('Contrato', { exact: true })).toHaveValue(contractId)

    // ================= Pestaña Contrato =================
    const title = section.getByLabel(/^Título/)
    await expect(title).toHaveValue('Contrato marco')
    await expect(section.getByLabel('Número')).toHaveValue(client.currentContract!.contractNumber!)
    await title.fill(`Marco ${suffix}`)
    await section.getByLabel('Fecha fin').fill(tenantDay(300))
    await section.getByLabel('Notas').fill('Contrato de prueba del recorrido')
    // fin anterior al inicio: lo rechaza la pantalla y luego el servidor tal cual
    const general = section.locator('form').filter({ has: page.getByLabel(/^Título/) })
    await section.getByLabel('Fecha fin').fill(tenantDay(-60))
    await general.getByRole('button', { name: 'Guardar' }).click()
    await expect(general.getByText('La fecha fin no puede ser anterior a la fecha de inicio.')).toBeVisible()
    await section.getByLabel('Fecha fin').fill(tenantDay(300))
    await general.getByRole('button', { name: 'Guardar' }).click()
    // (los avisos duran unos segundos y pueden ser el de la acción anterior: se espera el estado en el servidor)
    await expect.poll(async () => (await getContract()).title).toBe(`Marco ${suffix}`)
    let contract = await getContract()
    expect(contract.endDate).toBe(tenantDay(300))
    expect(contract.notes).toBe('Contrato de prueba del recorrido')
    // quitar la fecha fin (clearEndDate)
    await section.getByLabel('Fecha fin').fill('')
    await general.getByRole('button', { name: 'Guardar' }).click()
    await expect.poll(async () => (await getContract()).endDate ?? null).toBeNull()

    // modelo de facturación: los 5 checks, despacho y COD
    const billing = section.locator('form').filter({ has: page.getByRole('switch', { name: 'Cargo por despacho' }) })
    for (const check of ['Pieza extra con precio especial', 'Cargo por despacho', 'Cargo por COD', 'Servicios especiales']) await billing.getByRole('switch', { name: check }).check()
    await billing.getByLabel(/Monto fijo por despacho/).fill('3.5')
    await billing.getByLabel('Tipo de cargo').selectOption('PERCENT')
    await billing.getByLabel('Valor (%)').fill('150')
    await billing.getByRole('button', { name: 'Guardar' }).click()
    await expect(billing.getByText('El por ciento del cargo por COD debe estar entre 0 y 100.')).toBeVisible()
    await billing.getByLabel('Valor (%)').fill('2.5')
    await billing.getByRole('button', { name: 'Guardar' }).click()
    await expect.poll(async () => (await getContract()).codFee?.value).toBe(2.5)
    contract = await getContract()
    expect(contract.billingModel).toMatchObject({ billPerService: true, billExtraPiece: true, billDispatchFee: true, billCodFee: true, billSpecialServices: true })
    expect(contract.dispatchFee).toBe(3.5)
    expect(contract.codFee).toMatchObject({ type: 'PERCENT', value: 2.5 })
    await expect(section.getByText('Facturación:').locator('xpath=..')).toContainText('COD')

    // estatus: DRAFT → ACTIVE por el pipeline (POST …/status) y queda en el historial
    await section.getByRole('button', { name: /Avanzar a/ }).first().click()
    await page.getByRole('dialog').getByRole('button', { name: 'Cambiar estatus' }).click()
    await expect.poll(async () => (await getContract()).status).toBe('ACTIVE')
    await shot(page, isMobile, 'fa2-contrato')
    await expectNoHorizontalScroll(page)

    // ================= Pestaña SLA =================
    await section.getByRole('tab', { name: 'SLA' }).click()
    const standard = section.getByRole('group', { name: /Est[aá]ndar/i }).first()
    await expect(standard.getByLabel('Horas máximas de tránsito')).toHaveValue('')
    await standard.getByLabel('Horas máximas de tránsito').fill('0')
    await section.getByRole('button', { name: 'Guardar SLA' }).click()
    await expect(standard.getByText('Las horas máximas de tránsito deben ser mayores que cero.')).toBeVisible()
    await standard.getByLabel('Horas máximas de tránsito').fill('48')
    await standard.getByLabel('Ventana de recogido (min)').fill('30')
    await standard.getByLabel('Meta de puntualidad (%)').fill('95')
    await standard.getByLabel('Penalidad ($)').fill('10')
    await shot(page, isMobile, 'fa2-sla')
    await expectNoHorizontalScroll(page)
    await section.getByRole('button', { name: 'Guardar SLA' }).click()
    await expect(toast(page, 'SLA guardado.')).toBeVisible()
    contract = await getContract()
    expect(contract.serviceLevels).toHaveLength(1)
    expect(contract.serviceLevels![0]).toMatchObject({ maxTransitHours: 48, pickupWindowMin: 30, onTimeTargetPct: 95, penaltyAmount: 10 })

    // ================= Pestaña Tarifas =================
    await section.getByRole('tab', { name: 'Tarifas' }).click()
    const perService = section.getByRole('region', { name: 'Tarifas por servicio' })
    await expect(perService.getByText('Sin tarifas por servicio configuradas.')).toBeVisible()
    await perService.getByRole('button', { name: 'Agregar tarifa' }).click()
    const rateDialog = page.getByRole('dialog', { name: 'Agregar tarifa por servicio' })
    await expect(rateDialog.getByLabel(/Vigente desde/)).toHaveValue(/^\d{4}-\d{2}-\d{2}$/)
    await rateDialog.getByLabel(/^Servicio/).selectOption('STANDARD')
    await rateDialog.getByLabel(/^Paquete/).selectOption('BOX')
    await rateDialog.getByLabel(/^Tarifa/).fill('6.5')
    await rateDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(perService.locator(ROW).filter({ hasText: /\$6\.50/ })).toBeVisible()
    // el mismo par otra vez: el 409 del servidor, tal cual
    await perService.getByRole('button', { name: 'Agregar tarifa' }).click()
    const dupDialog = page.getByRole('dialog', { name: 'Agregar tarifa por servicio' })
    await dupDialog.getByLabel(/^Servicio/).selectOption('STANDARD')
    await dupDialog.getByLabel(/^Paquete/).selectOption('BOX')
    await dupDialog.getByLabel(/^Tarifa/).fill('7')
    await dupDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dupDialog.getByText(/Ya existe una tarifa vigente en esa fecha para ese servicio y tipo de paquete/)).toBeVisible()
    await dupDialog.getByRole('button', { name: 'Cancelar' }).click()
    // nueva versión (la tarifa vigente se cierra y se abre otra) y ver historial
    await perService.locator(ROW).filter({ hasText: /\$6\.50/ }).getByRole('button', { name: 'Editar' }).click()
    const editDialog = page.getByRole('dialog', { name: 'Nueva versión de la tarifa' })
    await editDialog.getByLabel(/^Tarifa/).fill('7')
    await editDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(perService.locator(ROW).filter({ hasText: /\$7\.00/ })).toBeVisible()
    await section.getByRole('switch', { name: 'Ver historial' }).check()
    await expect(perService.locator(ROW).filter({ hasText: /\$6\.50/ })).toBeVisible()
    await section.getByRole('switch', { name: 'Ver historial' }).uncheck()

    // pieza extra: componente y tramos
    const extra = section.getByRole('region', { name: 'Tarifas por pieza extra (por tramo)' })
    await extra.getByRole('button', { name: 'Agregar pieza extra' }).click()
    const compDialog = page.getByRole('dialog', { name: 'Agregar pieza extra' })
    await compDialog.getByLabel(/^Servicio/).selectOption('STANDARD')
    await compDialog.getByLabel(/^Paquete/).selectOption('BOX')
    await compDialog.getByRole('button', { name: 'Guardar' }).click()
    const component = extra.getByRole('group').first()
    await component.getByRole('button', { name: 'Agregar tramo' }).click()
    const tierDialog = page.getByRole('dialog', { name: 'Agregar tramo' })
    await tierDialog.getByLabel(/^Desde pieza/).fill('1')
    await tierDialog.getByLabel(/^Tarifa/).fill('1')
    await tierDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(tierDialog.getByText(/Rango inválido: 'desde' debe ser al menos 2/)).toBeVisible()
    await tierDialog.getByLabel(/^Desde pieza/).fill('2')
    await tierDialog.getByLabel(/^Hasta pieza/).fill('5')
    await tierDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(component.locator(ROW).filter({ hasText: /2–5/ })).toBeVisible()
    await component.getByRole('button', { name: 'Agregar tramo' }).click()
    const overlap = page.getByRole('dialog', { name: 'Agregar tramo' })
    await overlap.getByLabel(/^Desde pieza/).fill('4')
    await overlap.getByLabel(/^Hasta pieza/).fill('7')
    await overlap.getByLabel(/^Tarifa/).fill('1')
    await overlap.getByRole('button', { name: 'Guardar' }).click()
    await expect(overlap.getByText(/se traslapa con el tramo vigente 2–5/)).toBeVisible()
    await overlap.getByLabel(/^Desde pieza/).fill('6')
    await overlap.getByLabel(/^Hasta pieza/).fill('')
    await overlap.getByLabel(/^Tarifa/).fill('0.75')
    await overlap.getByRole('button', { name: 'Guardar' }).click()
    await expect(component.locator(ROW).filter({ hasText: /6\+/ })).toBeVisible()
    await shot(page, isMobile, 'fa2-tarifas')
    await expectNoHorizontalScroll(page)
    // quitar la tarifa por servicio = cerrarla (queda en el historial)
    await perService.locator(ROW).filter({ hasText: /\$7\.00/ }).getByRole('button', { name: 'Quitar' }).click()
    await page.getByRole('dialog', { name: 'Quitar la tarifa' }).getByRole('button', { name: 'Quitar' }).click()
    await expect(perService.getByText('Sin tarifas por servicio configuradas.')).toBeVisible()
    const rates = await ok<{ perService: { rate: number; effectiveTo: string | null }[]; extraPiece: { tiers: unknown[] }[] }>(
      request.get(`${API_URL}/api/v1/contracts/${contractId}/rate-components?includeHistory=true`, { headers }),
      'tarifas',
    )
    expect(rates.perService.length).toBeGreaterThanOrEqual(2)
    expect(rates.perService.every((r) => r.effectiveTo !== null)).toBe(true)
    expect(rates.extraPiece[0].tiers).toHaveLength(2)

    // ================= Pestaña Servicios especiales =================
    await section.getByRole('tab', { name: 'Servicios especiales' }).click()
    await expect(section.getByText('Este cliente no tiene servicios especiales todavía.')).toBeVisible()
    await section.getByRole('button', { name: 'Agregar servicio especial' }).click()
    const specialDialog = page.getByRole('dialog', { name: 'Agregar servicio especial' })
    await specialDialog.getByLabel(/^Servicio/).selectOption('__new__')
    const typeName = `Grúa ${suffix}`
    await specialDialog.getByLabel('Nombre del nuevo servicio especial').fill(typeName)
    await specialDialog.getByLabel(/^Tarifa/).fill('150')
    await specialDialog.getByRole('button', { name: 'Guardar' }).click()
    const specialRow = section.locator(ROW).filter({ hasText: new RegExp(typeName) })
    await expect(specialRow).toContainText('$150.00')
    // nueva versión
    await specialRow.getByRole('button', { name: 'Editar' }).click()
    const specialEdit = page.getByRole('dialog', { name: 'Nueva versión de la tarifa' })
    await specialEdit.getByLabel(/^Tarifa/).fill('160')
    await specialEdit.getByRole('button', { name: 'Guardar' }).click()
    await expect(section.locator(ROW).filter({ hasText: new RegExp(typeName) })).toContainText('$160.00')
    // el mismo tipo otra vez: el 409 del servidor, tal cual
    await section.getByRole('button', { name: 'Agregar servicio especial' }).click()
    const specialDup = page.getByRole('dialog', { name: 'Agregar servicio especial' })
    await specialDup.getByLabel(/^Servicio/).selectOption({ label: typeName })
    await specialDup.getByLabel(/^Tarifa/).fill('10')
    await specialDup.getByRole('button', { name: 'Guardar' }).click()
    await expect(specialDup.getByText(/ya tiene una tarifa vigente en esa fecha para el tipo/)).toBeVisible()
    await specialDup.getByRole('button', { name: 'Cancelar' }).click()
    await shot(page, isMobile, 'fa2-especiales')
    await expectNoHorizontalScroll(page)
    // quitar = cerrar
    await section.locator(ROW).filter({ hasText: new RegExp(typeName) }).getByRole('button', { name: 'Quitar' }).click()
    await page.getByRole('dialog', { name: 'Quitar el servicio especial' }).getByRole('button', { name: 'Quitar' }).click()
    await expect(section.getByText('Este cliente no tiene servicios especiales todavía.')).toBeVisible()

    // ================= Nuevo contrato =================
    await section.getByRole('button', { name: 'Nuevo contrato' }).click()
    const newContract = page.getByRole('dialog', { name: 'Nuevo contrato' })
    await newContract.getByRole('button', { name: 'Guardar' }).click()
    await expect(newContract.getByText('El título del contrato es obligatorio.')).toBeVisible()
    await newContract.getByLabel(/^Título/).fill(`Anexo ${suffix}`)
    await newContract.getByRole('button', { name: 'Guardar' }).click()
    await expect(newContract).toBeHidden()
    // el contrato nuevo queda elegido en el selector, en borrador
    await expect(section.getByLabel(/^Título/)).toHaveValue(`Anexo ${suffix}`)
    const all = await ok<{ title: string; status: string }[]>(request.get(`${API_URL}/api/v1/contracts?clientId=${clientId}`, { headers }), 'contratos')
    expect(all.map((c) => c.title)).toContain(`Anexo ${suffix}`)
    expect(all.find((c) => c.title === `Anexo ${suffix}`)?.status).toBe('DRAFT')
    // activar el segundo con el primero ya ACTIVE: el 422 del servidor, tal cual
    await section.getByRole('button', { name: /Avanzar a/ }).first().click()
    const confirm = page.getByRole('dialog')
    await confirm.getByRole('button', { name: 'Cambiar estatus' }).click()
    await expect(confirm.getByText('El cliente ya tiene un contrato vigente. Cancele o expire el contrato anterior antes de activar este.')).toBeVisible()
    await confirm.getByRole('button', { name: 'Cancelar' }).click()

    await expectNoHorizontalScroll(page)
  })
})
