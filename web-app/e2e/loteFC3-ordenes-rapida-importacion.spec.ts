// Recorrido de Órdenes — Entrada rápida e importación CSV (Lote F-C3) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo, admin con todos los permisos, módulo LTL_GROUND y CATALOG encendidos).
//
// 1) Entrada rápida: «Nueva orden» abre la rejilla Rápida; Tab recorre los campos; Enter y «Guardar fila» crean UNA orden de 1 paquete y 1 pieza
//    Sometida; validaciones sin llamar al servidor; un cliente que asigna su número de envío habilita la columna; el segmentado recuerda
//    la última elección (Detallada) aunque se recargue la página.
// 2) Importación CSV: plantilla nueva (editor de órdenes), cliente, archivo UTF-8 con filas válidas e inválidas, vista previa del servidor
//    (errores, avisos y consignatario existente / por crear), «Descartar archivo», importar (las órdenes nacen Sometidas, ya cotizadas y con el crédito medido), importar otra vez con otra plantilla y
//    verificar el resultado y la lista.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fc3-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'
import { contractData, withRates } from './rates'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type LocationDto = components['schemas']['LocationDto']
type OrderPageDto = components['schemas']['OrderPageDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string, height = 1000, only?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height })
  // las tablas con scroll propio (editor de plantilla, vista previa) se ven desde arriba
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
  await (only ?? page).screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  if (size) await page.setViewportSize(size)
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F-B / F-C2). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      if (el.parentElement?.closest('.dt-scroll, .cs-tablewrap')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      if (el.matches('.sw input, .cs-fileinput')) return
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

/** "Hoy" de la compañía demo (America/Puerto_Rico) y días después, 'YYYY-MM-DD'. */
function tenantDay(plus = 0): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Puerto_Rico', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  const d = new Date(`${today}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + plus)
  return d.toISOString().slice(0, 10)
}

/** Elige el cliente en el selector del modal de importación (las opciones llevan «código · nombre»). */
async function pickImportClient(dialog: Locator, name: string) {
  const value = await dialog.locator('#oi-client option', { hasText: name }).first().getAttribute('value')
  await dialog.getByLabel('Cliente', { exact: true }).selectOption(value ?? '')
}

