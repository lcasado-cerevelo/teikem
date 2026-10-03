// Recorrido del Lote 14 (plan de cambios, lote 4: Transferencias y ajustes, Kárdex, Conteo cíclico y "Necesita tu atención")
// contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo "Advance Logistics").
// - escritorio (en serie): menú y ruta vieja, ajuste Bajar (motivo filtrado por dirección, nota obligatoria) y Subir,
//   transferencia origen → ítem → destino, filas en sus pestañas y detalle del movimiento; Kárdex (filtros compartidos,
//   resumen, ?txn=); "Conteo de lo cambiado" con vista previa, captura en la fila y confirmación en un paso; Pulso;
// - móvil (360 px): Transferencias y ajustes, Kárdex (3 pestañas) y Conteo cíclico sin scroll horizontal.
// El producto (AJ14-{timestamp}) es nuevo en cada corrida (creado por API). El e2e no toca SQL ni provoca descuadres.
// Capturas para el manual: docs/manual/frontend/img/l14-<pantalla>.png (proyecto 'escritorio').
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const WAREHOUSE = 'ALM-01 · Almacén principal'
const STAMP = Date.now()
const SKU = `AJ14-${STAMP}`
/** Id del movimiento cuyo detalle se abrió en el paso 4 (lo usa el paso 5 con `?txn=`). */
let txnId = ''
/** Código de la posición donde el paso 3 subió el inventario (la transferencia del paso 4 sale de ella). */
let binCode = ''

