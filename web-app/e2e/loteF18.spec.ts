// Recorrido del Lote F18 (Rentas F-R2) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo "Advance Logistics", almacén ALM-01, módulos Rentas y Análisis encendidos por el aprovisionamiento demo).
// Siembra por API un cliente con su localidad y un producto con 3 unidades en una posición propia de ALM-01, lo convierte a serie
// y crea una renta en Borrador con las 3 series (recogido a 30 días: no entra en "Necesita tu atención"). En la web: Programar y
// Despachar → "Registrar devolución" ANTICIPADA por daño de 2 de los 3 equipos con proceso ("Otro" sin notas da el mensaje del
// servidor; la renta sigue En renta con el tercero) → ficha de la devolución (renta ↔ devolución) → cola de proceso: un salto
// fuera de orden da el 422 del motor tal cual, se recorre Inspección → Limpieza → Pruebas y "Completar" deja la serie Lista (el
// disponible vuelve, comprobado por API) → "Dar de baja" la otra unidad con la confirmación fuerte (serie dada de baja, en mano −1)
// → lista de devoluciones (anticipada) → Reportes de rentas (vista "Devoluciones de renta por motivo" con el motor de Análisis) →
// tarjetas de resumen de la lista de rentas. Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal), cada uno con sus
// propios datos. Capturas: docs/manual/frontend/img/f18-<paso>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })

/** "Hoy" de la compañía demo (America/Puerto_Rico) y días después, 'YYYY-MM-DD'. */
function tenantDay(plus = 0): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Puerto_Rico', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  const d = new Date(`${today}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + plus)
  return d.toISOString().slice(0, 10)
}

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f18-${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F17). */
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

interface Seed {
  headers: Record<string, string>
  clientName: string
  productPublicId: string
  sku: string
  bin: string
  serials: [string, string, string]
  rentalPublicId: string
  number: string
}

