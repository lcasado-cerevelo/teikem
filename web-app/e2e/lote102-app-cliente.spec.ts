// Recorrido de la app del cliente (app-cliente, Lote 102) en su versión web contra el API real.
//
// Requiere: el API con Auth:TrustedBrowsers:ReturnLinkInResponse=true, Portal:ReturnInviteTokenInResponse=true y Auth:PasswordReset:WebBaseUrl = la app (CLIENT_APP_URL), y la app del cliente en CLIENT_APP_URL
// (`npm run web -- --port 8081` en app-cliente, con EXPO_PUBLIC_API_URL = el API). Se crean dos clientes con órdenes y un usuario de portal para el primero.
// Recorre: computadora nueva (confirmar por correo) → inscribir el segundo paso (MFA) → código de recuperación → aceptar el documento legal (botón solo al final) → inicio, órdenes, ficha, avisos y ajustes →
// la casilla «No confiar en esta computadora» se RECUERDA → una computadora de confianza con el código recordado entra directo → con «No confiar» pide el código y se olvida la computadora → una orden de OTRO cliente no se ve.
// Capturas (solo escritorio): docs/manual/frontend/img/l102-<paso>.png.
import { createHmac } from 'node:crypto'
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const APP_URL = process.env.CLIENT_APP_URL ?? 'http://localhost:8081'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForTimeout(400)
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
}

/** Código TOTP de 6 dígitos (RFC 6238, SHA-1, 30 s) para la clave en base32 que muestra la app. */
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

async function adminToken(request: APIRequestContext): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok(), `login: ${await res.text()}`).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    const again = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await again.json()) as AuthResultDto
  }
  const token = body.tokens?.accessToken ?? ''
  expect(token).toBeTruthy()
  // con el documento legal exigido, el administrador de las pruebas lo acepta por API
  const headers = { Authorization: `Bearer ${token}` }
  const status = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (status.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: status.currentVersion, lang: 'es' } })
  return token
}

async function makeClientWithOrders(request: APIRequestContext, headers: Record<string, string>, name: string, orders: number) {
  const c = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name } })
  expect(c.ok(), await c.text()).toBeTruthy()
  const clientId = ((await c.json()) as { publicId: string }).publicId
  const loc = await request.post(`${API_URL}/api/v1/locations`, {
    headers,
    data: { clientPublicId: clientId, name: `Sucursal ${name}`, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' },
  })
  expect(loc.ok(), await loc.text()).toBeTruthy()
  const locId = ((await loc.json()) as { publicId: string }).publicId
  const made: { publicId: string; orderNumber: string }[] = []
  for (let i = 0; i < orders; i++) {
    const o = await request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: clientId, consigneeLocationPublicId: locId, packages: [{ pieces: 1 }] } })
    expect(o.ok(), await o.text()).toBeTruthy()
    made.push((await o.json()) as { publicId: string; orderNumber: string })
  }
  return { clientId, orders: made }
}