test.describe('Órdenes — entrada rápida e importación (Lote F-C3)', () => {
  test('entrada rápida: Enter guarda una orden por fila, validaciones, cliente que asigna el número y el segmentado recuerda la elección', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Cliente FC3 ${suffix}`
    const clientNumName = `Cliente Num FC3 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const place = (clientPublicId: string | null | undefined, name: string, line1: string, city: string, zip: string) =>
      ok<LocationDto>(
        request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId, locationType: 'DELIVERY', name, line1, city, postalCode: zip, country: 'PR' } }),
        `consignatario ${name}`,
      )

    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, contract: contractData() } }), 'cliente')
    await withRates(request, headers, client)
    const clientNum = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientNumName, contract: contractData() } }), 'cliente que asigna')
    await withRates(request, headers, clientNum)
    await ok(
      request.patch(`${API_URL}/api/v1/clients/${clientNum.publicId}/number-settings`, { headers, data: { clientAssignsOrderNumber: true } }),
      'el cliente asigna su número de envío',
    )
    const cons1 = await place(client.publicId, `Farmacia Sol FC3 ${suffix}`, 'Calle Sol 123', 'Ponce', '00716')
    const cons2 = await place(client.publicId, `Colmado Luna FC3 ${suffix}`, 'Calle Luna 45', 'Bayamón', '00959')
    const consNum = await place(clientNum.publicId, `Tienda Numerada FC3 ${suffix}`, 'Calle Norte 7', 'Arecibo', '00612')

    await login(page)
    await page.goto('/orders')
    await expect(page.getByRole('heading', { level: 1, name: 'Envíos' })).toBeVisible()
    await page.getByRole('button', { name: /Nuevo envío/ }).click()

    // ---- La Rápida es la predeterminada ----
    const qk = page.locator('.qk')
    await expect(page.getByRole('button', { name: /Rápida/ })).toHaveAttribute('aria-pressed', 'true')
    await expect(qk.getByRole('button', { name: 'Guardar fila' })).toBeVisible()
    await expect(qk.getByRole('switch', { name: 'Label al guardar' })).toBeVisible() // F-G2
    await expect(qk.getByLabel('N.º de envío')).toBeDisabled()
    await expect(qk.getByLabel('N.º de envío')).toHaveAttribute('placeholder', '(automático)')
    await expect(qk.getByRole('combobox', { name: 'Consignatario' })).toBeDisabled()

    // ---- Validaciones: sin cliente ni consignatario no se llama al servidor ----
    await qk.getByRole('button', { name: 'Guardar fila' }).click()
    await expect(qk.getByText('El cliente es obligatorio.')).toBeVisible()
    const clientBox = qk.getByRole('combobox', { name: 'Cliente', exact: true })
    await clientBox.click()
    await clientBox.fill(clientName)
    await page.getByRole('option', { name: new RegExp(clientName) }).first().click()
    await qk.getByRole('button', { name: 'Guardar fila' }).click()
    await expect(qk.getByText('El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.')).toBeVisible()
    await expect(qk.getByRole('combobox', { name: 'Consignatario' })).toBeFocused()
    await page.keyboard.press('Escape') // la lista del consignatario se abre sola al enfocarlo: se cierra para la captura
    await shot(page, isMobile, 'fc3-rapida-validacion', 900, page.locator('.oe-wrap'))
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- Fila 1: Tab recorre los campos y Enter guarda ----
    const consBox = qk.getByRole('combobox', { name: 'Consignatario' })
    await consBox.click()
    await page.getByRole('option', { name: new RegExp(cons1.name ?? '') }).click()
    await expect(qk.getByText('Calle Sol 123 · Ponce')).toBeVisible()
    // la compañía demo puede no tener servicio ni paquete predeterminados: se eligen en la primera fila
    for (const label of ['Paquete', 'Servicio']) if ((await qk.getByLabel(label, { exact: true }).inputValue()) === '') await qk.getByLabel(label, { exact: true }).selectOption({ index: 1 })
    if (!isMobile) {
      await consBox.press('Tab')
      await expect(qk.getByRole('button', { name: 'Nuevo consignatario' })).toBeFocused()
      await page.keyboard.press('Tab')
      await expect(qk.getByLabel('Paquete')).toBeFocused()
      await page.keyboard.press('Tab')
      await expect(qk.getByLabel('COD ($)')).toBeFocused()
    }
    await qk.getByLabel('COD ($)').fill('25')
    await qk.getByLabel('Comentario').fill('Llamar antes de entregar')
    await qk.getByLabel('COD ($)').press('Enter')
    const saved1 = qk.locator('tr.saved').first()
    await expect(saved1.getByRole('link', { name: /^ORD-/ })).toBeVisible()
    await expect(saved1).toContainText(cons1.name ?? '')
    await expect(saved1).toContainText('$25.00')
    await expect(qk.getByTestId('qk-lines')).toHaveText('1')
    await expect(qk.getByTestId('qk-cod')).toHaveText('$25.00')
    // la fila nueva conserva el cliente y el servicio y está vacía
    await expect(clientBox).toHaveValue(new RegExp(clientName))
    await expect(qk.getByRole('combobox', { name: 'Consignatario' })).toHaveValue('')

    // ---- Fila 2 con el botón «Guardar fila» ----
    await qk.getByRole('combobox', { name: 'Consignatario' }).click()
    await page.getByRole('option', { name: new RegExp(cons2.name ?? '') }).click()
    await qk.getByLabel('COD ($)').fill('10')
    await qk.getByRole('button', { name: 'Guardar fila' }).click()
    await expect(qk.locator('tr.saved')).toHaveCount(2)
    await expect(qk.getByTestId('qk-cod')).toHaveText('$35.00')
    await page.keyboard.press('Escape')
    await shot(page, isMobile, 'fc3-rapida', 900, page.locator('.oe-wrap'))
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- Lo que quedó en el servidor: dos órdenes Sometidas de 1 paquete y 1 pieza ----
    const page1 = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?search=${encodeURIComponent(`FC3 ${suffix}`)}&take=50`, { headers }), 'órdenes')
    const mine = (page1.items ?? []).filter((o) => o.clientPublicId === client.publicId)
    expect(mine).toHaveLength(2)
    for (const o of mine) {
      expect(o.isInitialStatus).toBe(true)
      expect(o.totalPieces).toBe(1)
      expect(o.packages).toHaveLength(1)
    }
    expect(mine.map((o) => o.codAmount).sort()).toEqual([10, 25])

    // ---- Cliente que asigna su número de envío: la columna se habilita y el número tecleado queda ----
    await clientBox.click()
    await clientBox.fill(clientNumName)
    await page.getByRole('option', { name: new RegExp(clientNumName) }).first().click()
    const orderNo = qk.getByLabel('N.º de envío')
    await expect(orderNo).toBeEnabled()
    await qk.getByRole('combobox', { name: 'Consignatario' }).click()
    await page.getByRole('option', { name: new RegExp(consNum.name ?? '') }).click()
    await orderNo.fill(`PO-${suffix}`)
    await qk.getByLabel('Comentario').press('Enter')
    await expect(qk.locator('tr.saved').first().getByRole('link', { name: `PO-${suffix}` })).toBeVisible()
    await expect(qk.getByTestId('qk-lines')).toHaveText('3')

    // ---- El segmentado recuerda la última elección, aunque se recargue la página ----
    await page.getByRole('button', { name: /Detallada/ }).click()
    await expect(page.getByText('Ticket del envío')).toBeVisible()
    await page.reload()
    await page.getByRole('button', { name: /Nuevo envío/ }).click()
    await expect(page.getByText('Ticket del envío')).toBeVisible()
    await expect(page.getByRole('button', { name: /Detallada/ })).toHaveAttribute('aria-pressed', 'true')
    await page.getByRole('button', { name: /Rápida/ }).click()
    await expect(page.locator('.qk').getByRole('button', { name: 'Guardar fila' })).toBeVisible()
    await expectNoHorizontalScroll(page)

    // las órdenes de la rápida aparecen en la lista
    await page.getByRole('button', { name: 'Ocultar formulario' }).click()
    await page.getByRole('searchbox', { name: 'Buscar…' }).fill(suffix)
    // escritorio: tabla; móvil: tarjetas (sin rol de tabla)
    await expect(page.locator('.stage').getByText(cons1.name ?? '', { exact: true }).first()).toBeVisible()
  })

  test('importación: plantilla nueva, CSV con filas válidas e inválidas, vista previa, descartar, importar e importar de nuevo', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Cliente Imp FC3 ${suffix}`
    const template = `Plantilla FC3 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }

    // cliente con contrato activo, tarifas y crédito de sobra (al importar se cotiza y se mide el crédito)
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, creditLimit: 100000, contract: { startDate: tenantDay(-30), title: 'Contrato FC3' } } }),
      'cliente',
    )
    const contractId = client.currentContract?.publicId ?? ''
    for (const [packageType, rate] of [['BOX', 100], ['ENVELOPE', 60]] as const)
      await ok(
        request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType, rate, effectiveFrom: tenantDay(-30) } }),
        `tarifa ${packageType}`,
      )
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    const existing = await ok<LocationDto>(
      request.post(`${API_URL}/api/v1/locations`, {
        headers,
        data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name: `Farmacia Sol FC3 ${suffix}`, line1: 'Calle Sol 123', city: 'Ponce', postalCode: '00716', country: 'PR' },
      }),
      'consignatario',
    )
    const newName = `Tienda Ñandú FC3 ${suffix}`
    // UTF-8 con acentos y eñes; 4 filas: existente válida, consignatario nuevo válido, paquete inválido y 0 piezas
    const header = 'Consignatario,Dirección,Pueblo,Servicio,Paquete,Piezas,COD'
    const csv1 = [
      header,
      `${existing.name},Calle Sol 123,Ponce,STANDARD,BOX,2,40`,
      `${newName},Calle Ñ 9,Bayamón,STANDARD,ENVELOPE,1,`,
      `Colmado Roto FC3 ${suffix},Calle 3,Ponce,STANDARD,XYZ,1,`,
      `${existing.name},Calle Sol 123,Ponce,STANDARD,BOX,0,`,
    ].join('\n')
    const csv2 = [header, `${existing.name},Calle Sol 123,Ponce,STANDARD,BOX,1,15`, `${newName},Calle Ñ 9,Bayamón,STANDARD,ENVELOPE,3,`].join('\n')
    const file = (content: string) => ({ name: 'ordenes.csv', mimeType: 'text/csv', buffer: Buffer.from(content, 'utf-8') })

    await login(page)
    await page.goto('/orders')
    await page.getByRole('button', { name: 'Importar', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: 'Importar envíos desde CSV' })
    await expect(dialog.getByText(/Máximo .* filas y 2 MB por archivo/)).toBeVisible()
    await expect(dialog.getByText(/Guarda el CSV en UTF-8/)).toBeVisible()
    await expect(dialog.getByLabel('Archivo CSV', { exact: true })).toBeDisabled()

    // ---- Plantilla nueva: columna → campo destino ----
    await dialog.getByRole('button', { name: 'Nueva plantilla' }).click()
    const editor = page.getByRole('dialog', { name: 'Nueva plantilla de importación de envíos' })
    await editor.getByRole('button', { name: 'Guardar plantilla' }).click()
    await expect(editor.getByText('El nombre de la plantilla es obligatorio.')).toBeVisible()
    await editor.getByLabel(/Nombre de la plantilla/).fill(template)
    await editor.getByLabel('Columna 1', { exact: true }).selectOption({ label: 'Piezas' })
    await editor.getByRole('button', { name: 'Guardar plantilla' }).click()
    await expect(editor.getByText('La plantilla debe incluir consigneeName o consigneeCode.')).toBeVisible()
    const fields = ['Consignatario (nombre)', 'Dirección', 'Pueblo', 'Servicio', 'Tipo de paquete', 'Piezas', 'Monto COD']
    await editor.getByRole('button', { name: 'Agregar columna' }).click()
    await editor.getByRole('button', { name: 'Agregar columna' }).click()
    for (const [i, label] of fields.entries()) await editor.getByLabel(`Columna ${i + 1}`, { exact: true }).selectOption({ label })
    await shot(page, isMobile, 'fc3-plantilla', 1300)
    await editor.getByRole('button', { name: 'Guardar plantilla' }).click()
    await expect(editor).toBeHidden()
    await expect(dialog.getByText(/1\. Consignatario \(nombre\) · 2\. Dirección · 3\. Pueblo · 4\. Servicio · 5\. Tipo de paquete · 6\. Piezas · 7\. Monto COD/)).toBeVisible()

    // ---- Cliente → archivo → vista previa que valida el servidor ----
    await expect(dialog.getByLabel('Archivo CSV', { exact: true })).toBeDisabled()
    await shot(page, isMobile, 'fc3-importar', 1000)
    await pickImportClient(dialog, clientName)
    await expect(dialog.getByLabel('Archivo CSV', { exact: true })).toBeEnabled()
    await dialog.getByLabel('Archivo CSV', { exact: true }).setInputFiles(file(csv1))
    await expect(dialog.getByText(/ordenes\.csv · 4 filas detectadas/)).toBeVisible()
    await expect(dialog.getByText('Tipo de paquete: Tipo de paquete desconocido: XYZ.')).toBeVisible()
    await expect(dialog.getByText('Piezas: La cantidad de piezas debe ser al menos 1.')).toBeVisible()
    await expect(dialog.getByText(`Existente: ${existing.name}`).first()).toBeVisible()
    await expect(dialog.getByText(`Se creará: ${newName}`)).toBeVisible()
    await expect(dialog.locator('.cs-stat.ok').first()).toContainText('2 válidas')
    await expect(dialog.locator('.cs-stat.bad').first()).toContainText('2 con errores')
    await expect(dialog.getByRole('switch', { name: 'Confirmar los envíos al importar' })).toHaveCount(0) // Lote 106: ya no existe
    await shot(page, isMobile, 'fc3-importar-previa', 1500)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- Descartar: no se crea nada y se vuelve a pedir un archivo ----
    await dialog.getByRole('button', { name: 'Descartar archivo' }).click()
    await expect(dialog.getByText('Selecciona un archivo CSV para continuar')).toBeVisible()
    const none = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&take=50`, { headers }), 'órdenes')
    expect(none.items ?? []).toHaveLength(0)

    // ---- Importar: solo las 2 válidas ----
    await dialog.getByLabel('Archivo CSV', { exact: true }).setInputFiles(file(csv1))
    await expect(dialog.getByText(/ordenes\.csv · 4 filas detectadas/)).toBeVisible()
    await dialog.getByRole('button', { name: 'Importar', exact: true }).click()
    const result = dialog.locator('.cs-result')
    await expect(result).toContainText('2 creadas')
    await expect(result).toContainText('0 fallidas')
    await expect(result.getByRole('link', { name: /^ORD-/ })).toHaveCount(2)
    await shot(page, isMobile, 'fc3-importar-resultado', 1100)
    if (isMobile) await expectNoHorizontalScroll(page)
    await dialog.getByRole('button', { name: 'Cerrar' }).last().click()
    await expect(dialog).toBeHidden()

    const drafts = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&take=50`, { headers }), 'órdenes importadas')
    expect(drafts.items ?? []).toHaveLength(2)
    for (const o of drafts.items ?? []) expect(o.isInitialStatus).toBe(true)
    expect((drafts.items ?? []).map((o) => o.consigneeName).sort()).toEqual([existing.name, newName].sort())
    expect((drafts.items ?? []).find((o) => o.consigneeName === existing.name)?.codAmount).toBe(40)
    // el consignatario nuevo se creó con la dirección del archivo (UTF-8 intacto)
    const created = await ok<LocationDto[]>(request.get(`${API_URL}/api/v1/locations?search=${encodeURIComponent(newName)}&locationType=DELIVERY`, { headers }), 'consignatario nuevo')
    expect(created).toHaveLength(1)
    expect(created[0]).toMatchObject({ clientPublicId: client.publicId, line1: 'Calle Ñ 9', city: 'Bayamón' })

    // ---- Importar otra vez: cada orden nace Sometida y cotizada ----
    await page.getByRole('button', { name: 'Importar', exact: true }).click()
    const again = page.getByRole('dialog', { name: 'Importar envíos desde CSV' })
    await again.getByLabel('Plantilla', { exact: true }).selectOption({ label: template })
    await pickImportClient(again, clientName)
    await expect(again.getByLabel('Archivo CSV', { exact: true })).toBeEnabled()
    await again.getByLabel('Archivo CSV', { exact: true }).setInputFiles(file(csv2))
    await expect(again.getByText(/ordenes\.csv · 2 filas detectadas/)).toBeVisible()
    await expect(again.getByText(`Existente: ${newName}`)).toBeVisible()
    await expect(again.getByRole('switch', { name: 'Autorizar crédito excedido' })).toBeEnabled()
    await shot(page, isMobile, 'fc3-importar-opciones', 1500)
    await again.getByRole('button', { name: 'Importar', exact: true }).click()
    await expect(again.locator('.cs-result')).toContainText('2 creadas')
    await again.getByRole('button', { name: 'Cerrar' }).last().click()
    await expect(again).toBeHidden()

    const all = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&take=50`, { headers }), 'todas')
    expect(all.items ?? []).toHaveLength(4)
    expect((all.items ?? []).filter((o) => o.isInitialStatus)).toHaveLength(4) // todas nacen Sometidas
    expect((all.items ?? []).every((o) => (o.quotedAmount ?? 0) > 0)).toBe(true) // y ya vienen cotizadas

    // ---- La lista muestra las órdenes importadas ----
    await page.getByRole('searchbox', { name: 'Buscar…' }).fill(newName)
    await expect(page.locator('.stage').getByText(newName, { exact: true }).first()).toBeVisible()
    await shot(page, isMobile, 'fc3-lista', 900)
    await expectNoHorizontalScroll(page)
  })
})
