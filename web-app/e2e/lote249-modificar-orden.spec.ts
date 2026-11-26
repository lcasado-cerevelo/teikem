// Lote 249 (Lote E) — Modificar la orden de venta con autorización, contra el API real de pruebas (SIN correo: Brevo__ApiKey vacía, Auth__Onboarding__Enabled=false).
// Prepara por API: dos productos con existencia, la tasa del IVU, un cliente A con una orden solicitada y una parte ya recolectada, un cliente B con segunda aprobación
// SIEMPRE y una modificación pendiente, y un cliente C con una orden que ya salió completa (solo admite adición) y una adición recolectada. Recorre: la ficha de la orden, el
// asistente «Modificar orden» (líneas con su mínimo → términos → autorización → vista previa → guardar), el historial y su detalle, la modificación pendiente y la bandeja
// «Por aprobar», la orden que solo admite adición, la orden interna de adición, «Unir al envío de OV-…» en Recolección y empaque y los ajustes de la compañía y del cliente,
// midiendo: sin scroll horizontal, filas ≤ 56 px en escritorio y paneles que no se desbordan. 1440 px es/claro y en/oscuro; 390 px es/oscuro y en/claro (deviceScaleFactor 2).
// Capturas en SHOTS_DIR (por omisión docs/manual/frontend/img/l249-*.png). Los módulos SALES_ORDERS y TAX se encienden por SQL en TenantModule (encenderlos por API exige ser
// administrador de la plataforma).
// Correr: WEB_URL=http://localhost:5175 API_URL=http://localhost:5101 npx playwright test lote249 --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test, type Page } from '@playwright/test'

import { API_URL, LETTERS, apiHeaders, login, ok, tenantDay } from './staffHelpers'

test.use({ locale: 'es-PR', deviceScaleFactor: 2 })
test.setTimeout(300_000)
test.describe.configure({ mode: 'serial' })

const SHOTS = process.env.SHOTS_DIR ?? fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

interface Ids {
  orderA: string
  numberA: string
  lineP1: number
  lineP2: number
  orderB: string
  numberB: string
  orderC: string
  numberC: string
  additionBatch: string
  additionNumber: string
  clientA: string
  skuP2: string
}
let S: Ids

type Json = Record<string, unknown>

