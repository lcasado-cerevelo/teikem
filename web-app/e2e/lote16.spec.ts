// Recorrido del Lote 16 (Recibo directo a posición) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics"). Solo toca la demo: ALM-01 pasa a "Directo a posición" y vuelve a
// "Con acomodo" en afterAll (y al terminar el recorrido 1 de cada corrida), por eso este archivo corre en sus propios proyectos
// (`escritorio-lote16` y `movil-lote16`, después de los demás: ver playwright.config.ts), para no chocar con los recorridos que
// esperan tareas de acomodo en ALM-01.
// - escritorio (en serie): ficha del almacén con la sección Recepción y su diálogo de cambio; recibo ciego de 2 líneas en
//   directo (Confirmar bloqueado, "Usar posiciones sugeridas", una línea a cuarentena, confirmar → Acomodado, sin acomodo
//   pendiente, Kárdex con las posiciones finales); aviso de cupo ("Excede el cupo de …") que no bloquea; un recibo cambiado a
//   "Con acomodo" en el encabezado genera tareas; Recolección solo ofrece las posiciones del producto con su disponible;
// - móvil (360 px): detalle del recibo directo (con la columna nueva) y ficha del almacén sin scroll horizontal.
// Los productos (P16*-{timestamp}) y la posición con cupo (C16-{timestamp}) son nuevos en cada corrida (creados por API).
// Capturas para el manual: docs/manual/frontend/img/l16-<pantalla>.png (proyecto `escritorio-lote16`).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type WarehouseDto = components['schemas']['WarehouseDto']
type ReceiptDetailDto = components['schemas']['ReceiptDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const WAREHOUSE = 'ALM-01 · Almacén principal'
const STAMP = Date.now()
const SKU_A = `P16A-${STAMP}`
const SKU_B = `P16B-${STAMP}`
const SKU_MA = `P16MA-${STAMP}`
const SKU_MB = `P16MB-${STAMP}`
const SKU_C = `P16C-${STAMP}`
const SKU_D = `P16D-${STAMP}`
const QUARANTINE_BIN = 'Q-01'

test.use({ locale: 'es-PR' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

/** Captura para el manual, cuando ya no hay peticiones pendientes. */
async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}l16-${name}.png`, animations: 'disabled', caret: 'hide' })
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
  await page.waitForURL((url) => url.pathname !== '/login', { timeout: 30_000 })
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
}

async function pickProduct(scope: Page | Locator, label: string | RegExp, sku: string) {
  const box = scope.getByRole('combobox', { name: label })
  await box.click()
  await box.fill(sku)
  await scope.getByRole('option', { name: new RegExp(`^${sku} · `) }).click()
}

async function pickWarehouse(scope: Page | Locator, box: Locator) {
  await box.click()
  await box.fill('ALM-01')
  await scope.getByRole('option', { name: WAREHOUSE, exact: true }).click()
  await expect(box).toHaveValue(WAREHOUSE)
}

async function expectToast(page: Page, text: string | RegExp) {
  await expect(page.locator('.toast').filter({ hasText: text }).first()).toBeVisible()
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que lote15). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    const describe = (el: Element) => `${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')}`
    document.querySelectorAll('.stage *, .bar *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      const r = el.getBoundingClientRect()
      if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${describe(el)} [${Math.round(r.left)}, ${Math.round(r.right)}] fuera de 0..${vw}`)
    })
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

interface Fixture {
  headers: { Authorization: string }
  warehouse: WarehouseDto
  reserveZoneId: number
  /** Posición con cupo de 5 unidades (se crea a demanda con `createCapBin`). */
  capBinId: number
  capBinCode: string
  binIds: Map<string, number>
  products: Record<string, string>
}

/** Datos de la demo (por API): ALM-01, sus posiciones y los productos de esta corrida. */
async function buildFixture(request: APIRequestContext, skus: string[]): Promise<Fixture> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const whs = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers })).json()) as WarehouseDto[]
  const warehouse = whs.find((w) => w.code === 'ALM-01')
  expect(warehouse, 'la demo necesita el almacén ALM-01').toBeTruthy()
  const binsRes = await request.get(`${API_URL}/api/v1/warehouses/${warehouse?.publicId}/bins?take=500`, { headers })
  const bins = ((await binsRes.json()) as { items?: { id?: number; code?: string; zoneId?: number; zoneTypeCode?: string }[] }).items ?? []
  const binIds = new Map<string, number>()
  for (const b of bins) if (b.code && b.id != null) binIds.set(b.code, b.id)
  expect(binIds.has(QUARANTINE_BIN), 'ALM-01 necesita la posición de cuarentena Q-01').toBeTruthy()
  const reserve = bins.find((b) => b.zoneTypeCode === 'RESERVE' && b.zoneId != null)
  expect(reserve, 'ALM-01 necesita una posición en una zona de reserva').toBeTruthy()

  const products: Record<string, string> = {}
  for (const sku of skus) {
    const created = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Producto recibo directo e2e ${STAMP} ${sku}`, trackingType: 'NONE' } })
    expect(created.ok(), await created.text()).toBeTruthy()
    products[sku] = ((await created.json()) as { product: { publicId: string } }).product.publicId
  }
  return { headers, warehouse: warehouse as WarehouseDto, reserveZoneId: reserve?.zoneId ?? 0, capBinId: 0, capBinCode: '', binIds, products }
}

