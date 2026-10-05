// Recorrido del Lote F14 (reportes de códigos de barras para el conteo) contra el API real (db-init hecho, API en API_URL, por
// defecto http://localhost:5000; compañía demo "Advance Logistics", almacén ALM-01).
// Siembra por API: tres productos sin categoría con SKU <SUF>-1 / -2 / -10 y nombre "Etiqueta <SUF>", y cinco posiciones en
// la zona RSV de ALM-01 (R1-<SUF>, R01-<SUF>-B, R2-<SUF>, R10-<SUF>, GEN-<SUF>); <SUF> son solo letras (no forman grupos).
// Recorrido: Productos e inventario → filtro Nombre = "Etiqueta <SUF>" → "Códigos de barras" → el PDF descargado es válido,
// tiene exactamente los SKU que da el API con ese filtro, en orden natural (-1, -2, -10) bajo "Sin categoría (3)".
// Posiciones (/warehouse/locations, ALM-01) → filtro Posición = <SUF> → "Códigos de barras" → mismas comprobaciones con las posiciones: grupos
// "Grupo 1 (2)" (R1 y R01 juntos), "Grupo 2 (1)", "Grupo 10 (1)" y "Otras posiciones (1)" en ese orden.
// Móvil (360 px): los botones y su selector sin scroll horizontal de página, y la descarga funciona.
// Capturas para el manual: docs/manual/frontend/img/f14-<pantalla>.png (solo escritorio). Con F14_PDF_DIR, guarda ahí los PDF.
import { mkdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { constants, inflateSync } from 'node:zlib'
import { expect, test, type APIRequestContext, type Download, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras): no agrega números a los códigos. */
const LETTERS = String(Date.now())
  .slice(-7)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR', acceptDownloads: true })

interface Seed {
  suffix: string
  warehousePublicId: string
  skus: string[]
  bins: string[]
}

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f14-${name}.png`, animations: 'disabled', caret: 'hide' })
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
    document.querySelectorAll('.stage *, .bar *').forEach((el) => {
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

/** Siembra por API. `variant` separa escritorio y móvil (letra inicial del sufijo). */
async function seed(request: APIRequestContext, variant: 'D' | 'M'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const ok = async <T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> => {
    const res = await p
    expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
    return (await res.json()) as T
  }
  const suffix = `QF${variant}${LETTERS}`
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const skus = [`${suffix}-10`, `${suffix}-2`, `${suffix}-1`]
  for (const sku of skus) {
    await ok(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Etiqueta ${suffix} ${sku.split('-').pop()}`, trackingType: 'NONE' } }), 'producto')
  }
  const bins = [`R10-${suffix}`, `GEN-${suffix}`, `R2-${suffix}`, `R01-${suffix}-B`, `R1-${suffix}`]
  for (const code of bins) await ok(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code } }), 'posición')
  return { suffix, warehousePublicId: wh, skus, bins }
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

/** Comprueba que el PDF es válido y devuelve su texto. */
async function readPdf(download: Download, name: string): Promise<string> {
  const bytes = readFileSync((await download.path())!)
  expect(bytes.subarray(0, 5).toString('latin1')).toBe('%PDF-')
  expect(bytes.subarray(-8).toString('latin1')).toContain('%%EOF')
  if (process.env.F14_PDF_DIR) await download.saveAs(`${process.env.F14_PDF_DIR}/${name}.pdf`)
  return pdfText(bytes)
}

/** Valores (SKU / código de posición) del sufijo que aparecen como texto del PDF, en orden de aparición y sin repetir. */
function valuesInPdf(text: string, suffix: string): string[] {
  const seen: string[] = []
  for (const m of text.matchAll(new RegExp(`\\(([A-Z0-9-]*${suffix}[A-Z0-9-]*)\\) Tj`, 'g'))) if (!seen.includes(m[1])) seen.push(m[1])
  return seen
}

async function downloadReport(page: Page, scope: Page | ReturnType<Page['locator']>): Promise<Download> {
  // 2026-10-05: "Códigos de barras" es un menú; al elegir "Automático" se genera el PDF
  await scope.getByRole('button', { name: 'Códigos de barras' }).click()
  const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('menuitem', { name: 'Automático' }).click()])
  return download
}

