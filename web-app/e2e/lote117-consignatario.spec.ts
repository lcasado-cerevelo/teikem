// Recorrido del acceso del consignatario (Lote 117) en la app del cliente (versión web) contra el API real.
//
// Requiere: el API con Auth:TrustedBrowsers:ReturnLinkInResponse=true, Portal:ReturnInviteTokenInResponse=true y Auth:PasswordReset:WebBaseUrl = la app (CLIENT_APP_URL), y la app del cliente en CLIENT_APP_URL
// (`npm run web -- --port 8081` en app-cliente, con EXPO_PUBLIC_API_URL = el API); API_URL apunta al mismo API.
// Recorre: el cliente (persona con MFA) abre «Configurar avisos → Acceso» y le da acceso a un consignatario → el consignatario acepta la invitación, entra SIN MFA a sus cuatro pestañas (Envíos, Avisos, Mensajes y Ajustes), ve
// su envío (solo lectura, sin precios), le escribe a su cliente y apaga los avisos por correo → el cliente ve el contador, la lista de conversaciones y contesta → el consignatario ve la respuesta → tema oscuro.
// Capturas (solo escritorio): docs/manual/frontend/img/l117-<paso>.png.
import { createHmac } from 'node:crypto'
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const APP_URL = process.env.CLIENT_APP_URL ?? 'http://localhost:8081'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForTimeout(500)
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
}

/** Código TOTP de 6 dígitos (RFC 6238, SHA-1, 30 s) para la clave en base32. */
function totp(secretBase32: string): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of secretBase32.replace(/=+$/, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)))
  const h = createHmac('sha1', key).update(counter).digest()
  const o = h[h.length - 1] & 15
  return String(((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000)).padStart(6, '0')
}

async function staffHeaders(request: APIRequestContext): Promise<Record<string, string>> {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok(), `login: ${await res.text()}`).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    body = (await (await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })).json()) as AuthResultDto
  }
  const headers = { Authorization: `Bearer ${body.tokens?.accessToken ?? ''}` }
  const status = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (status.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: status.currentVersion, lang: 'es' } })
  return headers
}

async function post<T>(request: APIRequestContext, path: string, headers: Record<string, string>, data?: unknown): Promise<T> {
  const r = await request.post(`${API_URL}${path}`, { headers, data })
  expect(r.ok(), `${path}: ${await r.text()}`).toBeTruthy()
  const text = await r.text()
  return (text ? JSON.parse(text) : null) as T
}

/** La computadora nueva se confirma con el enlace de desarrollo que trae la respuesta del login. */
async function confirmBrowserByApi(request: APIRequestContext, email: string, password: string, browserToken: string) {
  const first = await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email, password, trustedBrowserToken: browserToken } })
  const body = (await first.json()) as AuthResultDto
  expect(body.status).toBe('browser_confirmation_required')
  const token = (body.browser?.devLink ?? '').split('token=')[1]?.split('&')[0]
  expect(token).toBeTruthy()
  const ok = await request.post(`${API_URL}/api/v1/auth/confirm-browser`, { data: { token } })
  expect(ok.status()).toBe(204)
}

