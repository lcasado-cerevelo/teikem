// Recorrido del Lote F12 (conteo cíclico por producto, revisión rápida y corrección del supervisor en la web) contra el API real
// (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo "Advance Logistics", almacén ALM-01).
// Siembra por API (un producto con existencia en tres posiciones propias del recorrido y otro en una cuarta):
// - conteo A por producto: el operario (capturado por API) cuenta 9 donde hay 8, 3 donde hay 5 (error que el supervisor corrige a 5)
//   y 6 donde hay 6, y halla 2 en una posición nueva creada desde el conteo (provisional); se termina (Contado);
// - conteo B por producto del otro: cuenta lo mismo que hay (cuadra).
// Escritorio: "Por revisar" (estado calculado, búsqueda), detalle con solo las líneas que fallan y su evidencia, corrección en la
// fila (queda "Corregido a 5"), vista previa del efecto, "Cerrar los que cuadran" (cierra B en Concordancia), confirmar la
// posición provisional y reconciliar A (Diferencia con 2 ajustes); el API lo confirma. Móvil (360 px): la pestaña, el detalle y la
// vista previa sin scroll horizontal de página.
// Capturas para el manual: docs/manual/frontend/img/f12-<pantalla>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type CycleCountDetailDto = components['schemas']['CycleCountDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const STAMP = String(Date.now())
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

test.use({ locale: 'es-PR' })

