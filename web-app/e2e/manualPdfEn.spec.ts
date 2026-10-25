// Manual en inglés (2026-10-24): genera los PDF de los lotes F14/F15/F16 con la interfaz en INGLÉS (el PDF lo arma el cliente con el idioma
// de la interfaz del momento) y los deja en MANUAL_PDF_DIR para pasarlos a imagen (scripts: pdftoppm). Solo corre con MANUAL_LANG=en.
// - Reales: lista de códigos de barras de productos y de posiciones (F14) y etiquetas de posición 4×2, 4×2 girada, 4×4 y 4×6 (F16): se
//   recorre la pantalla en español, y justo antes de pulsar el botón que genera el PDF se pasa la interfaz a inglés.
// - Muestras con datos de prueba (rejilla de 46, hojas de posición, etiqueta que no cabe): se arman en la propia página con los generadores
//   de la aplicación (import dinámico del servidor de Vite), igual que las imágenes españolas del manual.
import { mkdirSync, writeFileSync } from 'node:fs'
import { expect, test, type APIRequestContext, type ElementHandle, type Page } from '@playwright/test'
import { manualLang, MANUAL_LANG } from './manualLang'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const PDF_DIR = process.env.MANUAL_PDF_DIR ?? ''
const LETTERS = String(Date.now())
  .slice(-7)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR', acceptDownloads: true })
test.skip(MANUAL_LANG !== 'en' || !PDF_DIR, 'solo para regenerar los PDF del manual en inglés')

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
  // la compañía/usuario pueden llegar en inglés por una corrida anterior: el recorrido siempre empieza en español
  await manualLang(page, 'es')
}

async function ok<T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> {
  const res = await p
  expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
  return (await res.json()) as T
}

/** Pasa a inglés, pulsa el botón (ya resuelto en español) y guarda el PDF que descarga; vuelve a español. */
async function downloadInEnglish(page: Page, handle: ElementHandle, name: string) {
  await manualLang(page, 'en')
  const [download] = await Promise.all([page.waitForEvent('download'), handle.click()])
  mkdirSync(PDF_DIR, { recursive: true })
  await download.saveAs(`${PDF_DIR}/${name}.pdf`)
  await manualLang(page, 'es')
}

async function warehouseAndZone(request: APIRequestContext, headers: Record<string, string>) {
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  return { wh, zone: zones.find((z) => z.code === 'RSV')!.id }
}

async function filterBins(page: Page, suffix: string, codes: string[]) {
  await page.getByRole('button', { name: 'Posición', exact: true }).first().click()
  await page.getByRole('searchbox', { name: 'Buscar…' }).fill(suffix)
  for (const code of codes) await page.getByRole('option', { name: new RegExp(`^${code} · `) }).click()
  await page.keyboard.press('Escape')
}

