// Ayudantes comunes de los recorridos de la web del personal (Lotes 111–115): sesión del administrador por API y por pantalla, datos por API, capturas.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { MANUAL_LANG, manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'

export type AuthResultDto = components['schemas']['AuthResultDto']

export const API_URL = process.env.API_URL ?? 'http://localhost:5000'
export const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
export const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
export const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
export async function shot(page: Page, isMobile: boolean, name: string, height = 1000, only?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height })
  const manualShot1 = await manualTarget((only ?? page))
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
  if (size) await page.setViewportSize(size)
}

export async function apiHeaders(request: APIRequestContext): Promise<Record<string, string>> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  const headers = { Authorization: `Bearer ${body.tokens?.accessToken ?? ''}` }
  // con el documento legal exigido, el administrador de las pruebas lo acepta por API
  const status = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (status.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: status.currentVersion, lang: 'es' } })
  return headers
}

export async function login(page: Page) {
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

export async function ok<T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> {
  const res = await p
  expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
  const text = await res.text()
  return (text ? JSON.parse(text) : null) as T
}

/** "Hoy" de la compañía demo (America/Puerto_Rico) y días después, 'YYYY-MM-DD'. */
export function tenantDay(plus = 0): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Puerto_Rico', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  const d = new Date(`${today}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + plus)
  return d.toISOString().slice(0, 10)
}

export interface TestClient {
  publicId: string
  name: string
  code: string
  contractId: string
}

/** Un cliente con contrato activo y tarifa (el cernidor es al someter la orden). */
export async function createClient(request: APIRequestContext, headers: Record<string, string>, name: string): Promise<TestClient> {
  const client = await ok<{ publicId: string; code?: string; currentContract?: { publicId?: string } | null }>(
    request.post(`${API_URL}/api/v1/clients`, { headers, data: { name, contract: { startDate: tenantDay(-30), title: 'Contrato de prueba' } } }),
    'cliente',
  )
  const contractId = client.currentContract?.publicId ?? ''
  await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 10, effectiveFrom: tenantDay(-30) } }), 'tarifa')
  await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
  return { publicId: client.publicId, name, code: client.code ?? '', contractId }
}

export async function createConsignee(request: APIRequestContext, headers: Record<string, string>, clientPublicId: string, name: string) {
  return ok<{ id: number; publicId: string }>(
    request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId, name, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' } }),
    'consignatario',
  )
}

export interface TestOrder {
  publicId: string
  orderNumber: string
  waybillNumber?: string
  pieceNumbers: string[]
}

export async function createOrder(request: APIRequestContext, headers: Record<string, string>, clientPublicId: string, consigneePublicId: string, pieces = 1): Promise<TestOrder> {
  const o = await ok<{ publicId: string; orderNumber: string; waybillNumber?: string; packages: { pieceNumbers: string[] }[] }>(
    request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId, consigneeLocationPublicId: consigneePublicId, packages: [{ pieces, description: MANUAL_LANG === 'en' ? 'Test boxes' : 'Cajas de prueba' }] } }),
    'orden',
  )
  return { publicId: o.publicId, orderNumber: o.orderNumber, waybillNumber: o.waybillNumber, pieceNumbers: o.packages.flatMap((p) => p.pieceNumbers) }
}

/** Una acción de la estación de escaneo (PICKUP, ARRIVE, ROUTE, DEPART, RETURN) sobre una pieza. */
export async function scan(request: APIRequestContext, headers: Record<string, string>, action: string, code: string) {
  return ok<Record<string, unknown>>(request.post(`${API_URL}/api/v1/scan/action`, { headers, data: { action, code } }), `escaneo ${action}`)
}

/** La ficha del cliente abre todos sus paneles cerrados (arrancan plegados cada vez que se abre): se esperan a que cargue la ficha y se despliegan todos. */
export async function openClientPanels(page: Page) {
  await page.getByRole('heading', { name: 'Direcciones', exact: true }).waitFor({ timeout: 30_000 })
  const closed = page.getByRole('button', { name: 'Abrir el panel' })
  for (let i = 0; i < 20 && (await closed.count()) > 0; i++) await closed.first().click()
}

/** Enciende un módulo de la compañía de prueba (pide verificación reciente: se reautentica con la contraseña del administrador). */
export async function enableModule(request: APIRequestContext, key: string) {
  const headers = await apiHeaders(request)
  const reauth = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers, data: { password: ADMIN.password } })
  expect(reauth.ok(), await reauth.text()).toBeTruthy()
  const token = ((await reauth.json()) as { accessToken: string }).accessToken
  const res = await request.put(`${API_URL}/api/v1/modules/${key}`, { headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() }, data: { isEnabled: true } })
  expect(res.ok(), await res.text()).toBeTruthy()
}
