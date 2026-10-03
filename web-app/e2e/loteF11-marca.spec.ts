// Recorrido del lote F11 (marca por compañía en el servidor) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics"). CAMBIA la marca de toda la compañía demo (sube logos y un tema) y la
// RESTAURA al final (y en afterAll por API, aunque falle): corre en su propio proyecto (`escritorio-f11`, después de todos los
// demás; ver playwright.config.ts).
// - sube un logo (lockup, normal e invertido) y lo ve en la barra lateral; cambia de tema y cambia de variante;
// - 415 (no es una imagen), 400 (SVG con <script>) y el tope de 512 KB salen junto a la ranura, con el mensaje del servidor;
// - una marca inválida por API (contraste, acentos parecidos, color de estado) da 400 y no cambia la guardada;
// - guarda un tema predefinido, lo ve aplicado, quita los logos y deja la marca como estaba.
// Capturas para el manual: docs/manual/frontend/img/f11-<pantalla>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type TenantSettingsDto = components['schemas']['TenantSettingsDto']
type BrandLogoDto = components['schemas']['BrandLogoDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const SLOTS = ['lockup', 'lockup-inverted', 'mark', 'mark-inverted']

test.use({ locale: 'es-PR' })
test.describe.configure({ mode: 'serial' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

const svg = (fill: string, label: string) =>
  Buffer.from(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 64"><rect width="240" height="64" rx="12" fill="${fill}"/>` +
      `<text x="120" y="40" font-family="Arial" font-size="22" font-weight="700" text-anchor="middle" fill="#ffffff">${label}</text></svg>`,
  )
const SVG_LIGHT = svg('#0E7C86', 'ADVANCE')
const SVG_DARK = svg('#E2622C', 'ADVANCE')
const SVG_MARK = Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><circle cx="32" cy="32" r="30" fill="#0E7C86"/><path d="M20 44 32 16l12 28z" fill="#fff"/></svg>')
const SVG_WITH_SCRIPT = Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><rect width="10" height="10"/></svg>')

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f11-${name}.png`, animations: 'disabled', caret: 'hide', fullPage: true })
}

/** Captura solo la sección de logos (la pantalla scrolla dentro de su escenario y la captura de página la corta). */
async function shotLogos(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  const logos = page.locator('.set-logos')
  await logos.scrollIntoViewIfNeeded()
  await logos.screenshot({ path: `${IMG_DIR}f11-${name}.png`, animations: 'disabled', caret: 'hide' })
}

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login', { timeout: 30_000 })
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
}

async function apiToken(request: APIRequestContext): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  const body = (await res.json()) as AuthResultDto
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

/** Deja la compañía demo sin logos y sin marca propia (lo que había antes del recorrido), por API. */
async function restoreBrand(request: APIRequestContext) {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const list = (await (await request.get(`${API_URL}/api/v1/tenant/brand/logos`, { headers })).json()) as BrandLogoDto[]
  for (const l of list) await request.delete(`${API_URL}/api/v1/tenant/brand/logos/${l.slot}`, { headers })
  const res = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers, data: { brandingJson: '' } })
  expect(res.ok()).toBeTruthy()
}

async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
}

test.beforeAll(async ({ request }) => {
  await restoreBrand(request)
})
test.afterAll(async ({ request }) => {
  await restoreBrand(request)
})

test('Marca: sube el logo, se ve en la barra lateral por tema, y los rechazos del servidor salen junto a la ranura', async ({ page }) => {
  await login(page)
  const lockupInSidebar = page.locator('.rbrand [data-testid="brand-lockup"]')
  await expect(lockupInSidebar).not.toHaveAttribute('data-company-logo', 'true')   // sin logos: el de Teikem

  await page.goto('/system/settings?tab=brand')
  await expect(page.getByRole('heading', { name: 'Colores y logo de la compañía' })).toBeVisible()
  await expect(page.getByText('Sin logo: se usa el de Teikem')).toHaveCount(4)

  // 415: un archivo que dice ser PNG pero es texto
  const lockupCard = page.getByTestId('logo-lockup')
  const lockupInput = page.getByLabel('Archivo de Lockup (fondo claro)')
  await lockupInput.setInputFiles({ name: 'falso.png', mimeType: 'image/png', buffer: Buffer.from('esto no es una imagen') })
  await expect(lockupCard.getByRole('alert')).toHaveText('Formato no admitido: el logo debe ser SVG, PNG, JPG o WebP.')
  // 400: SVG con contenido activo
  await lockupInput.setInputFiles({ name: 'malo.svg', mimeType: 'image/svg+xml', buffer: SVG_WITH_SCRIPT })
  await expect(lockupCard.getByRole('alert')).toHaveText('El SVG no se acepta: contiene el elemento <script>, que puede ejecutar código o cargar contenido externo.')
  // más de 512 KB: lo avisa la pantalla antes de mandarlo (y el servidor lo rechaza igual: ver la prueba por API abajo)
  await lockupInput.setInputFiles({ name: 'enorme.png', mimeType: 'image/png', buffer: Buffer.alloc(512 * 1024 + 1) })
  await expect(lockupCard.getByRole('alert')).toHaveText('El logo supera el tamaño máximo de 512 KB.')
  await shotLogos(page, 'marca-errores')

  // logo bueno: normal (fondo claro) e invertido (fondo oscuro)
  await lockupInput.setInputFiles({ name: 'advance.svg', mimeType: 'image/svg+xml', buffer: SVG_LIGHT })
  await expect(lockupCard.getByRole('alert')).toHaveCount(0)
  await expect(lockupCard.getByRole('img', { name: 'Vista previa de Lockup (fondo claro)' })).toBeVisible()
  await expect(lockupCard.getByText('SVG ·')).toBeVisible()
  await page.getByLabel('Archivo de Lockup (fondo oscuro)').setInputFiles({ name: 'advance-inv.svg', mimeType: 'image/svg+xml', buffer: SVG_DARK })
  await expect(page.getByTestId('logo-lockup-inverted').getByRole('img')).toBeVisible()
  await page.getByLabel('Archivo de Marca cuadrada (fondo claro)').setInputFiles({ name: 'marca.svg', mimeType: 'image/svg+xml', buffer: SVG_MARK })
  await expect(page.getByTestId('logo-mark').getByRole('img')).toBeVisible()

  // la barra lateral usa el logo de la compañía, y cambia de variante con el tema
  await expect(lockupInSidebar).toHaveAttribute('data-company-logo', 'true')
  await expect(lockupInSidebar).toHaveAttribute('alt', 'Advance Logistics')
  await page.getByRole('button', { name: 'Oscuro', exact: true }).click()
  const darkSrc = await lockupInSidebar.getAttribute('src')
  expect(darkSrc).toMatch(/^blob:/)
  await page.getByRole('button', { name: 'Claro', exact: true }).click()
  await expect.poll(async () => lockupInSidebar.getAttribute('src')).not.toBe(darkSrc)
  await page.getByRole('button', { name: 'Oscuro', exact: true }).click()
  await shotLogos(page, 'marca-logos')

  // sobrevive a recargar: el logo viene del servidor, no de la memoria de la pantalla
  await page.reload()
  await expect(page.locator('.rbrand [data-testid="brand-lockup"]')).toHaveAttribute('data-company-logo', 'true')
  await page.goto('/')
  await expect(page.locator('.rbrand [data-testid="brand-lockup"]')).toHaveAttribute('data-company-logo', 'true')
  await shot(page, 'barra-lateral')

  // quitar: la ranura vuelve al respaldo y, sin ningún lockup, la barra vuelve al de Teikem
  await page.goto('/system/settings?tab=brand')
  await page.getByRole('button', { name: 'Quitar Lockup (fondo claro)' }).click()
  await expect(page.getByTestId('logo-lockup').getByText('Sin logo: se usa el de Teikem')).toBeVisible()
  await page.getByRole('button', { name: 'Quitar Lockup (fondo oscuro)' }).click()
  await expect(page.getByTestId('logo-lockup-inverted').getByText('Sin logo: se usa el de Teikem')).toBeVisible()
  await expect(page.locator('.rbrand [data-testid="brand-lockup"]')).not.toHaveAttribute('data-company-logo', 'true')
})

test('Marca por API: una marca inválida da 400 con el mensaje exacto y no cambia la guardada; los logos piden permiso y validan el archivo', async ({ request }) => {
  await restoreBrand(request)   // el recorrido anterior deja la marca cuadrada subida
  const token = await apiToken(request)
  const headers = { Authorization: `Bearer ${token}` }
  const put = (brandingJson: string) => request.put(`${API_URL}/api/v1/tenant/settings`, { headers, data: { brandingJson } })

  const ok = await put('{"preset":"selva","useCustom":false}')
  expect(ok.status()).toBe(200)
  const saved = ((await ok.json()) as TenantSettingsDto).brandingJson

  const cases: [string, string][] = [
    ['{"useCustom":true,"custom":{"flow":"#000000"}}', 'El contraste del color de operación en modo oscuro es 1.34:1; el mínimo es 4.5:1.'],
    ['{"useCustom":true,"custom":{"flow":"#1F6FE5","money":"#2060E0"}}', 'Los colores de operación y de dinero son demasiado parecidos: 4° de separación y el mínimo es 40°.'],
    ['{"danger":"#00FF00"}', "Los colores de estado (ok, warn, danger, info) no se pueden personalizar: 'danger'."],
    ['{"preset":"neon"}', "El tema predefinido 'neon' no existe."],
    ['{', 'La marca no es un JSON válido.'],
  ]
  for (const [json, message] of cases) {
    const res = await put(json)
    expect(res.status(), json).toBe(400)
    const body = (await res.json()) as { title: string; errors?: Record<string, string[]> }
    expect(body.title).toBe(message)
    expect(body.errors?.brandingJson).toEqual([message])
  }
  const after = (await (await request.get(`${API_URL}/api/v1/tenant/settings`, { headers })).json()) as TenantSettingsDto
  expect(after.brandingJson).toBe(saved)

  // logos por API: sin sesión 401; archivo de más de 512 KB 413; no imagen 415; SVG con manejador 400
  const upload = (slot: string, name: string, mimeType: string, buffer: Buffer, h: Record<string, string> = headers) =>
    request.put(`${API_URL}/api/v1/tenant/brand/logos/${slot}`, { headers: h, multipart: { file: { name, mimeType, buffer } } })
  expect((await upload('mark', 'x.svg', 'image/svg+xml', SVG_MARK, {})).status()).toBe(401)
  const big = await upload('mark', 'grande.png', 'image/png', Buffer.alloc(525_000, 0x20))
  expect(big.status()).toBe(413)
  expect(((await big.json()) as { title: string }).title).toBe('El logo supera el tamaño máximo de 512 KB.')
  const fake = await upload('mark', 'falso.png', 'image/png', Buffer.from('texto'))
  expect(fake.status()).toBe(415)
  expect(((await fake.json()) as { title: string }).title).toBe('Formato no admitido: el logo debe ser SVG, PNG, JPG o WebP.')
  const handler = await upload('mark', 'x.svg', 'image/svg+xml', Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"/>'))
  expect(handler.status()).toBe(400)
  expect(((await handler.json()) as { title: string }).title).toBe("El SVG no se acepta: el atributo 'onload' ejecuta código.")
  expect((await request.get(`${API_URL}/api/v1/tenant/brand/logos/mark`, { headers })).status()).toBe(404)

  // un logo bueno se sirve con nosniff y una CSP restrictiva
  expect((await upload('mark', 'marca.svg', 'image/svg+xml', SVG_MARK)).status()).toBe(200)
  const got = await request.get(`${API_URL}/api/v1/tenant/brand/logos/mark`, { headers })
  expect(got.status()).toBe(200)
  expect(got.headers()['x-content-type-options']).toBe('nosniff')
  expect(got.headers()['content-security-policy']).toContain("default-src 'none'")
  expect(got.headers()['content-type']).toContain('image/svg+xml')
  expect(Buffer.from(await got.body()).equals(SVG_MARK)).toBe(true)
  for (const slot of SLOTS) await request.delete(`${API_URL}/api/v1/tenant/brand/logos/${slot}`, { headers })
})

test('Marca: guarda un tema predefinido, se aplica a toda la app, y vuelve a la marca de siempre', async ({ page, request }) => {
  await login(page)
  await page.goto('/system/settings?tab=brand')
  await page.getByRole('button', { name: /Bosque/ }).click()
  await expect(page.getByTestId('brand-checks')).toContainText('La combinación pasa las validaciones')
  await page.getByRole('button', { name: 'Guardar cambios' }).click()
  await expect(page.getByText('Marca guardada')).toBeVisible()
  await page.reload()
  await expect.poll(() => page.evaluate(() => document.documentElement.style.getPropertyValue('--flow'))).toBe('#1E8E5A')
  await shot(page, 'marca-tema')

  // a 360 px la pestaña no desborda
  await page.setViewportSize({ width: 360, height: 780 })
  await page.goto('/system/settings?tab=brand')
  await expect(page.getByRole('heading', { name: 'Colores y logo de la compañía' })).toBeVisible()
  await page.waitForLoadState('networkidle')
  await expectNoHorizontalScroll(page)

  // restaurar por API y comprobar que la pantalla lo recoge
  await restoreBrand(request)
  await page.reload()
  await expect.poll(() => page.evaluate(() => document.documentElement.style.getPropertyValue('--flow'))).toBe('')
})