/** Siembra por API. `variant` separa escritorio y móvil (letra inicial del sufijo). */
async function seed(request: APIRequestContext, variant: 'D' | 'M'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const suffix = `RD${variant}${LETTERS}`
  const clientName = `Clínica ${suffix}`
  const client = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } }), 'cliente')
  const location = await ok<{ publicId: string }>(
    request.post(`${API_URL}/api/v1/locations`, {
      headers,
      data: { clientPublicId: client.publicId, name: `Piso 2 ${suffix}`, locationType: 'DELIVERY', line1: 'Calle Salud 2', city: 'Mayagüez', country: 'PR' },
    }),
    'localidad',
  )
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const sku = `EQ-${suffix}`
  const created = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Monitor de signos ${suffix}`, trackingType: 'NONE' } }),
    'producto',
  )
  const productPublicId = created.product.publicId
  const binCode = `${suffix}-A`
  const bin = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code: binCode } }), 'posición')
  await ok(
    request.post(`${API_URL}/api/v1/inventory/adjustments`, {
      headers,
      data: { productPublicId, warehousePublicId: wh, binId: bin.id, quantity: 3, reason: 'FOUND', notes: 'Recorrido F18 (devoluciones de renta)' },
    }),
    'ajuste',
  )
  const serials: [string, string, string] = [`SN-${suffix}-1`, `SN-${suffix}-2`, `SN-${suffix}-3`]
  await ok(
    request.post(`${API_URL}/api/v1/products/${productPublicId}/convert-to-serial`, { headers, data: { positions: [{ binId: bin.id, serialNumbers: serials }] } }),
    'convertir a serie',
  )
  const rental = await ok<{ rental: { publicId: string; number: string } }>(
    request.post(`${API_URL}/api/v1/rentals`, {
      headers,
      data: {
        clientPublicId: client.publicId,
        locationPublicId: location.publicId,
        warehousePublicId: wh,
        startDate: tenantDay(-10),
        pickupDate: tenantDay(30),
        contractNumber: `CT-${suffix}`,
        lines: [{ productPublicId, serialNumbers: serials, rate: { frequency: 'MONTHLY', amount: 200 } }],
      },
    }),
    'renta',
  )
  return { headers, clientName, productPublicId, sku, bin: binCode, serials, rentalPublicId: rental.rental.publicId, number: rental.rental.number }
}

async function stock(request: APIRequestContext, s: Seed) {
  const p = await ok<{ product: { qtyOnHand: number; qtyAvailable: number } }>(request.get(`${API_URL}/api/v1/products/${s.productPublicId}`, { headers: s.headers }), 'existencia')
  return { onHand: p.product.qtyOnHand, available: p.product.qtyAvailable }
}

async function serialStatus(request: APIRequestContext, s: Seed, serial: string) {
  const list = await ok<{ serialNumber: string; statusCode: string }[]>(request.get(`${API_URL}/api/v1/products/${s.productPublicId}/serials`, { headers: s.headers }), 'series')
  return list.find((x) => x.serialNumber === serial)?.statusCode
}

/** Fila (escritorio) o tarjeta (móvil) de la tabla que contiene el texto. */
function rowOf(page: Page, mobile: boolean, text: string): Locator {
  return mobile ? page.locator('.dt-card', { hasText: text }) : page.getByRole('row', { name: new RegExp(text) })
}

/** Abre un diálogo de la cola de proceso con la acción de la fila de la serie. */
async function processAction(page: Page, mobile: boolean, serial: string, action: string) {
  await page.goto(`/warehouse/rental-processes?search=${encodeURIComponent(serial)}`)
  const row = rowOf(page, mobile, serial)
  await expect(row).toBeVisible()
  await row.getByRole('button', { name: action }).click()
}

async function expectToast(page: Page, text: string | RegExp) {
  await expect(page.locator('.toast.on', { hasText: text })).toBeVisible()
}

/** El recorrido completo; en móvil además comprueba que nada se sale del ancho. */
async function journey(page: Page, request: APIRequestContext, variant: 'D' | 'M') {
  const mobile = variant === 'M'
  const s = await seed(request, variant)
  const [S1, S2, S3] = s.serials
  const noScroll = async () => {
    if (mobile) await expectNoHorizontalScroll(page)
  }
  const prefix = mobile ? 'movil-' : ''
  await login(page)

  // ---- 1. Programar y despachar (la renta llega en Borrador por API) ----
  await page.goto(`/warehouse/rentals/${s.rentalPublicId}`)
  await expect(page.getByRole('heading', { level: 1 })).toContainText(s.number)
  await page.getByRole('button', { name: 'Programar' }).click()
  await page.getByRole('dialog', { name: `¿Programar la renta ${s.number}?` }).getByRole('button', { name: 'Programar' }).click()
  await expect(page.locator('.head .chip', { hasText: 'Programada' })).toBeVisible()
  await page.getByRole('button', { name: 'Despachar' }).click()
  await page.getByRole('dialog', { name: `¿Despachar la renta ${s.number}?` }).getByRole('button', { name: 'Despachar' }).click()
  await expect(page.locator('.head .chip', { hasText: 'En renta' }).first()).toBeVisible()
  expect(await stock(request, s)).toEqual({ onHand: 3, available: 0 })

  // ---- 2. Registrar devolución anticipada por daño de 2 de los 3 equipos, con proceso ----
  await page.getByRole('button', { name: 'Registrar devolución' }).click()
  const form = page.getByRole('dialog', { name: `Registrar devolución de la renta ${s.number}` })
  await expect(form.getByText('Equipos que vuelven (3 de 3)')).toBeVisible()
  await expect(form.getByLabel(/^Fecha de devolución/)).toHaveValue(tenantDay(0))
  await expect(form.getByText(/Devolución anticipada: la fecha es anterior al recogido/)).toBeVisible()
  // "Otro" sin notas: el mensaje del servidor, sin enviar
  await form.getByLabel(/^Motivo/).selectOption('OTHER')
  await form.getByRole('button', { name: 'Registrar devolución (3)' }).click()
  await expect(form.getByText("Con el motivo 'Otro' describa la devolución en las notas.")).toBeVisible()
  await form.getByLabel(/^Motivo/).selectOption('EARLY_DAMAGE')
  await form.getByLabel(/^Notas$/).fill('Se mojaron en una inundación del piso 2')
  // parcial: el tercero se queda en el cliente
  await form.getByRole('checkbox', { name: new RegExp(`^${S3} `) }).uncheck()
  await expect(form.getByText('Equipos que vuelven (2 de 3)')).toBeVisible()
  for (const serial of [S1, S2]) {
    const line = form.locator('fieldset.ren-ret-line', { hasText: serial })
    await line.getByLabel('Condición').selectOption('DAMAGED')
    await expect(line.getByRole('switch', { name: `¿Pasa por proceso? ${serial}` })).toBeChecked()
  }
  await noScroll()
  await shot(page, `${prefix}registrar-devolucion`)
  await form.getByRole('button', { name: 'Registrar devolución (2)' }).click()
  await expectToast(page, /Devolución DRN-\d{5} registrada: 2 equipo\(s\)\./)
  await expect(form).toBeHidden()
  // la renta sigue En renta (queda S3); el panel Devoluciones enlaza la devolución
  await expect(page.locator('.head .chip', { hasText: 'En renta' }).first()).toBeVisible()
  const retLink = page.getByRole('link', { name: /^DRN-\d{5}$/ }).first()
  await expect(retLink).toBeVisible()
  const drn = ((await retLink.textContent()) ?? '').trim()
  expect(await stock(request, s)).toEqual({ onHand: 3, available: 0 })
  expect(await serialStatus(request, s, S1)).toBe('IN_PROCESS')
  expect(await serialStatus(request, s, S3)).toBe('ON_RENT')
  await noScroll()
  if (!mobile) {
    await page.locator('section.panel', { hasText: 'Devoluciones' }).last().scrollIntoViewIfNeeded()
    await shot(page, 'ficha-renta-devoluciones')
  }

  // ---- 3. Ficha de la devolución (renta ↔ devolución) ----
  await retLink.click()
  await page.waitForURL(/\/warehouse\/rental-returns\/[0-9a-f-]{36}$/)
  await expect(page.getByRole('heading', { level: 1 })).toContainText(drn)
  await expect(page.locator('.head .chip', { hasText: /^Anticipada$/ })).toBeVisible()
  await expect(page.getByText('Pendiente').first()).toBeVisible()
  await expect(page.getByRole('link', { name: `Renta ${s.number}` })).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}devolucion-ficha`)

  // ---- 4. Cola de proceso: salto ilegal (422 tal cual), Inspección → Limpieza → Pruebas y Completar (Lista) ----
  await processAction(page, mobile, S1, 'Avanzar')
  let adv = page.getByRole('dialog', { name: `Avanzar el proceso de ${S1}` })
  await expect(adv.getByLabel('Pasa a')).toHaveValue('INSPECTION')
  await adv.getByLabel('Pasa a').selectOption('TESTING')
  await adv.getByRole('button', { name: 'Avanzar' }).click()
  await expect(adv.getByText("Salto ilegal: de 'PENDING' solo se puede avanzar a 'INSPECTION'.")).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}proceso-avanzar-error`)
  await adv.getByLabel('Pasa a').selectOption('INSPECTION')
  await adv.getByRole('button', { name: 'Avanzar' }).click()
  await expectToast(page, `${S1} pasó a Inspección.`)
  for (const [code, label] of [
    ['CLEANING', 'Limpieza'],
    ['TESTING', 'Pruebas'],
  ] as const) {
    await processAction(page, mobile, S1, 'Avanzar')
    adv = page.getByRole('dialog', { name: `Avanzar el proceso de ${S1}` })
    await expect(adv.getByLabel('Pasa a')).toHaveValue(code)
    await adv.getByRole('button', { name: 'Avanzar' }).click()
    await expectToast(page, `${S1} pasó a ${label}.`)
  }
  await processAction(page, mobile, S1, 'Completar')
  const done = page.getByRole('dialog', { name: `Completar el proceso de ${S1}` })
  await expect(done.getByText(/se libera la reserva/)).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}proceso-completar`)
  await done.getByRole('button', { name: 'Completar' }).click()
  await expectToast(page, `${S1} quedó Lista y disponible.`)
  // el disponible vuelve (1 de los 3)
  expect(await stock(request, s)).toEqual({ onHand: 3, available: 1 })
  expect(await serialStatus(request, s, S1)).toBe('AVAILABLE')

  // ---- 5. Dar de baja la otra unidad (confirmación fuerte) ----
  await page.goto(`/warehouse/rental-processes?search=${encodeURIComponent(s.sku)}`)
  await expect(rowOf(page, mobile, S2)).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}proceso-cola`)
  await processAction(page, mobile, S2, 'Dar de baja')
  const scrap = page.getByRole('dialog', { name: `Dar de baja ${S2}` })
  await expect(scrap.getByText(/motivo «Daño»/)).toBeVisible()
  await scrap.getByRole('button', { name: 'Dar de baja' }).click()
  await expect(scrap.getByText(`Escriba la serie ${S2} para confirmar la baja.`)).toBeVisible()
  await scrap.getByLabel(`Para confirmar, escriba la serie ${S2}`).fill(S2.toLowerCase())
  await noScroll()
  await shot(page, `${prefix}proceso-baja`)
  await scrap.getByRole('button', { name: 'Dar de baja' }).click()
  await expectToast(page, `${S2} quedó dada de baja.`)
  expect(await serialStatus(request, s, S2)).toBe('SCRAPPED')
  expect(await stock(request, s)).toEqual({ onHand: 2, available: 1 })
  // terminados: sin acciones (solo Historial)
  await page.goto(`/warehouse/rental-processes?search=${encodeURIComponent(S2)}&open=all`)
  const finished = rowOf(page, mobile, S2)
  await expect(finished.getByText('Dada de baja')).toBeVisible()
  await expect(finished.getByRole('button', { name: 'Avanzar' })).toHaveCount(0)

  // ---- 6. Lista de devoluciones (anticipadas) ----
  await page.goto('/warehouse/rental-returns?early=true')
  await expect(page.getByRole('heading', { level: 1, name: 'Devoluciones de renta' })).toBeVisible()
  await page.getByRole('searchbox').first().fill(drn)
  const retRow = rowOf(page, mobile, drn)
  await expect(retRow).toBeVisible()
  await expect(retRow.getByText('Anticipada por daño')).toBeVisible()
  await expect(retRow.getByRole('link', { name: s.number })).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}devoluciones-lista`)

  // ---- 7. Reportes de rentas (motor de Análisis) y resumen de la lista de rentas ----
  await page.goto('/warehouse/rentals')
  const summary = page.getByRole('group', { name: 'Resumen de rentas' })
  await expect(summary.getByRole('button', { name: /En renta hoy/ })).toBeVisible()
  await expect(summary.getByRole('button', { name: /En renta hoy/ })).not.toContainText('—')
  await noScroll()
  await shot(page, `${prefix}rentas-resumen`)
  await page.getByRole('link', { name: 'Reportes de rentas' }).click()
  await page.waitForURL(/\/warehouse\/rental-reports/)
  await expect(page.getByRole('heading', { level: 1, name: 'Reportes de rentas' })).toBeVisible()
  await expect(page.getByRole('group', { name: 'Rentas vencidas' })).toBeVisible()
  if (mobile) await page.getByLabel('Vista').selectOption({ label: 'Devoluciones de renta por motivo' })
  else await page.getByRole('button', { name: /Devoluciones de renta por motivo/ }).click()
  await expect(page.getByText('Anticipada por daño').first()).toBeVisible()
  await expect(page.getByRole('group', { name: 'Totales' })).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}reportes`)
}

test.describe('Lote F18 — Devoluciones y proceso de rentas (F-R2)', () => {
  test('escritorio: despachar, devolver anticipadamente por daño con proceso, recorrer el proceso hasta Lista y dar de baja otra unidad', async ({ page, request }, info) => {
    test.skip(info.project.name !== 'escritorio-f18', 'solo en el proyecto escritorio-f18')
    test.setTimeout(240_000)
    await journey(page, request, 'D')
  })

  test('móvil (360 px): el mismo recorrido sin scroll horizontal', async ({ page, request }, info) => {
    test.skip(info.project.name !== 'movil-f18', 'solo en el proyecto movil-f18')
    test.setTimeout(240_000)
    await journey(page, request, 'M')
  })
})