/** Posición nueva de la zona de reserva de ALM-01 con cupo de 5 unidades, vacía (así "caben 5"). */
async function createCapBin(request: APIRequestContext, fx: Fixture, tag: string) {
  const code = `C16${tag}-${STAMP}`
  const res = await request.post(`${API_URL}/api/v1/warehouses/${fx.warehouse.publicId}/bins`, {
    headers: fx.headers,
    data: { zoneId: fx.reserveZoneId, code, maxCapacityQty: 5 },
  })
  expect(res.ok(), await res.text()).toBeTruthy()
  fx.capBinId = ((await res.json()) as { id?: number }).id ?? 0
  fx.capBinCode = code
  expect(fx.capBinId).toBeGreaterThan(0)
}

/** Da de baja la posición con cupo si quedó vacía (si tiene existencia, el API lo rechaza y se deja). */
async function dropCapBin(request: APIRequestContext, fx: Fixture) {
  if (!fx.capBinId) return
  await request.post(`${API_URL}/api/v1/warehouses/${fx.warehouse.publicId}/bins/${fx.capBinId}/deactivate`, { headers: fx.headers, data: {} })
}

/** Cambia el modo de recepción de ALM-01 por API (con el rowVersion vigente). */
async function setWarehouseMode(request: APIRequestContext, fx: Fixture, mode: 'PUTAWAY' | 'DIRECT') {
  const whs = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers: fx.headers })).json()) as WarehouseDto[]
  const current = whs.find((w) => w.publicId === fx.warehouse.publicId)
  if (current?.receivingModeCode === mode) return
  const res = await request.patch(`${API_URL}/api/v1/warehouses/${fx.warehouse.publicId}`, { headers: fx.headers, data: { receivingMode: mode, rowVersion: current?.rowVersion } })
  expect(res.ok(), await res.text()).toBeTruthy()
}

/** Recibo ciego por API con el modo indicado (explícito, para no depender del modo del almacén). */
async function createBlindReceipt(
  request: APIRequestContext,
  fx: Fixture,
  mode: 'PUTAWAY' | 'DIRECT',
  lines: { sku: string; qty: number; targetBinId?: number }[],
): Promise<ReceiptDetailDto> {
  const res = await request.post(`${API_URL}/api/v1/receipts`, {
    headers: fx.headers,
    data: {
      warehousePublicId: fx.warehouse.publicId,
      type: 'BLIND',
      receivingMode: mode,
      lines: lines.map((l) => ({ productPublicId: fx.products[l.sku], receivedQty: l.qty, targetBinId: l.targetBinId })),
    },
  })
  expect(res.ok(), await res.text()).toBeTruthy()
  return (await res.json()) as ReceiptDetailDto
}

/** Completa por API las tareas de acomodo abiertas de un recibo (no deja pendientes en la demo). */
async function finishPutawayTasks(request: APIRequestContext, fx: Fixture, receiptPublicId: string) {
  const detail = (await (await request.get(`${API_URL}/api/v1/receipts/${receiptPublicId}`, { headers: fx.headers })).json()) as ReceiptDetailDto
  for (const task of detail.putawayTasks ?? []) {
    if (task.id == null) continue
    await request.post(`${API_URL}/api/v1/warehouse-tasks/${task.id}/start`, { headers: fx.headers, data: {} })
    await request.post(`${API_URL}/api/v1/warehouse-tasks/${task.id}/complete`, { headers: fx.headers, data: { toBinId: fx.binIds.get('B01-R01-N1-P01') } })
  }
}