async function fillLogin(page: Page, email: string, password: string) {
  await page.getByLabel('Correo electrónico').fill(email)
  await page.getByLabel('Contraseña', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

/** Computadora nueva en la pantalla: se confirma con el botón de desarrollo y se vuelve a entrar. */
async function loginConfirmingTheComputer(page: Page, email: string, password: string) {
  await page.goto(`${APP_URL}/login`)
  await expect(page.getByRole('heading', { name: 'Portal de clientes' })).toBeVisible({ timeout: 60_000 })
  await fillLogin(page, email, password)
  await expect(page.getByRole('heading', { name: 'Confirme esta computadora' })).toBeVisible({ timeout: 30_000 })
  const [popup] = await Promise.all([page.waitForEvent('popup', { timeout: 8_000 }).catch(() => null), page.getByText('Confirmar ahora (solo desarrollo)').click()])
  const confirmPage = popup ?? page
  await expect(confirmPage.getByText('Listo: esta computadora quedó confirmada.')).toBeVisible({ timeout: 30_000 })
  if (popup) {
    await popup.close()
    await page.getByRole('button', { name: 'Volver a la pantalla de entrada' }).click()
  } else {
    await page.getByRole('link', { name: 'Ir a la pantalla de entrada' }).click()
  }
  await fillLogin(page, email, password)
}

async function newConsigneeContext(browser: Browser, isMobile: boolean) {
  const context = await browser.newContext({ locale: 'es-PR', viewport: isMobile ? { width: 412, height: 915 } : { width: 1100, height: 760 } })
  return { context, page: await context.newPage() }
}

test('Acceso del consignatario: el cliente invita, el consignatario entra sin MFA, ve sus envíos, se escriben y apaga los avisos', async ({ page, request, browser, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const staff = await staffHeaders(request)

  // 0) cliente con contrato y tarifa, un consignatario con una orden, y una persona del cliente (administrador) con MFA
  const day = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10)
  const client = await post<{ publicId: string; name: string; currentContract?: { publicId?: string } | null }>(request, '/api/v1/clients', staff, { name: `Cliente 117 ${suffix}`, contract: { startDate: day, title: 'Contrato de prueba' } })
  const contractId = client.currentContract?.publicId ?? ''
  await post(request, `/api/v1/contracts/${contractId}/rate-components`, staff, { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 10, effectiveFrom: day })
  await post(request, `/api/v1/contracts/${contractId}/status`, staff, { toCode: 'ACTIVE' })
  const consigneeName = `Tienda Norte ${suffix}`
  const loc = await post<{ publicId: string }>(request, '/api/v1/locations', staff, { clientPublicId: client.publicId, name: consigneeName, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' })
  await post(request, '/api/v1/orders', staff, { clientPublicId: client.publicId, consigneeLocationPublicId: loc.publicId, packages: [{ pieces: 2, description: 'Caja de pruebas' }] })

  const password = `C117_${suffix}_Clave-2026!`
  const adminMail = `c117.admin.${suffix.toLowerCase()}@demo.test`
  const invite = await post<{ inviteToken?: string }>(request, `/api/v1/clients/${client.publicId}/portal-users/invite`, staff, { email: adminMail, fullName: 'Ana Admin', role: 'CLIENT_ADMIN' })
  test.skip(!invite.inviteToken, 'Portal:ReturnInviteTokenInResponse está apagado en este API')
  expect((await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email: adminMail, token: invite.inviteToken, password } })).status()).toBe(204)

  // la persona del cliente: computadora de confianza e inscripción del MFA por API (los códigos de recuperación sirven para entrar por la pantalla)
  const adminBrowser = (crypto.randomUUID() + crypto.randomUUID()).replace(/-/g, '')
  await confirmBrowserByApi(request, adminMail, password, adminBrowser)
  const mfa = (await (await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email: adminMail, password, trustedBrowserToken: adminBrowser } })).json()) as AuthResultDto
  const challenge = { Authorization: `Bearer ${mfa.mfaChallengeToken}` }
  const enroll = await post<{ secret?: string; sharedKey?: string }>(request, '/api/v1/auth/mfa/totp/enroll', challenge)
  const confirmed = await post<{ recoveryCodes?: string[]; codes?: string[] }>(request, '/api/v1/auth/mfa/totp/confirm', challenge, { code: totp((enroll.secret ?? enroll.sharedKey)!) })
  const recovery = (confirmed.recoveryCodes ?? confirmed.codes)!
  const session = await post<AuthResultDto>(request, '/api/v1/auth/mfa/verify', challenge, { code: recovery[0], deviceInfo: 'e2e', trustedBrowserToken: adminBrowser })
  const clientApi = { Authorization: `Bearer ${session.tokens?.accessToken}` }
  const legal = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers: clientApi })).json()) as { currentVersion: string }
  await post(request, '/api/v1/legal/accept', clientApi, { version: legal.currentVersion, lang: 'es' })
  await request.put(`${API_URL}/api/v1/portal/consignee-notices`, { headers: clientApi, data: { enabled: true, lang: 'es' } })

  // 1) EL CLIENTE: Configurar avisos → Acceso → invita al consignatario
  if (!isMobile) await page.setViewportSize({ width: 1100, height: 760 })
  await loginConfirmingTheComputer(page, adminMail, password)
  await expect(page.getByRole('heading', { name: 'Código de seguridad' })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Código').fill(recovery[1])
  await page.getByRole('button', { name: 'Verificar' }).click()
  await expect(page.getByRole('heading', { name: 'Hola, Ana' })).toBeVisible({ timeout: 30_000 })
  await page.goto(`${APP_URL}/consignee-notices`)
  await expect(page.getByText(consigneeName)).toBeVisible({ timeout: 30_000 })
  await page.getByTestId(/^access-open-/).first().click()
  const consigneeMail = `c117.tienda.${suffix.toLowerCase()}@demo.test`
  await expect(page.getByText('Nadie tiene acceso todavía.')).toBeVisible({ timeout: 30_000 })
  await page.getByTestId('access-email').fill(consigneeMail)
  await page.getByTestId('access-name').fill('Doña Tienda')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l117-acceso-invitar')
  await page.getByTestId('access-invite').click()
  await expect(page.getByText(`Invitación enviada a ${consigneeMail}.`)).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Invitación pendiente')).toBeVisible()
  await shot(page, isMobile, 'l117-acceso-pendiente')

  // 2) el consignatario acepta la invitación (el enlace vuelve a emitirse por API: la pantalla no lo muestra)
  const access = (await (await request.get(`${API_URL}/api/v1/portal/consignee-access/${loc.publicId}`, { headers: clientApi })).json()) as { id: number }[]
  const resent = await post<{ inviteToken?: string }>(request, `/api/v1/portal/consignee-access/${loc.publicId}/${access[0].id}/resend`, clientApi)
  expect((await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email: consigneeMail, token: resent.inviteToken, password } })).status()).toBe(204)
  // el documento legal del consignatario se acepta por API (la pantalla ya se recorrió en el Lote 102)
  const cBrowser = (crypto.randomUUID() + crypto.randomUUID()).replace(/-/g, '')
  await confirmBrowserByApi(request, consigneeMail, password, cBrowser)
  const cLogin = (await (await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email: consigneeMail, password, trustedBrowserToken: cBrowser } })).json()) as AuthResultDto
  expect(cLogin.status).toBe('ok')   // el consignatario NO pasa por el MFA
  const cApi = { Authorization: `Bearer ${cLogin.tokens?.accessToken}` }
  await post(request, '/api/v1/legal/accept', cApi, { version: legal.currentVersion, lang: 'es' })

  // 3) EL CONSIGNATARIO en otra computadora (contexto nuevo): entra sin MFA y aterriza en Avisos con sus cuatro pestañas
  const c = await newConsigneeContext(browser, isMobile)
  const cp = c.page
  await loginConfirmingTheComputer(cp, consigneeMail, password)
  await expect(cp.getByRole('heading', { name: 'Mis avisos' })).toBeVisible({ timeout: 30_000 })
  for (const tab of ['Envíos', 'Avisos', 'Mensajes', 'Ajustes']) await expect(cp.getByRole('tab', { name: tab })).toBeVisible()
  await expect(cp.getByRole('tab', { name: 'Inicio' })).toHaveCount(0)
  expect(await noHScroll(cp)).toBe(true)
  await shot(cp, isMobile, 'l117-consignatario-avisos')

  // 4) sus envíos: solo lectura, sin precios, sin botón de crear
  await cp.getByRole('tab', { name: 'Envíos' }).click()
  await expect(cp.getByRole('heading', { name: 'Mis envíos' })).toBeVisible({ timeout: 30_000 })
  await expect(cp.getByText(/^Conduce /)).toHaveCount(1)
  await expect(cp.getByTestId('orders-new')).toHaveCount(0)
  await shot(cp, isMobile, 'l117-consignatario-envios')
  await cp.getByText(/^Conduce /).click()
  await expect(cp.getByText('Calle 5 #12')).toBeVisible({ timeout: 30_000 })
  await expect(cp.getByText('2 × Caja de pruebas')).toBeVisible()
  await expect(cp.getByText(/Factura|COD/)).toHaveCount(0)
  await shot(cp, isMobile, 'l117-consignatario-envio')

  // 5) le escribe a su cliente
  await cp.goto(`${APP_URL}/messages`)
  await expect(cp.getByText('Su conversación con su cliente. Solo usted y las personas de su cliente la ven.')).toBeVisible({ timeout: 30_000 })
  await cp.getByTestId('messages-input').fill('Hola, ¿cuándo llega mi paquete?')
  await cp.getByTestId('messages-send').click()
  await expect(cp.getByText('Hola, ¿cuándo llega mi paquete?')).toBeVisible({ timeout: 30_000 })
  await shot(cp, isMobile, 'l117-consignatario-mensajes')

  // 6) su cuenta: apaga el correo y se le dice cómo volver a prenderlo
  await cp.goto(`${APP_URL}/settings`)
  await expect(cp.getByText('Cómo recibo los avisos')).toBeVisible({ timeout: 30_000 })
  await expect(cp.getByText(consigneeName)).toBeVisible()
  await shot(cp, isMobile, 'l117-consignatario-ajustes')

  // 7) EL CLIENTE ve el contador, la conversación, y contesta
  await page.goto(`${APP_URL}/messages`)
  await expect(page.getByText('1 sin leer')).toBeVisible({ timeout: 45_000 })
  await shot(page, isMobile, 'l117-cliente-mensajes')
  await page.getByTestId('open-consignee-messages').click()
  await expect(page.getByText(consigneeName)).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l117-cliente-conversaciones')
  await page.getByText(consigneeName).click()
  await expect(page.getByTestId('bubble-team').getByText('Hola, ¿cuándo llega mi paquete?')).toBeVisible({ timeout: 30_000 })
  // la pestaña Mensajes queda montada debajo: el hilo abierto es el último
  await page.getByTestId('messages-input').last().fill('Mañana por la mañana.')
  await page.getByTestId('messages-send').last().click()
  await expect(page.getByTestId('bubble-mine').getByText('Mañana por la mañana.')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l117-cliente-conversacion')

  // 8) el consignatario ve la respuesta
  await cp.goto(`${APP_URL}/messages`)
  await expect(cp.getByTestId('bubble-team').getByText('Mañana por la mañana.')).toBeVisible({ timeout: 30_000 })

  // 9) lo que no le toca: las rutas del cliente le responden 403 (el servidor limita, no la interfaz)
  const forbidden = await request.get(`${API_URL}/api/v1/portal/orders`, { headers: cApi })
  expect(forbidden.status()).toBe(403)
  const create = await request.post(`${API_URL}/api/v1/portal/orders`, { headers: cApi, data: { packages: [{ pieces: 1 }] } })
  expect(create.status()).toBe(403)   // el consignatario no crea órdenes

  // 10) tema oscuro desde sus ajustes: se aplica al instante
  await cp.goto(`${APP_URL}/settings`)
  await cp.getByTestId('theme-dark').click()
  await expect(cp.getByTestId('theme-dark')).toBeChecked()
  await shot(cp, isMobile, 'l117-consignatario-ajustes-oscuro')
  await c.context.close()
})
