// Recorrido del Lote F6 (docs/frontend/loteF6-plan.md, "Recorrido (Playwright)") contra el API real (db-init hecho,
// API en API_URL, por defecto http://localhost:5000). Proyecto 'escritorio': pasos 1-9, en serie (cada paso usa el producto,
// el proveedor y los documentos del anterior); proyecto 'movil' (Pixel 7 a 360 px): paso 10.
// Nada de este recorrido rompe otra corrida: el producto (PROD-{timestamp}), el proveedor (Prov-{timestamp}) y el cliente
// del empaque son nuevos en cada corrida; el inventario que entra se queda en ese producto.
// Capturas para el manual (docs/manual/frontend/img/f6-<pantalla>.png): las del proyecto 'escritorio'.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const WAREHOUSE = 'ALM-01 · Almacén principal'

const STAMP = Date.now()
const SKU = `PROD-${STAMP}`
const PRODUCT_NAME = `Producto e2e ${STAMP}`
const SUPPLIER = `Prov-${STAMP}`
const CLIENT = `Cliente e2e F6 ${STAMP}`
/** Número del recibo del paso 7 (lo usa el paso 8; el describe corre en serie). */
let receiptNumber = ''

// Interfaz en español (el idioma inicial sale del navegador si el usuario no eligió otro).
test.use({ locale: 'es-PR' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

/** Captura de la pantalla actual para el manual, cuando ya no hay peticiones pendientes. `mask` tapa datos variables. */
async function shot(page: Page, name: string, opts: { mask?: Locator[] } = {}) {
  await page.waitForLoadState('networkidle')
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({
    path: `${IMG_DIR}f6-${name}.png`,
    animations: 'disabled',
    caret: 'hide',
    mask: opts.mask,
    maskColor: '#2a3346',
  })
}

/** Token de acceso del admin por el API (para preparar datos del recorrido). */
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
  // Si el usuario pertenece a varias compañías, se elige la predeterminada.
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/** Enlace del grupo 'Almacén' del menú (abre el grupo si el usuario lo dejó cerrado). */
async function warehouseMenuLink(page: Page, name: string): Promise<Locator> {
  const menu = page.getByRole('complementary', { name: 'Menú principal' })
  const group = menu.getByRole('button', { name: 'Almacén' })
  if ((await group.getAttribute('aria-expanded')) === 'false') await group.click()
  return menu.getByRole('link', { name })
}

/** Elige un producto en un ProductPicker (combobox con buscador) por su SKU. */
async function pickProduct(scope: Page | Locator, label: string | RegExp, sku: string) {
  const box = scope.getByRole('combobox', { name: label })
  await box.click()
  await box.fill(sku)
  await scope.getByRole('option', { name: new RegExp(`^${sku} · `) }).click()
}

/** Elige ALM-01 en un WarehousePicker (combobox que filtra en el cliente por código o nombre). */
async function pickWarehouse(scope: Page | Locator, box: Locator) {
  await box.click()
  await box.fill('ALM-01')
  await scope.getByRole('option', { name: WAREHOUSE, exact: true }).click()
  await expect(box).toHaveValue(WAREHOUSE)
}

/** Recibo de la lista maestra (botón con su número) de la pestaña visible de Recibo. */
function receiptItem(page: Page, number: string): Locator {
  return page.getByRole('group', { name: 'Lista de recibos' }).getByRole('button', { name: new RegExp(`${number}\\b`) })
}

/**
 * Pestaña 'Acomodo pendiente' de Recibo (lista de recibos con tareas de acomodo abiertas) con el recibo elegido: sus tareas
 * de acomodo quedan a la derecha.
 */
async function openPutawayOf(page: Page, number: string) {
  await page.goto('/warehouse/receipts?tab=putaway')
  await expect(page.getByRole('tab', { name: 'Acomodo pendiente' })).toHaveAttribute('aria-selected', 'true')
  await receiptItem(page, number).click()
}

/** Espera el aviso (toast) de éxito con ese texto. */
async function expectToast(page: Page, text: string | RegExp) {
  await expect(page.locator('.toast').filter({ hasText: text }).first()).toBeVisible()
}

/**
 * Sin scroll horizontal de página y sin contenido recortado (igual que en loteF1.spec.ts): se mide cada contenedor que
 * recorta y cada elemento visible dentro de `.stage` y `.bar`; los contenedores con scroll propio (`.seg`) se miden ellos.
 */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    const describe = (el: Element) => `${el.tagName.toLowerCase()}${el.id ? `#${el.id}` : ''}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')}`
    for (const sel of ['.stage', '.main', '.bar']) {
      document.querySelectorAll<HTMLElement>(sel).forEach((el) => {
        if (el.scrollWidth > el.clientWidth + 1) out.push(`${describe(el)} scrollWidth ${el.scrollWidth} > ${el.clientWidth}`)
      })
    }
    const OWN_SCROLL = '.seg'
    const roots = document.querySelectorAll('.stage, .bar')
    const scope: Element[] = roots.length > 0 ? Array.from(roots) : [document.body]
    for (const root of scope) {
      root.querySelectorAll('*').forEach((el) => {
        if (el.parentElement?.closest(OWN_SCROLL)) return
        const style = getComputedStyle(el)
        if (style.visibility === 'hidden' || style.display === 'none') return
        const r = el.getBoundingClientRect()
        if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${describe(el)} [${Math.round(r.left)}, ${Math.round(r.right)}] fuera de 0..${vw}`)
      })
    }
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

test.describe('Lote F6 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })

  test("1. Pulso muestra el panel 'Almacén' (saldo, recibos abiertos, tareas pendientes, conteos abiertos)", async ({ page }) => {
    await login(page)
    await expect(page.getByRole('heading', { level: 2, name: 'Almacén', exact: true })).toBeVisible()
    // desde F7A el panel trae filtros (almacén, categoría o producto) y la tarjeta 'Bajo mínimo' (recorrido en f7a.spec.ts)
    await expect(page.getByText('Saldo actual (no depende de un rango de fecha).', { exact: false })).toBeVisible()
    for (const label of ['Recibos abiertos', 'En almacén', 'Reservado', 'Disponible', 'Bajo mínimo', 'Tareas pendientes', 'Conteos abiertos']) {
      // cada nodo del río y cada tarjeta es un role=group con su etiqueta (los de documentos llevan además la marca 'almacén')
      await expect(page.getByRole('group', { name: label, exact: true })).toBeVisible()
    }
    await expect(await warehouseMenuLink(page, 'Almacenes')).toBeVisible()
    // el panel 'Almacén' va debajo de los indicadores del API: se lleva a la vista para la captura
    await page.getByRole('heading', { level: 2, name: 'Almacén', exact: true }).scrollIntoViewIfNeeded()
    await shot(page, 'pulso-almacen')
  })

  test('2. Almacenes lista ALM-01 y su ficha muestra las posiciones sembradas', async ({ page }) => {
    await login(page)
    await (await warehouseMenuLink(page, 'Almacenes')).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Almacenes' })).toBeVisible()
    const row = page.getByRole('row').filter({ hasText: 'ALM-01' })
    await expect(row.getByText('Almacén principal')).toBeVisible()
    await shot(page, 'almacenes')
    // Lote 11: la lista es maestro-detalle — el clic elige el almacén (panel "Zonas de este almacén") y el lápiz abre la ficha
    await row.getByText('ALM-01', { exact: true }).click()
    await expect(page).toHaveURL(/\/warehouse\/warehouses\?warehouse=/)
    await expect(page.getByText('Zonas de este almacén')).toBeVisible()
    await page.getByRole('button', { name: 'Editar almacén' }).click()
    await expect(page).toHaveURL(/\/warehouse\/warehouses\/[0-9a-f-]+$/)
    await shot(page, 'almacen-ficha')
    await page.getByRole('tab', { name: 'Zonas' }).click()
    await expect(page.getByRole('table', { name: 'Zonas' }).getByRole('row').nth(1)).toBeVisible()
    await shot(page, 'almacen-zonas')
    await page.getByRole('tab', { name: 'Posiciones' }).click()
    // al menos una posición sembrada con su zona
    const bins = page.getByRole('table', { name: 'Posiciones' })
    await expect(bins.getByRole('row').nth(1)).toBeVisible()
    await expect(bins.getByRole('row').nth(1).getByRole('cell').nth(1)).not.toHaveText('')
    await shot(page, 'almacen-posiciones')
    await page.getByRole('tab', { name: 'Muelles' }).click()
    await expect(page.getByRole('table', { name: 'Muelles' })).toBeVisible()
    await shot(page, 'almacen-muelles')
  })

  test("3. alta de producto: sin SKU muestra 'El SKU es obligatorio.'; con SKU queda en la lista con 0 en mano", async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/products')
    await page.getByRole('button', { name: 'Nuevo producto' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Nombre').fill(PRODUCT_NAME)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    const sku = dialog.getByLabel('SKU')
    await expect(sku).toHaveAttribute('aria-invalid', 'true')
    await expect(sku).toHaveAccessibleDescription('El SKU es obligatorio.')
    await shot(page, 'producto-nuevo-error')

    await sku.fill(SKU)
    // Rastreo es un desplegable con buscador (Lote 12): muestra la etiqueta del catálogo
    await expect(dialog.getByRole('combobox', { name: 'Rastreo' })).toHaveValue('Ninguno')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expectToast(page, 'Producto creado.')
    // modal único de la maqueta: tras el alta se queda en la lista
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page).toHaveURL(/\/warehouse\/products$/)

    // Lote 12: sin buscador en la tabla; el filtro Nombre (contiene) va al API
    await page.getByRole('searchbox', { name: 'Nombre' }).fill(PRODUCT_NAME)
    const row = page.getByRole('row').filter({ hasText: SKU })
    await expect(row).toHaveCount(1)
    // columnas: SKU, Producto, Categoría, Empaque, Marca, Dueño, Disponible, Reservado, Total, Rastreo, Estado
    await expect(row.getByRole('cell').nth(8)).toHaveText('0')
    await shot(page, 'productos')
    // clic en la fila = "Editar producto" (SKU bloqueado, Total en solo lectura)
    await row.click()
    const edit = page.getByRole('dialog', { name: 'Editar producto' })
    await expect(edit.getByLabel('SKU')).toHaveValue(SKU)
    await expect(edit.getByLabel('SKU')).toBeDisabled()
    await shot(page, 'producto-ficha')
    await edit.getByRole('button', { name: 'Cancelar' }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await page.getByRole('searchbox', { name: 'Nombre' }).fill('')
    await page.getByRole('tab', { name: 'Categorías' }).click()
    await shot(page, 'categorias')
  })

  test('4. Saldos: ajuste +10 con motivo FOUND en ALM-01 sube el saldo del producto a 10', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/kardex?tab=balances')
    await pickProduct(page, 'Producto', SKU)
    await expect(page.getByRole('button', { name: `Quitar ${SKU}` })).toBeVisible()
    // sin existencias todavía (la lista no incluye saldos en cero)
    await expect(page.getByText('Sin resultados')).toBeVisible()

    await page.getByRole('button', { name: 'Ajustar' }).click()
    const dialog = page.getByRole('dialog', { name: 'Ajuste de inventario' })
    await pickProduct(dialog, /Producto/, SKU)
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén/ }))
    // BinPicker: combobox con buscador sobre las posiciones del almacén elegido
    const bin = dialog.getByRole('combobox', { name: /^Posición/ })
    await expect(bin).toBeEnabled()
    await bin.click()
    // opciones del listbox del BinPicker (llegan del API tras la pausa de 250 ms), no las del <select> Motivo
    await dialog.getByRole('listbox').getByRole('option').first().click()
    await expect(bin).not.toHaveValue('')
    // Lote 14 (D11): Subir/Bajar con cantidad positiva; el motivo se busca (Encontrado solo aparece al subir)
    await dialog.getByRole('radio', { name: /Subir/ }).click()
    await dialog.getByLabel(/^Cantidad/).fill('10')
    const reason = dialog.getByRole('combobox', { name: /^Motivo/ })
    await reason.click()
    await reason.fill('Encontrado')
    await dialog.getByRole('option', { name: 'Encontrado' }).click()
    await dialog.getByLabel(/^Notas/).fill(`Recorrido e2e ${STAMP}`)
    await shot(page, 'inventario-ajuste')
    await dialog.getByRole('button', { name: 'Aplicar ajuste' }).click()
    await expectToast(page, /Ajuste registrado/)

    const row = page.getByRole('row').filter({ hasText: SKU })
    await expect(row).toHaveCount(1)
    await expect(row.getByRole('cell').nth(9)).toHaveText('10')
    await shot(page, 'inventario-saldos')
  })

  test('5. Kárdex: el ajuste aparece con signo +10 y el motivo capturado', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/kardex')
    await expect(page.getByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    await pickProduct(page, 'Producto', SKU)
    const row = page.getByRole('row').filter({ hasText: SKU })
    await expect(row).toHaveCount(1)
    // tipo ADJUSTMENT ('Ajuste'), signo visible y el motivo FOUND ('Encontrado') con las notas capturadas
    await expect(row.getByText('Ajuste', { exact: true })).toBeVisible()
    await expect(row.getByText('+10')).toBeVisible()
    await expect(row.getByRole('cell').nth(10)).toHaveText('Encontrado')
    await expect(row.getByRole('cell').nth(10).locator('span')).toHaveAttribute('title', `Recorrido e2e ${STAMP}`)
    await shot(page, 'inventario-kardex')
    await page.getByRole('tab', { name: 'Conciliación' }).click()
    await shot(page, 'inventario-conciliacion')
  })

  test('6. proveedor nuevo y orden de compra: Enviar pasa de Borrador a Enviada', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/suppliers')
    await page.getByRole('button', { name: 'Nuevo proveedor' }).click()
    let dialog = page.getByRole('dialog')
    await dialog.getByLabel(/^Nombre/).fill(SUPPLIER)
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expectToast(page, 'Proveedor creado.')
    await expect(page.getByRole('row').filter({ hasText: SUPPLIER })).toHaveCount(1)
    await shot(page, 'proveedores')

    await page.goto('/warehouse/purchase-orders')
    await page.getByRole('button', { name: 'Nueva orden de compra' }).click()
    dialog = page.getByRole('dialog')
    // Lote 12: el proveedor es un combobox con buscador (ComboSelectInput), no un <select>
    await dialog.getByRole('combobox', { name: /^Proveedor/ }).fill(SUPPLIER)
    await page.getByRole('option', { name: SUPPLIER }).click()
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén/ }))
    await pickProduct(dialog, /Producto/, SKU)
    await dialog.getByLabel(/^Cantidad ordenada/).fill('5')
    // el producto nuevo no tiene costo de compra: la línea lo pide
    await dialog.getByLabel(/^Costo unitario/).fill('2.5')
    await shot(page, 'orden-compra-nueva')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expectToast(page, 'Orden de compra creada.')
    await expect(page).toHaveURL(/\/warehouse\/purchase-orders\/[0-9a-f-]+$/)

    const pipeline = page.locator('.stpipe')
    await expect(pipeline.getByText('Borrador').first()).toBeVisible()
    // en Borrador el proveedor y el almacén se pueden cambiar desde la ficha (combobox); al enviarla pasan a solo lectura
    await expect(page.getByRole('combobox', { name: /^Proveedor/ })).toHaveValue(SUPPLIER)
    await page.getByRole('button', { name: 'Avanzar a Enviada' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Cambiar estatus' }).click()
    await expect(page.getByRole('button', { name: 'Avanzar a Enviada' })).toHaveCount(0)
    await expect(page.locator('.chip', { hasText: 'Enviada' }).first()).toBeVisible()
    await expect(page.getByRole('combobox', { name: /^Proveedor/ })).toHaveCount(0)
    await shot(page, 'orden-compra-ficha')
    await page.goto('/warehouse/purchase-orders')
    await expect(page.getByRole('row').filter({ hasText: SUPPLIER }).locator('.chip', { hasText: 'Enviada' })).toBeVisible()
    await shot(page, 'ordenes-compra')
  })

  test('7. recibo ciego: lo recibido copia lo esperado; al confirmarlo sube el saldo y nace una tarea PUTAWAY', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/receipts')
    await page.getByRole('button', { name: 'Nuevo recibo' }).click()
    const dialog = page.getByRole('dialog', { name: 'Nuevo recibo' })
    // Lote 13: el modal solo lleva el encabezado; las líneas se capturan en el detalle
    await dialog.getByLabel(/^Origen/).selectOption('BLIND')
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén/ }))
    await dialog.getByLabel('Transporte').fill('Camión e2e')
    await dialog.getByLabel('Referencia').fill(`BOL-${STAMP}`)
    await shot(page, 'recibo-nuevo')
    await dialog.getByRole('button', { name: 'Crear recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ creado\./)
    // maestro-detalle en la misma pantalla: ?receipt= elige el recibo y el detalle queda a la derecha
    await expect(page).toHaveURL(/\/warehouse\/receipts\?receipt=[0-9a-f-]+/)
    const detail = page.locator('.rcp-side')
    receiptNumber = /REC-\d+/.exec((await detail.locator('.ref').first().textContent()) ?? '')?.[0] ?? ''
    expect(receiptNumber).not.toBe('')
    await expect(detail.locator('.chip', { hasText: 'Esperado' }).first()).toBeVisible()

    // recibo ciego: al escribir lo recibido, lo esperado se copia solo y el recibo pasa a Recibiendo
    await pickProduct(detail, 'Producto de la línea 1', SKU)
    const received = detail.getByRole('textbox', { name: 'Recibido de la línea 1' })
    await received.fill('5')
    await expect(detail.getByRole('textbox', { name: 'Esperado de la línea 1' })).toHaveValue('5')
    await received.press('Tab')
    await expect(detail.locator('.chip', { hasText: 'Recibiendo' }).first()).toBeVisible()

    await detail.getByRole('button', { name: 'Confirmar recibo' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirmar recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ confirmado\./)
    // sin diferencia: Completado, con su tarea de acomodo debajo de las líneas
    await expect(detail.locator('.chip', { hasText: /^Completado$/ }).first()).toBeVisible()
    await expect(detail.getByRole('row').filter({ hasText: SKU }).filter({ hasText: 'Pendiente' })).toHaveCount(1)
    await shot(page, 'recibo-ficha')
    await page.goto('/warehouse/receipts')
    await expect(page.getByRole('heading', { level: 1, name: 'Recibo' })).toBeVisible()
    await shot(page, 'recepcion')

    await page.goto('/warehouse/kardex?tab=balances')
    await pickProduct(page, 'Producto', SKU)
    const rows = page.getByRole('row').filter({ hasText: SKU })
    await expect(rows).toHaveCount(2) // la posición del ajuste (10) y la de recepción (5)
    const onHand = await rows.evaluateAll((trs) => trs.map((tr) => Number(tr.querySelectorAll('td')[9]?.textContent ?? 0)))
    expect(onHand.reduce((a, b) => a + b, 0)).toBe(15)
  })

  test('8. Recibo → Acomodo pendiente: asignar a mí, iniciar y completar la PUTAWAY; el recibo pasa a Acomodado', async ({ page }) => {
    await login(page)
    await openPutawayOf(page, receiptNumber)
    const row = page.getByRole('row').filter({ hasText: SKU })
    await expect(row).toHaveCount(1)
    await shot(page, 'tareas')

    // acciones de la tarea como íconos (su nombre accesible es el de la acción)
    await row.getByRole('button', { name: 'Asignar' }).click()
    let dialog = page.getByRole('dialog')
    await dialog.getByLabel('Usuario').selectOption({ label: 'Administrador Advance' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expectToast(page, 'Tarea asignada.')
    await expect(row.getByText('Administrador Advance')).toBeVisible()

    await row.getByRole('button', { name: 'Iniciar' }).click()
    await expectToast(page, 'Tarea iniciada.')

    await row.getByRole('button', { name: 'Completar' }).click()
    dialog = page.getByRole('dialog')
    await expect(dialog.getByText('Posición sugerida:')).toBeVisible()
    // BinPicker: las sugeridas del acomodo van primero con la marca 'Sugerida'
    const toBin = dialog.getByRole('combobox', { name: /^Posición destino/ })
    await toBin.click()
    await dialog.getByRole('option').filter({ hasText: 'Sugerida' }).first().click()
    await expect(toBin).not.toHaveValue('')
    await shot(page, 'tarea-completar')
    await dialog.getByRole('button', { name: /Guardar|Completar/ }).click()
    await expectToast(page, 'Tarea completada.')
    // sin tareas abiertas el recibo pasa a Acomodado y sale de 'Acomodo pendiente'
    await expect(receiptItem(page, receiptNumber)).toHaveCount(0)
    await page.goto('/warehouse/receipts')
    await receiptItem(page, receiptNumber).click()
    await expect(page.locator('.rcp-side .chip', { hasText: 'Acomodado' }).first()).toBeVisible()
  })

  test('9. recolectar 3 unidades (EMP-#####) y empacarlas desde la fila: la orden creada se ve en Consulta de órdenes', async ({ page, request }) => {
    // Cliente de la orden del empaque (propio de esta corrida)
    const token = await apiToken(request)
    const created = await request.post(`${API_URL}/api/v1/clients`, { headers: { Authorization: `Bearer ${token}` }, data: { name: CLIENT } })
    expect(created.ok()).toBeTruthy()
    const clientCode = ((await created.json()) as { code?: string }).code ?? ''

    await login(page)
    await page.goto('/warehouse/pick-batches')
    // Lote 13: dos paneles — Recolección (captura en rejilla) a la izquierda, Recolecciones a la derecha
    await pickWarehouse(page, page.getByRole('combobox', { name: /^Almacén/ }))
    await pickProduct(page, 'Producto de la línea 1', SKU)
    await page.getByLabel('Cantidad de la línea 1').fill('3')
    await shot(page, 'recoleccion-nueva')
    await page.getByRole('button', { name: 'Recolectar (bajar de inventario)' }).click()
    const collectedToast = page.locator('.toast').filter({ hasText: /Recolección EMP-\d{5} creada\./ }).first()
    await expect(collectedToast).toBeVisible()
    const batchNumber = /EMP-\d{5}/.exec((await collectedToast.textContent()) ?? '')?.[0] ?? ''
    expect(batchNumber).not.toBe('')

    // el panel de Recolecciones pasa a tarjetas cuando le queda angosto (forceCards): fila de tabla o tarjeta
    const batchRow = page.locator('tr, li.dt-card').filter({ hasText: batchNumber })
    await expect(batchRow).toHaveCount(1)
    await batchRow.getByRole('button', { name: 'Empacar' }).click()
    const dialog = page.getByRole('dialog', { name: `Empacar ${batchNumber}` })
    const client = dialog.getByRole('combobox', { name: /Cliente de la orden/ })
    await client.click()
    await client.fill(clientCode)
    await dialog.getByRole('option', { name: new RegExp(clientCode) }).first().click()
    // tipo de servicio y de paquete explícitos: "Predeterminado de la compañía (…)" solo se ofrece si la compañía lo configuró
    // (lo hace scripts/smoke.sh; una base recién inicializada no lo trae y entonces la pantalla exige elegir uno); elegirlos a
    // mano funciona en ambos casos
    await dialog.getByLabel(/^Tipo de servicio/).selectOption('STANDARD')
    await dialog.getByLabel(/^Tipo de paquete/).selectOption('BOX')
    await dialog.getByLabel(/^Consignatario(?! del)/).selectOption('new')
    await dialog.getByLabel(/^Nombre/).fill(`Consignatario e2e ${STAMP}`)
    await dialog.getByLabel(/^Dirección \(línea 1\)/).fill('Calle Luna 12')
    await dialog.getByLabel(/^Ciudad/).fill('San Juan')
    await dialog.getByLabel(/^Piezas/).fill('1')
    await shot(page, 'empacar')
    await dialog.getByRole('button', { name: 'Empacar y crear orden' }).click()
    const packedToast = page.locator('.toast').filter({ hasText: /Empacada: se creó la orden / }).first()
    await expect(packedToast).toBeVisible()
    const orderNumber = /orden\s+([A-Z0-9-]+)/.exec((await packedToast.textContent()) ?? '')?.[1] ?? ''
    expect(orderNumber.length).toBeGreaterThan(0)
    // la recolección queda Empacada (ya no ofrece Empacar) y su detalle (clic en la fila) enlaza la orden creada
    await expect(batchRow.locator('.chip', { hasText: 'Empacada' })).toBeVisible()
    await expect(batchRow.getByRole('button', { name: 'Empacar' })).toHaveCount(0)
    await shot(page, 'recolecciones')
    await batchRow.getByText(batchNumber, { exact: true }).click()
    const detailDialog = page.getByRole('dialog')
    await expect(detailDialog.getByText(orderNumber).first()).toBeVisible()
    await shot(page, 'recoleccion-ficha')
    await detailDialog.getByRole('button', { name: 'Cerrar' }).first().click()

    await page.goto('/orders')
    // la búsqueda libre cubre número, factura, lote de empaque y consignatario: se busca por la recolección (EMP-#####),
    // que queda como lote de empaque de la orden; se espera a que la lista ya venga filtrada (una sola orden)
    await page.getByRole('searchbox').fill(batchNumber)
    // el conteo va en el pie de la tabla ("1–1 de 1") y la tabla trae el encabezado + una sola fila
    await expect(page.getByText('1–1 de 1', { exact: true })).toBeVisible()
    await expect(page.getByRole('table', { name: 'Órdenes' }).getByRole('row')).toHaveCount(2)
    const row = page.getByRole('row').filter({ hasText: CLIENT }).filter({ hasText: orderNumber })
    await expect(row).toHaveCount(1)
    // solo lectura: sin 'Nuevo'
    await expect(page.getByRole('button', { name: /^Nuev/ })).toHaveCount(0)
    await shot(page, 'ordenes')
    await row.getByText(orderNumber, { exact: true }).click()
    await expect(page).toHaveURL(/\/orders\/[0-9a-f-]+$/)
    // ficha básica de solo lectura: número y cliente, datos generales con el lote de empaque de origen
    await expect(page.getByRole('heading', { level: 1, name: `${orderNumber} · ${CLIENT}` })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: 'Datos generales' })).toBeVisible()
    await expect(page.getByText(batchNumber, { exact: true })).toBeVisible()
    await shot(page, 'orden-ficha')
  })
})

test.describe('Lote F6 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  test('10. Almacenes, Productos e inventario y Kárdex de movimientos sin scroll horizontal y con las tablas como tarjetas', async ({ page, request }) => {
    // Producto con un SKU de 60 caracteres sin espacios (el máximo) y +1 en ALM-01: así Saldos tiene al menos una fila
    // aunque la base no tenga existencias (la lista no incluye saldos en cero). Se deshace al final.
    const longSku = `LARGO-${STAMP}-`.padEnd(60, 'X')
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const whRes = await request.get(`${API_URL}/api/v1/warehouses`, { headers })
    expect(whRes.ok()).toBeTruthy()
    const warehousePublicId = ((await whRes.json()) as { code?: string; publicId?: string }[]).find((w) => w.code === 'ALM-01')?.publicId ?? ''
    expect(warehousePublicId).toBeTruthy()
    const binRes = await request.get(`${API_URL}/api/v1/warehouses/${warehousePublicId}/bins`, { headers, params: { includeInactive: false, take: 1 } })
    expect(binRes.ok()).toBeTruthy()
    const binId = ((await binRes.json()) as { items?: { id?: number }[] }).items?.[0]?.id ?? 0
    expect(binId).toBeTruthy()
    const created = await request.post(`${API_URL}/api/v1/products`, {
      headers,
      data: { sku: longSku, name: `SKU largo e2e ${STAMP}`, trackingType: 'NONE' },
    })
    expect(created.ok()).toBeTruthy()
    const productPublicId = ((await created.json()) as { product?: { publicId?: string } }).product?.publicId ?? ''
    const adjust = (quantity: number, reason: string) =>
      request.post(`${API_URL}/api/v1/inventory/adjustments`, {
        headers,
        data: { productPublicId, warehousePublicId, binId, quantity, reason, notes: `Recorrido e2e F6 móvil ${STAMP}` },
      })
    expect((await adjust(1, 'FOUND')).ok()).toBeTruthy()

    try {
      await login(page)
      expect(page.viewportSize()?.width).toBe(360)
      for (const [path, heading] of [
        ['/warehouse/warehouses', 'Almacenes'],
        ['/warehouse/products', 'Productos e inventario'],
        ['/warehouse/kardex?tab=balances', 'Kárdex de movimientos'],
      ] as const) {
        await page.goto(path)
        await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
        // bajo 720 px DataTable pinta tarjetas (lista), no una tabla
        const cards = page.locator('ul.dt-cards')
        await expect(cards.first()).toBeVisible()
        await expect(page.locator('.dt table.lst')).toHaveCount(0)
        await expectNoHorizontalScroll(page)
      }
      // Saldos → Kárdex (otra tabla paginada con más columnas)
      await page.getByRole('tab', { name: 'Kárdex' }).click()
      await expect(page.locator('ul.dt-cards').first()).toBeVisible()
      await expectNoHorizontalScroll(page)

      // Filtro Producto con el SKU de 60 caracteres: la píldora se recorta y no desborda
      await pickProduct(page, 'Producto', longSku)
      await expect(page.getByRole('button', { name: `Quitar ${longSku}` })).toBeVisible()
      await expectNoHorizontalScroll(page)
    } finally {
      await adjust(-1, 'LOSS')
      await request.post(`${API_URL}/api/v1/products/${productPublicId}/deactivate`, { headers })
    }
  })
})
