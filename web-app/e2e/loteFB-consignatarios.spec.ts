// Recorrido de Consignatarios (Lote F-B) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos, módulo CATALOG encendido).
//
// 1) Lista y ficha: alta por la pantalla (compartido y con teléfono), edición (PATCH de lo cambiado), «Número de factura repetido»,
//    baja y reactivación, filtro de cliente, «Compartidos» y «Mostrar inactivos», buscador.
// 2) Importación: plantilla nueva por el editor (columna → campo), archivo CSV con una fila válida y una con error → vista previa
//    del servidor con el mensaje de la fila → Importar → resultado (creados/fallidos/omitidos) → el nuevo aparece en la lista;
//    Cancelar descarta el lote; desactivar y reactivar la plantilla.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fb-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type LocationDto = components['schemas']['LocationDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(150_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  // ventana más alta solo para la captura: los formularios y modales largos salen completos
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height: 1000 })
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F18). */
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

/** Aviso de éxito (toast) con ese texto. */
const toast = (page: Page, text: string): Locator => page.locator('.toast.on', { hasText: text })

/** Panel de la ficha por su título (h2). */
const panel = (page: Page, name: string): Locator => page.locator('section.panel', { has: page.getByRole('heading', { name, exact: true }) })

test.describe('Consignatarios (Lote F-B)', () => {
  test('lista y ficha: alta, edición, factura repetida, baja y reactivación, filtros y buscador', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const name = `Consignatario FB ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FB ${suffix}` } }), 'cliente')

    await login(page)
    await page.goto('/catalog/consignees')
    await expect(page.getByRole('heading', { level: 1, name: 'Consignatarios' })).toBeVisible()
    await expect(page.getByRole('list', { name: 'Consignatarios' })).toBeVisible()
    await shot(page, isMobile, 'fb-lista')

    // ---- Alta: sin nombre el servidor no se llama ----
    await page.getByRole('button', { name: /Nuevo consignatario/ }).click()
    const dialog = page.getByRole('dialog', { name: 'Nuevo consignatario' })
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog.getByText('El nombre es obligatorio.')).toBeVisible()
    await expect(dialog.getByText('La dirección (línea 1) es obligatoria.')).toBeVisible()
    await expect(dialog.getByText('La ciudad es obligatoria.')).toBeVisible()
    await dialog.getByLabel(/^Nombre/).fill(name)
    await dialog.getByLabel('Número', { exact: true }).fill(`FB${suffix}`)
    await dialog.getByLabel(/^Dirección\s*\*?$/).fill('Calle Sol 123')
    await dialog.getByRole('combobox', { name: /Pueblo/ }).click()
    await dialog.getByRole('combobox', { name: /Pueblo/ }).fill('Ponce')
    await dialog.getByRole('listbox').getByRole('option', { name: /· PONCE, PR/ }).first().click()
    await dialog.getByLabel('Teléfono').fill('7875550142')
    await dialog.getByLabel('Cliente dueño').selectOption({ label: `Cliente FB ${suffix}` })
    await shot(page, isMobile, 'fb-alta')
    await dialog.getByText('Opciones de entrega').click()
    await dialog.getByLabel('Ventana: desde').fill('08:00')
    await dialog.getByLabel('Ventana: hasta').fill('12:00')
    await shot(page, isMobile, 'fb-alta-entrega')
    await dialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(dialog).toBeHidden()
    await expect(page).toHaveURL(/consignee=/)
    const ficha = panel(page, 'Ficha del consignatario')
    await expect(ficha.getByText(name)).toBeVisible()
    await expect(ficha.getByText('(787) 555-0142')).toBeVisible()
    await expect(ficha.getByText(`Cliente FB ${suffix}`)).toBeVisible()
    const listed = await ok<LocationDto[]>(request.get(`${API_URL}/api/v1/locations?search=${encodeURIComponent(name)}&locationType=DELIVERY`, { headers }), 'buscar')
    expect(listed).toHaveLength(1)
    expect(listed[0]).toMatchObject({ locationType: 'DELIVERY', country: 'PR', defaultWindowStart: '08:00:00', defaultWindowEnd: '12:00:00' })
    const publicId = listed[0].publicId ?? ''
    await shot(page, isMobile, 'fb-ficha')

    // ---- Edición: solo cambia el nombre ----
    await ficha.getByRole('button', { name: /Editar/ }).click()
    const edit = page.getByRole('dialog', { name: 'Editar consignatario' })
    await edit.getByLabel(/^Nombre/).fill(`${name} II`)
    await edit.getByRole('button', { name: 'Guardar' }).click()
    await expect(edit).toBeHidden()
    await expect(toast(page, 'Consignatario actualizado')).toBeVisible()
    const edited = await ok<LocationDto>(request.get(`${API_URL}/api/v1/locations/${publicId}`, { headers }), 'ficha')
    expect(edited.name).toBe(`${name} II`)
    expect(edited.code).toBe(`FB${suffix}`)

    // ---- Número de factura repetido: guarda al instante ----
    await ficha.getByRole('switch', { name: 'Número de factura repetido' }).click()
    await expect(ficha.getByText('Permitido (avisa antes de crear)')).toBeVisible()
    expect((await ok<LocationDto>(request.get(`${API_URL}/api/v1/locations/${publicId}`, { headers }), 'ficha')).allowDupInvoice).toBe(true)

    // ---- Baja y reactivación ----
    await ficha.getByRole('switch', { name: 'Estado' }).click()
    await page.getByRole('dialog', { name: 'Dar de baja al consignatario' }).getByRole('button', { name: 'Dar de baja' }).click()
    await expect(ficha.getByText('Inactivo')).toBeVisible()
    await expect(page.getByRole('list', { name: 'Consignatarios' }).getByText(`${name} II`)).toBeHidden()
    await page.getByRole('switch', { name: 'Mostrar inactivos' }).click()
    await expect(page.getByRole('list', { name: 'Consignatarios' }).getByText(`${name} II`)).toBeVisible()
    await ficha.getByRole('switch', { name: 'Estado' }).click()
    await page.getByRole('dialog', { name: 'Reactivar al consignatario' }).getByRole('button', { name: 'Reactivar' }).click()
    await expect(ficha.getByText('Activo', { exact: true })).toBeVisible()

    // ---- Filtro de cliente, compartidos y buscador ----
    const clientFilter = page.locator('label.cs-filter select')
    await clientFilter.selectOption({ label: `Cliente FB ${suffix}` })
    await expect(page.getByRole('list', { name: 'Consignatarios' }).getByText(`${name} II`)).toBeVisible()
    await clientFilter.selectOption({ label: 'Compartidos (sin dueño)' })
    await expect(page.getByRole('list', { name: 'Consignatarios' }).getByText(`${name} II`)).toBeHidden()
    await clientFilter.selectOption({ label: 'Todos los clientes' })
    await page.getByRole('searchbox').fill(suffix)
    await expect(page.getByRole('list', { name: 'Consignatarios' }).getByText(`${name} II`)).toBeVisible()
    await expectNoHorizontalScroll(page)
    expect(client.publicId).toBeTruthy()
  })

  test('importación: plantilla nueva, archivo CSV, vista previa del servidor, importar, cancelar y plantilla activa/inactiva', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const template = `Plantilla FB ${suffix}`
    const goodName = `Importado FB ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente Imp FB ${suffix}` } }), 'cliente')
    const csv = ['name,code,line1,city,contactPhone', `${goodName},IM${suffix},Calle Luna 5,Ponce,7875550199`, ',,Calle sin nombre 6,Ponce,'].join('\n')
    const file = { name: 'consignatarios.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'utf-8') }

    await login(page)
    await page.goto('/catalog/consignees')
    await page.getByRole('button', { name: /Importar/ }).click()
    const dialog = page.getByRole('dialog', { name: 'Importar consignatarios desde CSV' })
    await expect(dialog.getByText(/Máximo .* filas y 2 MB por archivo/)).toBeVisible()
    await expect(dialog.getByText(/Guarda el CSV en UTF-8/)).toBeVisible()
    await shot(page, isMobile, 'fb-importar')

    // ---- Plantilla nueva: columna → campo destino ----
    await dialog.getByRole('button', { name: 'Nueva plantilla' }).click()
    const editor = page.getByRole('dialog', { name: 'Nueva plantilla de importación' })
    await editor.getByRole('button', { name: 'Guardar plantilla' }).click()
    await expect(editor.getByText('El nombre de la plantilla es obligatorio.')).toBeVisible()
    await editor.getByLabel(/Nombre de la plantilla/).fill(template)
    await editor.getByLabel('Columna 1', { exact: true }).selectOption({ label: 'Nombre *' })
    await editor.getByLabel('Columna 2', { exact: true }).selectOption({ label: 'Número' })
    await editor.getByLabel('Columna 3', { exact: true }).selectOption({ label: 'Dirección *' })
    await editor.getByLabel('Columna 4', { exact: true }).selectOption({ label: 'Pueblo *' })
    await editor.getByLabel('Columna 5', { exact: true }).selectOption({ label: 'Teléfono' })
    await shot(page, isMobile, 'fb-plantilla')
    await editor.getByRole('button', { name: 'Guardar plantilla' }).click()
    await expect(editor).toBeHidden()
    await expect(dialog.getByText(/1\. Nombre · 2\. Número · 3\. Dirección · 4\. Pueblo · 5\. Teléfono/)).toBeVisible()

    // ---- Archivo → vista previa que valida el servidor ----
    await dialog.getByLabel('Cliente dueño de los consignatarios').selectOption({ label: `Cliente Imp FB ${suffix}` })
    // al cambiar de dueño se vuelven a leer las plantillas: el selector de archivo se habilita al terminar
    await expect(dialog.getByText(/Mapeo:/)).toBeVisible()
    await expect(dialog.getByLabel('Archivo CSV', { exact: true })).toBeEnabled()
    await dialog.getByLabel('Archivo CSV', { exact: true }).setInputFiles(file)
    await expect(dialog.getByText(/consignatarios\.csv · 2 filas detectadas/)).toBeVisible()
    await expect(dialog.getByText('Nombre: El nombre es obligatorio.')).toBeVisible()
    await expect(dialog.getByText('1 válidas')).toBeVisible()
    await shot(page, isMobile, 'fb-importar-previa')

    // ---- Cancelar descarta el lote; se vuelve a validar y se importa ----
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(dialog).toBeHidden()
    await page.getByRole('button', { name: /Importar/ }).click()
    const again = page.getByRole('dialog', { name: 'Importar consignatarios desde CSV' })
    await again.getByLabel('Plantilla', { exact: true }).selectOption({ label: template })
    await again.getByLabel('Cliente dueño de los consignatarios').selectOption({ label: `Cliente Imp FB ${suffix}` })
    await expect(again.getByText(/Mapeo:/)).toBeVisible()
    await expect(again.getByLabel('Archivo CSV', { exact: true })).toBeEnabled()
    await again.getByLabel('Archivo CSV', { exact: true }).setInputFiles(file)
    await expect(again.getByText('Nombre: El nombre es obligatorio.')).toBeVisible()
    await again.getByRole('button', { name: 'Importar', exact: true }).click()
    const result = again.locator('.cs-result')
    await expect(result).toContainText('1 creados')
    await expect(result).toContainText('0 fallidos')
    await shot(page, isMobile, 'fb-importar-resultado')
    await expectNoHorizontalScroll(page)
    await again.getByRole('button', { name: 'Cerrar' }).last().click()
    await expect(again).toBeHidden()

    const created = await ok<LocationDto[]>(request.get(`${API_URL}/api/v1/locations?search=${encodeURIComponent(goodName)}&locationType=DELIVERY`, { headers }), 'importado')
    expect(created).toHaveLength(1)
    expect(created[0]).toMatchObject({ clientPublicId: client.publicId, code: `IM${suffix}`, city: 'Ponce' })
    await page.getByRole('searchbox').fill(goodName)
    await expect(page.getByRole('list', { name: 'Consignatarios' }).getByText(goodName)).toBeVisible()

    // ---- Plantilla: desactivar y reactivar (no hay «eliminar») ----
    await page.getByRole('button', { name: /Importar/ }).click()
    const last = page.getByRole('dialog', { name: 'Importar consignatarios desde CSV' })
    await last.getByLabel('Plantilla', { exact: true }).selectOption({ label: template })
    await last.getByRole('button', { name: 'Desactivar esta plantilla' }).click()
    await page.getByRole('dialog', { name: 'Desactivar la plantilla' }).getByRole('button', { name: 'Desactivar esta plantilla' }).click()
    await expect(toast(page, 'Plantilla desactivada')).toBeVisible()
    await last.getByRole('switch', { name: 'Mostrar plantillas inactivas' }).click()
    await last.getByLabel('Plantilla', { exact: true }).selectOption({ label: `${template} (inactiva)` })
    await last.getByRole('button', { name: 'Reactivar esta plantilla' }).click()
    await page.getByRole('dialog', { name: 'Reactivar la plantilla' }).getByRole('button', { name: 'Reactivar esta plantilla' }).click()
    await expect(toast(page, 'Plantilla reactivada')).toBeVisible()
  })
})
