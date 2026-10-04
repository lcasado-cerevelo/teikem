// Recorrido del Lote F16 (etiquetas de posición) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics", almacén ALM-01).
// Siembra por API tres posiciones vacías en la zona RSV de ALM-01 (<SUF>-10, <SUF>-02 y <SUF>-01, esta con pasillo, rack y
// nivel). <SUF> son solo letras. No se mueve inventario.
// Escritorio: Ubicaciones (ALM-01) → filtro Posición = <SUF> → "Etiquetas de posición" → 4×2, 4×4 y 4×6 (y 4×2 girada):
// cada PDF tiene UNA página por posición del filtro, del tamaño exacto de la etiqueta (/MediaBox 288×144, 288×288,
// 288×432; girada = la misma con /Rotate 90), con el código de cada una en orden natural; volver a generarlas (reimprimir)
// no llama a "marcar impresas" ni a las hojas y el estado de la hoja de las posiciones no cambia. Segundo caso: marcar una
// casilla e imprimir solo esa. Móvil (360 px): el modal sin scroll horizontal y un PDF 4×6.
// Capturas para el manual: docs/manual/frontend/img/f16-<paso>.png. Con F16_PDF_DIR, guarda ahí los PDF (para pasarlos a
// imagen y decodificar sus códigos fuera del repo).
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

/** Página de cada tamaño en pt (ancho × alto de la etiqueta). */
const SIZES = {
  '4x2': { radio: /4 × 2 pulgadas \(10 × 5 cm\)/, width: 288, height: 144 },
  '4x4': { radio: /4 × 4 pulgadas \(10 × 10 cm\)/, width: 288, height: 288 },
  '4x6': { radio: /4 × 6 pulgadas \(10 × 15 cm\)/, width: 288, height: 432 },
} as const
type Size = keyof typeof SIZES

test.use({ locale: 'es-PR', acceptDownloads: true })

interface Seed {
  suffix: string
  warehousePublicId: string
  /** En orden natural: -01, -02, -10. */
  bins: { id: number; code: string }[]
  headers: Record<string, string>
}

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f16-${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14/F15). */
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
  return (await res.json()) as T
}

/** Siembra por API. `variant` separa escritorio y móvil (letra inicial del sufijo). */
async function seed(request: APIRequestContext, variant: 'D' | 'E' | 'M'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const suffix = `ET${variant}${LETTERS}`
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const mk = async (code: string, extra: Record<string, string> = {}) => {
    const b = await ok<{ id: number; code: string }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code, ...extra } }), 'posición')
    return { id: b.id, code }
  }
  // se crean fuera de orden: la etiqueta sale en orden natural (-01, -02, -10)
  const b10 = await mk(`${suffix}-10`)
  const b02 = await mk(`${suffix}-02`)
  const b01 = await mk(`${suffix}-01`, { aisle: '01', rack: '02', level: '3' })
  return { suffix, warehousePublicId: wh, bins: [b01, b02, b10], headers }
}

/** Estado de la hoja de posición (F15) de una posición: las etiquetas no lo cambian. */
async function sheetStatuses(request: APIRequestContext, s: Seed): Promise<string[]> {
  const page = await ok<{ items: { code: string; sheetStatus: string }[] }>(
    request.get(`${API_URL}/api/v1/warehouses/${s.warehousePublicId}/bins`, { headers: s.headers, params: { search: s.suffix } }),
    'posiciones',
  )
  return page.items.map((b) => `${b.code}:${b.sheetStatus}`).sort()
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

interface PdfInfo {
  pages: number
  mediaBoxes: number[][]
  rotates: number
  text: string
}

async function readPdf(download: Download, name: string): Promise<PdfInfo> {
  const bytes = readFileSync((await download.path())!)
  const raw = bytes.toString('latin1')
  expect(raw.slice(0, 5)).toBe('%PDF-')
  expect(raw.slice(-8)).toContain('%%EOF')
  if (process.env.F16_PDF_DIR) await download.saveAs(`${process.env.F16_PDF_DIR}/${name}.pdf`)
  return {
    // los diccionarios de página no van comprimidos: /Type /Page (no /Pages) y su /MediaBox
    pages: (raw.match(/\/Type \/Page(?!s)/g) ?? []).length,
    mediaBoxes: [...raw.matchAll(/\/MediaBox \[([^\]]+)\]/g)].map((m) => m[1].trim().split(/\s+/).map(Number)),
    rotates: (raw.match(/\/Rotate 90/g) ?? []).length,
    text: pdfText(bytes),
  }
}

