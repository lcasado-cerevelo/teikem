// Recorridos de F-A3 contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo, admin con todos
// los permisos, módulos CATALOG y CLIENT_PORTAL encendidos). El API corre en Development: la respuesta de invitar y reenviar trae el
// token y el «correo» solo se registra en el log (no sale ningún correo real); el recorrido usa ese token para aceptar la invitación
// por el API anónimo, como lo haría el portal del cliente.
//
// 1) «Usuarios de portal» de la ficha: invitar por la pantalla (el token no se muestra), reenviar (el enlace anterior deja de valer),
//    aceptar, suspender, reactivar, dar de baja, volver a invitar, el 409 de correo no disponible (repetido y de usuario interno) y el
//    409 de cliente dado de baja (otra sesión lo da de baja con la ficha abierta).
// 2) «Cotizador» de la sección de contratos: con tarifa de contrato (el ejemplo del manual: 21.25), con COD, una línea sin tarifa, un
//    cliente sin tarifas, validación en pantalla y el 409 de cliente dado de baja.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos (nombres únicos por corrida).
// Capturas: docs/manual/frontend/img/fa3-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang } from './manualLang'
import { openClientPanels } from './staffHelpers'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type PortalUserDto = components['schemas']['PortalUserDto']
type PortalInviteResultDto = components['schemas']['PortalInviteResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const PORTAL_PASSWORD = 'Fa3-Portal-2026-Segura!'
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
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
async function shot(page: Page, isMobile: boolean, name: string, target?: Locator) {
  if (isMobile) return
  await page.mouse.move(0, 0) // sin el puntero encima de un botón (el estado «hover» tapa su texto)
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  const size = page.viewportSize()
  if (target && size) await page.setViewportSize({ width: size.width, height: 1300 }) // el panel entero a la vista
  await page.waitForTimeout(150)
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await (target ?? page).screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
  if (target && size) await page.setViewportSize(size)
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
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T // las bajas y reactivaciones responden 204 sin cuerpo
}

/** Panel de la ficha por su título (h2). */
const panel = (page: Page, name: string): Locator => page.locator('section.panel', { has: page.getByRole('heading', { name, exact: true }) })

/** Aviso de éxito (toast) con ese texto. */
const toast = (page: Page, text: string | RegExp): Locator => page.locator('.toast.on', { hasText: text })

const auth = (token: string) => ({ Authorization: `Bearer ${token}` })

async function portalUsers(request: APIRequestContext, token: string, clientId: string): Promise<PortalUserDto[]> {
  return ok<PortalUserDto[]>(request.get(`${API_URL}/api/v1/clients/${clientId}/portal-users`, { headers: auth(token) }), 'usuarios de portal')
}

async function apiInvite(request: APIRequestContext, token: string, clientId: string, email: string, role = 'CLIENT_OPERATOR'): Promise<PortalInviteResultDto> {
  return ok<PortalInviteResultDto>(request.post(`${API_URL}/api/v1/clients/${clientId}/portal-users/invite`, { headers: auth(token), data: { email, fullName: 'Persona de prueba', role } }), 'invitar')
}

/** El invitado acepta el enlace (API anónimo, como el portal del cliente). */
async function apiAccept(request: APIRequestContext, email: string, inviteToken: string) {
  const res = await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email, token: inviteToken, password: PORTAL_PASSWORD } })
  expect(res.status(), await res.text()).toBe(204)
}