/** Abre un recibo en Recibo (maestro-detalle) y devuelve el panel derecho. */
async function openReceipt(page: Page, publicId: string): Promise<Locator> {
  await login(page)
  await page.goto(`/warehouse/receipts?receipt=${publicId}`)
  const detail = page.locator('.rcp-side')
  await expect(detail.getByRole('heading', { level: 2 }).first()).toBeVisible()
  return detail
}

test.describe('Lote 16 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })
  // la rejilla directa pasa a tarjetas bajo 760 px de panel: con 1600 px de ventana se ve como tabla
  test.use({ viewport: { width: 1600, height: 900 } })

  let fx: Fixture
  let blindReceiptId = ''
  let blindReceiptNumericId = 0

  test.beforeAll(async ({ request }) => {
    fx = await buildFixture(request, [SKU_A, SKU_B, SKU_C, SKU_D])
    await setWarehouseMode(request, fx, 'PUTAWAY')
  })

  test.afterAll(async ({ request }) => {
    // ALM-01 vuelve a "Con acomodo" pase lo que pase y la posición con cupo se da de baja si ya no tiene existencia
    await setWarehouseMode(request, fx, 'PUTAWAY')
    await dropCapBin(request, fx)
  })

  test('1. ficha de ALM-01: sección Recepción; pasar a "Directo a posición" pide confirmación con los recibos abiertos', async ({ page, request }) => {
    try {
      await login(page)
      await page.goto(`/warehouse/warehouses/${fx.warehouse.publicId}`)
      const mode = page.getByLabel('Modo de recepción')
      await expect(page.getByText('Recepción', { exact: true }).first()).toBeVisible()
      await expect(mode).toHaveValue('PUTAWAY')
      await expect(page.getByLabel('Posición de recepción por defecto')).toBeVisible()
      await mode.scrollIntoViewIfNeeded()
      await shot(page, 'almacen-recepcion')

      await mode.selectOption({ label: 'Directo a posición' })
      await page.getByRole('button', { name: 'Guardar' }).click()
      const dialog = page.getByRole('dialog', { name: '¿Cambiar el modo de recepción?' })
      await expect(dialog).toBeVisible()
      // los conteos llegan del API: abiertos y con acomodo pendiente
      await expect(dialog).toContainText('Los recibos nuevos de ALM-01 entrarán')
      await expect(dialog).toContainText(/Los [\d,]+ recibos abiertos conservan su modo y los [\d,]+ recibos con acomodo pendiente siguen igual\./)
      await shot(page, 'almacen-recepcion-dialogo')
      await dialog.getByRole('button', { name: 'Cambiar el modo' }).click()
      await expectToast(page, 'Cambios guardados.')
      await expect(mode).toHaveValue('DIRECT')

      const whs = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers: fx.headers })).json()) as WarehouseDto[]
      expect(whs.find((w) => w.code === 'ALM-01')?.receivingModeCode).toBe('DIRECT')
    } catch (err) {
      // si el recorrido falla a medias, ALM-01 no se queda en directo para los demás
      await setWarehouseMode(request, fx, 'PUTAWAY')
      throw err
    }
  })

  test('2. recibo ciego de 2 líneas en directo: Confirmar bloqueado, sugeridas, una línea a cuarentena, Acomodado sin acomodo pendiente y Kárdex con las posiciones finales', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/receipts')
    await page.getByRole('button', { name: 'Nuevo recibo' }).click()
    const modal = page.getByRole('dialog', { name: 'Nuevo recibo' })
    await modal.getByLabel(/^Origen/).selectOption('BLIND')
    await pickWarehouse(modal, modal.getByRole('combobox', { name: /^Almacén/ }))
    // el modo del recibo arranca con el del almacén y no se pide posición de recepción
    await expect(modal.getByLabel('Modo de recepción')).toHaveValue('DIRECT')
    await expect(modal.getByLabel('Posición de recepción')).toHaveCount(0)
    await modal.getByRole('button', { name: 'Crear recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ creado\./)
    await expect(page).toHaveURL(/\/warehouse\/receipts\?receipt=[0-9a-f-]+/)
    blindReceiptId = new URL(page.url()).searchParams.get('receipt') ?? ''
    expect(blindReceiptId).not.toBe('')
    const detail = page.locator('.rcp-side')
    await expect(detail.locator('.chip', { hasText: 'Directo a posición' }).first()).toBeVisible()
    await expect(detail.getByRole('columnheader', { name: 'Posición destino' })).toBeVisible()

    // dos líneas a ciegas: 5 de A y 3 de B
    await pickProduct(detail, 'Producto de la línea 1', SKU_A)
    const received1 = detail.getByRole('textbox', { name: 'Recibido de la línea 1' })
    await received1.fill('5')
    await received1.press('Tab')
    await expect(detail.locator('.chip', { hasText: 'Recibiendo' }).first()).toBeVisible()
    await pickProduct(detail, 'Producto de la línea 2', SKU_B)
    const received2 = detail.getByRole('textbox', { name: 'Recibido de la línea 2' })
    await received2.fill('3')
    await received2.press('Tab')
    await expect(detail.getByRole('combobox', { name: 'Posición destino de la línea 2' })).toBeVisible()

    // sin destino en las dos líneas: Confirmar bloqueado con el motivo y la pista "Sugerida: …"
    await expect(detail.getByRole('button', { name: 'Confirmar recibo' })).toBeDisabled()
    await expect(detail.getByText('Falta la posición destino en 2 línea(s).')).toBeVisible()
    await expect(detail.getByText(/^Sugerida: /).first()).toBeVisible()
    await shot(page, 'recibo-directo')

    // "Usar posiciones sugeridas" llena las que caben y deja el resto a mano
    await detail.getByRole('button', { name: 'Usar posiciones sugeridas' }).click()
    await expectToast(page, /Se asignó posición a \d+ línea\(s\); \d+ sin sugerencia\./)
    await expect(detail.getByRole('combobox', { name: 'Posición destino de la línea 1' })).not.toHaveValue('')
    await expect(detail.getByRole('combobox', { name: 'Posición destino de la línea 2' })).not.toHaveValue('')
    await expect(detail.getByText('Falta la posición destino en')).toHaveCount(0)
    await shot(page, 'recibo-directo-sugeridas')

    // la línea 1 va a la cuarentena Q-01 (la línea 2 se queda en la sugerida)
    const target1 = detail.getByRole('combobox', { name: 'Posición destino de la línea 1' })
    await target1.click()
    await target1.fill(QUARANTINE_BIN)
    await page.getByRole('option', { name: new RegExp(QUARANTINE_BIN) }).first().click()
    await expect(target1).toHaveValue(new RegExp(QUARANTINE_BIN))
    const target2Code = (await detail.getByRole('combobox', { name: 'Posición destino de la línea 2' }).inputValue()).split(/\s/)[0]
    expect(target2Code).not.toBe('')
    await expect(detail.getByRole('button', { name: 'Confirmar recibo' })).toBeEnabled()

    await detail.getByRole('button', { name: 'Confirmar recibo' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirmar recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ confirmado\./)
    // Completado → Acomodado en el mismo momento y sin tareas de acomodo
    await expect(detail.locator('.chip', { hasText: 'Acomodado' }).first()).toBeVisible()
    await expect(detail.getByText('Tareas de acomodo')).toHaveCount(0)
    await expect(detail.getByRole('row').filter({ hasText: 'Pendiente' })).toHaveCount(0)
    await shot(page, 'recibo-acomodado')

    // "Acomodo pendiente": nada de este recibo y, con ALM-01 filtrado, el aviso del almacén directo
    const number = /REC-\d+/.exec((await detail.locator('.ref').first().textContent()) ?? '')?.[0] ?? ''
    expect(number).not.toBe('')
    await page.goto('/warehouse/receipts?tab=putaway')
    await expect(page.getByRole('tab', { name: 'Acomodo pendiente' })).toHaveAttribute('aria-selected', 'true')
    const filterBox = page.getByRole('combobox', { name: /^Almacén/ }).first()
    await filterBox.click()
    await filterBox.fill('ALM-01')
    await page.getByRole('option', { name: /ALM-01/ }).first().click()
    await expect(page.getByText('ALM-01 recibe directo a posición')).toBeVisible()
    await expect(page.getByRole('group', { name: 'Lista de recibos' }).getByRole('button', { name: new RegExp(`${number}\\b`) })).toHaveCount(0)
    await shot(page, 'acomodo-pendiente-aviso')

    // Kárdex del recibo: cada línea entró a su posición final (cuarentena y la sugerida), no a la de recepción
    const list = await (await page.request.get(`${API_URL}/api/v1/receipts/${blindReceiptId}`, { headers: fx.headers })).json()
    blindReceiptNumericId = (list as ReceiptDetailDto).header?.id ?? 0
    expect(blindReceiptNumericId).toBeGreaterThan(0)
    await page.goto(`/warehouse/kardex?refEntity=RECEIPT&refId=${blindReceiptNumericId}`)
    await expect(page.getByRole('group', { name: 'Resumen de movimientos' })).toBeVisible()
    const rowA = page.getByRole('row').filter({ hasText: SKU_A })
    const rowB = page.getByRole('row').filter({ hasText: SKU_B })
    await expect(rowA).toHaveCount(1)
    await expect(rowB).toHaveCount(1)
    await expect(rowA).toContainText(`ALM-01/${QUARANTINE_BIN}`)
    await expect(rowB).toContainText(`ALM-01/${target2Code}`)
    await expect(rowA).toContainText('Recepción')
    await expect(page.getByRole('row').filter({ hasText: 'STG-01' })).toHaveCount(0)
  })

  test('3. aviso de cupo: 8 unidades a una posición de cupo 5 muestra "Excede el cupo de …" y se confirma igual', async ({ page, request }) => {
    await createCapBin(request, fx, 'D')
    const receipt = await createBlindReceipt(request, fx, 'DIRECT', [{ sku: SKU_C, qty: 8, targetBinId: fx.capBinId }])
    const publicId = receipt.header?.publicId ?? ''
    const detail = await openReceipt(page, publicId)
    await expect(detail.locator('.chip', { hasText: 'Directo a posición' }).first()).toBeVisible()
    await expect(detail.getByRole('combobox', { name: 'Posición destino de la línea 1' })).toHaveValue(new RegExp(fx.capBinCode))
    const over = detail.getByText(`Excede el cupo de ${fx.capBinCode}: caben 5`)
    await expect(over).toBeVisible()
    await shot(page, 'recibo-directo-cupo')

    // avisa pero no bloquea: Confirmar sigue activo y el recibo termina Acomodado
    await expect(detail.getByRole('button', { name: 'Confirmar recibo' })).toBeEnabled()
    await detail.getByRole('button', { name: 'Confirmar recibo' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirmar recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ confirmado\./)
    await expect(detail.locator('.chip', { hasText: 'Acomodado' }).first()).toBeVisible()
    // ya confirmado: la posición se muestra como texto con la cantidad que entró
    await expect(detail.getByRole('row').filter({ hasText: SKU_C })).toContainText(fx.capBinCode)
  })

  test('4. un recibo cambiado a "Con acomodo" en el encabezado genera tareas en "Acomodo pendiente"', async ({ page, request }) => {
    const receipt = await createBlindReceipt(request, fx, 'DIRECT', [{ sku: SKU_D, qty: 4 }])
    const publicId = receipt.header?.publicId ?? ''
    const number = receipt.header?.number ?? ''
    const detail = await openReceipt(page, publicId)
    await expect(detail.getByRole('columnheader', { name: 'Posición destino' })).toBeVisible()
    await detail.getByRole('button', { name: 'Editar el encabezado del recibo' }).click()
    const modal = page.getByRole('dialog', { name: /^Encabezado del recibo/ })
    await expect(modal.getByLabel('Modo de recepción')).toHaveValue('DIRECT')
    await modal.getByLabel('Modo de recepción').selectOption({ label: 'Con acomodo' })
    // con acomodo vuelve la posición de recepción
    await expect(modal.getByLabel('Posición de recepción')).toBeVisible()
    await modal.getByRole('button', { name: 'Guardar' }).click()
    await expectToast(page, /Encabezado del recibo REC-\d+ guardado\./)
    await expect(detail.locator('.chip', { hasText: 'Directo a posición' })).toHaveCount(0)
    await expect(detail.getByRole('columnheader', { name: 'Posición destino' })).toHaveCount(0)

    await detail.getByRole('button', { name: 'Confirmar recibo' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirmar recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ confirmado\./)
    await expect(detail.getByRole('row').filter({ hasText: SKU_D }).filter({ hasText: 'Pendiente' })).toHaveCount(1)

    // aparece en Acomodo pendiente con su tarea
    await page.goto('/warehouse/receipts?tab=putaway')
    const item = page.getByRole('group', { name: 'Lista de recibos' }).getByRole('button', { name: new RegExp(`${number}\\b`) })
    await expect(item).toHaveCount(1)
    await item.click()
    await expect(page.getByRole('row').filter({ hasText: SKU_D }).filter({ hasText: 'Pendiente' })).toHaveCount(1)
    await finishPutawayTasks(request, fx, publicId)
  })

  test('5. Recolección: el selector de posición solo ofrece las posiciones del producto con su disponible', async ({ page }) => {
    // SKU_C (recibo del recorrido 3) está solo en la posición con cupo: 8 unidades
    await login(page)
    await page.goto('/warehouse/pick-batches')
    await pickWarehouse(page, page.getByRole('combobox', { name: /^Almacén/ }))
    const bin = page.getByRole('combobox', { name: 'Posición de la línea 1' })
    // sin producto, deshabilitado
    await expect(bin).toBeDisabled()
    await pickProduct(page, 'Producto de la línea 1', SKU_C)
    await expect(bin).toBeEnabled()
    await bin.click()
    // solo las opciones de la lista abierta (con su disponible); otras listas cerradas pueden seguir en el DOM
    const options = page.getByRole('option').filter({ hasText: 'disp.' })
    await expect(options).toHaveCount(1)
    await expect(options.first()).toContainText(fx.capBinCode)
    await expect(options.first()).toContainText('8 disp.')
    await expect(options.first()).toContainText('Sugerida')
    await shot(page, 'recoleccion-posicion')
    await options.first().click()
    await expect(bin).toHaveValue(new RegExp(fx.capBinCode))
    // al cambiar de producto se limpia la posición elegida (la de C no es una posición de D)
    await pickProduct(page, 'Producto de la línea 1', SKU_D)
    await expect(bin).toHaveValue('')
  })
})

test.describe('Lote 16 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  let fx: Fixture

  test.beforeAll(async ({ request }) => {
    fx = await buildFixture(request, [SKU_MA, SKU_MB])
    await createCapBin(request, fx, 'M')
  })

  test.afterAll(async ({ request }) => {
    await dropCapBin(request, fx)
  })

  test('6. detalle del recibo directo (con la columna Posición destino) y ficha del almacén sin scroll horizontal', async ({ page, request }) => {
    const receipt = await createBlindReceipt(request, fx, 'DIRECT', [
      { sku: SKU_MA, qty: 5 },
      { sku: SKU_MB, qty: 8, targetBinId: fx.capBinId },
    ])
    expect(page.viewportSize()?.width).toBe(360)
    await login(page)
    await page.goto(`/warehouse/receipts?receipt=${receipt.header?.publicId}`)
    await expect(page.locator('.chip', { hasText: 'Directo a posición' }).first()).toBeVisible()
    // a este ancho la rejilla pasa a tarjetas: cada una trae su posición destino y el aviso de cupo no la desborda
    await expect(page.getByRole('combobox', { name: 'Posición destino de la línea 2' })).toBeVisible()
    await expect(page.getByText(`Excede el cupo de ${fx.capBinCode}: caben 5`)).toBeVisible()
    await expect(page.getByText('Falta la posición destino en 1 línea(s).')).toBeVisible()
    await page.waitForLoadState('networkidle')
    await expectNoHorizontalScroll(page)

    await page.goto(`/warehouse/warehouses/${fx.warehouse.publicId}`)
    await expect(page.getByLabel('Modo de recepción')).toBeVisible()
    await expect(page.getByLabel('Posición de recepción por defecto')).toBeVisible()
    await page.waitForLoadState('networkidle')
    await expectNoHorizontalScroll(page)
  })
})
