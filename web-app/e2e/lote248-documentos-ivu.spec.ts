// Lote 248 (Lote D) — Documentos del cliente con vigencia, exención de IVU atada a su documento, exención por temporada, aviso al vender y avisos de vencimiento, contra el API real
// de pruebas (SIN correo: Brevo__ApiKey vacía, Auth__Onboarding__Enabled=false). Prepara por API: la tasa del IVU (estatal 10.5 % + municipal 1 %), un cliente con el certificado municipal
// vigente (captura única por API: certificado + exención, vence en 12 días), otro con un certificado ya vencido, un período por temporada y una orden en borrador. Recorre la ficha del cliente
// (Documentos e Impuestos), Documentos por vencer, Impuestos → Exención por temporada (lista y alta), Ajustes → Avisos de vencimiento y el aviso al vender (pedido guiado y orden),
// midiendo: sin scroll horizontal, filas ≤ 56 px en escritorio y paneles que no se desbordan. 1440 px es/claro y en/oscuro; 390 px es/oscuro y en/claro (deviceScaleFactor 2).
// Capturas en SHOTS_DIR (por omisión docs/manual/frontend/img/l248-*.png).
// Correr: WEB_URL=http://localhost:5175 API_URL=http://localhost:5101 npx playwright test lote248-documentos-ivu --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test, type Page } from '@playwright/test'

import { API_URL, apiHeaders, login, ok, tenantDay } from './staffHelpers'

test.use({ locale: 'es-PR', deviceScaleFactor: 2 })
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const SHOTS = process.env.SHOTS_DIR ?? fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const CLIENT_1 = 'FARMACIA IVU UNO'
const CLIENT_2 = 'COLMADO IVU DOS'
const PERIOD = 'Emergencia IVU 248'
const PDF = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF')

const ids = { client1: '', client2: '' }

