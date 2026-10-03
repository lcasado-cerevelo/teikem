// Recorrido del Lote F13 (crear un conteo por producto desde la web) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics", almacén ALM-01).
// Siembra por API: un producto con existencia en una posición propia del recorrido y otro sin existencia.
// Recorrido: Almacén → Conteo cíclico → "Nuevo conteo" → "Por producto" → producto sin existencia (el 400 del servidor sale bajo el
// selector y el modal sigue abierto) → producto con existencia → el conteo se crea con origen Producto, se abre en el panel y se ve
// en la lista; el API lo confirma (origen PRODUCT, una línea). Móvil (360 px): el modal sin scroll horizontal de página.
// Capturas para el manual: docs/manual/frontend/img/f13-<pantalla>.png (solo escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type CycleCountDetailDto = components['schemas']['CycleCountDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const STAMP = String(Date.now())
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const NO_STOCK = 'Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.'

test.use({ locale: 'es-PR' })

interface Seed {
  skuStock: string
  skuEmpty: string
  binCode: string
}

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f13-${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que lote14). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      if (el.parentElement?.closest('.dt-scroll')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      const r = el.getBoundingClientRect()
      if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')} [${Math.round(r.left)}, ${Math.round(r.right)}]`)
    })
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

/** Un producto con existencia en una posición propia del recorrido (zona RSV de ALM-01) y otro sin existencia. `variant` separa escritorio y móvil. */
async function seed(request: APIRequestContext, variant: 'd' | 'm'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const ok = async <T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> => {
    const res = await p
    expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
    return (await res.json()) as T
  }
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const suffix = `${variant}${STAMP.slice(-7)}`.toUpperCase()
  const binCode = `F13-${suffix}`
  const bin = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code: binCode } }), 'posición')
  const product = async (sku: string, name: string) =>
    (await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name, trackingType: 'NONE', purchaseCost: 1 } }), 'producto')).product.publicId
  const skuStock = `F13${suffix}-A`
  const skuEmpty = `F13${suffix}-B`
  const pStock = await product(skuStock, `Tornillo web ${suffix}`)
  await product(skuEmpty, `Tuerca sin existencia ${suffix}`)
  await ok(
    request.post(`${API_URL}/api/v1/inventory/adjustments`, {
      headers,
      data: { productPublicId: pStock, warehousePublicId: wh, binId: bin.id, quantity: 7, reason: 'FOUND', notes: 'e2e lote F13' },
    }),
    'ajuste',
  )
  return { skuStock, skuEmpty, binCode }
}

async function openCreate(page: Page): Promise<Locator> {
  await page.goto('/warehouse/cycle-counts')
  await expect(page.getByRole('heading', { level: 1, name: 'Conteo cíclico' })).toBeVisible()
  await page.getByRole('button', { name: 'Nuevo conteo' }).click()
  const dialog = page.getByRole('dialog', { name: 'Nuevo conteo' })
  await expect(dialog.getByRole('tab', { name: 'Por posiciones' })).toHaveAttribute('aria-selected', 'true')
  return dialog
}

async function pickWarehouse(page: Page, dialog: Locator) {
  const box = dialog.getByRole('combobox', { name: /^Almacén/ })
  await box.click()
  await box.fill('ALM-01')
  await page.getByRole('option', { name: /ALM-01/ }).first().click()
  await expect(box).toHaveValue(/ALM-01/)
}

async function pickProduct(page: Page, dialog: Locator, sku: string) {
  const box = dialog.getByRole('combobox', { name: /^Producto/ })
  await box.click()
  await box.fill(sku)
  await page.getByRole('option', { name: new RegExp(`^${sku} · `) }).click()
}

test.describe('Lote F13 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'd')
  })

  test('crear un conteo por producto: el 400 sin existencia junto al selector y, con existencia, el conteo queda abierto y en la lista', async ({ page, request }) => {
    await login(page)
    const dialog = await openCreate(page)
    await shot(page, 'nuevo-conteo-posiciones')
    await dialog.getByRole('tab', { name: 'Por producto' }).click()
    await expect(dialog.getByRole('combobox', { name: /^Producto/ })).toBeVisible()
    await expect(dialog.getByText('Zonas', { exact: true })).toHaveCount(0)

    // sin almacén ni producto: validación del formulario, sin llamar al API
    await dialog.getByRole('button', { name: 'Crear conteo' }).click()
    await expect(dialog.getByText('Elija el producto.')).toBeVisible()

    // producto sin existencia: el 400 del servidor sale junto al selector y el modal sigue abierto
    await pickWarehouse(page, dialog)
    await pickProduct(page, dialog, s.skuEmpty)
    await dialog.getByRole('button', { name: 'Crear conteo' }).click()
    await expect(dialog.getByText(NO_STOCK)).toBeVisible()
    await shot(page, 'sin-existencia')

    // producto con existencia: se crea, se abre y aparece en la lista con origen Producto
    await pickProduct(page, dialog, s.skuStock)
    await expect(dialog.getByText(NO_STOCK)).toHaveCount(0)
    await shot(page, 'por-producto')
    await dialog.getByRole('button', { name: 'Crear conteo' }).click()
    await expect(dialog).toHaveCount(0)
    await expect(page).toHaveURL(/count=\d+/)
    const id = Number(new URL(page.url()).searchParams.get('count'))
    const detail = page.locator('.cc-detail')
    await expect(detail.getByText(s.skuStock).first()).toBeVisible()
    await expect(detail.getByText(s.binCode).first()).toBeVisible()
    await expect(page.locator('.toast').filter({ hasText: /creado con 1 línea\(s\)\./ }).first()).toBeVisible()
    await shot(page, 'conteo-creado')

    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const created = (await (await request.get(`${API_URL}/api/v1/cycle-counts/${id}`, { headers })).json()) as CycleCountDetailDto
    expect(created.count?.originCode).toBe('PRODUCT')
    expect(created.count?.statusCode).toBe('OPEN')
    expect(created.lines).toHaveLength(1)
    expect(created.lines![0].sku).toBe(s.skuStock)

    // se ve en la lista (buscador libre por SKU)
    await page.getByRole('searchbox').first().fill(s.skuStock)
    await expect(page.locator('.cc-list').getByText(created.count!.number!).first()).toBeVisible()
  })
})

test.describe('Lote F13 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (!testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'm')
  })

  test('el modal con las dos opciones y el conteo creado, sin scroll horizontal de página', async ({ page }) => {
    await login(page)
    const dialog = await openCreate(page)
    await expectNoHorizontalScroll(page)
    await dialog.getByRole('tab', { name: 'Por producto' }).click()
    await pickWarehouse(page, dialog)
    await pickProduct(page, dialog, s.skuStock)
    await expectNoHorizontalScroll(page)
    await dialog.getByRole('button', { name: 'Crear conteo' }).click()
    await expect(dialog).toHaveCount(0)
    await expect(page).toHaveURL(/count=\d+/)
    await expectNoHorizontalScroll(page)
  })
})
