// Recorrido del informe "Productos por posición" contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics", almacén ALM-01).
// Siembra por API: un producto con código de barras (SKU <SUF>-1) y dos posiciones en la zona RSV de ALM-01 (<SUF>-A1 en el
// pasillo A y <SUF>-B2 en el pasillo B); un ajuste (+5, FOUND) mete el producto en A1. <SUF> son solo letras.
// Recorrido: Ubicaciones (ALM-01) → filtro Posición = <SUF> → "Productos por posición" (filtro actual) → "Generar PDF" → se
// descarga un PDF válido con A1, el SKU y el código de barras (B2, vacía, no sale). Filtro Pasillo = B → solo B2. Casillas:
// marcar una posición e imprimir solo esa. Móvil (360 px): el mismo recorrido en tarjetas, sin scroll horizontal de página.
// Con PRODPOS_PDF_DIR, guarda ahí los PDF.
import { readFileSync } from 'node:fs'
import { constants, inflateSync } from 'node:zlib'
import { expect, test, type APIRequestContext, type Download, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
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
  const suffix = `PP${variant}${LETTERS}`
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const sku = `${suffix}-1`
  const barcode = `75${String(Date.now()).slice(-10)}${'DEM'.indexOf(variant) + 1}`
  const created = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Informe ${suffix} tornillo`, trackingType: 'NONE', barcode } }),
    'producto',
  )
  const product = created.product
  const mk = async (code: string, aisle: string) => {
    const b = await ok<{ id: number; code: string }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code, aisle } }), 'posición')
    return { id: b.id, code }
  }
  const binA = await mk(`${suffix}-A1`, 'A')
  const binB = await mk(`${suffix}-B2`, 'B')
  await ok(
    request.post(`${API_URL}/api/v1/inventory/adjustments`, {
      headers,
      data: { productPublicId: product.publicId, warehousePublicId: wh, binId: binA.id, quantity: 5, reason: 'FOUND', notes: 'Recorrido productos por posición' },
    }),
    'ajuste',
  )
  return { suffix, warehousePublicId: wh, productPublicId: product.publicId, sku, barcode, binA, binB, headers }
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
  if (process.env.PRODPOS_PDF_DIR) await download.saveAs(`${process.env.PRODPOS_PDF_DIR}/${name}.pdf`)
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
  const dialog = page.getByRole('dialog', { name: 'Productos por posición' })
  const [download] = await Promise.all([page.waitForEvent('download'), dialog.getByRole('button', { name: 'Generar PDF' }).click()])
  await expect(dialog).toHaveCount(0)
  return download
}

async function flow(page: Page, s: Seed, mobile: boolean) {
  await openLocations(page, s)
  // ya no hay estado de hoja: ni columna, ni filtro, ni aviso
  await expect(page.getByText(/desactualizada/i)).toHaveCount(0)
  if (mobile) await expectNoHorizontalScroll(page)

  // imprimir las del filtro actual (A1 y B2; B2 vacía no sale)
  await page.getByRole('button', { name: 'Productos por posición' }).click()
  const dialog = page.getByRole('dialog', { name: 'Productos por posición' })
  await expect(dialog.getByLabel('Las posiciones del filtro actual (2)')).toBeChecked()
  await expect(dialog.getByRole('switch', { name: 'Incluir posiciones vacías' })).not.toBeChecked()
  if (mobile) await expectNoHorizontalScroll(page)
  const download = await generate(page)
  expect(download.suggestedFilename()).toMatch(/^productos-por-posicion-advance-logistics-\d{4}-\d{2}-\d{2}\.pdf$/)
  const text = await readPdf(download, `productos-${s.suffix}`)
  // desde 2026-10-10 el PDF usa el formato de Códigos de barras: cada posición abre con su encabezado «Posición {código} · Zona … — N producto(s)»
  expect(text).toContain(`Posición ${s.binA.code}`)
  expect(text).toContain(`(${s.sku}) Tj`)
  expect(text).toContain(`(${s.barcode}) Tj`)
  expect(text).toContain('Zona RSV')
  expect(text).not.toContain(`(${s.binB.code}) Tj`)

  // filtro Pasillo = B: solo B2 (vacía) en la tabla
  await page.getByLabel('Pasillo', { exact: true }).fill('B')
  await expect(binRow(page, s.binA.code)).toHaveCount(0)
  await expect(binRow(page, s.binB.code)).toBeVisible()
  if (mobile) await expectNoHorizontalScroll(page)
}

test.describe('Productos por posición — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'D')
  })

  test('imprimir el filtro actual (PDF con el producto y su código de barras) y filtrar por pasillo', async ({ page }) => {
    await login(page)
    await flow(page, s, false)
  })

  test('casillas: marcar una posición e imprimir solo esa', async ({ page, request }) => {
    const m = await seed(request, 'E')
    await login(page)
    await openLocations(page, m)
    await binRow(page, m.binA.code).getByRole('checkbox', { name: `Marcar la posición ${m.binA.code}` }).check()
    await expect(page.getByText('1 marcada')).toBeVisible()
    await page.getByRole('button', { name: 'Productos por posición' }).click()
    await expect(page.getByRole('dialog', { name: 'Productos por posición' }).getByLabel('Las posiciones marcadas (1)')).toBeChecked()
    const text = await readPdf(await generate(page), `marcadas-${m.suffix}`)
    expect(text).toContain(`Posición ${m.binA.code}`)
  })
})

test.describe('Productos por posición — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (!testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'M')
  })

  test('el mismo recorrido en tarjetas, sin scroll horizontal (lista y modal)', async ({ page }) => {
    await login(page)
    await flow(page, s, true)
  })
})
