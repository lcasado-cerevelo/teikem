// Recorrido del Lote F15 (hojas de posición) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics", almacén ALM-01).
// Siembra por API: un producto con código de barras (SKU <SUF>-1) y dos posiciones en la zona RSV de ALM-01 (<SUF>-A1 y
// <SUF>-B2); un ajuste (+5, FOUND) mete el producto en A1. <SUF> son solo letras.
// Recorrido: Ubicaciones (ALM-01) → filtro Posición = <SUF> → A1 "Sin hoja impresa", B2 "—" y el aviso "1 posición con la
// hoja desactualizada o sin imprimir" → "Hojas de posición" (filtro actual) → "Generar PDF" → se descarga un PDF válido con
// A1, el SKU y el código de barras (B2, vacía, no sale) → A1 "Al día" (y el API dice CURRENT). Luego se transfiere todo
// de A1 a B2 por API → A1 "Desactualizada", B2 "Sin hoja impresa" y el aviso "2 posiciones…" → "Imprimir las
// desactualizadas" con "Incluir posiciones vacías" → A1 (vacía) y B2 quedan al día ("—" y "Al día").
// Móvil (360 px): el mismo recorrido en tarjetas, sin scroll horizontal de página (lista y modal).
// Capturas para el manual: docs/manual/frontend/img/f15-<paso>.png (escritorio, y una del celular). Con F15_PDF_DIR, guarda
// ahí los PDF.
import { mkdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { constants, inflateSync } from 'node:zlib'
import { expect, test, type APIRequestContext, type Download, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-7)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR', acceptDownloads: true })

interface Seed {
  suffix: string
  warehousePublicId: string
  productPublicId: string
  sku: string
  barcode: string
  binA: { id: number; code: string }
  binB: { id: number; code: string }
  headers: Record<string, string>
}

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f15-${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que lote14/F14). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      if (el.parentElement?.closest('.dt-scroll')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      // las casillas y radios nativos ocultos del interruptor (.sw input) miden su caja, no se ven
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

/** Siembra por API. `variant` separa escritorio y móvil (letra inicial del sufijo). */
async function seed(request: APIRequestContext, variant: 'D' | 'E' | 'M'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const suffix = `HS${variant}${LETTERS}`
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const sku = `${suffix}-1`
  const barcode = `75${String(Date.now()).slice(-10)}${'DEM'.indexOf(variant) + 1}`
  const created = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Hoja ${suffix} tornillo`, trackingType: 'NONE', barcode } }),
    'producto',
  )
  const product = created.product
  const mk = async (code: string) => {
    const b = await ok<{ id: number; code: string }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code } }), 'posición')
    return { id: b.id, code }
  }
  const binA = await mk(`${suffix}-A1`)
  const binB = await mk(`${suffix}-B2`)
  await ok(
    request.post(`${API_URL}/api/v1/inventory/adjustments`, {
      headers,
      data: { productPublicId: product.publicId, warehousePublicId: wh, binId: binA.id, quantity: 5, reason: 'FOUND', notes: 'Recorrido F15' },
    }),
    'ajuste',
  )
  return { suffix, warehousePublicId: wh, productPublicId: product.publicId, sku, barcode, binA, binB, headers }
}

async function sheetStatus(request: APIRequestContext, s: Seed, binId: number): Promise<string> {
  const page = await ok<{ items: { sheetStatus: string }[] }>(
    request.get(`${API_URL}/api/v1/warehouses/${s.warehousePublicId}/bins`, { headers: s.headers, params: { binIds: binId } }),
    'posición',
  )
  return page.items[0].sheetStatus
}

/** Texto de todos los flujos del PDF (los descomprime). */
function pdfText(bytes: Buffer): string {
  const raw = bytes.toString('latin1')
  let out = raw
  for (const m of raw.matchAll(/stream\r?\n([\s\S]*?)\r?\nendstream/g)) {
    try {
      out += '\n' + inflateSync(Buffer.from(m[1], 'latin1'), { finishFlush: constants.Z_SYNC_FLUSH }).toString('latin1')
    } catch {
      // flujo sin comprimir o de imagen: ya está en `raw`
    }
  }
  return out
}

async function readPdf(download: Download, name: string): Promise<string> {
  const bytes = readFileSync((await download.path())!)
  expect(bytes.subarray(0, 5).toString('latin1')).toBe('%PDF-')
  expect(bytes.subarray(-8).toString('latin1')).toContain('%%EOF')
  if (process.env.F15_PDF_DIR) await download.saveAs(`${process.env.F15_PDF_DIR}/${name}.pdf`)
  return pdfText(bytes)
}

/** Fila (escritorio) o tarjeta (móvil) de una posición. */
const binRow = (page: Page, code: string) => page.locator('tr, li.dt-card').filter({ hasText: code })

async function openLocations(page: Page, s: Seed) {
  await page.goto(`/warehouse/locations?warehouse=${s.warehousePublicId}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Posiciones' })).toBeVisible()
  await page.getByLabel('Posición', { exact: true }).fill(s.suffix)
  await expect(binRow(page, s.binA.code)).toBeVisible()
  await expect(binRow(page, s.binB.code)).toBeVisible()
}

async function generate(page: Page): Promise<Download> {
  const dialog = page.getByRole('dialog', { name: 'Hojas de posición' })
  const [download] = await Promise.all([page.waitForEvent('download'), dialog.getByRole('button', { name: 'Generar PDF' }).click()])
  await expect(dialog).toHaveCount(0)
  return download
}

async function flow(page: Page, request: APIRequestContext, s: Seed, shots: boolean, mobile: boolean) {
  await openLocations(page, s)
  // estado inicial: A1 con producto y nunca impresa; B2 vacía ("—"); el aviso acumulado cuenta 1
  await expect(binRow(page, s.binA.code).getByText('Sin hoja impresa')).toBeVisible()
  await expect(binRow(page, s.binB.code).getByLabel('Sin productos: no necesita hoja')).toBeVisible()
  await expect(page.locator('.loc-stale')).toContainText('1 posición con la hoja desactualizada o sin imprimir (con los filtros actuales)')
  if (shots) await shot(page, 'ubicaciones-sin-hoja')
  if (mobile) await expectNoHorizontalScroll(page)

  // imprimir las del filtro actual (A1 y B2; B2 vacía no sale)
  await page.getByRole('button', { name: 'Hojas de posición' }).click()
  const dialog = page.getByRole('dialog', { name: 'Hojas de posición' })
  await expect(dialog.getByLabel('Las posiciones del filtro actual (2)')).toBeChecked()
  await expect(dialog.getByRole('switch', { name: 'Incluir posiciones vacías' })).not.toBeChecked()
  if (shots) await shot(page, 'modal')
  if (mobile) await expectNoHorizontalScroll(page)
  const download = await generate(page)
  expect(download.suggestedFilename()).toMatch(/^hojas-de-posicion-advance-logistics-\d{4}-\d{2}-\d{2}\.pdf$/)
  const text = await readPdf(download, `hojas-${s.suffix}`)
  expect(text).toContain(`(${s.binA.code}) Tj`)
  expect(text).toContain(`(${s.sku}) Tj`)
  expect(text).toContain(`(${s.barcode}) Tj`)
  expect(text).toContain('Impresa el')
  expect(text).toContain('Zona RSV')
  expect(text).not.toContain(`(${s.binB.code}) Tj`)

  // solo DESPUÉS del PDF: A1 queda "Al día" (pantalla y API); B2 (no impresa) sigue vacía
  await expect(binRow(page, s.binA.code).getByText('Al día')).toBeVisible()
  await expect(binRow(page, s.binA.code).getByText(/^Impresa /)).toBeVisible()
  await expect(page.locator('.loc-stale')).toContainText('Todas las hojas de posición de esta lista están al día.')
  expect(await sheetStatus(request, s, s.binA.id)).toBe('CURRENT')
  expect(await sheetStatus(request, s, s.binB.id)).toBe('EMPTY')
  if (shots) await shot(page, 'al-dia')

  // mover TODO el producto de A1 a B2 (transferencia por API): A1 desactualizada (quedó vacía), B2 sin hoja
  await ok(
    request.post(`${API_URL}/api/v1/inventory/transfers`, {
      headers: s.headers,
      data: { productPublicId: s.productPublicId, fromWarehousePublicId: s.warehousePublicId, fromBinId: s.binA.id, toWarehousePublicId: s.warehousePublicId, toBinId: s.binB.id, quantity: 5, notes: 'Recorrido F15' },
    }),
    'transferencia',
  )
  await openLocations(page, s)
  await expect(binRow(page, s.binA.code).getByText('Desactualizada')).toBeVisible()
  await expect(binRow(page, s.binB.code).getByText('Sin hoja impresa')).toBeVisible()
  await expect(page.locator('.loc-stale')).toContainText('2 posiciones con la hoja desactualizada o sin imprimir (con los filtros actuales)')
  // sin ventanas emergentes por el movimiento: solo la insignia y el contador
  await expect(page.getByRole('dialog')).toHaveCount(0)
  if (shots) await shot(page, 'desactualizada')
  if (mobile) {
    await expectNoHorizontalScroll(page)
    // captura del celular para el manual: la tarjeta de la posición con su insignia y la última impresión
    await binRow(page, s.binA.code).scrollIntoViewIfNeeded()
    await shot(page, 'movil-desactualizada')
  }

  // "Imprimir las desactualizadas" con las vacías: A1 (vacía, hoja "Sin productos") y B2
  await page.getByRole('button', { name: 'Imprimir las desactualizadas' }).click()
  await expect(dialog.getByLabel('Solo las desactualizadas o sin hoja (2)')).toBeChecked()
  await dialog.getByRole('switch', { name: 'Incluir posiciones vacías' }).check()
  const second = await generate(page)
  const text2 = await readPdf(second, `desactualizadas-${s.suffix}`)
  expect(text2).toContain(`(${s.binA.code}) Tj`)
  expect(text2).toContain('(Sin productos) Tj')
  expect(text2).toContain(`(${s.binB.code}) Tj`)
  await expect(binRow(page, s.binB.code).getByText('Al día')).toBeVisible()
  await expect(binRow(page, s.binA.code).getByLabel('Sin productos: no necesita hoja')).toBeVisible()
  expect(await sheetStatus(request, s, s.binA.id)).toBe('EMPTY')
  expect(await sheetStatus(request, s, s.binB.id)).toBe('CURRENT')
}

test.describe('Lote F15 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'D')
  })

  test('hojas de posición: sin hoja → imprimir (PDF) → al día → mover → desactualizada → imprimir las desactualizadas', async ({ page, request }) => {
    await login(page)
    await flow(page, request, s, true, false)
  })

  test('casillas: marcar una posición e imprimir solo esa', async ({ page, request }) => {
    const m = await seed(request, 'E')
    await login(page)
    await openLocations(page, m)
    await binRow(page, m.binA.code).getByRole('checkbox', { name: `Marcar la posición ${m.binA.code}` }).check()
    await expect(page.getByText('1 marcada')).toBeVisible()
    await page.getByRole('button', { name: 'Hojas de posición' }).click()
    await expect(page.getByRole('dialog', { name: 'Hojas de posición' }).getByLabel('Las posiciones marcadas (1)')).toBeChecked()
    const text = await readPdf(await generate(page), `marcadas-${m.suffix}`)
    expect(text).toContain(`(${m.binA.code}) Tj`)
    await expect(binRow(page, m.binA.code).getByText('Al día')).toBeVisible()
    await expect(page.getByText('0 marcadas')).toBeVisible()
  })
})

test.describe('Lote F15 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (!testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'M')
  })

  test('el mismo recorrido en tarjetas, sin scroll horizontal (lista y modal)', async ({ page, request }) => {
    await login(page)
    await flow(page, request, s, false, true)
  })
})