test.describe('Usuarios de portal y cotizador del cliente (F-A3)', () => {
  test('Usuarios de portal: invitar, reenviar, aceptar, suspender, reactivar, baja, volver a invitar y los 409', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const ROW = isMobile ? 'li.dt-card' : 'tbody tr'
    const token = await apiToken(request)
    const headers = auth(token)
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FA3 portal ${suffix}` } }), 'cliente')
    const clientId = client.publicId!
    const mail = (who: string) => `fa3-${who}-${suffix}@example.com`.toLowerCase()

    await login(page)
    await page.goto(`/catalog/clients?client=${clientId}`)
    await openClientPanels(page)
    const portal = panel(page, 'Usuarios de portal')
    await expect(portal).toBeVisible()
    await expect(portal.getByText('Este cliente todavía no tiene usuarios de portal.')).toBeVisible()
    const row = (email: string) => portal.locator(ROW, { hasText: email })

    // ================= Invitar =================
    await portal.getByRole('button', { name: 'Invitar usuario' }).click()
    let dialog = page.getByRole('dialog', { name: 'Invitar a una persona del cliente' })
    // validación en pantalla
    await dialog.getByRole('button', { name: 'Enviar invitación' }).click()
    await expect(dialog.getByText('El correo es obligatorio.')).toBeVisible()
    await expect(dialog.getByText('El rol de portal es obligatorio.')).toBeVisible()
    await dialog.getByLabel(/^Correo/).fill('sin-arroba')
    await dialog.getByRole('button', { name: 'Enviar invitación' }).click()
    await expect(dialog.getByText('Correo inválido.')).toBeVisible()
    const ana = mail('ana')
    await dialog.getByLabel(/^Correo/).fill(ana)
    await dialog.getByLabel(/^Nombre/).fill('Ana Invitada')
    await dialog.getByLabel(/^Rol/).selectOption('CLIENT_OPERATOR')
    await shot(page, isMobile, 'fa3-invitar')
    const inviteResponse = page.waitForResponse((r) => r.url().includes(`/clients/${clientId}/portal-users/invite`) && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: 'Enviar invitación' }).click()
    const invited = (await (await inviteResponse).json()) as PortalInviteResultDto
    expect(invited.inviteToken, 'Development devuelve el token').toBeTruthy()
    await expect(toast(page, `Invitación enviada a ${ana}. El enlace vence el`)).toBeVisible()
    await expect(row(ana)).toContainText('Invitado')
    await expect(row(ana)).toContainText('Operador')
    // el token es un secreto: no aparece en ningún lugar de la pantalla
    expect(await page.content()).not.toContain(invited.inviteToken!)
    expect((await portalUsers(request, token, clientId)).map((u) => [u.email, u.status])).toEqual([[ana, 'INVITED']])

    // 409: el mismo correo en este cliente, y el de un usuario interno: el mismo mensaje neutro, a propósito
    for (const taken of [ana, ADMIN.email]) {
      await portal.getByRole('button', { name: 'Invitar usuario' }).click()
      dialog = page.getByRole('dialog', { name: 'Invitar a una persona del cliente' })
      await dialog.getByLabel(/^Correo/).fill(taken)
      await dialog.getByLabel(/^Rol/).selectOption('CLIENT_ADMIN')
      await dialog.getByRole('button', { name: 'Enviar invitación' }).click()
      await expect(dialog.getByText('Ese correo no está disponible para el portal de esta compañía.')).toBeVisible()
      await dialog.getByRole('button', { name: 'Cancelar' }).click()
      await expect(dialog).toBeHidden()
    }
    expect(await portalUsers(request, token, clientId)).toHaveLength(1)

    // ================= Reenviar: el enlace anterior deja de valer =================
    const resendResponse = page.waitForResponse((r) => r.url().includes('/resend-invite') && r.request().method() === 'POST')
    await row(ana).getByRole('button', { name: 'Reenviar invitación' }).click()
    dialog = page.getByRole('dialog', { name: 'Reenviar la invitación' })
    await expect(dialog).toContainText(`Se enviará un enlace nuevo a ${ana}. El enlace anterior deja de valer.`)
    await dialog.getByRole('button', { name: 'Reenviar invitación' }).click()
    const resent = (await (await resendResponse).json()) as PortalInviteResultDto
    await expect(toast(page, `Invitación enviada a ${ana}. El enlace vence el`)).toBeVisible()
    expect(resent.inviteToken).toBeTruthy()
    expect(resent.inviteToken).not.toBe(invited.inviteToken)
    const stale = await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email: ana, token: invited.inviteToken, password: PORTAL_PASSWORD } })
    expect(stale.status()).toBe(400)
    expect((await stale.json()).title).toBe('Invitación inválida o vencida.')
    await apiAccept(request, ana, resent.inviteToken!)

    // Reenviar a quien ya aceptó: ya no hay botón; si otra sesión lo hizo, el servidor responde 409 (la ficha abierta aún lo ofrece)
    await page.reload()
    await openClientPanels(page)
    await expect(row(ana)).toContainText('Activo')
    await expect(row(ana).getByRole('button', { name: 'Reenviar invitación' })).toHaveCount(0)

    // ================= Suspender / reactivar =================
    await row(ana).getByRole('button', { name: 'Suspender' }).click()
    dialog = page.getByRole('dialog', { name: 'Suspender el acceso' })
    await dialog.getByRole('button', { name: 'Suspender' }).click()
    await expect(toast(page, 'Acceso suspendido.')).toBeVisible()
    await expect(row(ana)).toContainText('Suspendido')
    expect((await portalUsers(request, token, clientId))[0].status).toBe('SUSPENDED')
    await row(ana).getByRole('button', { name: 'Reactivar' }).click()
    dialog = page.getByRole('dialog', { name: 'Reactivar el acceso' })
    await dialog.getByRole('button', { name: 'Reactivar' }).click()
    await expect(toast(page, 'Acceso reactivado.')).toBeVisible()
    await expect(row(ana)).toContainText('Activo')
    expect((await portalUsers(request, token, clientId))[0].status).toBe('ACTIVE')

    // ================= Dar de baja y volver a invitar =================
    await row(ana).getByRole('button', { name: 'Dar de baja' }).click()
    dialog = page.getByRole('dialog', { name: 'Dar de baja al usuario' })
    await dialog.getByRole('button', { name: 'Dar de baja' }).click()
    await expect(toast(page, 'Usuario dado de baja.')).toBeVisible()
    await expect(row(ana)).toContainText('Inhabilitado')
    expect((await portalUsers(request, token, clientId))[0].status).toBe('DISABLED')
    await row(ana).getByRole('button', { name: 'Volver a invitar' }).click()
    dialog = page.getByRole('dialog', { name: 'Volver a invitar' })
    await expect(dialog.getByLabel(/^Correo/)).toHaveValue(ana)
    await expect(dialog.getByLabel(/^Nombre/)).toHaveValue('Ana Invitada')
    await expect(dialog.getByLabel(/^Rol/)).toHaveValue('CLIENT_OPERATOR')
    await dialog.getByRole('button', { name: 'Enviar invitación' }).click()
    await expect(toast(page, `Invitación enviada a ${ana}.`)).toBeVisible()
    await expect(row(ana)).toContainText('Invitado')

    // ================= Lista con los cuatro estatus (captura) =================
    const beto = mail('beto')
    const carla = mail('carla')
    const dario = mail('dario')
    const b = await apiInvite(request, token, clientId, beto, 'CLIENT_ADMIN')
    await apiAccept(request, beto, b.inviteToken!)
    const c = await apiInvite(request, token, clientId, carla, 'CLIENT_READONLY')
    await apiAccept(request, carla, c.inviteToken!)
    const d = await apiInvite(request, token, clientId, dario, 'CLIENT_READONLY')
    await apiAccept(request, dario, d.inviteToken!)
    const all = await portalUsers(request, token, clientId)
    const idOf = (email: string) => all.find((u) => u.email === email)!.id
    expect((await request.post(`${API_URL}/api/v1/clients/${clientId}/portal-users/${idOf(carla)}/suspend`, { headers })).status()).toBe(204)
    expect((await request.post(`${API_URL}/api/v1/clients/${clientId}/portal-users/${idOf(dario)}/remove`, { headers })).status()).toBe(204)
    await page.reload()
    await openClientPanels(page)
    await expect(row(beto)).toContainText('Activo')
    await expect(row(carla)).toContainText('Suspendido')
    await expect(row(dario)).toContainText('Inhabilitado')
    await expect(row(ana)).toContainText('Invitado')
    // las acciones de cada estatus
    await expect(row(carla).getByRole('button', { name: 'Reactivar' })).toBeVisible()
    await expect(row(dario).getByRole('button', { name: 'Volver a invitar' })).toBeVisible()
    await expect(row(dario).getByRole('button', { name: 'Suspender' })).toHaveCount(0)
    await shot(page, isMobile, 'fa3-portal', portal)
    await expectNoHorizontalScroll(page)

    // ================= Cliente dado de baja con la ficha abierta en otra sesión =================
    await ok(request.post(`${API_URL}/api/v1/clients/${clientId}/deactivate`, { headers }), 'baja del cliente')
    await portal.getByRole('button', { name: 'Invitar usuario' }).click()
    dialog = page.getByRole('dialog', { name: 'Invitar a una persona del cliente' })
    await dialog.getByLabel(/^Correo/).fill(mail('eva'))
    await dialog.getByLabel(/^Rol/).selectOption('CLIENT_ADMIN')
    await dialog.getByRole('button', { name: 'Enviar invitación' }).click()
    await expect(dialog.getByText('El cliente está dado de baja; solo se consulta su historial.')).toBeVisible()
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    // al recargar la ficha, la pantalla ya lo sabe: aviso, sin invitar y sin reenviar; lo demás se sigue consultando
    await page.reload()
    await openClientPanels(page)
    await expect(portal.getByText('El cliente está dado de baja; solo se consulta su historial.')).toBeVisible()
    await expect(portal.getByRole('button', { name: 'Invitar usuario' })).toBeDisabled()
    await expect(row(ana).getByRole('button', { name: 'Reenviar invitación' })).toHaveCount(0)
    await expect(row(dario).getByRole('button', { name: 'Volver a invitar' })).toHaveCount(0)
    await expect(row(ana)).toBeVisible()
    await shot(page, isMobile, 'fa3-portal-baja', portal)
    // el servidor responde lo mismo si se le insiste por el API
    const direct = await request.post(`${API_URL}/api/v1/clients/${clientId}/portal-users/invite`, { headers, data: { email: mail('eva'), role: 'CLIENT_ADMIN' } })
    expect(direct.status()).toBe(409)
    expect((await direct.json()).title).toBe('El cliente está dado de baja; solo se consulta su historial.')
    await expectNoHorizontalScroll(page)
    await ok(request.post(`${API_URL}/api/v1/clients/${clientId}/reactivate`, { headers }), 'reactivar el cliente')
  })

  test('Cotizador: con tarifa de contrato, COD, línea sin tarifa, cliente sin tarifas, validación y cliente de baja', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const token = await apiToken(request)
    const headers = auth(token)
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FA3 cotiza ${suffix}`, contract: { startDate: tenantDay(-10), title: 'Contrato de cotización' } } }),
      'cliente',
    )
    const clientId = client.publicId!
    const contractId = client.currentContract!.publicId!
    const contractNumber = client.currentContract!.contractNumber!
    // el ejemplo del manual: base 7 + 8 piezas (2–5 a 1.00, 6+ a 0.75 = 6.25) + despacho 3 + COD 2.5 % de 200 (5.00) = 21.25
    await ok(request.patch(`${API_URL}/api/v1/contracts/${contractId}/billing-model`, { headers, data: { billPerService: true, billExtraPiece: true, billDispatchFee: true, billCodFee: true } }), 'modelo')
    await ok(request.patch(`${API_URL}/api/v1/contracts/${contractId}/dispatch-fee`, { headers, data: { amount: 3 } }), 'despacho')
    await ok(request.patch(`${API_URL}/api/v1/contracts/${contractId}/cod-fee`, { headers, data: { type: 'PERCENT', value: 2.5 } }), 'cod')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 7, effectiveFrom: tenantDay(-10) } }), 'tarifa')
    const extra = await ok<{ componentId?: number; id?: number }>(
      request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'EXTRA_PIECE', serviceType: 'STANDARD', packageType: 'BOX', effectiveFrom: tenantDay(-10) } }),
      'pieza extra',
    )
    const extraId = extra.componentId ?? extra.id
    for (const tier of [
      { fromUnit: 2, toUnit: 5, rate: 1 },
      { fromUnit: 6, toUnit: null, rate: 0.75 },
    ])
      await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components/${extraId}/tiers`, { headers, data: { ...tier, effectiveFrom: tenantDay(-10) } }), 'tramo')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'activar')
    // un segundo cliente con contrato en borrador y sin tarifas
    const bare = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FA3 sin tarifas ${suffix}`, contract: { startDate: tenantDay(-10), title: 'Sin tarifas' } } }),
      'cliente sin tarifas',
    )

    await login(page)
    await page.goto(`/catalog/clients?client=${clientId}`)
    await openClientPanels(page)
    const section = panel(page, 'Contratos')
    await expect(section.getByRole('tab')).toHaveText(['Contrato', 'Tarifas', 'SLA', 'Servicios especiales', 'Cotizador'])
    await section.getByRole('tab', { name: 'Cotizador' }).click()
    const line = (n: number) => section.getByRole('group', { name: `Línea ${n}` })
    const quote = section.getByRole('button', { name: 'Cotizar', exact: true })

    // ----- validación en pantalla -----
    await quote.click()
    await expect(line(1).getByText('El tipo de servicio es obligatorio.')).toBeVisible()
    await expect(line(1).getByText('El tipo de paquete es obligatorio.')).toBeVisible()
    await line(1).getByLabel(/^Servicio/).selectOption('STANDARD')
    await line(1).getByLabel(/^Paquete/).selectOption('BOX')
    await line(1).getByLabel(/^Piezas/).fill('0')
    await quote.click()
    await expect(line(1).getByText('La cantidad de piezas debe ser al menos 1.')).toBeVisible()

    // ----- con tarifa de contrato y COD: 21.25 -----
    await line(1).getByLabel(/^Piezas/).fill('8')
    await section.getByLabel('Monto COD').fill('200')
    await shot(page, isMobile, 'fa3-cotizador', section)
    await quote.click()
    const result = section.getByRole('region', { name: 'Resultado de la cotización' })
    await expect(result).toBeVisible()
    await expect(result.getByTestId('quote-contract')).toContainText(`Se usó el contrato ${contractNumber} · Contrato de cotización (Vigente), vigente al`)
    await expect(result.getByText('Cargo por despacho').locator('xpath=following-sibling::dd')).toContainText('3.00')
    await expect(result.getByText('Cargo por COD').locator('xpath=following-sibling::dd')).toContainText('5.00')
    await expect(result.getByText('Total', { exact: true }).locator('xpath=following-sibling::dd')).toContainText('21.25')
    await expect(result.getByText('Contrato', { exact: true }).first()).toBeVisible()
    await expect(result.getByText('13.25')).toBeVisible()
    await shot(page, isMobile, 'fa3-cotizacion', section)
    await expectNoHorizontalScroll(page)

    // ----- sin COD: el despacho se queda, el COD es 0 -----
    await section.getByLabel('Monto COD').fill('')
    await quote.click()
    await expect(result.getByText('Total', { exact: true }).locator('xpath=following-sibling::dd')).toContainText('16.25')
    await expect(result.getByText('Cargo por COD').locator('xpath=following-sibling::dd')).toContainText('0.00')

    // ----- una segunda línea que el contrato no tiene: aviso, sin error -----
    await section.getByRole('button', { name: 'Agregar línea' }).click()
    await line(2).getByLabel(/^Servicio/).selectOption('EXPRESS')
    await line(2).getByLabel(/^Paquete/).selectOption('ENVELOPE')
    await line(2).getByLabel(/^Piezas/).fill('3')
    await quote.click()
    await expect(result.getByText(/No hay tarifa vigente para .+ \/ .+: esa línea se cotiza en 0\./)).toBeVisible()
    await expect(result.getByText('Sin tarifa').first()).toBeVisible()
    await shot(page, isMobile, 'fa3-sin-tarifa', section)
    // el par repetido lo detecta la pantalla con el texto del servidor
    await line(2).getByLabel(/^Servicio/).selectOption('STANDARD')
    await line(2).getByLabel(/^Paquete/).selectOption('BOX')
    await quote.click()
    await expect(section.getByText('El par servicio/tipo de paquete está repetido; envíe una sola línea por (servicio, tipo de paquete) con el total de piezas.')).toBeVisible()
    await line(2).getByRole('button', { name: 'Quitar la línea 2' }).click()
    await expect(line(2)).toHaveCount(0)

    // ----- fecha en el pasado antes de que existiera el contrato: tarifas genéricas, sin despacho ni COD -----
    await section.getByLabel('Cotizar a la fecha').fill(tenantDay(-400))
    await quote.click()
    await expect(result.getByTestId('quote-contract')).toContainText('no tiene contrato vigente al')
    await expect(result.getByText('Cargo por despacho').locator('xpath=following-sibling::dd')).toContainText('0.00')

    // ----- cliente con contrato en borrador y sin tarifas -----
    await page.goto(`/catalog/clients?client=${bare.publicId}`)
    await openClientPanels(page)
    const section2 = panel(page, 'Contratos')
    await section2.getByRole('tab', { name: 'Cotizador' }).click()
    await section2.getByLabel(/^Servicio/).selectOption('STANDARD')
    await section2.getByLabel(/^Paquete/).selectOption('BOX')
    await section2.getByLabel(/^Piezas/).fill('2')
    await section2.getByRole('button', { name: 'Cotizar', exact: true }).click()
    const result2 = section2.getByRole('region', { name: 'Resultado de la cotización' })
    await expect(result2.getByText(/No hay tarifa vigente para .+ \/ .+: esa línea se cotiza en 0\./)).toBeVisible()
    await expect(result2.getByText('Total', { exact: true }).locator('xpath=following-sibling::dd')).toContainText('0.00')

    // ----- cliente dado de baja con la ficha abierta: 409 del servidor, tal cual -----
    await ok(request.post(`${API_URL}/api/v1/clients/${bare.publicId}/deactivate`, { headers }), 'baja del cliente')
    await section2.getByRole('button', { name: 'Cotizar', exact: true }).click()
    await expect(section2.getByRole('alert')).toContainText('El cliente está dado de baja; solo se consulta su historial.')
    await expect(result2).toHaveCount(0)
    await expectNoHorizontalScroll(page)
  })
})