test.beforeAll(async ({ request }) => {
  const headers = await apiHeaders(request)
  const post = <T>(path: string, data: Json, what: string) => ok<T>(request.post(`${API_URL}${path}`, { headers, data }), what)
  const get = <T>(path: string, what: string) => ok<T>(request.get(`${API_URL}${path}`, { headers }), what)
  // la tasa del IVU (el módulo de impuestos exige una por omisión para crear órdenes)
  const rates = await get<{ code: string }[]>('/api/v1/tax/rates', 'tasas')
  if (!rates.some((r) => r.code === 'IVU249')) {
    await post('/api/v1/tax/rates', { code: 'IVU249', name: 'IVU 11.5 %', effectiveFrom: tenantDay(-400), components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] }, 'tasa')
  }
  await ok(request.patch(`${API_URL}/api/v1/tax/settings`, { headers, data: { defaultRateCode: 'IVU249' } }), 'tasa por omisión')
  const warehouse = (await get<{ publicId: string }[]>('/api/v1/warehouses', 'almacenes'))[0].publicId
  const product = async (sku: string, name: string, price: number) => {
    const p = await post<{ product: { publicId: string } }>('/api/v1/products', { sku, name, trackingType: 'NONE', salePrice: price, purchaseCost: 1 }, 'producto')
    await post('/api/v1/inventory/adjustments', { productPublicId: p.product.publicId, warehousePublicId: warehouse, binId: 2, quantity: 80, reason: 'FOUND', notes: 'e2e lote 249' }, 'existencia')
    return p.product.publicId
  }
  const skuP1 = `SOC1-${LETTERS}`
  const skuP2 = `SOC2-${LETTERS}`
  const p1 = await product(skuP1, `Guantes de nitrilo ${LETTERS}`, 12.5)
  const p2 = await product(skuP2, `Cinta de embalaje ${LETTERS}`, 4)
  const client = async (name: string) => (await post<{ publicId: string }>('/api/v1/clients', { name }, name)).publicId
  const clientA = await client(`Cliente SOC A ${LETTERS}`)
  const clientB = await client(`Cliente SOC B ${LETTERS}`)
  const clientC = await client(`Cliente SOC C ${LETTERS}`)
  const auth = { authorizedByName: 'Marta Ríos', authorizedByTitle: 'Compras', authorizationChannel: 'PHONE', reason: 'El cliente llamó para cambiar la cantidad.' }
  const order = async (client: string, lines: { productPublicId: string; quantity: number }[], deliveryMode: string) => {
    const o = await post<{ publicId: string; number: string }>('/api/v1/sales-orders', { warehousePublicId: warehouse, clientPublicId: client, lines, deliveryMode }, 'orden')
    await post(`/api/v1/sales-orders/${o.publicId}/request`, { allowBackorder: true }, 'solicitar')
    return o
  }
  // A: solicitada, 3 de los guantes ya recolectados (entrega: la recolección se puede deshacer)
  const a = await order(clientA, [{ productPublicId: p1, quantity: 8 }, { productPublicId: p2, quantity: 5 }], 'DELIVERY')
  const aFull = await get<{ lines: { id: number; sku: string }[] }>(`/api/v1/sales-orders/${a.publicId}`, 'orden A')
  const lineP1 = aFull.lines.find((l) => l.sku === skuP1)!.id
  const lineP2 = aFull.lines.find((l) => l.sku === skuP2)!.id
  await post(`/api/v1/sales-orders/${a.publicId}/dispatch`, { lines: [{ lineId: lineP1, quantity: 3 }] }, 'recolectar A')
  // B: el cliente exige segunda aprobación SIEMPRE; una modificación queda pendiente
  await ok(request.patch(`${API_URL}/api/v1/sales-order-changes/settings/clients/${clientB}`, { headers, data: { approvalMode: 'ALWAYS' } }), 'ajuste del cliente B')
  const b = await order(clientB, [{ productPublicId: p1, quantity: 4 }], 'DELIVERY')
  const bFull = await get<{ lines: { id: number }[] }>(`/api/v1/sales-orders/${b.publicId}`, 'orden B')
  await post(`/api/v1/sales-orders/${b.publicId}/changes`, { ops: [{ kind: 'QTY', lineId: bFull.lines[0].id, quantity: 6 }], ...auth }, 'modificación pendiente')
  // C: salió completa (retiro): solo admite una orden interna de adición; y una adición ya recolectada
  const c = await order(clientC, [{ productPublicId: p2, quantity: 4 }], 'PICKUP')
  const cFull = await get<{ lines: { id: number }[] }>(`/api/v1/sales-orders/${c.publicId}`, 'orden C')
  await post(`/api/v1/sales-orders/${c.publicId}/dispatch`, { lines: [{ lineId: cFull.lines[0].id, quantity: 4 }] }, 'despachar C')
  const addition = await post<{ item: { additionPublicId: string; additionNumber: string } }>(
    `/api/v1/sales-orders/${c.publicId}/additions`,
    { lines: [{ productPublicId: p1, quantity: 2 }], ...auth, reason: 'El cliente pidió dos cajas más.' },
    'adición',
  )
  const addFull = await get<{ lines: { id: number }[] }>(`/api/v1/sales-orders/${addition.item.additionPublicId}`, 'adición')
  const batch = await post<{ pickBatchPublicId: string }>(`/api/v1/sales-orders/${addition.item.additionPublicId}/dispatch`, { lines: [{ lineId: addFull.lines[0].id, quantity: 2 }] }, 'recolectar la adición')
  S = {
    orderA: a.publicId, numberA: a.number, lineP1, lineP2, orderB: b.publicId, numberB: b.number, orderC: c.publicId, numberC: c.number,
    additionBatch: batch.pickBatchPublicId, additionNumber: addition.item.additionNumber, clientA, skuP2,
  }
})

async function noHScroll(page: Page, what: string) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(over, `scroll horizontal de la página: ${what}`).toBeLessThanOrEqual(1)
}

async function setLook(page: Page, lang: 'es' | 'en', theme: 'light' | 'dark') {
  await page.evaluate(([l, th]) => {
    localStorage.setItem('teikem.lang', l)
    localStorage.setItem('teikem.theme', th)
    // los paneles recuerdan abierto/cerrado: el recorrido parte de lo de fábrica
    Object.keys(localStorage).filter((k) => k.startsWith('teikem.panel.')).forEach((k) => localStorage.removeItem(k))
  }, [lang, theme] as const)
  await page.reload()
}

