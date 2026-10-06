// Recorrido del Lote F17 (Rentas F-R1) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo "Advance Logistics", almacén ALM-01, módulo Rentas encendido por el aprovisionamiento demo).
// Siembra por API un cliente con su localidad, un producto SIN seguimiento con 3 unidades (2 en una posición y 1 en otra de la
// zona RSV de ALM-01). En la web: "Convertir a serie" en la ficha del producto (repetida y conteo con el mensaje exacto, luego
// la confirmación de neto cero) → Almacén → Rentas → "Nueva renta" con el cliente, la localidad, fechas, contrato y 2 equipos
// por serie con tarifa mensual → Programar → Despachar (las series quedan "En renta" y el disponible baja, el en mano no) →
// Extender (el 400 de fecha y el de motivo, luego bien con tarifa nueva) → la lista con "por vencer"/"vencidas" muestra la renta
// con su vencimiento calculado → "Necesita tu atención" del Pulso muestra el aviso de rentas (texto RENTAL_DUE). Escritorio y
// móvil (Pixel 7 a 360 px, sin scroll horizontal), cada uno con sus propios datos. Capturas: docs/manual/frontend/img/f17-<paso>.png.
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
  await page.screenshot({ path: `${IMG_DIR}f17-${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F16). */
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
  suffix: string
  headers: Record<string, string>
  clientName: string
  locationName: string
  productPublicId: string
  sku: string
  binA: { id: number; code: string }
  binB: { id: number; code: string }
  serials: [string, string, string]
}

/** Siembra por API. `variant` separa escritorio y móvil (letra inicial del sufijo). */
async function seed(request: APIRequestContext, variant: 'D' | 'M'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const suffix = `RF${variant}${LETTERS}`
  const clientName = `Hospital ${suffix}`
  const client = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } }), 'cliente')
  const locationName = `Sala ${suffix}`
  await ok(
    request.post(`${API_URL}/api/v1/locations`, {
      headers,
      data: { clientPublicId: client.publicId, name: locationName, locationType: 'DELIVERY', line1: 'Calle Hospital 1', city: 'Ponce', country: 'PR' },
    }),
    'localidad',
  )
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const sku = `EQ-${suffix}`
  const created = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Concentrador de oxígeno ${suffix}`, trackingType: 'NONE' } }),
    'producto',
  )
  const productPublicId = created.product.publicId
  const mk = async (code: string) => {
    const b = await ok<{ id: number; code: string }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code } }), 'posición')
    return { id: b.id, code }
  }
  const binA = await mk(`${suffix}-A`)
  const binB = await mk(`${suffix}-B`)
  for (const [bin, quantity] of [
    [binA, 2],
    [binB, 1],
  ] as const) {
    await ok(
      request.post(`${API_URL}/api/v1/inventory/adjustments`, {
        headers,
        data: { productPublicId, warehousePublicId: wh, binId: bin.id, quantity, reason: 'FOUND', notes: 'Recorrido F17 (rentas)' },
      }),
      'ajuste',
    )
  }
  return { suffix, headers, clientName, locationName, productPublicId, sku, binA, binB, serials: [`SN-${suffix}-1`, `SN-${suffix}-2`, `SN-${suffix}-3`] }
}