async function fillLogin(page: Page, email: string, password: string) {
  await page.getByLabel('Correo electrónico').fill(email)
  await page.getByLabel('Contraseña', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

test('App del cliente: computadora nueva, MFA, documento legal, órdenes, «No confiar» recordada y aislamiento', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = () => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await adminToken(request)}` }

  // 0) dos clientes con órdenes y un usuario de portal para el primero
  const a = await makeClientWithOrders(request, headers, `Cliente A ${suffix}`, 2)
  const b = await makeClientWithOrders(request, headers, `Cliente B ${suffix}`, 1)
  const email = `app.${suffix.toLowerCase()}@demo.test`
  const password = `App_${suffix}_Clave-2026!`
  const invite = await request.post(`${API_URL}/api/v1/clients/${a.clientId}/portal-users/invite`, { headers, data: { email, fullName: 'Cliente Prueba', role: 'CLIENT_READONLY' } })
  expect(invite.ok(), await invite.text()).toBeTruthy()
  const inviteToken = ((await invite.json()) as { inviteToken?: string }).inviteToken
  test.skip(!inviteToken, 'Portal:ReturnInviteTokenInResponse está apagado en este API')
  const accept = await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email, token: inviteToken, password } })
  expect(accept.status()).toBe(204)

  if (!isMobile) await page.setViewportSize({ width: 1100, height: 760 })
  await page.goto(`${APP_URL}/login`)
  await expect(page.getByRole('heading', { name: 'Portal de clientes' })).toBeVisible({ timeout: 60_000 })
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l102-login')

  // 1) computadora nueva: pide confirmar por correo (en desarrollo, con el enlace en pantalla)
  await fillLogin(page, email, password)
  await expect(page.getByRole('heading', { name: 'Confirme esta computadora' })).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l102-confirmar')
  // el enlace se abre en otra pestaña (o en la misma, según el navegador): en las dos se confirma la computadora de esta marca
  const [popup] = await Promise.all([page.waitForEvent('popup', { timeout: 8_000 }).catch(() => null), page.getByText('Confirmar ahora (solo desarrollo)').click()])
  const confirmPage = popup ?? page
  await expect(confirmPage.getByText('Listo: esta computadora quedó confirmada.')).toBeVisible({ timeout: 30_000 })
  if (popup) {
    await popup.close()
    await page.getByRole('button', { name: 'Volver a la pantalla de entrada' }).click()
  } else {
    await page.getByRole('link', { name: 'Ir a la pantalla de entrada' }).click()
  }

  // 2) primer ingreso: inscribir el segundo paso (MFA) y guardar los códigos de recuperación
  await fillLogin(page, email, password)
  await expect(page.getByRole('heading', { name: 'Active la verificación en dos pasos' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('button', { name: 'Empezar' }).click()
  const secret = (await page.getByTestId('mfa-secret').textContent())!.trim()
  await page.getByLabel('Código').fill(totp(secret))
  await page.getByRole('button', { name: 'Confirmar' }).click()
  await expect(page.getByRole('heading', { name: 'Códigos de recuperación' })).toBeVisible({ timeout: 30_000 })
  const recovery = await page.getByText(/^[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}$/).allTextContents()
  expect(recovery.length).toBeGreaterThanOrEqual(2)
  await shot(page, isMobile, 'l102-recuperacion')
  await page.getByRole('button', { name: 'Ya los guardé' }).click()
  // el código de la app ya se usó en esta ventana de 30 s: se entra con un código de recuperación
  await page.getByLabel('Código').fill(recovery[0])
  await page.getByRole('button', { name: 'Verificar' }).click()

  // 3) documento legal: «Acepto» solo al llegar al final
  await expect(page.getByRole('heading', { name: 'Política de privacidad y términos de uso' })).toBeVisible({ timeout: 30_000 })
  const accept2 = page.getByRole('button', { name: 'Acepto la política de privacidad y los términos de uso' })
  await expect(accept2).toBeDisabled()
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l102-legal')
  // como lo haría una persona: con la rueda del ratón sobre el texto, hasta el final
  await page.getByTestId('legal-scroll').hover()
  for (let i = 0; i < 12; i++) await page.mouse.wheel(0, 4000)
  await expect(accept2).toBeEnabled()
  await accept2.click()

  // 4) inicio: saludo, el cliente y cuántas órdenes tiene
  await expect(page.getByRole('heading', { name: 'Hola, Cliente' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(`Cliente: Cliente A ${suffix}`)).toBeVisible()
  await expect(page.getByTestId('home-total')).toHaveText('2')
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l102-inicio')

  // 5) órdenes: solo las del cliente; buscar y abrir la ficha
  await page.getByRole('tab', { name: 'Órdenes' }).click()
  await expect(page.getByRole('heading', { name: 'Mis órdenes' })).toBeVisible()
  await expect(page.getByText(/^Orden ORD-/)).toHaveCount(2)
  await expect(page.getByRole('progressbar').first()).toBeVisible()   // cada orden de la lista trae su barra de progreso («Paso 1 de N · Borrador»)
  await shot(page, isMobile, 'l102-ordenes')
  await page.getByTestId('orders-search').fill(a.orders[0].orderNumber)
  await expect(page.getByText(/^Orden ORD-/)).toHaveCount(1, { timeout: 15_000 })
  await page.getByText(/^Orden ORD-/).click()
  await expect(page.getByText('Seguimiento')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Entrega', { exact: true })).toBeVisible()
  await expect(page.getByTestId('step-current')).toHaveCount(1)   // la barra y la lista de etapas (una por estatus de la compañía)
  expect(await page.getByTestId(/^step-/).count()).toBeGreaterThanOrEqual(3)
  await shot(page, isMobile, 'l102-orden')
  const orderUrl = page.url()
  expect(orderUrl).toContain(a.orders[0].publicId)

  // 6) una orden de OTRO cliente no se ve: responde como si no existiera
  await page.goto(`${APP_URL}/order/${b.orders[0].publicId}`)
  await expect(page.getByRole('alert')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Seguimiento')).toHaveCount(0)

  // 7) avisos y ajustes
  await page.goto(`${APP_URL}/notices`)
  await expect(page.getByText('Los resúmenes que su compañía de entregas configuró mandarle.')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Todavía no hay avisos.')).toBeVisible()
  await page.goto(`${APP_URL}/settings`)
  await expect(page.getByText('Mis computadoras de confianza')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Esta computadora', { exact: true }).first()).toBeVisible()
  await expect(page.getByText(/no está disponible en este aparato/)).toBeVisible()   // la huella solo existe en el teléfono
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l102-ajustes')

  // 8) cerrar sesión; la casilla «No confiar» se RECUERDA entre visitas
  await page.getByRole('button', { name: 'Cerrar sesión' }).click()
  await expect(page.getByRole('heading', { name: 'Portal de clientes' })).toBeVisible({ timeout: 30_000 })
  const noTrust = page.getByRole('checkbox', { name: 'No confiar en esta computadora' })
  await expect(noTrust).not.toBeChecked()
  await noTrust.click()
  await expect(noTrust).toBeChecked()
  await page.reload()
  await expect(page.getByRole('checkbox', { name: 'No confiar en esta computadora' })).toBeChecked({ timeout: 30_000 })
  await shot(page, isMobile, 'l102-notrust')

  // 9) con «No confiar» entra pero el código se pide SIEMPRE (aquí con el segundo código de recuperación) y la computadora se olvida
  await fillLogin(page, email, password)
  await expect(page.getByRole('heading', { name: 'Código de seguridad' })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Código').fill(recovery[1])
  await page.getByRole('button', { name: 'Verificar' }).click()
  await expect(page.getByRole('heading', { name: 'Hola, Cliente' })).toBeVisible({ timeout: 30_000 })
  await page.goto(`${APP_URL}/settings`)
  await page.getByRole('button', { name: 'Cerrar sesión' }).click()
  await expect(page.getByRole('heading', { name: 'Portal de clientes' })).toBeVisible({ timeout: 30_000 })
  await fillLogin(page, email, password)
  await expect(page.getByRole('heading', { name: 'Confirme esta computadora' })).toBeVisible({ timeout: 30_000 })   // se olvidó: vuelve a pedir el correo
})