test('F14 real: códigos de barras de productos y de posiciones', async ({ page, request }) => {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const suffix = `QFR${LETTERS}`
  const { wh, zone } = await warehouseAndZone(request, headers)
  const skus = [`${suffix}-10`, `${suffix}-2`, `${suffix}-1`]
  for (const sku of skus) await ok(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Label ${suffix} ${sku.split('-').pop()}`, trackingType: 'NONE' } }), 'producto')
  const bins = [`R10-${suffix}`, `GEN-${suffix}`, `R2-${suffix}`, `R01-${suffix}-B`, `R1-${suffix}`]
  for (const code of bins) await ok(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code } }), 'posición')

  await login(page)
  await page.goto('/warehouse/products')
  await expect(page.getByRole('heading', { level: 1, name: 'Productos e inventario' })).toBeVisible()
  for (const sku of skus) {
    await page.getByRole('button', { name: 'SKU o nombre', exact: true }).first().click()
    await page.getByRole('searchbox', { name: 'Buscar…' }).fill(`Label ${suffix}`)
    await page.getByRole('option', { name: new RegExp(`^${sku} · `) }).click()
    await page.keyboard.press('Escape')
  }
  await page.getByRole('button', { name: 'Códigos de barras' }).click()
  await downloadInEnglish(page, (await page.getByRole('menuitem', { name: 'Automático' }).elementHandle())!, 'f14-productos')

  await page.goto(`/warehouse/locations?warehouse=${wh}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Posiciones' })).toBeVisible()
  await filterBins(page, suffix, bins)
  await expect(page.getByText(`R10-${suffix}`).first()).toBeVisible()
  await page.getByRole('button', { name: 'Códigos de barras' }).click()
  await downloadInEnglish(page, (await page.getByRole('menuitem', { name: 'Automático' }).elementHandle())!, 'f14-posiciones')
})

test('F16 real: etiquetas de posición 4×2, girada, 4×4 y 4×6', async ({ page, request }) => {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const suffix = `ETR${LETTERS}`
  const { wh, zone } = await warehouseAndZone(request, headers)
  const mk = async (code: string, extra: Record<string, string> = {}) => {
    await ok(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code, ...extra } }), 'posición')
    return code
  }
  const codes = [await mk(`${suffix}-10`), await mk(`${suffix}-02`), await mk(`${suffix}-01`, { aisle: '01', rack: '02', level: '3' })]

  await login(page)
  await page.goto(`/warehouse/locations?warehouse=${wh}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Posiciones' })).toBeVisible()
  await filterBins(page, suffix, codes)
  await page.getByRole('button', { name: 'Etiquetas de posición' }).click()
  const dialog = page.getByRole('dialog', { name: 'Etiquetas de posición' })
  const sizes = { '4x2': /4 × 2 pulgadas \(10 × 5 cm\)/, '4x4': /4 × 4 pulgadas \(10 × 10 cm\)/, '4x6': /4 × 6 pulgadas \(10 × 15 cm\)/ }
  const run = async (size: keyof typeof sizes, rotate: boolean, name: string) => {
    await dialog.getByRole('radio', { name: sizes[size] }).check()
    await dialog.getByRole('radio', { name: rotate ? 'Girar 90°' : 'Automática (recomendada)' }).check()
    const btn = (await dialog.getByRole('button', { name: 'Generar PDF' }).elementHandle())!
    await manualLang(page, 'en')
    const [download] = await Promise.all([page.waitForEvent('download'), btn.click()])
    mkdirSync(PDF_DIR, { recursive: true })
    await download.saveAs(`${PDF_DIR}/${name}.pdf`)
    await manualLang(page, 'es')
    await expect(dialog).toHaveCount(0)
    await page.getByRole('button', { name: 'Etiquetas de posición' }).click()
  }
  await run('4x2', false, 'f16-4x2')
  await run('4x4', false, 'f16-4x4')
  await run('4x6', false, 'f16-4x6')
  await run('4x2', true, 'f16-4x2-girada')
})

test('muestras con datos de prueba en inglés: rejilla de 46, hojas de posición y etiqueta que no cabe', async ({ page }) => {
  await page.goto('/login')
  await manualLang(page, 'en')
  const out = await page.evaluate(async () => {
    const i18n = await import('/src/kernel/i18n/index.ts')
    i18n.setLang('en')
    const T = i18n.t
    const barcode = await import('/src/kernel/ui/barcodeReportPdf.ts')
    const sheet = await import('/src/kernel/ui/binSheetPdf.ts')
    const label = await import('/src/kernel/ui/binLabelPdf.ts')
    const b64 = (buf: ArrayBuffer) => {
      let s = ''
      const u = new Uint8Array(buf)
      for (let k = 0; k < u.length; k += 0x8000) s += String.fromCharCode(...u.subarray(k, k + 0x8000))
      return btoa(s)
    }
    const when = new Date(2026, 9, 3, 18, 35)
    const res: Record<string, string> = {}

    // f14 rejilla: 46 códigos, 40 en el grupo 01 (página 2 con "(continuación)")
    const rows = (from: number, to: number) =>
      Array.from({ length: to - from + 1 }, (_, k) => {
        const n = String(from + k).padStart(2, '0')
        return { value: `A01-R${n}-N1`, meta: 'Zone RSV (Reserve) · Warehouse ALM-01' }
      })
    const rejilla = await barcode.renderBarcodeReportPdf({
      title: T('warehouse.barcodes.productsTitle'),
      subtitle: T('warehouse.barcodes.productsSubtitle'),
      company: 'Advance Logistics',
      user: 'Advance Administrator',
      generatedAt: when,
      locale: 'en-US',
      filters: [{ label: 'Warehouse', value: 'ALM-01 (Main warehouse)' }],
      groups: [
        { title: T('warehouse.barcodes.binGroup', { group: '01', count: 40 }), rows: rows(1, 40) },
        { title: T('warehouse.barcodes.binGroup', { group: '02', count: 6 }), rows: rows(41, 46) },
      ],
    })
    res['f14-rejilla'] = b64(rejilla.output('arraybuffer'))

    // f15: hojas de posición
    const details = [
      { label: 'Zone', value: 'RSV' },
      { label: 'Aisle', value: 'A01' },
      { label: 'Rack', value: 'R010' },
      { label: 'Level', value: 'N1' },
    ]
    const prods = (n: number) =>
      Array.from({ length: n }, (_, k) => {
        const p = String(k + 1).padStart(3, '0')
        const long = ['', ' long', ' long long', ' long long long'][k % 4]
        return { sku: `SKU${n}-${p}`, name: `Test product number ${k + 1} with description${long}`, barcode: k % 2 ? `7501234${String(k).padStart(5, '0')}` : null }
      })
    const mkSheet = (code: string, n: number, d = details) => ({
      title: T('warehouse.binProducts.pdfTitle'),
      company: 'Advance Logistics',
      warehouse: 'ALM-01 · Main warehouse',
      locale: 'en-US',
      printedAt: when,
      bins: [{ code, details: d, products: prods(n) }],
    })
    const diez = await sheet.renderBinSheetsPdf(mkSheet('A01-R00-N1-P10', 10))
    res['f15-diez'] = b64(diez.doc.output('arraybuffer'))
    const once = await sheet.renderBinSheetsPdf(mkSheet('A01-R01-N1-P11', 11, [{ label: 'Zone', value: 'RSV' }, { label: 'Aisle', value: 'A01' }, { label: 'Rack', value: 'R011' }, { label: 'Level', value: 'N1' }]))
    res['f15-once'] = b64(once.doc.output('arraybuffer'))
    const hoja = await sheet.renderBinSheetsPdf({
      ...mkSheet('HSDGJJFEHK-A1', 1, [{ label: 'Zone', value: 'RSV' }]),
      bins: [{ code: 'HSDGJJFEHK-A1', details: [{ label: 'Zone', value: 'RSV' }], products: [{ sku: 'HSDGJJFEHK-1', name: 'Sheet HSDGJJFEHK bolt', barcode: '7510668861191' }] }],
    })
    res['f15-hoja'] = b64(hoja.doc.output('arraybuffer'))

    // f16: etiqueta que no cabe
    const nocabe = await label.renderBinLabelsPdf({
      title: T('warehouse.binLabels.pdfTitle'),
      company: 'Advance Logistics',
      locale: 'en-US',
      size: '4x2',
      generatedAt: when,
      bins: [{ code: 'RACKAB-CDEFGHJKLMNPQRSTUVWXYZ-ABCD' }],
    })
    res['f16-nocabe'] = b64(nocabe.doc.output('arraybuffer'))
    return res
  })
  mkdirSync(PDF_DIR, { recursive: true })
  for (const [name, b64] of Object.entries(out)) writeFileSync(`${PDF_DIR}/${name}.pdf`, Buffer.from(b64, 'base64'))
  expect(Object.keys(out)).toHaveLength(5)
})