interface Seed {
  /** Texto común de los SKU del recorrido (para el buscador). */
  token: string
  skuA: string
  /** Código de la posición `n` (1..4) del recorrido. */
  binCode: (n: number) => string
  countA: { id: number; number: string }
  countB: { id: number; number: string }
  provisionalCode: string
  warehousePublicId: string
}

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f12-${name}.png`, animations: 'disabled', caret: 'hide' })
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

async function expectToast(page: Page, text: string | RegExp) {
  await expect(page.locator('.toast').filter({ hasText: text }).first()).toBeVisible()
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que lote14). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    const describe = (el: Element) => `${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')}`
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      // lo que vive dentro de un contenedor con desplazamiento propio (tabla de un modal) no desplaza la página
      if (el.parentElement?.closest('.dt-scroll')) return
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
 * Siembra por API. `variant` distingue la del escritorio de la del móvil (cada proyecto la suya: corren en paralelo).
 * Posiciones propias del recorrido en la zona RSV de ALM-01 (ningún otro recorrido las toca).
 */
async function seed(request: APIRequestContext, variant: 'd' | 'm'): Promise<Seed> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const ok = async <T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> => {
    const res = await p
    expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
    return (await res.json()) as T
  }
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const wh = warehouses.find((w) => w.code === 'ALM-01')!.publicId
  const zones = await ok<{ id: number; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses/${wh}/zones`, { headers }), 'zonas')
  const zone = zones.find((z) => z.code === 'RSV')!.id
  // el API guarda códigos y SKU en mayúsculas
  const suffix = `${variant}${STAMP.slice(-7)}`.toUpperCase()
  const bin = async (n: number) =>
    (await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${wh}/bins`, { headers, data: { zoneId: zone, code: `F12-${suffix}-${n}` } }), 'posición')).id
  const [b1, b2, b3, b4] = [await bin(1), await bin(2), await bin(3), await bin(4)]
  const token = `F12${suffix}`
  const skuA = `${token}-A`
  const product = async (sku: string, name: string) =>
    (await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name, trackingType: 'NONE', purchaseCost: 1 } }), 'producto')).product
      .publicId
  const pA = await product(skuA, `Tornillo por producto ${suffix}`)
  const pB = await product(`${token}-B`, `Tuerca por producto ${suffix}`)
  const adjust = (productPublicId: string, binId: number, quantity: number) =>
    ok(
      request.post(`${API_URL}/api/v1/inventory/adjustments`, {
        headers,
        data: { productPublicId, warehousePublicId: wh, binId, quantity, reason: 'FOUND', notes: 'e2e lote F12' },
      }),
      'ajuste',
    )
  await adjust(pA, b1, 8)
  await adjust(pA, b2, 5)
  await adjust(pA, b3, 6)
  await adjust(pB, b4, 4)

  // conteo A por producto: una línea por posición con existencia; lo hallado en una posición nueva (provisional)
  const a = await ok<CycleCountDetailDto>(request.post(`${API_URL}/api/v1/cycle-counts`, { headers, data: { warehousePublicId: wh, productPublicIds: [pA] } }), 'conteo A')
  expect(a.count?.originCode).toBe('PRODUCT')
  expect(a.lines).toHaveLength(3)
  const lineOf = (d: CycleCountDetailDto, binId: number) => d.lines!.find((l) => l.binId === binId)!.id!
  const provisionalCode = `PF12-${suffix}`
  const prov = await ok<{ id: number; isProvisional: boolean }>(
    request.post(`${API_URL}/api/v1/cycle-counts/${a.count!.id}/bins`, { headers, data: { zoneId: zone, code: provisionalCode } }),
    'posición provisional',
  )
  expect(prov.isProvisional).toBe(true)
  await ok(
    request.put(`${API_URL}/api/v1/cycle-counts/${a.count!.id}/lines/batch`, {
      headers,
      data: {
        lines: [
          { lineId: lineOf(a, b1), countedQty: 9 },
          { lineId: lineOf(a, b2), countedQty: 3 },
          { lineId: lineOf(a, b3), countedQty: 6 },
          { binId: prov.id, productPublicId: pA, countedQty: 2 },
        ],
      },
    }),
    'captura A',
  )
  await ok(request.post(`${API_URL}/api/v1/cycle-counts/${a.count!.id}/finish`, { headers, data: {} }), 'terminar A')

  // conteo B: cuadra
  const b = await ok<CycleCountDetailDto>(request.post(`${API_URL}/api/v1/cycle-counts`, { headers, data: { warehousePublicId: wh, productPublicIds: [pB] } }), 'conteo B')
  await ok(request.put(`${API_URL}/api/v1/cycle-counts/${b.count!.id}/lines/batch`, { headers, data: { lines: [{ lineId: b.lines![0].id, countedQty: 4 }] } }), 'captura B')
  await ok(request.post(`${API_URL}/api/v1/cycle-counts/${b.count!.id}/finish`, { headers, data: {} }), 'terminar B')

  return {
    token,
    skuA,
    binCode: (n: number) => `F12-${suffix}-${n}`,
    countA: { id: a.count!.id!, number: a.count!.number! },
    countB: { id: b.count!.id!, number: b.count!.number! },
    provisionalCode,
    warehousePublicId: wh,
  }
}

/** Fila (tabla) o tarjeta (panel angosto o móvil) de una lista que contiene `text`. */
const item = (scope: Locator, text: string) => scope.locator('tbody tr, .dt-card').filter({ hasText: text })

/** Abre "Por revisar" y busca los conteos del recorrido. */
async function openReview(page: Page, s: Seed): Promise<Locator> {
  await page.goto('/warehouse/cycle-counts')
  await expect(page.getByRole('heading', { level: 1, name: 'Conteo cíclico' })).toBeVisible()
  await page.getByRole('tab', { name: 'Por revisar' }).click()
  await expect(page).toHaveURL(/tab=review/)
  const review = page.locator('.cc-review')
  await review.getByRole('searchbox').fill(s.token)
  await expect(review.getByText(s.countA.number)).toBeVisible()
  await expect(review.getByText(s.countB.number)).toBeVisible()
  return review
}

