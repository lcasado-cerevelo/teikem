// Recorrido del Lote 246 (Lote F del plan de Solutions): Compras con plantilla del usuario y QuickBooks, contra el API real (db-update hecho, API en API_URL; compañía demo «Advance Logistics»).
// - siembra por API: proveedor, dos productos, una plantilla PURCHASE_ORDER, una importación con esa plantilla (origen Excel, enviadas) y una orden de Teikem enviada;
// - escritorio: Sistema → Plantillas (pestaña Órdenes de compra), el importador con el origen Plantilla, la lista con Origen y QuickBooks (filas ≤ 64 px), el diálogo de QuickBooks
//   (exportar con descarga, «¿Ya las importaste?», «Sí, ya están»), la ficha con la franja de QuickBooks, quitar la marca y Ajustes → QuickBooks;
// - móvil (360 px): lista y diálogo sin scroll horizontal.
// El archivo se baja de verdad (evento download); nunca se manda nada a QuickBooks ni correo (el API de pruebas va sin Brevo).
// Capturas para el manual: docs/manual/frontend/img/l246-<pantalla>.png (proyecto 'escritorio'); además claro/oscuro y es/en a la carpeta temporal de la corrida.
import { mkdirSync } from 'node:fs'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { API_URL, apiHeaders, enableModule, login, ok, shot } from './staffHelpers'

const STAMP = `${Date.now()}`.slice(-7)
const SUPPLIER = `Proveedor L246 ${STAMP}`
const SKU1 = `L246A-${STAMP}`
const SKU2 = `L246B-${STAMP}`
const DOC1 = `L246-${STAMP}-1`
const DOC2 = `L246-${STAMP}-2`
const TEMPLATE = `Compras semanales ${STAMP}`
const SCRATCH = process.env.E2E_SHOTS_DIR ?? '/tmp/l246-shots'

test.use({ locale: 'es-PR' })

type Headers = Record<string, string>
const seeded: { supplierId: number; templateId: string; teikemOrder: string; teikemNumber: string } = { supplierId: 0, templateId: '', teikemOrder: '', teikemNumber: '' }

async function seed(request: APIRequestContext, headers: Headers) {
  if (seeded.supplierId) return
  const supplier = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/suppliers`, { headers, data: { name: SUPPLIER, contactName: 'Ana Compras', email: `ana+${STAMP}@proveedor.test`, paymentTerm: 'NET30' } }), 'proveedor')
  for (const [sku, cost] of [[SKU1, 2.5], [SKU2, 4]] as const) await ok(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Producto ${sku}`, trackingType: 'NONE', purchaseCost: cost } }), `producto ${sku}`)
  const tpl = await ok<{ publicId: string }>(
    request.post(`${API_URL}/api/v1/purchase-orders/import-templates`, {
      headers,
      data: { name: TEMPLATE, delimiter: ';', hasHeader: false, columns: [{ position: 1, field: 'supplier' }, { position: 2, field: 'documentNumber' }, { position: 3, field: 'orderDate' }, { position: 4, field: 'sku' }, { position: 5, field: 'quantity' }, { position: 6, field: 'unitCost' }] },
    }),
    'plantilla',
  )
  const whs = await ok<{ items?: { publicId: string; code?: string; isMain?: boolean }[] } | { publicId: string; code?: string; isMain?: boolean }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const whList = Array.isArray(whs) ? whs : (whs.items ?? [])
  const warehousePublicId = (whList.find((w) => w.isMain) ?? whList[0]).publicId
  const csv = [`${SUPPLIER};${DOC1};2026-11-02;${SKU1};10;2.50`, `${SUPPLIER};${DOC1};2026-11-02;${SKU2};4;4.00`, `${SUPPLIER};${DOC2};2026-11-03;${SKU1};6;2.50`].join('\n')
  const imported = await ok<{ created: number }>(
    request.post(`${API_URL}/api/v1/purchase-orders/import`, { headers, multipart: { source: 'TEMPLATE', templatePublicId: tpl.publicId, status: 'SENT', commit: 'true', warehousePublicId, file: { name: 'compras.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) } } }),
    'importación',
  )
  expect(imported.created).toBe(2)
  const po = await ok<{ publicId: string; number: string }>(
    request.post(`${API_URL}/api/v1/purchase-orders`, { headers, data: { supplierId: supplier.id, warehousePublicId, lines: [{ productPublicId: (await productId(request, headers, SKU1)), qtyOrdered: 3, unitCost: 2.5 }] } }),
    'orden de Teikem',
  )
  await ok(request.post(`${API_URL}/api/v1/purchase-orders/${po.publicId}/send`, { headers, data: {} }), 'enviar')
  Object.assign(seeded, { supplierId: supplier.id, templateId: tpl.publicId, teikemOrder: po.publicId, teikemNumber: po.number })
}

async function productId(request: APIRequestContext, headers: Headers, sku: string): Promise<string> {
  const list = await ok<{ items?: { publicId: string; sku: string }[] } | { publicId: string; sku: string }[]>(request.get(`${API_URL}/api/v1/products?q=${encodeURIComponent(sku)}`, { headers }), 'producto')
  const items = Array.isArray(list) ? list : (list.items ?? [])
  return items.find((p) => p.sku === sku)!.publicId
}

/** Sin scroll horizontal de página (a 360 px también). */
async function expectNoHScroll(page: Page) {
  const w = await page.evaluate(() => ({ s: document.documentElement.scrollWidth, i: window.innerWidth }))
  expect(w.s, `scrollWidth ${w.s} > innerWidth ${w.i}`).toBeLessThanOrEqual(w.i + 1)
}

/** Foto de verificación en claro/oscuro y es/en (carpeta temporal; no va al manual). */
async function variants(page: Page, name: string) {
  mkdirSync(SCRATCH, { recursive: true })
  for (const theme of ['light', 'dark'] as const) {
    await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), theme)
    await page.screenshot({ path: `${SCRATCH}/${name}-${theme}.png`, fullPage: false })
  }
  await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'light'))
}