test.beforeAll(async ({ request }) => {
  // los módulos TAX y SALES_ORDERS se encienden por SQL en TenantModule (encenderlos por API exige ser administrador de la plataforma), como en el recorrido de impuestos
  const headers = await apiHeaders(request)
  // la tasa del IVU (una sola vez) y como tasa por omisión
  const rates = await ok<{ code: string }[]>(request.get(`${API_URL}/api/v1/tax/rates`, { headers }), 'tasas')
  if (!rates.some((r) => r.code === 'IVU248')) {
    await ok(
      request.post(`${API_URL}/api/v1/tax/rates`, {
        headers,
        data: { code: 'IVU248', name: 'IVU 11.5 %', effectiveFrom: tenantDay(-400), components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] },
      }),
      'tasa',
    )
  }
  await ok(request.patch(`${API_URL}/api/v1/tax/settings`, { headers, data: { defaultRateCode: 'IVU248' } }), 'tasa por omisión')
  const client = async (name: string) => {
    const find = async () => (await ok<{ publicId: string; name: string }[]>(request.get(`${API_URL}/api/v1/clients?search=${encodeURIComponent(name)}`, { headers }), 'clientes')).find((c) => c.name === name)
    let c = await find()
    if (!c) {
      await ok(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name } }), name)
      c = await find()
    }
    return c!.publicId
  }
  ids.client1 = await client(CLIENT_1)
  ids.client2 = await client(CLIENT_2)
  // cliente 1: captura única (certificado municipal que vence en 12 días + la exención solo del componente municipal)
  const existing = await ok<{ status: string }[]>(request.get(`${API_URL}/api/v1/clients/${ids.client1}/tax-exemptions`, { headers }), 'exenciones')
  if (existing.length === 0) {
    await ok(
      request.post(`${API_URL}/api/v1/clients/${ids.client1}/tax-exemptions`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        multipart: {
          file: { name: 'certificado-municipal.pdf', mimeType: 'application/pdf', buffer: PDF },
          documentType: 'IVU_EXEMPTION_CITY',
          documentNumber: 'MUN-248-001',
          validFrom: tenantDay(-200),
          validTo: tenantDay(12),
          scope: 'COMPONENTS',
          componentCodes: 'CITY',
        },
      }),
      'captura única',
    )
  }
  // cliente 2: un certificado estatal ya vencido (el registro de un documento real; no respalda ninguna exención) y una licencia sin vigencia
  const docs2 = await ok<unknown[]>(request.get(`${API_URL}/api/v1/clients/${ids.client2}/documents`, { headers }), 'documentos')
  if (docs2.length === 0) {
    await ok(
      request.post(`${API_URL}/api/v1/clients/${ids.client2}/documents`, {
        headers,
        multipart: { file: { name: 'certificado-estatal.pdf', mimeType: 'application/pdf', buffer: PDF }, documentType: 'IVU_EXEMPTION_STATE', documentNumber: 'EST-248-009', validFrom: tenantDay(-400), validTo: tenantDay(-20) },
      }),
      'documento vencido',
    )
    await ok(request.post(`${API_URL}/api/v1/clients/${ids.client2}/documents`, { headers, multipart: { file: { name: 'licencia.pdf', mimeType: 'application/pdf', buffer: PDF }, documentType: 'OTHER', notes: 'Licencia municipal' } }), 'documento sin vigencia')
  }
  // período por temporada (una sola vez)
  const periods = await ok<{ name: string }[]>(request.get(`${API_URL}/api/v1/tax/exemption-periods?includeInactive=true`, { headers }), 'períodos')
  if (!periods.some((p) => p.name === PERIOD)) {
    await ok(request.post(`${API_URL}/api/v1/tax/exemption-periods`, { headers, data: { name: PERIOD, reason: 'Orden administrativa 2026-248', validFrom: tenantDay(30), validTo: tenantDay(60), scope: 'ALL' } }), 'período')
  }
  // una orden en borrador del cliente 1 para ver el aviso en la ventana de la orden
  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const found = (await ok<{ items: { publicId: string; sku: string }[] }>(request.get(`${API_URL}/api/v1/products?search=IVU-248`, { headers }), 'productos')).items.find((x) => x.sku === 'IVU-248')
  const product = found?.publicId ?? (await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: 'IVU-248', name: 'Producto IVU 248', trackingType: 'NONE', salePrice: 10, purchaseCost: 1 } }), 'producto')).product.publicId
  await ok(request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { clientPublicId: ids.client1, warehousePublicId: warehouses[0]?.publicId, requiredDate: tenantDay(), lines: [{ productPublicId: product, quantity: 1 }] } }), 'orden')
})

async function noHScroll(page: Page, what: string) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(over, `scroll horizontal de la página: ${what}`).toBeLessThanOrEqual(1)
}

async function setLook(page: Page, lang: 'es' | 'en', theme: 'light' | 'dark') {
  await page.evaluate(([l, th]) => {
    localStorage.setItem('teikem.lang', l)
    localStorage.setItem('teikem.theme', th)
    // los paneles recuerdan abierto/cerrado: el recorrido parte de lo de fábrica
    Object.keys(localStorage).filter((k) => k.startsWith('teikem.panel.')).forEach((k) => localStorage.removeItem(k))
  }, [lang, theme] as const)
  await page.reload()
}

/** Todas las filas de la tabla miden ≤ 56 px (solo escritorio, donde la tabla es tabla). */
async function rowsAtMost(page: Page, name: string, max = 56) {
  if ((await page.getByRole('table', { name }).count()) === 0) return
  const rows = page.getByRole('table', { name }).locator('tbody tr')
  const n = await rows.count()
  expect(n, `filas de «${name}»`).toBeGreaterThan(0)
  for (let i = 0; i < n; i++) expect((await rows.nth(i).boundingBox())!.height, `alto de la fila ${i} de «${name}»`).toBeLessThanOrEqual(max)
}

