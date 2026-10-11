// Recorrido del Lote F19 (Despacho manual en la web) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics", almacén ALM-01). Siembra por API un producto con 5 unidades en una
// posición propia de ALM-01. En la web, todo en «Recolección y empaque» (sin pantalla ni menú nuevos): el panel Recolección con el modo
// «Despacho manual (sin entrega)»: existencia insuficiente da el 409 del servidor en la cantidad de su fila (nada se descuenta), un
// despacho de 2 con motivo «Muestra» y nota graba el DMA-##### (existencia 5 → 3 comprobada por API), la lista lo muestra con su Tipo,
// el motivo y «Despachado» (filtro Tipo), la ficha (modal y ruta) con motivo, nota y sin Empacar, el enlace al Kárdex (nota
// «DMA-… · Muestra») y Eliminar devuelve el inventario (5) y lo deja «Cancelado». Escritorio y móvil (Pixel 7 a 360 px, sin
// scroll horizontal), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/f19-<paso>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const WAREHOUSE = 'ALM-01 · Almacén principal'
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f19-${name}.png`, animations: 'disabled', caret: 'hide' })
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
  productPublicId: string
  sku: string
  bin: string
}

/** Siembra por API: producto con 5 unidades en una posición propia de ALM-01 (zona de reserva, recolectable). */
async function seed(request: APIRequestContext, variant: 'D' | 'M'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const suffix = `DM${variant}${LETTERS}`
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  const sku = `DM-${suffix}`
  const created = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Muestra médica ${suffix}`, trackingType: 'NONE' } }),
    'producto',
  )
  const productPublicId = created.product.publicId
  const binCode = `${suffix}-A`
  const bin = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code: binCode } }), 'posición')
  await ok(
    request.post(`${API_URL}/api/v1/inventory/adjustments`, {
      headers,
      data: { productPublicId, warehousePublicId: wh, binId: bin.id, quantity: 5, reason: 'FOUND', notes: 'Recorrido F19 (despacho manual)' },
    }),
    'ajuste',
  )
  return { headers, productPublicId, sku, bin: binCode }
}

async function available(request: APIRequestContext, s: Seed) {
  const p = await ok<{ product: { qtyOnHand: number; qtyAvailable: number } }>(request.get(`${API_URL}/api/v1/products/${s.productPublicId}`, { headers: s.headers }), 'existencia')
  return p.product.qtyAvailable
}

async function pickWarehouse(scope: Page | Locator, box: Locator) {
  await box.click()
  await box.fill('ALM-01')
  await scope.getByRole('option', { name: WAREHOUSE, exact: true }).click()
  await expect(box).toHaveValue(WAREHOUSE)
}

async function journey(page: Page, request: APIRequestContext, variant: 'D' | 'M') {
  const prefix = variant === 'D' ? 'd-' : 'm-'
  const s = await seed(request, variant)
  const noScroll = () => expectNoHorizontalScroll(page)
  await login(page)
  try {
    await runJourney(page, request, variant, prefix, s, noScroll)
  } finally {
    // la compañía queda sin motivo por default (el escritorio lo fijó en Ajustes)
    if (variant === 'D') await request.put(`${API_URL}/api/v1/tenant/settings`, { headers: s.headers, data: { defaultManualIssueReason: '' } })
  }
}