/** Todas las filas de la tabla miden ≤ 56 px (solo escritorio, donde la tabla es tabla). */
async function rowsAtMost(page: Page, name: string, max = 56) {
  if ((await page.getByRole('table', { name }).count()) === 0) return
  const rows = page.getByRole('table', { name }).locator('tbody tr')
  const n = await rows.count()
  expect(n, `filas de «${name}»`).toBeGreaterThan(0)
  for (let i = 0; i < n; i++) expect((await rows.nth(i).boundingBox())!.height, `alto de la fila ${i} de «${name}»`).toBeLessThanOrEqual(max)
}

async function noOverflow(page: Page, selector: string, what: string) {
  const over = await page.locator(selector).first().evaluate((el) => el.scrollWidth - el.clientWidth)
  expect(over, `${what} no se desborda`).toBeLessThanOrEqual(1)
}

const LOOKS = [
  { lang: 'es', theme: 'light', width: 1440, height: 1500, tag: 'escritorio' },
  { lang: 'en', theme: 'dark', width: 1440, height: 1500, tag: 'escritorio' },
  { lang: 'es', theme: 'dark', width: 390, height: 2400, tag: 'movil' },
  { lang: 'en', theme: 'light', width: 390, height: 2400, tag: 'movil' },
] as const

for (const look of LOOKS) {
  test(`Modificar la orden ${look.tag} ${look.lang}/${look.theme}`, async ({ page, isMobile }) => {
    test.skip(isMobile, 'el recorrido fija sus propios anchos (1440 y 390)')
    mkdirSync(SHOTS, { recursive: true })
    const { lang, theme, tag } = look
    const L = lang === 'es'
    const desktop = look.width >= 720
    const shot = (name: string) => page.screenshot({ path: join(SHOTS, `l249-${name}-${tag}-${lang}-${theme}.png`) })
    const dialog = () => page.getByRole('dialog').last()
    await login(page)
    await page.setViewportSize({ width: look.width, height: look.height })

    // ---- la ficha de la orden A: el botón «Modificar orden» y su recolección
    await page.goto(`/sales-orders?order=${S.orderA}`)
    await setLook(page, lang, theme)
    await expect(dialog()).toContainText(S.numberA, { timeout: 30_000 })
    await expect(dialog().getByRole('button', { name: L ? 'Modificar orden' : 'Modify order' })).toBeVisible()
    await noHScroll(page, `ficha ${tag} ${lang} ${theme}`)
    await shot('ficha')

    // ---- el asistente: líneas con su mínimo
    await dialog().getByRole('button', { name: L ? 'Modificar orden' : 'Modify order' }).click()
    const wizard = page.getByRole('dialog', { name: L ? new RegExp(`Modificar la orden ${S.numberA}`) : new RegExp(`Modify order ${S.numberA}`) })
    await expect(wizard).toBeVisible({ timeout: 30_000 })
    await expect(wizard.locator('.soc-line')).toHaveCount(2, { timeout: 30_000 })
    await expect(wizard.getByText(L ? 'Mínimo: 0' : 'Minimum: 0').first()).toBeVisible()
    await expect(wizard.getByText(L ? 'Recolectada: se puede deshacer' : 'Collected: can be undone')).toBeVisible()
    await noOverflow(page, '.soc-form', 'las líneas')
    await noHScroll(page, `líneas ${tag} ${lang} ${theme}`)
    await shot('lineas')
    // una cantidad en cero no vale: el mensaje se ve y «Siguiente» se frena
    await wizard.locator(`#soc-qty-${S.lineP1}`).fill('0')
    await expect(wizard.getByText(L ? 'Escriba una cantidad mayor que 0' : 'Type a quantity greater than 0')).toBeVisible()
    await wizard.locator(`#soc-qty-${S.lineP1}`).fill('8')
    // sube la cinta una unidad más (cada recorrido suma una, así el historial crece)
    const current = Number(await wizard.locator(`#soc-qty-${S.lineP2}`).inputValue())
    await wizard.locator(`#soc-qty-${S.lineP2}`).fill(String(current + 1))
    // IVU manual con motivo en la primera línea (sales.tax)
    await wizard.locator('.soc-line').first().getByRole('button', { name: /^(IVU manual|Manual tax|Cambiar IVU manual|Change manual tax)$/ }).click()
    await wizard.locator(`#soc-tax-${S.lineP1}`).fill('0')
    await wizard.locator(`#soc-taxnote-${S.lineP1}`).fill(L ? 'Cliente exento por acuerdo' : 'Client exempt by agreement')
    await shot('lineas-iv')
    await wizard.getByRole('button', { name: L ? 'Siguiente' : 'Next' }).click()
    await expect(wizard.getByLabel(L ? 'Notas' : 'Notes')).toBeVisible()
    await noHScroll(page, `términos ${tag} ${lang} ${theme}`)
    await shot('terminos')
    await wizard.getByRole('button', { name: L ? 'Siguiente' : 'Next' }).click()

    // ---- autorización: sin nombre no avanza
    await wizard.getByRole('button', { name: L ? 'Siguiente' : 'Next' }).click()
    await expect(wizard.getByText(L ? 'Indique quién autorizó el cambio en el cliente.' : 'Enter who authorized the change at the client.')).toBeVisible()
    await shot('autorizacion-error')
    await wizard.locator('#soc-auth-name').fill('Marta Ríos')
    await wizard.locator('#soc-auth-title').fill(L ? 'Compras' : 'Purchasing')
    await wizard.locator('#soc-auth-channel').selectOption('PHONE')
    await wizard.locator('#soc-auth-reason').fill(L ? 'El cliente llamó para ajustar el pedido.' : 'The client called to adjust the order.')
    await noHScroll(page, `autorización ${tag} ${lang} ${theme}`)
    await shot('autorizacion')
    await wizard.getByRole('button', { name: L ? 'Siguiente' : 'Next' }).click()

    // ---- vista previa: la diferencia, sin escribir nada
    await expect(wizard.locator('.soc-diff-item').first()).toBeVisible({ timeout: 30_000 })
    await expect(wizard.getByText(L ? 'Total antes' : 'Total before')).toBeVisible()
    await noOverflow(page, '.soc-form', 'la vista previa')
    await noHScroll(page, `vista previa ${tag} ${lang} ${theme}`)
    await shot('vista-previa')
    await wizard.getByRole('button', { name: L ? 'Guardar la modificación' : 'Save the modification' }).click()
    await expect(page.locator('.toast.on').filter({ hasText: L ? 'aplicada' : 'applied' })).toBeVisible({ timeout: 30_000 })
    await expect(wizard).toHaveCount(0)

    // ---- historial: la modificación quedó registrada con quién la autorizó
    await expect(dialog().getByText('Marta Ríos').first()).toBeVisible({ timeout: 30_000 })
    await dialog().getByText(L ? 'Modificaciones' : 'Modifications', { exact: true }).first().scrollIntoViewIfNeeded()
    if (desktop) await rowsAtMost(page, L ? 'Modificaciones' : 'Modifications')
    await noHScroll(page, `historial ${tag} ${lang} ${theme}`)
    await shot('historial')
    await dialog().getByText('Marta Ríos').first().click()
    const detail = page.getByRole('dialog', { name: L ? /Modificación n\.º \d+ de/ : /Modification #\d+ of/ })
    await expect(detail).toBeVisible({ timeout: 30_000 })
    await expect(detail.locator('.soc-diff-item').first()).toBeVisible()
    await noOverflow(page, '.soc-facts', 'el detalle')
    await shot('detalle')
    await detail.getByRole('button', { name: L ? 'Cerrar' : 'Close' }).last().click()

    // ---- B: la modificación pendiente de segunda aprobación (aviso en la ficha y bandeja «Por aprobar»)
    await page.goto(`/sales-orders?order=${S.orderB}`)
    await expect(page.locator('.soc-pending')).toBeVisible({ timeout: 30_000 })
    await expect(page.locator('.soc-pending')).toContainText(L ? 'pendiente de segunda aprobación' : 'pending second approval')
    await expect(page.locator('.soc-pending')).toContainText(L ? 'otra persona' : 'someone other than')
    await noOverflow(page, '.soc-pending', 'el aviso pendiente')
    await noHScroll(page, `pendiente ${tag} ${lang} ${theme}`)
    await shot('pendiente')
    await page.goto('/sales-orders?tab=approvals')
    await expect(page.getByText(S.numberB).first()).toBeVisible({ timeout: 30_000 })
    if (desktop) await rowsAtMost(page, L ? 'Modificaciones por aprobar' : 'Modifications to approve')
    await noHScroll(page, `bandeja ${tag} ${lang} ${theme}`)
    await shot('bandeja')

    // ---- C: salió completa → solo admite la orden interna de adición
    await page.goto(`/sales-orders?order=${S.orderC}`)
    await expect(dialog()).toContainText(S.numberC, { timeout: 30_000 })
    await expect(dialog()).toContainText(S.additionNumber)
    await shot('ficha-con-adicion')
    await dialog().getByRole('button', { name: L ? 'Modificar orden' : 'Modify order' }).click()
    await expect(page.getByText(L ? 'solo se le puede añadir' : 'it can only be added to').first()).toBeVisible({ timeout: 30_000 })
    await noHScroll(page, `solo adición ${tag} ${lang} ${theme}`)
    await shot('solo-adicion')
    await dialog().getByRole('button', { name: L ? 'Orden interna de adición' : 'Internal addition order' }).click()
    const addition = page.getByRole('dialog', { name: L ? /Orden interna de adición a/ : /Internal addition order to/ })
    await expect(addition).toBeVisible({ timeout: 30_000 })
    await noOverflow(page, '.soc-form', 'la adición')
    await noHScroll(page, `adición ${tag} ${lang} ${theme}`)
    await shot('adicion')
    await addition.getByRole('button', { name: L ? 'Cancelar' : 'Cancel' }).click()

    // ---- almacén: «Unir al envío de OV-…» desde la recolección de la adición
    await page.goto('/warehouse/pick-batches')
    await setLook(page, lang, theme)
    await page.getByText(S.additionNumber, { exact: true }).first().click({ timeout: 30_000 })
    // la lista es de documentos: la recolección se abre desde su fila
    await dialog().getByText(/^EMP-\d+$/).first().click({ timeout: 30_000 })
    const joinButton = page.getByRole('button', { name: L ? /Unir al envío de/ : /Join the shipment of/ })
    await expect(joinButton).toBeVisible({ timeout: 30_000 })
    await joinButton.click()
    const joinDlg = page.getByRole('dialog', { name: L ? /Unir al envío de/ : /Join the shipment of/ })
    await expect(joinDlg.locator('#js-target')).toBeVisible({ timeout: 30_000 })
    expect(await joinDlg.evaluate((el) => el.scrollWidth - el.clientWidth), 'el diálogo de unir no se desborda').toBeLessThanOrEqual(1)
    const joinBox = (await joinButton.boundingBox())!
    expect(joinBox.x + joinBox.width, 'el botón «Unir al envío» cabe en la pantalla').toBeLessThanOrEqual(look.width)
    await noHScroll(page, `unir ${tag} ${lang} ${theme}`)
    await shot('unir-envio')
    await joinDlg.getByRole('button', { name: L ? 'Cancelar' : 'Cancel' }).click()

    // ---- ajustes de la compañía y del cliente (segunda aprobación y evidencia)
    await page.goto('/system/settings?tab=ops')
    await setLook(page, lang, theme)
    const heading = page.getByRole('heading', { name: L ? 'Modificaciones de órdenes' : 'Order modifications', exact: true })
    await heading.scrollIntoViewIfNeeded({ timeout: 30_000 })
    await page.locator('#set-chg-mode').selectOption('ABOVE_AMOUNT')
    await expect(page.locator('#set-chg-amount')).toBeVisible()
    await noHScroll(page, `ajustes ${tag} ${lang} ${theme}`)
    await heading.locator('xpath=ancestor::section[1]').screenshot({ path: join(SHOTS, `l249-ajustes-${tag}-${lang}-${theme}.png`) })
    await page.goto(`/catalog/clients?client=${S.clientA}`)
    await setLook(page, lang, theme)
    await page.getByRole('heading', { name: L ? 'Perfil' : 'Profile', exact: false }).first().click({ timeout: 30_000 })
    const clientHeading = page.getByRole('heading', { name: L ? 'Modificaciones de órdenes' : 'Order modifications', exact: true })
    await clientHeading.scrollIntoViewIfNeeded({ timeout: 30_000 })
    await noHScroll(page, `cliente ${tag} ${lang} ${theme}`)
    await clientHeading.locator('xpath=ancestor::section[1]').screenshot({ path: join(SHOTS, `l249-cliente-${tag}-${lang}-${theme}.png`) })
  })
}