test.use({ locale: 'es-PR' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

/** Captura para el manual, cuando ya no hay peticiones pendientes. */
async function shot(page: Page, name: string, opts: { mask?: Locator[] } = {}) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}l14-${name}.png`, animations: 'disabled', caret: 'hide', mask: opts.mask, maskColor: '#2a3346' })
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

async function pickReason(dialog: Locator, name: string) {
  const reason = dialog.getByRole('combobox', { name: /^Motivo/ })
  await reason.click()
  await reason.fill(name)
  await dialog.getByRole('option', { name }).click()
}

async function expectToast(page: Page, text: string | RegExp) {
  await expect(page.locator('.toast').filter({ hasText: text }).first()).toBeVisible()
}

/** Filtra la lista de la pantalla por el producto del recorrido (filtro "Producto" con buscador). */
async function filterBySku(page: Page) {
  await pickProduct(page, 'Producto', SKU)
  await expect(page.getByRole('button', { name: `Quitar ${SKU}` })).toBeVisible()
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que f6/lote13). */
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

/**
 * Da de baja los conteos Pendientes de "lo cambiado" de la lista (los que dejó una corrida anterior o esta misma generación):
 * una posición con un conteo abierto se salta al generar el siguiente, así que el recorrido parte y termina sin ellos.
 */
async function deletePendingChangeCounts(page: Page) {
  const rows = page.locator('.cc-list-items .cc-row')
  const pending = rows.filter({ hasText: 'Pendiente' }).filter({ hasText: 'Lo cambiado' })
  for (let i = 0; i < 40 && (await pending.count()) > 0; i++) {
    // se espera a que desaparezca ESE conteo (con la página llena, la siguiente la rellena y el número de filas no cambia;
    // Lote F12: otros recorridos siembran posiciones con movimientos que también entran en "lo cambiado")
    const number = ((await pending.first().innerText()).match(/CC-\d+/) ?? [])[0] ?? ''
    await pending.first().getByRole('button', { name: /^Eliminar el conteo/ }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Eliminar', exact: true }).click()
    await expectToast(page, /Conteo CC-\d+ eliminado\./)
    await expect(rows.filter({ hasText: number })).toHaveCount(0)
  }
  await expect(pending).toHaveCount(0)
}

test.describe('Lote 14 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.name === 'movil') return
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const created = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: SKU, name: `Producto ajustes e2e ${STAMP}`, trackingType: 'NONE' } })
    expect(created.ok()).toBeTruthy()
  })

  test('1. menú: Transferencias y ajustes va después de Recolección y empaque; "Ajustes de inventario" ya no está y su ruta redirige a Compras', async ({ page }) => {
    await login(page)
    const menu = page.getByRole('complementary', { name: 'Menú principal' })
    const group = menu.getByRole('button', { name: 'Almacén' })
    if ((await group.getAttribute('aria-expanded')) === 'false') await group.click()
    const names = (await menu.getByRole('link').allInnerTexts()).map((s) => s.trim())
    const pick = names.indexOf('Recolección y empaque')
    expect(pick).toBeGreaterThanOrEqual(0)
    expect(names[pick + 1]).toBe('Transferencias y ajustes')
    expect(names).not.toContain('Ajustes de inventario')

    await page.goto('/warehouse/inventory-adjustments')
    await expect(page).toHaveURL(/\/warehouse\/purchase-orders/)
    await expect(page.getByRole('heading', { level: 1, name: 'Órdenes de compra' })).toBeVisible()
  })

  test('2. ajuste Bajar: el motivo se filtra por dirección (Encontrado no aparece) y la nota es obligatoria', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/transfers-adjustments')
    await expect(page.getByRole('heading', { level: 1, name: 'Transferencias y ajustes' })).toBeVisible()
    await expect(page.getByRole('tab', { name: 'Ajustes' })).toHaveAttribute('aria-selected', 'true')

    await page.getByRole('button', { name: 'Ajustar' }).click()
    const dialog = page.getByRole('dialog', { name: 'Ajuste de inventario' })
    // sin elegir Subir o Bajar no se guarda
    await dialog.getByRole('button', { name: 'Aplicar ajuste' }).click()
    await expect(dialog.getByText('Elija si el ajuste sube o baja el inventario.')).toBeVisible()

    // Bajar: Encontrado (solo al subir) no se ofrece; Daño sí
    await dialog.getByRole('radio', { name: /Bajar/ }).click()
    const reason = dialog.getByRole('combobox', { name: /^Motivo/ })
    await reason.click()
    await expect(dialog.getByRole('option', { name: 'Daño' })).toBeVisible()
    await expect(dialog.getByRole('option', { name: 'Encontrado' })).toHaveCount(0)
    await reason.press('Escape')

    // al pasar a Subir sí aparece
    await dialog.getByRole('radio', { name: /Subir/ }).click()
    await reason.click()
    await expect(dialog.getByRole('option', { name: 'Encontrado' })).toBeVisible()
    await reason.press('Escape')

    // sin nota: mensaje del formulario y no se guarda
    await dialog.getByRole('radio', { name: /Bajar/ }).click()
    await pickProduct(dialog, /Producto/, SKU)
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén/ }))
    await dialog.getByLabel(/^Cantidad/).fill('1')
    await pickReason(dialog, 'Daño')
    await dialog.getByRole('button', { name: 'Aplicar ajuste' }).click()
    await expect(dialog.getByText('Escriba una nota que explique el ajuste.')).toBeVisible()
    await expect(dialog).toBeVisible()
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
  })

  test('3. ajustes Subir 10 y Bajar 2 con nota: aparecen en la pestaña Ajustes con signo y motivo', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/transfers-adjustments')
    await page.getByRole('button', { name: 'Ajustar' }).click()
    let dialog = page.getByRole('dialog', { name: 'Ajuste de inventario' })
    await dialog.getByRole('radio', { name: /Subir/ }).click()
    await pickProduct(dialog, /Producto/, SKU)
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén/ }))
    const bin = dialog.getByRole('combobox', { name: /^Posición/ })
    await expect(bin).toBeEnabled()
    await bin.click()
    // la última posición de la lista (la primera la usan otros recorridos)
    await dialog.getByRole('listbox').getByRole('option').last().click()
    await expect(bin).not.toHaveValue('')
    binCode = (await bin.inputValue()).split(' · ')[0].trim()
    await dialog.getByLabel(/^Cantidad/).fill('10')
    await pickReason(dialog, 'Encontrado')
    await dialog.getByLabel(/^Nota/).fill(`Subir e2e ${STAMP}`)
    await shot(page, 'ajuste-modal')
    await dialog.getByRole('button', { name: 'Aplicar ajuste' }).click()
    await expectToast(page, 'Ajuste registrado (+10).')

    // Bajar 2: no puede bajar más de lo disponible (10) y con 2 sí
    await page.getByRole('button', { name: 'Ajustar' }).click()
    dialog = page.getByRole('dialog', { name: 'Ajuste de inventario' })
    await dialog.getByRole('radio', { name: /Bajar/ }).click()
    await pickProduct(dialog, /Producto/, SKU)
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén/ }))
    const bin2 = dialog.getByRole('combobox', { name: /^Posición/ })
    await bin2.click()
    await bin2.fill(binCode)
    await dialog.getByRole('option', { name: new RegExp(`^${binCode}`) }).first().click()
    await expect(bin2).not.toHaveValue('')
    await expect(dialog.getByText('Disponible en la posición: 10').first()).toBeVisible()
    await dialog.getByLabel(/^Cantidad/).fill('50')
    await pickReason(dialog, 'Daño')
    await dialog.getByLabel(/^Nota/).fill(`Bajar e2e ${STAMP}`)
    await dialog.getByRole('button', { name: 'Aplicar ajuste' }).click()
    await expect(dialog.getByText('No puede bajar más de lo disponible en la posición (10).').first()).toBeVisible()
    await dialog.getByLabel(/^Cantidad/).fill('2')
    await dialog.getByRole('button', { name: 'Aplicar ajuste' }).click()
    await expectToast(page, 'Ajuste registrado (-2).')

    await filterBySku(page)
    const up = page.getByRole('row').filter({ hasText: SKU }).filter({ hasText: '+10' })
    const down = page.getByRole('row').filter({ hasText: SKU }).filter({ hasText: '−2' })
    await expect(up).toHaveCount(1)
    await expect(up.getByText('Encontrado')).toBeVisible()
    await expect(down).toHaveCount(1)
    await expect(down.getByText('Daño')).toBeVisible()
    await expect(page.getByRole('group', { name: 'Resumen de movimientos' })).toBeVisible()
    await shot(page, 'ajustes-lista')
  })

  test('4. transferencia origen → ítem → destino: la fila aparece en Transferencias y su detalle abre el movimiento', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/transfers-adjustments?tab=transfers')
    await expect(page.getByRole('tab', { name: 'Transferencias' })).toHaveAttribute('aria-selected', 'true')
    await page.getByRole('button', { name: 'Transferir' }).click()
    const dialog = page.getByRole('dialog', { name: 'Transferencia de inventario' })
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén de origen/ }))
    const fromBin = dialog.getByRole('combobox', { name: /^Posición de origen/ })
    await fromBin.click()
    // solo posiciones con existencias: la del ajuste del paso 3
    await fromBin.fill(binCode)
    await dialog.getByRole('option', { name: new RegExp(`^${binCode}`) }).first().click()
    await expect(fromBin).not.toHaveValue('')
    const item = dialog.getByRole('combobox', { name: /^Ítem/ })
    await item.click()
    await item.fill(SKU)
    await dialog.getByRole('option', { name: new RegExp(SKU) }).click()
    // destino: otra posición del mismo almacén (por defecto ya es ALM-01)
    const toBin = dialog.getByRole('combobox', { name: /^Posición de destino/ })
    await expect(dialog.getByRole('combobox', { name: /^Almacén de destino/ })).toHaveValue(WAREHOUSE)
    await toBin.click()
    await dialog.getByRole('listbox').getByRole('option').filter({ hasNotText: binCode }).first().click()
    await expect(toBin).not.toHaveValue('')
    await dialog.getByLabel(/^Cantidad/).fill('3')
    await dialog.getByLabel(/^Notas/).fill(`Transferir e2e ${STAMP}`)
    await dialog.getByRole('button', { name: 'Transferir' }).click()
    await expectToast(page, 'Transferencia registrada.')

    await filterBySku(page)
    const rows = page.getByRole('row').filter({ hasText: SKU })
    await expect(rows.first()).toBeVisible()
    expect(await rows.count()).toBeGreaterThanOrEqual(1)
    await shot(page, 'transferencias-lista')

    // la pestaña Ajustes conserva sus dos ajustes (los mismos productos no mezclan la transferencia)
    await page.getByRole('tab', { name: 'Ajustes' }).click()
    await filterBySku(page)
    await expect(page.getByRole('row').filter({ hasText: SKU }).filter({ hasText: 'Transferir e2e' })).toHaveCount(0)
    await page.getByRole('tab', { name: 'Transferencias' }).click()

    // clic en la fila: detalle del movimiento
    await page.getByRole('row').filter({ hasText: SKU }).first().click()
    const detail = page.getByRole('dialog', { name: /^Movimiento #\d+/ })
    await expect(detail).toBeVisible()
    await expect(detail.getByText(SKU).first()).toBeVisible()
    await expect(detail.getByText('Movimiento manual (sin documento)')).toBeVisible()
    await expect(detail.getByText(`Transferir e2e ${STAMP}`).first()).toBeVisible()
    txnId = ((await detail.getByRole('heading').first().innerText()).match(/#(\d+)/) ?? [])[1] ?? ''
    expect(txnId).not.toBe('')
    await shot(page, 'movimiento-detalle')
  })

  test('5. Kárdex: un filtro se conserva al pasar a Saldos, el resumen se ve y ?txn= abre el detalle', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/kardex')
    await expect(page.getByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    await filterBySku(page)
    const manual = page.getByRole('switch', { name: 'Solo manuales' })
    await manual.check()
    await expect(manual).toBeChecked()
    const summary = page.getByRole('group', { name: 'Resumen de movimientos' })
    await expect(summary).toBeVisible()
    await expect(summary.getByText('Movimientos', { exact: true })).toBeVisible()
    await expect(page.getByRole('row').filter({ hasText: SKU }).first()).toBeVisible()
    await shot(page, 'kardex-resumen')

    await page.getByRole('tab', { name: 'Saldos' }).click()
    await expect(page.getByRole('tab', { name: 'Saldos' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByRole('button', { name: `Quitar ${SKU}` })).toBeVisible()
    await expect(page.getByRole('switch', { name: 'Solo manuales' })).toBeChecked()
    await expect(summary.getByText('En mano', { exact: true })).toBeVisible()
    await expect(summary.getByText('Disponible', { exact: true })).toBeVisible()

    await page.getByRole('tab', { name: 'Conciliación' }).click()
    await expect(page.getByRole('button', { name: `Quitar ${SKU}` })).toBeVisible()
    await shot(page, 'conciliacion')

    await page.goto(`/warehouse/kardex?txn=${txnId}`)
    await expect(page.getByRole('dialog', { name: `Movimiento #${txnId}` })).toBeVisible()
  })

  test('6. Conteo de lo cambiado: vista previa tras un ajuste, creación, captura en la fila y confirmación en un paso', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/cycle-counts')
    await expect(page.getByRole('heading', { level: 1, name: 'Conteo cíclico' })).toBeVisible()
    // ya no hay pestaña "Tareas de conteo"
    await expect(page.getByRole('tab', { name: 'Tareas de conteo' })).toHaveCount(0)

    await deletePendingChangeCounts(page)

    await page.getByRole('button', { name: 'Conteo de lo cambiado' }).click()
    const dialog = page.getByRole('dialog', { name: 'Conteo de lo cambiado' })
    const wh = dialog.getByRole('combobox', { name: /^Almacén/ })
    if ((await wh.count()) > 0) await pickWarehouse(dialog, wh)
    const preview = dialog.getByRole('region', { name: 'Vista previa' })
    await expect(preview.getByText(/Se crearán \d+ conteo\(s\)/)).toBeVisible()
    await expect(preview.getByText(/movimiento\(s\) en la ventana/)).toBeVisible()
    await shot(page, 'conteo-cambiado-modal')
    await dialog.getByRole('button', { name: /^Crear \d+ conteo\(s\)$/ }).click()
    await expectToast(page, /Se crearon \d+ conteo\(s\) de lo cambiado\./)
    await expect(page.getByRole('dialog')).toHaveCount(0)

    // el conteo creado queda elegido; se busca el de este producto entre los recién creados
    const rows = page.locator('.cc-list-items button.cc-row-main')
    await expect(rows.first()).toBeVisible()
    const detail = page.locator('.cc-detail')
    let found = false
    for (let i = 0; i < Math.min(await rows.count(), 25) && !found; i++) {
      const number = ((await rows.nth(i).innerText()).match(/CC-d+/) ?? [])[0] ?? ''
      await rows.nth(i).click()
      // el panel derecho ya es el de ese conteo (y no el anterior) cuando muestra su número
      await expect(detail).toContainText(number)
      await expect(detail.locator('.cc-lines')).toBeVisible()
      found = (await detail.getByText(SKU).count()) > 0
    }
    expect(found).toBe(true)
    await expect(page).toHaveURL(/[?&]count=\d+/)
    await expect(detail.locator('.chip', { hasText: 'Pendiente' }).first()).toBeVisible()

    // sin contar todo, no se confirma
    const confirm = detail.getByRole('button', { name: 'Confirmar conteo y ajustar' })
    await expect(confirm).toBeDisabled()
    await expect(detail.getByText(/Faltan \d+ línea\(s\) por contar\./)).toBeVisible()

    // captura en la fila: lo esperado en cada línea, y en la de este producto una unidad menos
    const inputs = detail.locator('input.cc-qty')
    const total = await inputs.count()
    expect(total).toBeGreaterThan(0)
    for (let i = 0; i < total; i++) {
      const input = inputs.nth(i)
      const row = input.locator('xpath=ancestor::tr')
      const expected = ((await row.locator('.cc-expected .mono').first().innerText()) || '0').replace(/[^\d.-]/g, '') || '0'
      const value = (await row.getByText(SKU).count()) > 0 ? String(Math.max(Number(expected) - 1, 0)) : expected
      await input.fill(value)
      await input.press('Tab')
    }
    await expect(confirm).toBeEnabled()
    // captura limpia: sin avisos ni "Guardando…" y con la pantalla desde arriba
    await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
    await expect(detail.getByRole('status')).toHaveCount(0)
    await page.evaluate(() => {
      window.scrollTo(0, 0)
      document.querySelectorAll('.main, .stage').forEach((el) => el.scrollTo(0, 0))
    })
    await shot(page, 'conteo-dos-paneles')

    await confirm.click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirmar conteo y ajustar' }).click()
    await expectToast(page, /Conteo CC-\d+: Diferencia/)
    await expect(detail.locator('.chip', { hasText: 'Diferencia' }).first()).toBeVisible()
    await expect(detail.getByText('Conteo cerrado. Los ajustes ya están en el Kárdex de movimientos.')).toBeVisible()

    // los ajustes del conteo llevan al Kárdex, y su detalle muestra el documento de origen
    await detail.getByRole('link', { name: 'Ver los ajustes en el Kárdex' }).click()
    await expect(page).toHaveURL(/\/warehouse\/kardex\?/)
    // el cierre invalida saldos y Kárdex: se espera a que la lista termine de llegar antes del clic (si no, el clic cae en una
    // fila que se vuelve a pintar y el detalle no se abre)
    await page.waitForLoadState('networkidle')
    const txn = page.getByRole('dialog', { name: /^Movimiento #\d+/ })
    // si la fila se vuelve a pintar justo al hacer clic, el detalle no se abre: se reintenta el clic hasta que abra
    await expect(async () => {
      if ((await txn.count()) === 0) await page.getByRole('row').filter({ hasText: SKU }).first().click()
      await expect(txn).toBeVisible({ timeout: 3_000 })
    }).toPass({ timeout: 20_000 })
    await expect(txn.getByText('Documento de origen')).toBeVisible()

    // las demás posiciones con cambios quedaron con su conteo Pendiente: se dan de baja para no dejar basura en la demo
    await page.goto('/warehouse/cycle-counts')
    await deletePendingChangeCounts(page)
  })

  test('7. Pulso: "Necesita tu atención" se ve y, sin descuadres, dice "Todo en orden"', async ({ page }) => {
    await login(page)
    await page.goto('/')
    const panel = page.locator('section.inbox').filter({ has: page.getByRole('heading', { name: 'Necesita tu atención' }) })
    await expect(panel).toBeVisible()
    await expect(panel.getByText('Todo en orden')).toBeVisible()
    await expect(panel.getByText('No hay nada pendiente de revisar.')).toBeVisible()
    await shot(page, 'pulso-atencion')
  })
})