async function noOverflow(page: Page, selector: string, what: string) {
  const over = await page.locator(selector).first().evaluate((el) => el.scrollWidth - el.clientWidth)
  expect(over, `${what} no se desborda`).toBeLessThanOrEqual(1)
}

const LOOKS = [
  { lang: 'es', theme: 'light', width: 1440, height: 2600, tag: 'escritorio' },
  { lang: 'en', theme: 'dark', width: 1440, height: 2600, tag: 'escritorio' },
  { lang: 'es', theme: 'dark', width: 390, height: 3600, tag: 'movil' },
  { lang: 'en', theme: 'light', width: 390, height: 3600, tag: 'movil' },
] as const

for (const look of LOOKS) {
  test(`Documentos e IVU ${look.tag} ${look.lang}/${look.theme}`, async ({ page, isMobile }) => {
    test.skip(isMobile, 'el recorrido fija sus propios anchos (1440 y 390)')
    mkdirSync(SHOTS, { recursive: true })
    const { lang, theme, tag } = look
    const L = lang === 'es'
    const desktop = look.width >= 720
    const shot = (name: string) => page.screenshot({ path: join(SHOTS, `l248-${name}-${tag}-${lang}-${theme}.png`), fullPage: true })
    await login(page)
    await page.setViewportSize({ width: look.width, height: look.height })

    // ---- ficha del cliente 1: Documentos (número, vigencia, estado, «respalda exención») e Impuestos (la exención)
    await page.goto(`/catalog/clients?client=${ids.client1}`)
    await setLook(page, lang, theme)
    await page.getByRole('heading', { name: L ? 'Documentos del cliente' : 'Client documents', exact: true }).click({ timeout: 30_000 })
    await expect(page.getByText('MUN-248-001').first()).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(L ? 'Respalda exención' : 'Backs exemption').first()).toBeVisible()
    await expect(page.getByText(L ? 'Por vencer' : 'Expiring').first()).toBeVisible()
    if (desktop) await rowsAtMost(page, L ? 'Documentos del cliente' : 'Client documents')
    await noHScroll(page, `documentos ${tag} ${lang} ${theme}`)
    await shot('documentos')
    await page.getByRole('heading', { name: L ? 'Impuestos' : 'Taxes', exact: true }).click()
    await expect(page.getByText(L ? 'Certificado de exención de IVU municipal' : 'IVU exemption certificate (municipal)').first()).toBeVisible({ timeout: 30_000 })
    if (desktop) await rowsAtMost(page, L ? 'Impuestos' : 'Taxes')
    await noHScroll(page, `impuestos del cliente ${tag} ${lang} ${theme}`)
    await shot('cliente-impuestos')
    // la captura única con archivo nuevo (modal)
    await page.getByRole('button', { name: L ? 'Nueva exención' : 'New exemption' }).click()
    const capture = page.getByRole('dialog', { name: L ? 'Nueva exención de IVU' : 'New IVU exemption' })
    await expect(capture.getByLabel(L ? 'Tipo de documento' : 'Document type')).toBeVisible({ timeout: 30_000 })
    await noOverflow(page, '.pal', 'la captura única')
    await noHScroll(page, `captura ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `l248-captura-${tag}-${lang}-${theme}.png`) })
    await capture.getByRole('button', { name: L ? 'Cancelar' : 'Cancel' }).click()

    // ---- Documentos por vencer
    await page.goto('/catalog/expiring-documents')
    await setLook(page, lang, theme)
    await expect(page.getByText(CLIENT_1).first()).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(CLIENT_2).first()).toBeVisible()
    await expect(page.getByText(L ? 'Vencido' : 'Expired').first()).toBeVisible()
    if (desktop) await rowsAtMost(page, L ? 'Documentos' : 'Documents')
    await noHScroll(page, `por vencer ${tag} ${lang} ${theme}`)
    await shot('por-vencer')

    // ---- Impuestos → Exención por temporada y Exenciones
    await page.goto('/money/tax')
    await setLook(page, lang, theme)
    await page.getByRole('tab', { name: L ? 'Exenciones' : 'Exemptions' }).click()
    await expect(page.getByText('MUN-248-001').first()).toBeVisible({ timeout: 30_000 })
    if (desktop) await rowsAtMost(page, L ? 'Exenciones de clientes' : 'Client exemptions')
    await noHScroll(page, `exenciones ${tag} ${lang} ${theme}`)
    await shot('exenciones')
    await page.getByRole('tab', { name: L ? 'Exención por temporada' : 'Seasonal exemption' }).click()
    await expect(page.getByText(PERIOD).first()).toBeVisible({ timeout: 30_000 })
    await expect(page.locator('.tax-screen').getByText(L ? 'Programado' : 'Scheduled', { exact: true }).first()).toBeVisible()
    if (desktop) await rowsAtMost(page, L ? 'Exención por temporada' : 'Seasonal exemption')
    await noHScroll(page, `temporada ${tag} ${lang} ${theme}`)
    await shot('temporada')
    await page.getByRole('button', { name: L ? 'Nuevo período' : 'New period' }).click()
    const periodDialog = page.getByRole('dialog', { name: L ? 'Nuevo período' : 'New period' })
    await periodDialog.getByRole('radio', { name: L ? 'Categorías o productos escogidos' : 'Chosen categories or products' }).check()
    await expect(periodDialog.getByText(L ? 'Escoja al menos una categoría o un producto.' : 'Choose at least one category or product.')).toBeVisible()
    await noOverflow(page, '.pal', 'el período')
    await noHScroll(page, `período ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `l248-periodo-${tag}-${lang}-${theme}.png`) })
    await periodDialog.getByRole('button', { name: L ? 'Cancelar' : 'Cancel' }).click()

    // ---- Ajustes → Operación → Avisos de vencimiento
    await page.goto('/system/settings?tab=ops')
    await setLook(page, lang, theme)
    await page.getByRole('heading', { name: L ? 'Avisos de vencimiento' : 'Expiry notices', exact: true }).scrollIntoViewIfNeeded({ timeout: 30_000 })
    await expect(page.getByText(L ? '30 días antes' : '30 days before')).toBeVisible()
    await expect(page.getByText(L ? 'El día que vence' : 'The day it expires')).toHaveCount(0)
    await noHScroll(page, `ajustes ${tag} ${lang} ${theme}`)
    await page.getByRole('heading', { name: L ? 'Avisos de vencimiento' : 'Expiry notices', exact: true }).locator('xpath=ancestor::section[1]').screenshot({ path: join(SHOTS, `l248-ajustes-${tag}-${lang}-${theme}.png`) })

    // ---- aviso al vender: pedido guiado (cliente escogido)
    await page.goto('/sales-orders/new-order')
    await setLook(page, lang, theme)
    // el asistente escoge solo al único cliente que encuentra: se cambia al que tiene la exención
    const change = page.getByRole('button', { name: L ? 'Cambiar cliente' : 'Change client' })
    await page.waitForLoadState('networkidle')
    await expect(page.getByText(L ? 'Paso 1 de 4' : 'Step 1 of 4')).toBeVisible({ timeout: 30_000 })
    await page.waitForTimeout(1500)
    if (await change.isVisible()) await change.click()
    await page.locator('#wz-client-q').fill('FARMACIA IVU')
    await page.locator('.wz-results button', { hasText: 'FARMACIA IVU UNO' }).first().click({ timeout: 30_000 })
    const notice = page.getByTestId('tax-notice')
    await expect(notice).toBeVisible({ timeout: 30_000 })
    await expect(notice).toContainText(L ? 'vence el' : 'expires on')
    await expect(notice).toContainText(L ? 'se cobrará' : 'will be charged')
    await noOverflow(page, '.wz-step', 'el paso del cliente')
    await noHScroll(page, `aviso ${tag} ${lang} ${theme}`)
    await shot('aviso-pedido')
  })
}