async function runJourney(page: Page, request: APIRequestContext, variant: 'D' | 'M', prefix: string, s: Seed, noScroll: () => Promise<void>) {
  // Ajustes → Operación → Despacho manual: «Motivo por default» (solo el escritorio; el móvil elige el motivo explícitamente
  // porque los dos proyectos corren en paralelo sobre la misma compañía)
  if (variant === 'D') {
    await page.goto('/system/settings?tab=ops')
    const settingsPanel = page.locator('.panel', { has: page.getByRole('heading', { name: 'Despacho manual' }) })
    const select = settingsPanel.getByLabel('Motivo por default del despacho manual')
    await expect(select).toBeVisible()
    await expect(settingsPanel).toContainText('Llega preseleccionado al despachar; el motivo sigue siendo obligatorio.')
    await expect(select.locator('option', { hasText: 'Sin motivo por default' })).toHaveCount(1)
    await select.selectOption({ label: 'Muestra' })
    await settingsPanel.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(settingsPanel.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()
    await expect(select).toHaveValue('SAMPLE')
    await shot(page, 'd-ajuste-default')
  }

  // Todo vive en «Recolección y empaque» (menú de Almacén): el panel Recolección gana el modo «Despacho manual (sin entrega)»
  await page.goto('/warehouse/pick-batches')
  await expect(page.getByRole('heading', { name: 'Recolección y empaque', level: 1 })).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}lista-inicial`)

  // Modo manual: motivo y nota; existencia insuficiente = 409 del servidor en la cantidad de su fila, sin descontar nada
  const panel = page.locator('.collect-panel')
  await pickWarehouse(panel, panel.getByRole('combobox', { name: /^Almacén/ }))
  await panel.getByRole('switch', { name: 'Despacho manual (sin entrega)' }).check()
  const product = panel.getByRole('combobox', { name: 'Producto de la línea 1' })
  await product.click()
  await product.fill(s.sku)
  await panel.getByRole('option', { name: new RegExp(`^${s.sku} · `) }).click()
  await panel.getByLabel('Cantidad de la línea 1').fill('99')
  if (variant === 'D') {
    // llega PRESELECCIONADO con el default de la compañía: no se toca el selector
    await expect(panel.getByLabel(/^Motivo/)).toHaveValue('SAMPLE')
  } else {
    await panel.getByLabel(/^Motivo/).selectOption({ label: 'Muestra' })
  }
  // la nota es opcional y está colapsada detrás de «Agregar nota»
  await expect(panel.getByLabel('Nota')).toHaveCount(0)
  await panel.getByRole('button', { name: 'Agregar nota' }).click()
  await panel.getByLabel('Nota').fill('Feria de salud')
  await panel.getByRole('button', { name: 'Despachar (bajar de inventario)' }).click()
  await expect(panel.getByLabel('Cantidad de la línea 1')).toHaveAttribute('aria-invalid', 'true')
  await expect(panel.getByRole('alert').filter({ hasText: /Inventario insuficiente de .* disponible 5, solicitado 99\./ }).first()).toBeVisible()
  expect(await available(request, s)).toBe(5)
  await noScroll()
  await shot(page, `${prefix}409-sin-existencia`)

  // Cantidad válida: un solo POST con Idempotency-Key; el servidor numera DMA-#####
  await panel.getByLabel('Cantidad de la línea 1').fill('2')
  const posted = page.waitForResponse((r) => r.url().includes('/api/v1/manual-issues') && r.request().method() === 'POST')
  await panel.getByRole('button', { name: 'Despachar (bajar de inventario)' }).click()
  const res = await posted
  expect(res.ok(), await res.text()).toBeTruthy()
  expect(res.request().headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
  const issue = (await res.json()) as { publicId: string; number: string; reasonCode: string; note: string }
  expect(issue.number).toMatch(/^DMA-\d{5,}$/)
  expect(issue.reasonCode).toBe('SAMPLE')
  await expect(page.locator('.toast').filter({ hasText: `Despacho manual ${issue.number} registrado.` }).first()).toBeVisible()
  expect(await available(request, s)).toBe(3)
  // el panel queda listo para el siguiente: el último motivo usado sigue elegido y la nota vuelve a su botón
  await expect(panel.getByLabel(/^Motivo/)).toHaveValue('SAMPLE')
  await expect(panel.getByRole('button', { name: 'Agregar nota' })).toBeVisible()

  // La lista muestra el DMA con su tipo, el motivo y «Despachado»; el filtro Tipo y la búsqueda libre
  // la lista del panel derecho pasa a tarjetas cuando mide menos de 640 px (también en escritorio)
  const row = page.locator('.collect-list tr, .collect-list .dt-card').filter({ hasText: issue.number })
  await expect(row.first()).toBeVisible()
  await expect(row.first()).toContainText('Despachado')
  await expect(row.first()).toContainText('Despacho manual · Muestra')
  await page.getByLabel('Tipo', { exact: true }).selectOption({ label: 'Despachos manuales' })
  await expect(row.first()).toBeVisible()
  await page.getByLabel('Tipo', { exact: true }).selectOption({ label: 'Empaques' })
  await expect(page.getByText(issue.number)).toHaveCount(0)
  await page.getByLabel('Tipo', { exact: true }).selectOption({ label: 'Todos' })
  await expect(row.first()).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}lista`)

  // Ficha en un modal: motivo, nota, quién y cuándo, líneas con costo y enlace al Kárdex
  await row.first().click()
  const detail = page.getByRole('dialog', { name: new RegExp(issue.number) })
  await expect(detail.getByRole('button', { name: 'Empacar' })).toHaveCount(0)
  await expect(detail.getByTestId('mi-reason')).toHaveText('Muestra')
  await expect(detail.getByTestId('mi-note')).toHaveText('Feria de salud')
  await expect(detail.getByText(s.bin).first()).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}ficha-modal`)
  await detail.getByRole('link', { name: 'Ver los movimientos' }).click()
  await expect(page).toHaveURL(/\/warehouse\/kardex\?refEntity=PICK_BATCH&refId=\d+/)
  await expect(page.getByText(new RegExp(`Despacho manual ${issue.number}`)).first()).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}kardex`)

  // Ficha por su ruta y Eliminar con reversa
  await page.goto(`/warehouse/pick-batches/${issue.publicId}`)
  await expect(page.getByRole('heading', { name: new RegExp(issue.number), level: 1 })).toBeVisible()
  await noScroll()
  await shot(page, `${prefix}ficha`)
  await page.getByRole('button', { name: 'Eliminar', exact: true }).click()
  const confirm = page.getByRole('dialog', { name: 'Eliminar despacho manual' })
  await expect(confirm).toContainText(`¿Eliminar el despacho manual ${issue.number}?`)
  await shot(page, `${prefix}eliminar`)
  await confirm.getByRole('button', { name: 'Eliminar', exact: true }).click()
  await expect(page.locator('.toast').filter({ hasText: `Despacho manual ${issue.number} eliminado.` }).first()).toBeVisible()
  await expect(page.locator('.chip', { hasText: 'Cancelado' }).first()).toBeVisible()
  expect(await available(request, s)).toBe(5)
  await noScroll()
  await shot(page, `${prefix}cancelado`)
}

test.describe('Lote F19 — Despacho manual en la web', () => {
  test('escritorio: despachar con motivo y nota, ver el DMA y su ficha, 409 por existencia y eliminar con reversa', async ({ page, request }, info) => {
    test.skip(info.project.name !== 'escritorio-f19', 'solo en el proyecto escritorio-f19')
    test.setTimeout(240_000)
    await journey(page, request, 'D')
  })

  test('móvil (360 px): el mismo recorrido sin scroll horizontal', async ({ page, request }, info) => {
    test.skip(info.project.name !== 'movil-f19', 'solo en el proyecto movil-f19')
    test.setTimeout(240_000)
    await journey(page, request, 'M')
  })
})