/** Elige una opción en un combobox con buscador (ClientPicker, ComboSelect, ProductPicker, WarehousePicker). */
async function pick(scope: Page | Locator, label: string | RegExp, text: string, option: RegExp) {
  const box = scope.getByRole('combobox', { name: label })
  await box.click()
  await box.fill(text)
  await scope.getByRole('option', { name: option }).first().click()
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

  // ---- 1. Convertir a serie (ficha del producto) ----
  await page.goto(`/warehouse/products/${s.productPublicId}?tab=serials`)
  await page.getByRole('button', { name: 'Convertir a serie' }).click()
  const conv = page.getByRole('dialog', { name: `Convertir ${s.sku} a serie` })
  const boxA = conv.getByLabel(new RegExp(`Series para ${s.binA.code} · ALM-01 \\(2 en mano\\)`))
  const boxB = conv.getByLabel(new RegExp(`Series para ${s.binB.code} · ALM-01 \\(1 en mano\\)`))
  await boxA.fill(`${S1}\n${S1.toLowerCase()}`)
  await boxB.fill(S3)
  await conv.getByRole('button', { name: 'Revisar y convertir' }).click()
  await expect(conv.getByText(`El número de serie ${S1.toLowerCase()} está repetido.`).first()).toBeVisible()
  await boxA.fill(S1)
  await expect(conv.getByText(`Capture 2 número(s) de serie para ${s.binA.code} (hay 1).`).first()).toBeVisible()
  await boxA.fill(`${S1}\n${S2}`)
  await expect(conv.getByText('2 de 2 series')).toBeVisible()
  await noScroll()
  if (!mobile) await shot(page, 'convertir-captura')
  await conv.getByRole('button', { name: 'Revisar y convertir' }).click()
  await expect(conv.getByText(/Es un movimiento neto cero/)).toBeVisible()
  await expect(conv.getByText(`Se darán de alta 3 series de ${s.sku} en 2 posición(es).`)).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}convertir-confirmar`)
  await conv.getByRole('button', { name: 'Convertir a serie' }).click()
  await expect(page.getByText(`${s.sku} ahora se controla por serie: 3 series dadas de alta.`)).toBeVisible()
  await expect(conv).toBeHidden()
  await expect(page.getByRole('button', { name: 'Convertir a serie' })).toHaveCount(0)
  const product = await ok<{ product: { trackingTypeCode: string; qtyOnHand: number; qtyAvailable: number } }>(
    request.get(`${API_URL}/api/v1/products/${s.productPublicId}`, { headers: s.headers }),
    'producto convertido',
  )
  expect(product.product).toMatchObject({ trackingTypeCode: 'SERIAL', qtyOnHand: 3, qtyAvailable: 3 })

  // ---- 2. Nueva renta (Borrador) ----
  if (mobile) await page.goto('/warehouse/rentals')
  else {
    const menu = page.getByRole('complementary', { name: 'Menú principal' })
    const group = menu.getByRole('button', { name: 'Almacén' })
    if ((await group.getAttribute('aria-expanded')) === 'false') await group.click()
    await menu.getByRole('link', { name: 'Rentas' }).click()
  }
  await expect(page.getByRole('heading', { level: 1, name: 'Rentas' })).toBeVisible()
  await page.getByRole('button', { name: 'Nueva renta' }).click()
  const form = page.getByRole('dialog', { name: 'Nueva renta (Borrador)' })
  await pick(form, /^Cliente/, s.clientName, new RegExp(s.clientName))
  await pick(form, /^Localidad del cliente/, s.locationName, new RegExp(s.locationName))
  const wh = form.getByRole('combobox', { name: /^Almacén de origen/ })
  if ((await wh.inputValue()) === '') await pick(form, /^Almacén de origen/, 'ALM-01', /^ALM-01/)
  await form.getByLabel(/^Fecha de inicio/).fill(tenantDay(0))
  await form.getByLabel(/^Fecha de recogido/).fill(tenantDay(3))
  await form.getByLabel('Número de contrato').fill(`CT-${s.suffix}`)
  await form.getByLabel('Costo de transporte estimado').fill('45')
  // equipos por serie: el producto, dos de sus tres series y una tarifa mensual
  await pick(form, 'Equipo (SKU o nombre)', s.sku, new RegExp(`^${s.sku} · `))
  await expect(form.getByText(/Series disponibles en ALM-01 \(3\)/)).toBeVisible()
  await form.getByRole('checkbox', { name: new RegExp(S1) }).check()
  // la serie se puede escanear: Enter con la serie exacta la elige
  const scan = form.getByLabel('Buscar o escanear serie')
  await scan.fill(S2)
  await scan.press('Enter')
  await expect(form.getByRole('checkbox', { name: new RegExp(S2) })).toBeChecked()
  await form.getByLabel('Frecuencia de cobro').selectOption('MONTHLY')
  await form.getByLabel('Monto').fill('150')
  await form.getByRole('button', { name: 'Agregar 2 equipo(s)' }).click()
  await expect(form.getByRole('heading', { name: 'Equipos a rentar (2)' })).toBeVisible()
  // ya elegida: no se ofrece de nuevo; escanearla avisa
  await scan.fill(S1)
  await scan.press('Enter')
  await expect(form.getByText(`La serie ${S1} ya está en esta renta.`)).toBeVisible()
  await scan.fill('')
  await noScroll()
  await shot(page, `${prefix}nueva-renta`)
  await form.getByRole('button', { name: 'Crear renta con 2 equipo(s)' }).click()
  await page.waitForURL(/\/warehouse\/rentals\/[0-9a-f-]{36}$/)
  const heading = page.getByRole('heading', { level: 1 })
  await expect(heading).toContainText(s.clientName)
  const number = ((await heading.locator('.ref').textContent()) ?? '').trim()
  expect(number).toMatch(/^REN-\d{5}$/)
  await expect(page.locator('.head .chip', { hasText: 'Borrador' })).toBeVisible()
  const equipment = page.getByRole('table', { name: 'Equipos' }).or(page.locator('[aria-label="Equipos"]'))
  await expect(equipment.getByText(S1).first()).toBeVisible()

  // ---- 3. Programar y despachar ----
  await page.getByRole('button', { name: 'Programar' }).click()
  const sched = page.getByRole('dialog', { name: `¿Programar la renta ${number}?` })
  await expect(sched.getByText(/quedan reservados para esta renta/)).toBeVisible()
  await sched.getByLabel(/^Comentario/).fill('Confirmado con el hospital')
  await sched.getByRole('button', { name: 'Programar' }).click()
  await expect(page.locator('.head .chip', { hasText: 'Programada' })).toBeVisible()
  const reserved = await ok<{ product: { qtyOnHand: number; qtyAvailable: number } }>(request.get(`${API_URL}/api/v1/products/${s.productPublicId}`, { headers: s.headers }), 'reserva')
  expect(reserved.product).toMatchObject({ qtyOnHand: 3, qtyAvailable: 1 })

  await page.getByRole('button', { name: 'Despachar' }).click()
  const disp = page.getByRole('dialog', { name: `¿Despachar la renta ${number}?` })
  await expect(disp.getByText(/EN-RENTA del almacén ALM-01/)).toBeVisible()
  await noScroll()
  if (!mobile) await shot(page, 'despachar')
  await disp.getByRole('button', { name: 'Despachar' }).click()
  await expect(page.locator('.head .chip', { hasText: 'En renta' }).first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Cancelar renta' })).toHaveCount(0)
  const serials = await ok<{ serialNumber: string; statusCode: string; binCode: string }[]>(
    request.get(`${API_URL}/api/v1/products/${s.productPublicId}/serials`, { headers: s.headers }),
    'series',
  )
  expect(serials.filter((x) => x.serialNumber === S1 || x.serialNumber === S2).map((x) => [x.statusCode, x.binCode])).toEqual([
    ['ON_RENT', 'EN-RENTA'],
    ['ON_RENT', 'EN-RENTA'],
  ])

  // ---- 4. Extender (el 400 de fecha y el de motivo, luego bien) ----
  await page.getByRole('button', { name: 'Extender' }).click()
  const ext = page.getByRole('dialog', { name: `Extender la renta ${number}` })
  await ext.getByLabel(/^Nueva fecha de recogido/).fill(tenantDay(3))
  await ext.getByRole('button', { name: 'Extender' }).click()
  await expect(ext.getByText(`La nueva fecha de recogido debe ser posterior a la actual (${tenantDay(3)}).`)).toBeVisible()
  await expect(ext.getByText('Indique el motivo de la extensión.')).toBeVisible()
  await ext.getByLabel(/^Nueva fecha de recogido/).fill(tenantDay(5))
  await ext.getByLabel(/^Motivo/).fill('El hospital pidió dos días más')
  await ext.getByLabel('Frecuencia de cobro').selectOption('MONTHLY')
  await ext.getByLabel('Monto').fill('165')
  await noScroll()
  await shot(page, `${prefix}extender`)
  await ext.getByRole('button', { name: 'Extender' }).click()
  await expect(ext).toBeHidden()
  const extensions = page.getByRole('region', { name: 'Extensiones' }).or(page.locator('section.panel', { hasText: 'Extensiones' }))
  await expect(extensions.getByText('El hospital pidió dos días más').first()).toBeVisible()
  await expect(page.getByText('Vence en 5 días').first()).toBeVisible()
  await expect(page.getByText(/Renta en renta|En renta/).first()).toBeVisible()
  await noScroll()
  await page.evaluate(() => window.scrollTo(0, 0))
  await shot(page, `${prefix}ficha`)

  // ---- 5. La lista: por vencer o vencidas, con el vencimiento calculado ----
  await page.goto('/warehouse/rentals?dueWithinDays=7&overdue=true')
  await expect(page.getByLabel('Vencen en (días)')).toHaveValue('7')
  await expect(page.getByRole('switch', { name: 'Solo vencidas' })).toBeChecked()
  await page.getByRole('searchbox').first().fill(number)
  const row = mobile ? page.locator('.dt-card', { hasText: number }) : page.getByRole('row', { name: new RegExp(number) })
  await expect(row).toBeVisible()
  await expect(row.getByText('Vence en 5 días')).toBeVisible()
  await expect(row.getByText('En renta')).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}lista`)

  // ---- 6. "Necesita tu atención" (Pulso): aviso de rentas vencidas o por vencer ----
  await page.goto('/')
  const inbox = page.locator('section.inbox')
  await expect(inbox).toBeVisible()
  const review = inbox.getByRole('link', { name: new RegExp(`Revisar la renta ${number}`) })
  if ((await review.count()) > 0) {
    await expect(inbox.getByText(`Renta ${number}: vence en 5 días`)).toBeVisible()
    await shot(page, `${prefix}aviso`)
    await review.click()
    await page.waitForURL(/\/warehouse\/rentals\/[0-9a-f-]{36}$/)
    await expect(page.getByRole('heading', { level: 1 })).toContainText(number)
  } else {
    // con más de 5 avisos más antiguos, la renta se alcanza con "Ver todos" del grupo de rentas
    const all = inbox.getByRole('link', { name: /Rentas vencidas o por vencer|Ver todos/ }).last()
    await expect(all).toBeVisible()
    await shot(page, `${prefix}aviso`)
    await all.click()
    await page.waitForURL(/\/warehouse\/rentals\?/)
    await expect(page.getByLabel('Vencen en (días)')).toHaveValue('7')
  }
  await noScroll()
}

test.describe('Lote F17 — Rentas (F-R1)', () => {
  test('escritorio: convertir a serie, crear, programar, despachar y extender una renta; lista y aviso', async ({ page, request }, info) => {
    test.skip(info.project.name !== 'escritorio-f17', 'solo en el proyecto escritorio-f17')
    test.setTimeout(240_000)
    await journey(page, request, 'D')
  })

  test('móvil (360 px): el mismo recorrido sin scroll horizontal', async ({ page, request }, info) => {
    test.skip(info.project.name !== 'movil-f17', 'solo en el proyecto movil-f17')
    test.setTimeout(240_000)
    await journey(page, request, 'M')
  })
})