test.describe('Lote F12 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'd')
  })

  test('1. Por revisar: estado calculado y detalle con solo las líneas que fallan y su evidencia', async ({ page }) => {
    await login(page)
    const review = await openReview(page, s)
    const rowA = item(review, s.countA.number)
    const rowB = item(review, s.countB.number)
    await expect(rowA.locator('.chip', { hasText: 'Con diferencia' })).toBeVisible()
    await expect(rowB.locator('.chip', { hasText: 'Cuadra' })).toBeVisible()
    await expect(rowA).toContainText('Tornillo por producto')
    await shot(page, 'por-revisar')

    await rowA.click()
    await expect(page).toHaveURL(new RegExp(`count=${s.countA.id}`))
    const detail = page.locator('.cc-detail')
    await expect(detail.getByText(s.countA.number, { exact: true })).toBeVisible()
    // 3 de 4 fallan contra la existencia actual (la de 6 cuadra y no se ve)
    await expect(detail.getByText('3 de 4 líneas: solo las que fallan')).toBeVisible()
    await expect(detail.getByText(s.binCode(3))).toHaveCount(0)
    await expect(detail.getByText(/^Contó 3 \(Administrador Advance, .+\)$/)).toBeVisible()
    await expect(detail.getByText('Posición pendiente de revisión')).toBeVisible()
    await shot(page, 'detalle-solo-fallan')
    await detail.getByRole('switch', { name: 'Ver todas' }).check()
    await expect(detail.getByText('4 líneas', { exact: true })).toBeVisible()
    await expect(detail.getByText(s.binCode(3))).toBeVisible()
    await detail.getByRole('switch', { name: 'Ver todas' }).uncheck()
  })

  test('2. corrección en la fila (no es un ajuste) y vista previa del efecto', async ({ page }) => {
    await login(page)
    await page.goto(`/warehouse/cycle-counts?tab=review&count=${s.countA.id}`)
    const detail = page.locator('.cc-detail')
    await expect(detail.getByText('3 de 4 líneas: solo las que fallan')).toBeVisible()
    // la línea mal contada (3 donde hay 5): la única con −2
    const row = item(detail, s.binCode(2))
    const qty = row.getByRole('textbox', { name: new RegExp(`^Contado de la línea \\d+ \\(${s.skuA}\\)$`) })
    await qty.fill('5')
    await qty.press('Tab')
    await expect(row.getByText(/^Corregido a 5 \(Administrador Advance, .+\)$/)).toBeVisible()
    await expect(row.getByText('Corrección', { exact: true })).toBeVisible()
    // ya no falla pero se queda a la vista (se acaba de corregir)
    await expect(row).toBeVisible()
    await shot(page, 'correccion')

    await detail.getByRole('button', { name: /Confirmar conteo y ajustar/ }).click()
    const dialog = page.getByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    await expect(dialog.getByText('Al confirmar se asentarán 2 movimiento(s) en el Kárdex y el conteo terminará en Diferencia.')).toBeVisible()
    const totals = dialog.getByRole('group', { name: 'Totales de la vista previa' })
    await expect(totals).toContainText('Movimientos')
    const provRow = item(dialog, s.provisionalCode)
    await expect(provRow).toContainText('+2')
    await expect(dialog.getByRole('button', { name: 'Confirmar conteo y ajustar' })).toBeEnabled()
    await shot(page, 'vista-previa')
    await dialog.getByRole('button', { name: 'Volver a revisar' }).click()
    await expect(dialog).toHaveCount(0)
  })

  test('3. "Cerrar los que cuadran" cierra B en Concordancia; se confirma la posición y se reconcilia A', async ({ page, request }) => {
    await login(page)
    const review = await openReview(page, s)
    await review.getByRole('checkbox', { name: `Elegir ${s.countB.number} para cerrar` }).check()
    await review.getByRole('button', { name: 'Cerrar los elegidos (1)' }).click()
    const confirm = page.getByRole('dialog', { name: 'Cerrar los que cuadran' })
    await expect(confirm.getByText(new RegExp(`Se van a cerrar 1 conteo\\(s\\) que cuadran: ${s.countB.number}\\.`))).toBeVisible()
    await confirm.getByLabel('Comentario (opcional)').fill('Revisión e2e F12')
    await confirm.getByRole('button', { name: 'Cerrar 1 conteo(s)' }).click()
    const result = page.getByRole('dialog', { name: 'Resultado del cierre' })
    await expect(result.getByText(`${s.countB.number}: Concordancia, 1 línea(s), sin movimientos`)).toBeVisible()
    await shot(page, 'cierre-resultado')
    await result.getByRole('button', { name: 'Listo' }).click()
    await expect(review.getByText(s.countB.number)).toHaveCount(0)

    // A: confirmar la posición provisional y reconciliar
    await item(review, s.countA.number).click()
    const detail = page.locator('.cc-detail')
    await detail.getByRole('button', { name: `Confirmar la posición ${s.provisionalCode}` }).click()
    await expectToast(page, `Posición ${s.provisionalCode} confirmada.`)
    await expect(detail.getByText('Posición pendiente de revisión')).toHaveCount(0)
    await detail.getByRole('button', { name: /Confirmar conteo y ajustar/ }).click()
    const dialog = page.getByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    await expect(dialog.getByText(/se asentarán 2 movimiento\(s\)/)).toBeVisible()
    await dialog.getByRole('button', { name: 'Confirmar conteo y ajustar' }).click()
    await expectToast(page, new RegExp(`Conteo ${s.countA.number}: Diferencia, 2 ajuste\\(s\\) en el Kárdex\\.`))
    await expect(detail.getByText('Conteo cerrado. Los ajustes ya están en el Kárdex de movimientos.', { exact: false })).toBeVisible()

    // el API lo confirma
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const a = (await (await request.get(`${API_URL}/api/v1/cycle-counts/${s.countA.id}`, { headers })).json()) as CycleCountDetailDto
    const b = (await (await request.get(`${API_URL}/api/v1/cycle-counts/${s.countB.id}`, { headers })).json()) as CycleCountDetailDto
    expect(a.count?.statusCode).toBe('RECONCILED_VARIANCE')
    expect(b.count?.statusCode).toBe('RECONCILED')
    const corrected = a.lines!.find((l) => l.wasCorrected)
    expect(corrected?.capturedQty).toBe(3)
    expect(corrected?.countedQty).toBe(5)
    const bins = await (await request.get(`${API_URL}/api/v1/warehouses/${s.warehousePublicId}/bins?isProvisional=true&take=200`, { headers })).json()
    expect((bins.items as { code: string }[]).some((x) => x.code === s.provisionalCode)).toBe(false)
  })
})