async function productsFlow(page: Page, request: APIRequestContext, s: Seed, shots: boolean) {
  await page.goto('/warehouse/products')
  await expect(page.getByRole('heading', { level: 1, name: 'Productos e inventario' })).toBeVisible()
  await page.getByLabel('Nombre', { exact: true }).fill(`Etiqueta ${s.suffix}`)
  await expect(page.getByText(`${s.suffix}-10`).first()).toBeVisible()
  await expect(page.getByText(`${s.suffix}-1`, { exact: true }).first()).toBeVisible()
  if (shots) await shot(page, 'productos-boton')
  const download = await downloadReport(page, page)
  expect(download.suggestedFilename()).toMatch(/^codigos-de-barras-de-productos-advance-logistics-\d{4}-\d{2}-\d{2}\.pdf$/)
  const text = await readPdf(download, `productos-${s.suffix}`)

  // lo mismo que filtra la pantalla, según el API
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const list = (await (await request.get(`${API_URL}/api/v1/products`, { headers, params: { name: `Etiqueta ${s.suffix}`, take: 200 } })).json()) as { total: number }
  const values = valuesInPdf(text, s.suffix)
  expect(values).toHaveLength(list.total)
  expect(values).toEqual([`${s.suffix}-1`, `${s.suffix}-2`, `${s.suffix}-10`])
  expect(text).toContain('Sin categor')
  expect(text).toContain('(3\\))')
  expect(text).toContain('FILTROS APLICADOS')
  expect(text).toContain(`Etiqueta ${s.suffix}`)
}

async function locationsFlow(page: Page, request: APIRequestContext, s: Seed, shots: boolean) {
  await page.goto(`/warehouse/locations?warehouse=${s.warehousePublicId}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Posiciones' })).toBeVisible()
  await page.getByLabel('Posición', { exact: true }).fill(s.suffix)
  await expect(page.getByText(`R10-${s.suffix}`).first()).toBeVisible()
  await expect(page.getByText(`GEN-${s.suffix}`).first()).toBeVisible()
  if (shots) await shot(page, 'ubicaciones-boton')
  const download = await downloadReport(page, page)
  expect(download.suggestedFilename()).toMatch(/^codigos-de-barras-de-posiciones-advance-logistics-\d{4}-\d{2}-\d{2}\.pdf$/)
  const text = await readPdf(download, `posiciones-${s.suffix}`)

  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const list = (await (
    await request.get(`${API_URL}/api/v1/warehouses/${s.warehousePublicId}/bins`, { headers, params: { search: s.suffix, includeInactive: false, take: 200 } })
  ).json()) as { total: number }
  const values = valuesInPdf(text, s.suffix)
  expect(values).toHaveLength(list.total)
  // grupos por el primer número: 1 (R1 y R01 juntos), 2, 10 y al final las sin número
  expect(values).toEqual([`R1-${s.suffix}`, `R01-${s.suffix}-B`, `R2-${s.suffix}`, `R10-${s.suffix}`, `GEN-${s.suffix}`])
  const order = ['(Grupo 1 \\(2\\))', '(Grupo 2 \\(1\\))', '(Grupo 10 \\(1\\))', '(Otras posiciones \\(1\\))'].map((g) => text.indexOf(g))
  expect(order.every((i) => i >= 0)).toBe(true)
  expect([...order].sort((a, b) => a - b)).toEqual(order)
  expect(text).toContain('Zona RSV')
  expect(text).toContain('ALM-01')
}

test.describe('Lote F14 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'D')
  })

  test('productos: el PDF de códigos tiene exactamente lo filtrado, agrupado y en orden natural', async ({ page, request }) => {
    await login(page)
    await productsFlow(page, request, s, true)
  })

  test('posiciones: el PDF de códigos tiene exactamente lo filtrado, agrupado por el primer número', async ({ page, request }) => {
    await login(page)
    await locationsFlow(page, request, s, true)
  })
})

test.describe('Lote F14 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (!testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'M')
  })

  test('productos y posiciones: botón y selector sin scroll horizontal, y la descarga funciona', async ({ page, request }) => {
    await login(page)
    await page.goto('/warehouse/products')
    await expect(page.getByRole('button', { name: 'Códigos de barras' })).toBeVisible()
    await expectNoHorizontalScroll(page)
    await productsFlow(page, request, s, false)
    await locationsFlow(page, request, s, false)
    await expectNoHorizontalScroll(page)
  })
})
