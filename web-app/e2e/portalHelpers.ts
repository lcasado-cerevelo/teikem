// Ayudantes comunes de los recorridos de la app del cliente (Lotes 102, 116 y 117): entrar como la persona del cliente (MFA con códigos de recuperación), confirmar la computadora, capturas.
import { createHmac } from 'node:crypto'
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, type APIRequestContext, type Browser, type Page } from '@playwright/test'
import { manualImgDir, manualLang } from './manualLang'
import type { components } from '../src/kernel/api/schema'

export type AuthResultDto = components['schemas']['AuthResultDto']

export const API_URL = process.env.API_URL ?? 'http://localhost:5000'
export const APP_URL = process.env.CLIENT_APP_URL ?? 'http://localhost:8081'
export const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
export const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))

export async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForTimeout(500)
  mkdirSync(IMG_DIR, { recursive: true })
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
}

/** Código TOTP de 6 dígitos (RFC 6238, SHA-1, 30 s) para la clave en base32. */
export function totp(secretBase32: string): string {
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

export async function staffHeaders(request: APIRequestContext): Promise<Record<string, string>> {
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

export async function post<T>(request: APIRequestContext, path: string, headers: Record<string, string>, data?: unknown): Promise<T> {
  const r = await request.post(`${API_URL}${path}`, { headers, data })
  expect(r.ok(), `${path}: ${await r.text()}`).toBeTruthy()
  const text = await r.text()
  return (text ? JSON.parse(text) : null) as T
}

/** La computadora nueva se confirma con el enlace de desarrollo que trae la respuesta del login. */
export async function confirmBrowserByApi(request: APIRequestContext, email: string, password: string, browserToken: string) {
  const first = await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email, password, trustedBrowserToken: browserToken } })
  const body = (await first.json()) as AuthResultDto
  expect(body.status).toBe('browser_confirmation_required')
  const token = (body.browser?.devLink ?? '').split('token=')[1]?.split('&')[0]
  expect(token).toBeTruthy()
  const ok = await request.post(`${API_URL}/api/v1/auth/confirm-browser`, { data: { token } })
  expect(ok.status()).toBe(204)
}

export async function fillLogin(page: Page, email: string, password: string) {
  await page.getByLabel('Correo electrónico').fill(email)
  await page.getByLabel('Contraseña', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

/** Computadora nueva en la pantalla: se confirma con el botón de desarrollo y se vuelve a entrar. */
export async function loginConfirmingTheComputer(page: Page, email: string, password: string) {
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

export async function newConsigneeContext(browser: Browser, isMobile: boolean) {
  const context = await browser.newContext({ locale: 'es-PR', viewport: isMobile ? { width: 412, height: 915 } : { width: 1100, height: 760 } })
  return { context, page: await context.newPage() }
}

export interface ClientWithAdmin {
  staff: Record<string, string>
  client: { publicId: string; name: string }
  adminMail: string
  password: string
  /** Códigos de recuperación sin gastar (el primero ya se usó para la sesión por API). */
  recovery: string[]
  clientApi: Record<string, string>
  legalVersion: string
}

/** Un cliente con contrato y tarifa y una persona administradora con MFA inscrito (por API) y el documento legal aceptado; deja un código de recuperación para entrar por la pantalla. */
export async function createClientWithAdmin(request: APIRequestContext, suffix: string, label: string): Promise<ClientWithAdmin & { locations: { post: (name: string) => Promise<{ publicId: string; id: number }> } }> {
  const staff = await staffHeaders(request)
  const day = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10)
  const client = await post<{ publicId: string; name: string; currentContract?: { publicId?: string } | null }>(request, '/api/v1/clients', staff, { name: `${label} ${suffix}`, contract: { startDate: day, title: 'Contrato de prueba' } })
  const contractId = client.currentContract?.publicId ?? ''
  await post(request, `/api/v1/contracts/${contractId}/rate-components`, staff, { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 10, effectiveFrom: day })
  await post(request, `/api/v1/contracts/${contractId}/status`, staff, { toCode: 'ACTIVE' })
  const password = `C117_${suffix}_Clave-2026!`
  const adminMail = `c117.admin.${suffix.toLowerCase()}@demo.test`
  const invite = await post<{ inviteToken?: string }>(request, `/api/v1/clients/${client.publicId}/portal-users/invite`, staff, { email: adminMail, fullName: 'Ana Admin', role: 'CLIENT_ADMIN' })
  expect(invite.inviteToken, 'Portal:ReturnInviteTokenInResponse está apagado en este API').toBeTruthy()
  expect((await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email: adminMail, token: invite.inviteToken, password } })).status()).toBe(204)
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
  return {
    staff, client, adminMail, password, recovery: recovery.slice(1), clientApi, legalVersion: legal.currentVersion,
    locations: { post: (name: string) => post<{ publicId: string; id: number }>(request, '/api/v1/locations', staff, { clientPublicId: client.publicId, name, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' }) },
  }
}

/** Entra por la pantalla como la persona del cliente (computadora nueva → confirmar → código de recuperación) y espera el inicio. */
export async function loginAsClientAdmin(page: Page, c: ClientWithAdmin) {
  await loginConfirmingTheComputer(page, c.adminMail, c.password)
  await expect(page.getByRole('heading', { name: 'Código de seguridad' })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Código').fill(c.recovery[0])
  await page.getByRole('button', { name: 'Verificar' }).click()
  await expect(page.getByRole('heading', { name: 'Hola, Ana' })).toBeVisible({ timeout: 30_000 })
}

/** Un acceso de consignatario ya activo (invitación por API, aceptada, computadora confirmada y documento legal aceptado): devuelve su correo, su contraseña y sus encabezados de API. */
export async function createConsigneeAccess(request: APIRequestContext, c: ClientWithAdmin, locationPublicId: string, tag: string) {
  const mail = `c117.${tag.toLowerCase()}@demo.test`
  const invited = await post<{ inviteToken?: string; access: { id: number } }>(request, `/api/v1/portal/consignee-access/${locationPublicId}`, c.clientApi, { email: mail, fullName: 'Doña Tienda' })
  expect(invited.inviteToken).toBeTruthy()
  expect((await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email: mail, token: invited.inviteToken, password: c.password } })).status()).toBe(204)
  const browserToken = (crypto.randomUUID() + crypto.randomUUID()).replace(/-/g, '')
  await confirmBrowserByApi(request, mail, c.password, browserToken)
  const login = (await (await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email: mail, password: c.password, trustedBrowserToken: browserToken } })).json()) as AuthResultDto
  expect(login.status).toBe('ok')
  const api = { Authorization: `Bearer ${login.tokens?.accessToken}` }
  await post(request, '/api/v1/legal/accept', api, { version: c.legalVersion, lang: 'es' })
  return { mail, api }
}