test.describe('Lote F12 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil')
  let s: Seed

  test.beforeAll(async ({ request }, testInfo) => {
    if (!testInfo.project.name.startsWith('movil')) return
    s = await seed(request, 'm')
  })

  test('Por revisar, detalle y vista previa sin scroll horizontal de página', async ({ page }) => {
    await login(page)
    const review = await openReview(page, s)
    // bajo 720 px la tabla pasa a tarjetas
    await expect(review.locator('.dt-card').filter({ hasText: s.countA.number })).toBeVisible()
    await expectNoHorizontalScroll(page)
    await shot(page, 'movil-por-revisar')
    await review.locator('.dt-card').filter({ hasText: s.countA.number }).click()
    const detail = page.locator('.cc-detail')
    await expect(detail.getByText('3 de 4 líneas: solo las que fallan')).toBeVisible()
    await expectNoHorizontalScroll(page)
    await detail.getByRole('button', { name: /Confirmar conteo y ajustar/ }).click()
    const dialog = page.getByRole('dialog', { name: 'Confirmar conteo y ajustar' })
    await expect(dialog.getByText('Existencia actual').first()).toBeVisible()
    await expectNoHorizontalScroll(page)
    await shot(page, 'movil-vista-previa')
    await dialog.getByRole('button', { name: 'Volver a revisar' }).click()
  })
})