/** Fila (escritorio) o tarjeta (móvil) de una posición. */
const binRow = (page: Page, code: string) => page.locator('tr, li.dt-card').filter({ hasText: code })

async function openLocations(page: Page, s: Seed) {
  await page.goto(`/warehouse/locations?warehouse=${s.warehousePublicId}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Posiciones' })).toBeVisible()
  await page.getByLabel('Posición', { exact: true }).fill(s.suffix)
  for (const b of s.bins) await expect(binRow(page, b.code)).toBeVisible()
}

const dialogOf = (page: Page) => page.getByRole('dialog', { name: 'Etiquetas de posición' })

/** Abre el modal, elige tamaño y orientación y descarga el PDF. */
async function generate(page: Page, size: Size, rotate = false): Promise<Download> {
  if ((await dialogOf(page).count()) === 0) await page.getByRole('button', { name: 'Etiquetas de posición' }).click()
  const dialog = dialogOf(page)
  await dialog.getByRole('radio', { name: SIZES[size].radio }).check()
  await dialog.getByRole('radio', { name: rotate ? 'Girar 90°' : 'Automática (recomendada)' }).check()
  const [download] = await Promise.all([page.waitForEvent('download'), dialog.getByRole('button', { name: 'Generar PDF' }).click()])
  await expect(dialog).toHaveCount(0)
  return download
}

/** Una página por posición, todas del tamaño exacto, con el código de cada una en orden. */
function expectLabels(pdf: PdfInfo, size: Size, codes: string[], rotated = false) {
  const { width, height } = SIZES[size]
  expect(pdf.pages).toBe(codes.length)
  expect(pdf.mediaBoxes).toEqual(codes.map(() => [0, 0, width, height]))
  expect(pdf.rotates).toBe(rotated ? codes.length : 0)
  const positions = codes.map((c) => pdf.text.indexOf(`(${c}) Tj`))
  for (const p of positions) expect(p).toBeGreaterThan(-1)
  expect([...positions].sort((a, b) => a - b)).toEqual(positions)
}

/** Lo que la web le pide al API mientras corre `fn` (para comprobar que no se marca nada). */
async function apiCallsDuring(page: Page, fn: () => Promise<void>): Promise<string[]> {
  const urls: string[] = []
  const onRequest = (r: import('@playwright/test').Request) => {
    if (r.url().includes('/api/v1/')) urls.push(`${r.method()} ${new URL(r.url()).pathname}`)
  }
  page.on('request', onRequest)
  try {
    await fn()
  } finally {
    page.off('request', onRequest)
  }
  return urls
}

test.describe('Lote F16 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'D')
  })

  test('etiquetas del filtro actual: 4×2, 4×4 y 4×6 (una página por posición, del tamaño exacto), girada y reimpresa sin marcar nada', async ({ page, request }) => {
    await login(page)
    await openLocations(page, s)
    const codes = s.bins.map((b) => b.code)
    const before = await sheetStatuses(request, s)
    await shot(page, 'ubicaciones')

    await page.getByRole('button', { name: 'Etiquetas de posición' }).click()
    const dialog = dialogOf(page)
    await expect(dialog.getByLabel('Las posiciones del filtro actual (3)')).toBeChecked()
    await expect(dialog.getByLabel('Las posiciones marcadas (0)')).toBeDisabled()
    await shot(page, 'modal')

    const calls = await apiCallsDuring(page, async () => {
      for (const size of ['4x2', '4x4', '4x6'] as const) {
        const download = await generate(page, size)
        expect(download.suggestedFilename()).toMatch(new RegExp(`^etiquetas-de-posicion-${size}-advance-logistics-\\d{4}-\\d{2}-\\d{2}\\.pdf$`))
        const pdf = await readPdf(download, `etiquetas-${size}-${s.suffix}`)
        expectLabels(pdf, size, codes)
        // los datos de la ubicación (solo los que tienen valor)
        // (en 4×4 y 4×6 la letra es mayor y los datos van en 2 renglones)
        expect(pdf.text).toContain('(Almacén ALM-01 · Zona RSV · Pasillo 01')
        expect(pdf.text).toContain('Rack 02 · Nivel 3) Tj')
        expect(pdf.text).toContain('(Almacén ALM-01 · Zona RSV) Tj')
      }
      // girar 90°: la misma página con /Rotate 90
      const rotated = await readPdf(await generate(page, '4x2', true), `etiquetas-4x2-girada-${s.suffix}`)
      expectLabels(rotated, '4x2', codes, true)
      // reimprimir: lo mismo otra vez
      const again = await readPdf(await generate(page, '4x2'), `etiquetas-4x2-otra-vez-${s.suffix}`)
      expectLabels(again, '4x2', codes)
    })
    // solo lecturas del listado de posiciones: nada de "marcar impresas" ni de hojas de posición
    expect(calls.filter((c) => !c.startsWith('GET '))).toEqual([])
    expect(calls.filter((c) => c.includes('bin-sheets'))).toEqual([])
    expect(calls.filter((c) => c.endsWith(`/warehouses/${s.warehousePublicId}/bins`)).length).toBeGreaterThanOrEqual(5)
    expect(await sheetStatuses(request, s)).toEqual(before)
  })

  test('marcadas: marcar una posición e imprimir solo su etiqueta', async ({ page }) => {
    const m = await seed(page.request, 'E')
    await login(page)
    await openLocations(page, m)
    const target = m.bins[1]
    await binRow(page, target.code).getByRole('checkbox', { name: `Marcar la posición ${target.code}` }).check()
    await expect(page.getByText('1 marcada')).toBeVisible()
    await page.getByRole('button', { name: 'Etiquetas de posición' }).click()
    await expect(dialogOf(page).getByLabel('Las posiciones marcadas (1)')).toBeChecked()
    const pdf = await readPdf(await generate(page, '4x4'), `etiquetas-marcada-${m.suffix}`)
    expectLabels(pdf, '4x4', [target.code])
    expect(pdf.text).not.toContain(`(${m.bins[0].code}) Tj`)
    // la marca se queda (se puede reimprimir)
    await expect(page.getByText('1 marcada')).toBeVisible()
  })
})

test.describe('Lote F16 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (!testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'M')
  })

  test('el modal sin scroll horizontal y un PDF 4×6 de una página por posición', async ({ page }) => {
    await login(page)
    await openLocations(page, s)
    await expectNoHorizontalScroll(page)
    await page.getByRole('button', { name: 'Etiquetas de posición' }).click()
    await expect(dialogOf(page).getByLabel('Las posiciones del filtro actual (3)')).toBeChecked()
    await dialogOf(page).getByRole('radio', { name: SIZES['4x6'].radio }).check()
    await expectNoHorizontalScroll(page)
    await shot(page, 'movil-modal')
    const pdf = await readPdf(await generate(page, '4x6'), `etiquetas-movil-4x6-${s.suffix}`)
    expectLabels(pdf, '4x6', s.bins.map((b) => b.code))
    await expectNoHorizontalScroll(page)
  })
})
