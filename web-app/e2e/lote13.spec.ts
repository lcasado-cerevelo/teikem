// Recorrido del Lote 13 (plan de cambios, lote 3: Recibo + Recolección y empaque) contra el API real (db-init hecho, API en
// API_URL, por defecto http://localhost:5000). El flujo feliz (recibo ciego sin diferencia, acomodo, recolección y empaque)
// está en f6.spec.ts, pasos 7 a 9; aquí va lo propio del lote:
// - escritorio: recibo con diferencia (Esperado → Recibiendo → Discrepancia → Completado con diferencia) y la barra
//   arrastrable de Recolección y empaque (teclado, se recuerda al recargar, Enter vuelve a 60/40);
// - móvil (360 px): Recibo (sus tres pestañas) y Recolección y empaque sin scroll horizontal.
// El producto (REC13-{timestamp}) es nuevo en cada corrida; las 10 unidades recibidas se quedan en él.
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const WAREHOUSE = 'ALM-01 · Almacén principal'
const STAMP = Date.now()
const SKU = `REC13-${STAMP}`

test.use({ locale: 'es-PR' })

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

async function expectToast(page: Page, text: string | RegExp) {
  await expect(page.locator('.toast').filter({ hasText: text }).first()).toBeVisible()
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que f6.spec.ts). */
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

test.describe('Lote 13 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })

  test.beforeAll(async ({ request }) => {
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const created = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: SKU, name: `Producto recibo e2e ${STAMP}`, trackingType: 'NONE' } })
    expect(created.ok()).toBeTruthy()
  })

  test('1. recibo con diferencia: Recibiendo → Discrepancia (aviso) → Completado con diferencia, con lo recibido en inventario', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/receipts')
    await page.getByRole('button', { name: 'Nuevo recibo' }).click()
    const dialog = page.getByRole('dialog', { name: 'Nuevo recibo' })
    await dialog.getByLabel(/^Origen/).selectOption('BLIND')
    await pickWarehouse(dialog, dialog.getByRole('combobox', { name: /^Almacén/ }))
    await dialog.getByRole('button', { name: 'Crear recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ creado\./)
    const detail = page.locator('.rcp-side')
    await expect(detail.locator('.chip', { hasText: 'Esperado' }).first()).toBeVisible()
    // sin líneas no se puede confirmar
    await expect(detail.getByRole('button', { name: 'Confirmar recibo' })).toBeDisabled()

    await pickProduct(detail, 'Producto de la línea 1', SKU)
    const received = detail.getByRole('textbox', { name: 'Recibido de la línea 1' })
    await received.fill('10')
    await received.press('Tab')
    await expect(detail.locator('.chip', { hasText: 'Recibiendo' }).first()).toBeVisible()

    // esperado 12, recibido 10: Discrepancia, diferencia −2 y el aviso de lo que pasará al confirmar
    const expected = detail.getByRole('textbox', { name: 'Esperado de la línea 1' })
    await expected.fill('12')
    await expected.press('Tab')
    await expect(detail.locator('.chip', { hasText: 'Discrepancia' }).first()).toBeVisible()
    await expect(detail.getByText(/Completado con diferencia/).first()).toBeVisible()

    await detail.getByRole('button', { name: 'Confirmar recibo' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirmar recibo' }).click()
    await expectToast(page, /Recibo REC-\d+ confirmado\./)
    await expect(detail.locator('.chip', { hasText: 'Completado con diferencia' }).first()).toBeVisible()
    // entra lo recibido (10), no lo esperado: una tarea de acomodo por 10
    const task = detail.getByRole('row').filter({ hasText: SKU }).filter({ hasText: 'Pendiente' })
    await expect(task).toHaveCount(1)
    await expect(task.getByRole('cell').nth(2)).toHaveText('10')
    // confirmado: las líneas ya no se editan
    await expect(detail.getByRole('textbox', { name: 'Recibido de la línea 1' })).toHaveCount(0)
  })

  test('2. Recolección y empaque: la barra entre paneles se mueve con el teclado, se recuerda al recargar y Enter vuelve a 60/40', async ({ page }) => {
    await login(page)
    await page.goto('/warehouse/pick-batches')
    await page.evaluate(() => window.localStorage.removeItem('teikem.split.pick-batches'))
    await page.reload()
    const bar = page.getByRole('separator', { name: 'Cambiar el ancho de los paneles Recolección y Recolecciones' })
    await expect(bar).toHaveAttribute('aria-valuenow', '60')
    await bar.focus()
    await bar.press('ArrowRight')
    await expect(bar).toHaveAttribute('aria-valuenow', '65')
    await page.reload()
    await expect(bar).toHaveAttribute('aria-valuenow', '65')
    await bar.focus()
    await bar.press('Enter')
    await expect(bar).toHaveAttribute('aria-valuenow', '60')
  })
})

test.describe('Lote 13 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  test('3. Recibo (tres pestañas) y Recolección y empaque sin scroll horizontal; los paneles van uno debajo del otro', async ({ page }) => {
    await login(page)
    expect(page.viewportSize()?.width).toBe(360)
    for (const [path, tab] of [
      ['/warehouse/receipts', 'Recibos'],
      ['/warehouse/receipts?tab=asns', 'Avisos de llegada'],
      ['/warehouse/receipts?tab=putaway', 'Acomodo pendiente'],
    ] as const) {
      await page.goto(path)
      await expect(page.getByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true')
      await page.waitForLoadState('networkidle')
      await expectNoHorizontalScroll(page)
    }
    await page.goto('/warehouse/pick-batches')
    await expect(page.getByRole('heading', { level: 1, name: 'Recolección y empaque' })).toBeVisible()
    // apilados: sin barra para arrastrar
    await expect(page.getByRole('separator', { name: /Cambiar el ancho/ })).toHaveCount(0)
    await page.waitForLoadState('networkidle')
    await expectNoHorizontalScroll(page)
  })
})