async function openQuickBooks(page: Page) {
  await page.goto('/warehouse/purchase-orders')
  await page.locator('.head').getByRole('button', { name: 'QuickBooks', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Pasar órdenes de compra a QuickBooks' })
  await expect(dialog.getByText(SUPPLIER).first()).toBeVisible()
  return dialog
}

/** Deja elegidas solo las órdenes de esta corrida: desmarca todo, busca por el prefijo del documento y marca lo que queda en la vista. */
async function pickMine(page: Page, dialog: ReturnType<Page['getByRole']>) {
  const all = dialog.getByRole('checkbox', { name: /^Todas las de la vista/ })
  await all.uncheck()
  await dialog.getByRole('searchbox').fill(SUPPLIER)
  await expect(dialog.getByText(SUPPLIER).first()).toBeVisible()
  await dialog.getByRole('checkbox', { name: /^Todas las de la vista/ }).check()
}

test.describe('escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })

  test('1. Sistema → Plantillas tiene la pestaña Órdenes de compra, sin columna de cliente', async ({ page, request }) => {
    await seed(request, await apiHeaders(request))
    // Sistema → Plantillas se abre desde Integraciones: si el administrador no es el de la plataforma, el módulo ya debe estar encendido
    await enableModule(request, 'INTEGRATIONS').catch(() => undefined)
    await login(page)
    await page.goto('/system/templates')
    await page.getByRole('tab', { name: 'Órdenes de compra' }).click()
    const row = page.getByRole('row').filter({ hasText: TEMPLATE })
    await expect(row).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Cliente' })).toHaveCount(0)
    expect(((await row.boundingBox())?.height ?? 999)).toBeLessThanOrEqual(64)
    await expectNoHScroll(page)
    await shot(page, false, 'l246-plantillas-compras')
    await variants(page, 'plantillas')
  })

  test('2. el editor exige documentNumber, sku, quantity y proveedor', async ({ page }) => {
    await login(page)
    await page.goto('/system/templates')
    await page.getByRole('tab', { name: 'Órdenes de compra' }).click()
    await page.getByRole('button', { name: /Nueva plantilla/ }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel(/^Nombre/).fill(`Incompleta ${STAMP}`)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText(/Indique al menos una columna|La plantilla debe/).first()).toBeVisible()
    await expect(dialog.getByText(/cliente/i)).toHaveCount(0)
    await shot(page, false, 'l246-plantilla-editor', 1000, dialog)
  })

  test('3. el importador ofrece el origen Plantilla con las plantillas activas y revisa un archivo', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/purchase-orders')
    await page.getByRole('button', { name: 'Importar', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: 'Importar órdenes de compra' })
    await dialog.getByLabel('Origen del archivo').selectOption('TEMPLATE')
    await expect(dialog.getByLabel('Plantilla')).toContainText(TEMPLATE)
    await dialog.getByLabel('Plantilla').selectOption({ label: TEMPLATE })
    // el mismo archivo ya se importó por API: todo sale «Ya importada» y con costos distintos para el mismo SKU se bloquea
    const csv = [`${SUPPLIER};L246-${STAMP}-X;2026-11-04;${SKU1};1;2.50`, `${SUPPLIER};L246-${STAMP}-X;2026-11-04;${SKU1};1;3.00`, `${SUPPLIER};${DOC1};2026-11-02;${SKU1};10;2.50`].join('\n')
    await dialog.getByLabel('Elegir archivo').setInputFiles({ name: 'compras.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) })
    await dialog.getByRole('button', { name: 'Revisar' }).click()
    await expect(dialog.getByText(/viene en varias líneas de la orden con costos distintos/)).toBeVisible()
    await expect(dialog.getByText('Ya importada').first()).toBeVisible()
    await expect(dialog.getByText(`Plantilla: ${TEMPLATE}`)).toBeVisible()
    await expectNoHScroll(page)
    await shot(page, false, 'l246-importar-plantilla', 1000, dialog)
    await variants(page, 'importar')
  })

  test('4. la lista muestra Origen y QuickBooks con chips, filas bajas y los dos filtros', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/purchase-orders')
    const excel = page.getByRole('row').filter({ hasText: SUPPLIER }).filter({ hasText: 'Excel / archivo' }).first()
    await expect(excel).toBeVisible()
    await expect(excel.getByText('Excel / archivo')).toBeVisible()
    await expect(excel.getByText('Por pasar')).toBeVisible()
    for (const r of await page.locator('tbody tr').all()) expect((await r.boundingBox())?.height ?? 0).toBeLessThanOrEqual(64)
    await page.getByLabel('Origen', { exact: true }).selectOption('EXCEL')
    await expect(page.getByRole('row').filter({ hasText: SUPPLIER }).first()).toBeVisible()
    await expect(page.getByRole('row').filter({ hasText: seeded.teikemNumber })).toHaveCount(0)
    await page.getByLabel('Origen', { exact: true }).selectOption('')
    await expectNoHScroll(page)
    await shot(page, false, 'l246-ordenes-compra')
    await variants(page, 'lista')
  })

  test('5. exportar baja el .iif, pregunta «¿Ya las importaste?» y «Sí, ya están» las deja en QuickBooks', async ({ page }) => {
    await login(page)
    const dialog = await openQuickBooks(page)
    await expect(dialog.getByText('Proveedor nuevo').first()).toBeVisible()
    await expect(dialog.getByRole('button', { name: /^Exportar \d+$/ })).toBeEnabled()
    await page.waitForTimeout(600)
    await shot(page, false, 'l246-quickbooks-pendientes', 1000, dialog)
    await variants(page, 'qb-pendientes')
    await pickMine(page, dialog)
    const [download] = await Promise.all([page.waitForEvent('download'), dialog.getByRole('button', { name: /^Exportar \d+$/ }).click()])
    expect(download.suggestedFilename()).toMatch(/^teikem-quickbooks-compras-\d{8}-\d{4}\.iif$/)
    await expect(dialog.getByText(/¿Ya las importaste en QuickBooks\? \(\d+\)/)).toBeVisible()
    await shot(page, false, 'l246-quickbooks-ya-importaste', 1000, dialog)
    await variants(page, 'qb-pregunta')
    // «Todavía no» no cambia nada: las órdenes siguen exportadas y por confirmar
    await dialog.getByRole('button', { name: 'Todavía no' }).click()
    await expect(dialog.getByText(/Archivo del/).first()).toBeVisible()
    await pickMine(page, dialog)
    await dialog.getByRole('button', { name: /^Exportar \d+$/ }).click()
    await dialog.getByRole('button', { name: /^Sí, ya están \(\d+\)$/ }).click()
    await expect(page.locator('.toast.on').filter({ hasText: /marcadas como en QuickBooks/ })).toBeVisible()
    await dialog.locator('button.btn', { hasText: 'Cerrar' }).click()
    const row = page.getByRole('row').filter({ hasText: SUPPLIER }).filter({ hasText: 'Excel / archivo' }).first()
    await expect(row.getByText('En QuickBooks')).toBeVisible()
  })

  test('6. la ficha muestra la franja con quién y cuándo, y «Quitar la marca» devuelve la orden a por pasar', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/purchase-orders')
    await page.getByRole('row').filter({ hasText: SUPPLIER }).filter({ hasText: 'Excel / archivo' }).first().click()
    const bar = page.getByTestId('pqb-detail')
    await expect(bar.getByText('En QuickBooks')).toBeVisible()
    await expect(bar.getByText(/Marcada el .* por /)).toBeVisible()
    await expect(page.getByText(/editarla o cancelarla aquí no cambia nada allá/)).toBeVisible()
    await shot(page, false, 'l246-orden-ficha')
    await variants(page, 'ficha')
    await bar.getByRole('button', { name: 'Quitar la marca' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Quitar la marca' }).click()
    await expect(bar.getByText('Exportada, por confirmar')).toBeVisible()
    await bar.getByRole('button', { name: 'No pasó' }).click()
    await expect(bar.getByText('Por pasar')).toBeVisible()
    // la orden de Teikem también salió en el archivo: quitar la marca y «No pasó» la devuelve a por pasar, con Pasar y Marcar a la mano
    await page.goto(`/warehouse/purchase-orders/${seeded.teikemOrder}`)
    const teikem = page.getByTestId('pqb-detail')
    await expect(teikem.getByText('Teikem', { exact: true })).toBeVisible()
    await teikem.getByRole('button', { name: 'Quitar la marca' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Quitar la marca' }).click()
    await teikem.getByRole('button', { name: 'No pasó' }).click()
    await expect(teikem.getByRole('button', { name: 'Pasar a QuickBooks…' })).toBeVisible()
    await expect(teikem.getByRole('button', { name: 'Marcar como en QuickBooks' })).toBeVisible()
    await page.screenshot({ path: `${SCRATCH}/ficha-por-pasar.png` })
  })

  test('7. Ajustes → QuickBooks tiene la cuenta de órdenes de compra', async ({ page, request }) => {
    // la sección es del módulo de Órdenes de venta: si el administrador no es el de la plataforma, el módulo ya debe estar encendido
    await enableModule(request, 'SALES_ORDERS').catch(() => undefined)
    await login(page)
    await page.goto('/system/settings')
    await page.getByRole('tab', { name: /Operación/ }).click()
    await expect(page.getByLabel('Cuenta de órdenes de compra')).toBeVisible()
    await expect(page.getByLabel('Cuenta de órdenes de compra')).toHaveValue(/Purchase Orders|^$/)
  })
})

test.describe('móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil')

  test('la lista, el importador y el diálogo de QuickBooks caben sin scroll horizontal', async ({ page, request }) => {
    await seed(request, await apiHeaders(request))
    await login(page)
    await page.goto('/warehouse/purchase-orders')
    await expect(page.getByText(SUPPLIER).first()).toBeVisible()
    await expectNoHScroll(page)
    mkdirSync(SCRATCH, { recursive: true })
    await page.screenshot({ path: `${SCRATCH}/movil-lista.png` })
    await page.locator('.head').getByRole('button', { name: 'QuickBooks', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: 'Pasar órdenes de compra a QuickBooks' })
    await expect(dialog.getByText(SUPPLIER).first()).toBeVisible()
    await expectNoHScroll(page)
    await page.screenshot({ path: `${SCRATCH}/movil-qb.png` })
    await dialog.locator('button.btn', { hasText: 'Cerrar' }).click()
    await page.getByRole('button', { name: 'Importar', exact: true }).click()
    const imp = page.getByRole('dialog', { name: 'Importar órdenes de compra' })
    await imp.getByLabel('Origen del archivo').selectOption('TEMPLATE')
    await expect(imp.getByLabel('Plantilla')).toBeVisible()
    await expectNoHScroll(page)
    await page.screenshot({ path: `${SCRATCH}/movil-importar.png` })
  })
})
