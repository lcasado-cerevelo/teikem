// Recorrido del Lote 15 (plan de cambios, lote 5: Pulso del día) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics").
// - escritorio (en serie): franja "Almacén hoy" (4 tarjetas, 7 barritas, tooltip), actividad por API que cambia lo de hoy,
//   clic en cada tarjeta (Kárdex, Conteo, Productos con sus filtros), almacén compartido con el panel "Almacén", filas fijas
//   al desplazar, "Tus gráficos" siempre como gráfico (2 por fila, dona con total), "De la compañía" en Análisis → Gráficos
//   e indicadores en una fila por módulo;
// - móvil (360 px): franja 2×2 fija y sin scroll horizontal.
// El producto (P15-{timestamp}) es nuevo en cada corrida (creado por API). No toca SQL ni deja recolecciones abiertas.
// Capturas para el manual: docs/manual/frontend/img/l15-<pantalla>.png (proyectos 'escritorio' y 'movil').
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type WarehouseDto = components['schemas']['WarehouseDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const STAMP = Date.now()
const SKU = `P15-${STAMP}`
const CARDS = ['received', 'outbound', 'countsVariance', 'belowMin'] as const

test.use({ locale: 'es-PR' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

/** Captura para el manual, cuando ya no hay peticiones pendientes. */
async function shot(page: Page, name: string, opts: { mask?: Locator[]; settleMs?: number } = {}) {
  await page.waitForLoadState('networkidle')
  // los gráficos de Recharts se animan al entrar (~1,5 s): la captura espera a que terminen
  if (opts.settleMs) await page.waitForTimeout(opts.settleMs)
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}l15-${name}.png`, animations: 'disabled', caret: 'hide', mask: opts.mask, maskColor: '#2a3346' })
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

/** Tarjeta de la franja por su clave. */
function card(page: Page, key: (typeof CARDS)[number]): Locator {
  return page.locator(`.wh-band [data-card="${key}"]`)
}

/** Número de una tarjeta (el de HOY) o de su texto pequeño "7 días: N". */
async function cardNumbers(page: Page, key: (typeof CARDS)[number]): Promise<{ today: number; total: number }> {
  const num = (s: string) => Number(s.replace(/[^\d.-]/g, '') || '0')
  const c = card(page, key)
  await expect(c.locator('.big')).not.toHaveText(/…|—/)
  const today = num(await c.locator('.big').innerText())
  const sub = (await c.locator('.sub').count()) > 0 ? await c.locator('.sub').innerText() : ''
  return { today, total: num(sub.split(':')[1] ?? '0') }
}

/** Panel "Almacén" del Pulso (h2 exacto: no confundir con "Almacén hoy"). */
function warehousePanel(page: Page): Locator {
  return page.locator('section.panel').filter({ has: page.getByRole('heading', { level: 2, name: 'Almacén', exact: true }) })
}

/** Desplaza el contenedor que hace scroll en el Pulso (la ventana o el ancestro con overflow). */
async function scrollPulse(page: Page, y: number) {
  await page.evaluate((top) => {
    let el: HTMLElement | null = document.querySelector('.pulse-home')
    while (el) {
      const oy = getComputedStyle(el).overflowY
      if ((oy === 'auto' || oy === 'scroll') && el.scrollHeight > el.clientHeight) {
        el.scrollTo(0, top)
        return
      }
      el = el.parentElement
    }
    window.scrollTo(0, top)
  }, y)
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que lote14). */
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

/** Espera a que la franja y el resto del Pulso terminen de cargar. */
async function waitPulse(page: Page) {
  await expect(page.locator('.wh-band')).toBeVisible()
  for (const key of CARDS) await expect(card(page, key).locator('.big')).not.toHaveText(/…|—/)
  await expect(page.locator('.pulse-charts .pulse-chartbox').first()).toBeVisible()
}

test.describe('Lote 15 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })

  test('1. franja "Almacén hoy": 4 tarjetas, 7 barritas en las tres primeras y ninguna en "Productos bajo mínimo"', async ({ page }) => {
    await login(page)
    await waitPulse(page)
    const band = page.locator('.wh-band')
    await expect(band.getByRole('heading', { level: 2, name: /^Almacén hoy/ })).toBeVisible()
    await expect(band.getByRole('heading', { level: 2 })).toContainText('últimos 7 días')

    const cards = band.locator('[data-card]')
    await expect(cards).toHaveCount(4)
    expect(await cards.evaluateAll((els) => els.map((e) => e.getAttribute('data-card')))).toEqual([...CARDS])
    await expect(card(page, 'received')).toContainText('Unidades recibidas')
    await expect(card(page, 'outbound')).toContainText('Unidades de salida')
    await expect(card(page, 'countsVariance')).toContainText('Conteos con diferencia')
    await expect(card(page, 'belowMin')).toContainText('Productos bajo mínimo')

    for (const key of ['received', 'outbound', 'countsVariance'] as const) {
      await expect(card(page, key).locator('.recharts-bar-rectangle')).toHaveCount(7)
      await expect(card(page, key)).toContainText('7 días:')
    }
    await expect(card(page, 'belowMin').locator('.recharts-bar-rectangle')).toHaveCount(0)
    await expect(card(page, 'belowMin').locator('svg.recharts-surface')).toHaveCount(0)
    await expect(card(page, 'belowMin')).toContainText('en este momento')

    // el pulso del día va arriba de todo (antes de "Necesita tu atención")
    const bandTop = (await band.boundingBox())?.y ?? 0
    const attentionTop = (await page.locator('section.inbox').boundingBox())?.y ?? 0
    expect(bandTop).toBeLessThan(attentionTop)
    await shot(page, 'pulso-franja')

    // tooltip de una barrita: la fecha larga de hoy y la cantidad
    const last = card(page, 'received').locator('.recharts-bar-rectangle').last()
    await last.hover()
    const tip = page.locator('.recharts-tooltip-wrapper').filter({ hasText: 'Hoy' }).first()
    await expect(tip).toBeVisible()
    await expect(tip).toContainText('Unidades')
    await shot(page, 'pulso-franja-tooltip')
  })

  test('2. la actividad de hoy cambia la franja: un recibo suma a "Unidades recibidas" y una recolección a "de salida"', async ({ page, request }) => {
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    await login(page)
    await waitPulse(page)
    const before = { received: await cardNumbers(page, 'received'), outbound: await cardNumbers(page, 'outbound') }

    // producto nuevo + recibo a ciegas de 5 unidades en ALM-01 (se confirma) + recolección de 2
    const whs = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers })).json()) as WarehouseDto[]
    const wh = whs.find((w) => w.code === 'ALM-01') ?? whs[0]
    const bins = (await (await request.get(`${API_URL}/api/v1/warehouses/${wh.publicId}/bins?take=500`, { headers })).json()) as { items?: { id?: number; zoneTypeCode?: string }[] }
    const staging = bins.items?.find((b) => b.zoneTypeCode === 'STAGING')
    expect(staging, 'ALM-01 necesita una posición en zona STAGING').toBeTruthy()
    const created = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: SKU, name: `Producto pulso e2e ${STAMP}`, trackingType: 'NONE' } })
    expect(created.ok()).toBeTruthy()
    const product = ((await created.json()) as { product: { publicId: string } }).product
    const receipt = await request.post(`${API_URL}/api/v1/receipts`, {
      headers,
      data: { warehousePublicId: wh.publicId, type: 'BLIND', stagingBinId: staging?.id, lines: [{ productPublicId: product.publicId, receivedQty: 5 }] },
    })
    expect(receipt.ok(), await receipt.text()).toBeTruthy()
    const receiptBody = (await receipt.json()) as { header: { publicId: string } }
    const confirmed = await request.post(`${API_URL}/api/v1/receipts/${receiptBody.header.publicId}/confirm`, { headers, data: {} })
    expect(confirmed.ok()).toBeTruthy()
    const batch = await request.post(`${API_URL}/api/v1/pick-batches`, {
      headers,
      data: { warehousePublicId: wh.publicId, lines: [{ productPublicId: product.publicId, quantity: 2 }] },
    })
    expect(batch.ok()).toBeTruthy()
    const batchId = ((await batch.json()) as { publicId?: string }).publicId

    await page.reload()
    await waitPulse(page)
    const after = { received: await cardNumbers(page, 'received'), outbound: await cardNumbers(page, 'outbound') }
    // otros recorridos en paralelo también mueven inventario: solo se exige que suba al menos lo hecho aquí
    expect(after.received.today - before.received.today).toBeGreaterThanOrEqual(5)
    expect(after.received.total - before.received.total).toBeGreaterThanOrEqual(5)
    expect(after.outbound.today - before.outbound.today).toBeGreaterThanOrEqual(2)
    expect(after.outbound.total - before.outbound.total).toBeGreaterThanOrEqual(2)
    // hoy nunca supera el total de los 7 días, que incluye hoy
    expect(after.received.total).toBeGreaterThanOrEqual(after.received.today)

    // el recorrido no deja la recolección abierta: eliminarla resta su salida (neto cero)
    if (batchId) {
      const del = await request.delete(`${API_URL}/api/v1/pick-batches/${batchId}`, { headers })
      expect(del.ok()).toBeTruthy()
    }
  })

  test('3. clic en cada tarjeta lleva a su pantalla con los filtros (Kárdex con tipo y fechas, Conteo en Diferencia, Productos bajo mínimo)', async ({ page }) => {
    await login(page)
    await waitPulse(page)
    const query = () => new URL(page.url()).searchParams

    // recibidas → Kárdex de movimientos con el tipo Recepción y los 7 días
    await card(page, 'received').getByRole('link').click()
    await expect(page).toHaveURL(/\/warehouse\/kardex\?/)
    await expect(page.getByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    expect(query().getAll('types')).toEqual(['RECEIPT'])
    expect(query().get('from')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    expect(query().get('to')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    const from = new Date(`${query().get('from')}T00:00:00Z`).getTime()
    const to = new Date(`${query().get('to')}T00:00:00Z`).getTime()
    expect((to - from) / 86_400_000).toBe(6)
    await expect(page.getByRole('group', { name: 'Resumen de movimientos' })).toBeVisible()
    await page.waitForLoadState('networkidle')
    await shot(page, 'franja-kardex-recibidas')

    // salida → Kárdex con Despacho y Cruce de muelle
    await page.goBack()
    await waitPulse(page)
    await card(page, 'outbound').getByRole('link').click()
    await expect(page).toHaveURL(/\/warehouse\/kardex\?/)
    expect(query().getAll('types').sort()).toEqual(['CROSSDOCK', 'ISSUE'])
    expect(query().get('from')).toMatch(/^\d{4}-\d{2}-\d{2}$/)

    // conteos con diferencia → Conteo cíclico en el estatus Diferencia
    await page.goBack()
    await waitPulse(page)
    await card(page, 'countsVariance').getByRole('link').click()
    await expect(page).toHaveURL(/\/warehouse\/cycle-counts\?/)
    expect(query().get('status')).toBe('RECONCILED_VARIANCE')
    await expect(page.getByRole('heading', { level: 1, name: 'Conteo cíclico' })).toBeVisible()
    await page.waitForLoadState('networkidle')
    // la lista solo trae conteos en Diferencia (el filtro de estatus llegó de la URL)
    const chips = page.locator('.cc-list-items .chip[data-code]')
    await expect(chips.first()).toBeVisible()
    expect(await chips.evaluateAll((els) => [...new Set(els.map((e) => e.getAttribute('data-code')))])).toEqual(['RECONCILED_VARIANCE'])
    await shot(page, 'franja-conteo-diferencia')

    // bajo mínimo → Productos con el filtro Bajo mínimo
    await page.goBack()
    await waitPulse(page)
    await card(page, 'belowMin').getByRole('link').click()
    await expect(page).toHaveURL(/\/warehouse\/products\?/)
    expect(query().get('kpi')).toBe('low')
    await expect(page.getByRole('heading', { level: 1, name: /Productos/ })).toBeVisible()
    await page.waitForLoadState('networkidle')
    await shot(page, 'franja-productos-bajo-minimo')
  })

  test('4. el almacén elegido en la franja cambia el del panel "Almacén" (y al revés) y viaja en los enlaces', async ({ page }) => {
    await login(page)
    await waitPulse(page)
    const bandSelect = page.locator('.wh-band').getByRole('combobox', { name: 'Almacén de la franja Almacén hoy' })
    const panelSelect = warehousePanel(page).getByRole('combobox', { name: 'Almacén' })
    await expect(bandSelect).toHaveValue('')
    await expect(bandSelect.locator('option')).not.toHaveCount(1)

    // el segundo option es un almacén real
    const publicId = (await bandSelect.locator('option').nth(1).getAttribute('value')) ?? ''
    expect(publicId).not.toBe('')
    await bandSelect.selectOption(publicId)
    await expect(panelSelect).toHaveValue(publicId)
    await expect(card(page, 'received').getByRole('link')).toHaveAttribute('href', new RegExp(`warehousePublicIds=${encodeURIComponent(publicId)}`))

    // sigue elegido al recargar (se recuerda por usuario)
    await page.reload()
    await waitPulse(page)
    await expect(bandSelect).toHaveValue(publicId)
    await expect(panelSelect).toHaveValue(publicId)

    // al revés: elegir "Todos" en el panel lo cambia en la franja
    await panelSelect.selectOption('')
    await expect(bandSelect).toHaveValue('')
    await expect(card(page, 'received').getByRole('link')).not.toHaveAttribute('href', /warehousePublicIds=/)
  })

  test('5. al desplazar, la fecha y la franja siguen a la vista (fijas) y "Necesita tu atención" se va', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 })
    await login(page)
    await waitPulse(page)
    const title = page.getByRole('heading', { level: 1 })
    const band = page.locator('.wh-band')
    const attention = page.getByRole('heading', { name: 'Necesita tu atención' })
    await expect(attention).toBeInViewport()
    const bandTopBefore = (await band.boundingBox())?.y ?? 0

    await scrollPulse(page, 100_000)
    await expect(attention).not.toBeInViewport()
    await expect(title).toBeInViewport()
    await expect(band).toBeInViewport({ ratio: 0.99 })
    // la franja queda pegada justo bajo la fila de la fecha (el saludo, que no es fijo, se fue con el scroll)
    const bandTopAfter = (await band.boundingBox())?.y ?? -1
    const headBottom = await page.locator('.pulse-pin-head').evaluate((el) => el.getBoundingClientRect().bottom)
    expect(bandTopAfter).toBeGreaterThanOrEqual(0)
    expect(bandTopAfter).toBeLessThan(200)
    expect(Math.abs(bandTopAfter - headBottom)).toBeLessThan(24)
    expect(bandTopAfter).toBeLessThan(bandTopBefore)
    // "Tus gráficos" quedó a la vista debajo de las filas fijas
    await expect(page.getByRole('heading', { level: 2, name: 'Tus gráficos' })).toBeAttached()
    await shot(page, 'pulso-fijas')
  })

  test('6. "Tus gráficos": siempre gráfico (svg, sin lista de puntos), 2 por fila como máximo y los dos de almacén en la primera fila', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1250 })
    await login(page)
    await waitPulse(page)
    const section = page.locator('section').filter({ has: page.getByRole('heading', { level: 2, name: 'Tus gráficos' }) })
    const panels = section.locator('.pulse-charts > *')
    const count = await panels.count()
    expect(count).toBeGreaterThanOrEqual(2)

    // cada gráfico es un svg dibujado (role=img) y no hay lista de puntos (.pulse-pts) ni tablas de respaldo
    await expect(section.locator('.pulse-pts')).toHaveCount(0)
    await expect(section.locator('.pulse-chartbox svg.recharts-surface').first()).toBeVisible()
    for (let i = 0; i < count; i++) {
      const p = panels.nth(i)
      const empty = await p.locator('.pulse-muted').count()
      if (empty === 0) await expect(p.locator('.pulse-chartbox .recharts-wrapper > svg.recharts-surface')).toHaveCount(1)
    }

    // nunca más de 2 por fila
    const tops = await panels.evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().top)))
    const perRow = new Map<number, number>()
    for (const t of tops) perRow.set(t, (perRow.get(t) ?? 0) + 1)
    expect(Math.max(...perRow.values())).toBeLessThanOrEqual(2)

    // primera fila: valor de inventario (dona con total) a la izquierda y movimientos por tipo a la derecha
    const value = panels.filter({ hasText: 'Valor de inventario por categoría' })
    const moves = panels.filter({ hasText: 'Movimientos de inventario por tipo' })
    await expect(value).toHaveCount(1)
    await expect(moves).toHaveCount(1)
    const vBox = await value.boundingBox()
    const mBox = await moves.boundingBox()
    const firstTop = Math.min(...tops)
    expect(Math.round(vBox?.y ?? -1)).toBe(firstTop)
    expect(Math.round(mBox?.y ?? -1)).toBe(firstTop)
    expect(vBox?.x ?? 0).toBeLessThan(mBox?.x ?? 0)

    // la dona lleva el total en el centro
    await expect(value.locator('[data-chart-kind="donut"]')).toHaveCount(1)
    await expect(value.locator('svg text').filter({ hasText: /\d/ }).first()).toBeVisible()
    // los movimientos son barras
    await expect(moves.locator('[data-chart-kind="bar"]')).toHaveCount(1)

    await section.getByRole('heading', { level: 2, name: 'Tus gráficos' }).scrollIntoViewIfNeeded()
    await shot(page, 'pulso-graficos', { settleMs: 2000 })
  })

  test('7. Análisis → Gráficos: los dos de fábrica ahora son "De la compañía" y se pueden editar', async ({ page }) => {
    await login(page)
    await page.goto('/analytics/charts')
    await expect(page.getByRole('heading', { level: 1, name: 'Gráficos' })).toBeVisible()
    await page.waitForLoadState('networkidle')
    for (const name of ['Valor de inventario por categoría', 'Movimientos de inventario por tipo']) {
      const row = page.locator('.panel, section, article, li, tr').filter({ has: page.getByText(name, { exact: false }) }).filter({ hasText: 'De la compañía' }).last()
      await expect(row).toBeVisible()
      await expect(row).toContainText('De la compañía')
      await expect(row.getByRole('button', { name: /^Editar/ }).or(row.getByRole('link', { name: /^Editar/ })).first()).toBeVisible()
    }
    await page.getByText('Valor de inventario por categoría').first().scrollIntoViewIfNeeded()
    await shot(page, 'graficos-de-la-compania', { settleMs: 2000 })
  })

  test('8. "Tus indicadores": una fila por módulo con su etiqueta, en el orden del menú (Operación, Almacén…)', async ({ page }) => {
    await login(page)
    await waitPulse(page)
    const section = page.locator('section').filter({ has: page.getByRole('heading', { level: 2, name: 'Tus indicadores' }) })
    await expect(section.getByRole('heading', { level: 2, name: 'Tus indicadores' })).toBeVisible()
    const lines = section.locator('.pulse-line')
    expect(await lines.count()).toBeGreaterThanOrEqual(2)
    const names = (await lines.locator('h3').evaluateAll((els) => els.map((e) => (e.textContent ?? '').trim())))
    expect(names.slice(0, 2)).toEqual(['Operación', 'Almacén'])
    // cada fila tiene sus indicadores y esa fila es un río propio (una sola línea de tarjetas en escritorio)
    for (let i = 0; i < (await lines.count()); i++) expect(await lines.nth(i).locator('.river > .node').count()).toBeGreaterThan(0)
    // captura sin desplazar y con una ventana alta: desplazado, la fila "Operación" quedaría bajo la franja fija
    const viewport = page.viewportSize()
    await page.setViewportSize({ width: viewport?.width ?? 1280, height: 1250 })
    await scrollPulse(page, 0)
    await shot(page, 'pulso-indicadores')
    if (viewport) await page.setViewportSize(viewport)
  })
})

test.describe('Lote 15 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  test('9. franja compacta 2×2, fija al desplazar y sin scroll horizontal', async ({ page }) => {
    await login(page)
    expect(page.viewportSize()?.width).toBe(360)
    await waitPulse(page)
    const band = page.locator('.wh-band')
    const boxes = await page.locator('.wh-band [data-card]').evaluateAll((els) => els.map((e) => e.getBoundingClientRect()).map((r) => ({ x: Math.round(r.left), y: Math.round(r.top), w: r.width })))
    expect(boxes).toHaveLength(4)
    // 2×2: dos columnas y dos filas
    expect(new Set(boxes.map((b) => b.x)).size).toBe(2)
    expect(new Set(boxes.map((b) => b.y)).size).toBe(2)
    // compacta: sin el texto pequeño de los 7 días
    await expect(card(page, 'received').locator('.sub')).toBeHidden()
    await expectNoHorizontalScroll(page)
    await shot(page, 'pulso-franja-movil')

    // fija: tras desplazar la franja sigue a la vista y "Necesita tu atención" no
    const attention = page.getByRole('heading', { name: 'Necesita tu atención' })
    await scrollPulse(page, 100_000)
    await expect(attention).not.toBeInViewport()
    await expect(band).toBeInViewport({ ratio: 0.99 })
    await expect(page.getByRole('heading', { level: 1 })).toBeInViewport()
    await expectNoHorizontalScroll(page)

    // los gráficos se apilan (uno debajo del otro) y los indicadores siguen sin desbordar
    const tops = await page.locator('.pulse-charts > *').evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().left)))
    expect(new Set(tops).size).toBe(1)
  })
})