test.describe('Lote 14 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  test('8. Transferencias y ajustes, Kárdex (3 pestañas) y Conteo cíclico sin scroll horizontal', async ({ page }) => {
    await login(page)
    expect(page.viewportSize()?.width).toBe(360)
    await page.goto('/warehouse/transfers-adjustments')
    await expect(page.getByRole('heading', { level: 1, name: 'Transferencias y ajustes' })).toBeVisible()
    await page.waitForLoadState('networkidle')
    await expectNoHorizontalScroll(page)
    await page.goto('/warehouse/transfers-adjustments?tab=transfers')
    await expect(page.getByRole('tab', { name: 'Transferencias' })).toHaveAttribute('aria-selected', 'true')
    await page.waitForLoadState('networkidle')
    await expectNoHorizontalScroll(page)

    for (const [path, tab] of [
      ['/warehouse/kardex', 'Kárdex'],
      ['/warehouse/kardex?tab=balances', 'Saldos'],
      ['/warehouse/kardex?tab=reconciliation', 'Conciliación'],
    ] as const) {
      await page.goto(path)
      await expect(page.getByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true')
      await page.waitForLoadState('networkidle')
      await expectNoHorizontalScroll(page)
    }

    await page.goto('/warehouse/cycle-counts')
    await expect(page.getByRole('heading', { level: 1, name: 'Conteo cíclico' })).toBeVisible()
    // apilados bajo 900 px: sin barra para arrastrar
    await expect(page.getByRole('separator', { name: /Cambiar el ancho/ })).toHaveCount(0)
    await page.waitForLoadState('networkidle')
    await expectNoHorizontalScroll(page)
  })
})
