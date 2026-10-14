// Recorrido del Lote F-B3 contra el API real (db-init hecho y, para los tipos de ZIP, Diseño/logistica-db-zip-catalog.sql corrido; API en
// API_URL, por defecto http://localhost:5000; compañía demo, admin con todos los permisos, módulo CATALOG encendido).
//
// Un CSV que MEZCLA dos disposiciones de columnas (datos sintéticos con la forma del archivo real):
//   formato A: número, nombre, dirección 1, dirección 2, ZIP, contacto, teléfono (columnas 8 y 9 vacías);
//   formato B: número, nombre, contacto, teléfono, nombre repetido, dirección 1, barrio, pueblo, «PUERTO RICO» (sin ZIP).
// Se parte de una plantilla del formato A, se abre el editor con el archivo revisado, se agrega la variante «Formato B» (condición:
// columna 9 es igual a PUERTO RICO) con su propio mapeo y a la A se le pone «columna 9 está vacía». Vista previa: variante de cada
// fila, pueblo y estado tomados del ZIP (00952 → Toa Baja), ZIP asignado por el pueblo (Bayamón → 00956), «Código postal ajustado»
// (00734, apartados de Ponce → 00715), rechazo del pueblo desconocido y de la fila sin variante. Importar → resultado con los
// ajustes → consignatarios guardados con lo deducido.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos. Capturas:
// docs/manual/frontend/img/fb3-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type LocationDto = components['schemas']['LocationDto']
type ImportTemplateDto = components['schemas']['ImportTemplateDto']
type PostalLocalityDto = components['schemas']['PostalLocalityDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

async function shot(page: Page, isMobile: boolean, name: string, target?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height: 1100 })
  if (target) await target.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** CSV con las dos disposiciones mezcladas (datos sintéticos). */
function mixedCsv(s: string): string {
  return [
    'Numero,Nombre,C3,C4,C5,C6,C7,C8,C9',
    `A${s}1,Colmado Uno ${s},Calle Sol 1,Urb. Las Flores,00952,Ana Prueba,787-555-0101,,`,
    `B${s}1,Farmacia Dos ${s},Beto Prueba,787-555-0202,Farmacia Dos ${s},Carr. 2 km 3,Bo. Pueblo,Bayamón,PUERTO RICO`,
    `A${s}2,Ferretería Tres ${s},Calle Luna 3,,00734,,,,`,
    `B${s}2,Sin Pueblo ${s},Caro Prueba,787-555-0203,Sin Pueblo ${s},Calle 4,Bo. Centro,Ciudad Gótica,PUERTO RICO`,
    `X${s}1,Fila Rara ${s},a,b,c,d,e,f,USA`,
  ].join('\r\n')
}

test.describe('Variantes de mapeo y catálogo de códigos postales (Lote F-B3)', () => {
  test('archivo con dos formatos: variantes con condición, pueblo/estado/ZIP del catálogo, ZIP de apartados ajustado y filas rechazadas', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const s = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }

    // el catálogo trae el tipo de ZIP (logistica-db-zip-catalog.sql): el filtro DELIVERY no ofrece apartados
    const ponce = await ok<PostalLocalityDto[]>(request.get(`${API_URL}/api/v1/postal-localities?search=00734`, { headers }), 'ZIP 00734')
    expect(ponce[0]?.zipClass).toBe('PO_BOX')
    expect(await ok<PostalLocalityDto[]>(request.get(`${API_URL}/api/v1/postal-localities?search=00734&zipClass=DELIVERY`, { headers }), 'ZIP de entrega')).toEqual([])
    expect((await request.get(`${API_URL}/api/v1/postal-localities?zipClass=BOX`, { headers })).status()).toBe(400)

    const templateName = `Mixta FB3 ${s}`
    const base = await ok<ImportTemplateDto>(
      request.post(`${API_URL}/api/v1/locations/import-templates`, {
        headers,
        data: {
          name: templateName,
          columns: [
            { position: 1, field: 'code' },
            { position: 2, field: 'name' },
            { position: 3, field: 'line1' },
            { position: 4, field: 'line2' },
            { position: 5, field: 'postalCode' },
            { position: 6, field: 'contactName' },
            { position: 7, field: 'contactPhone' },
          ],
        },
      }),
      'plantilla',
    )

    await login(page)
    await page.goto('/catalog/consignees')
    await page.getByRole('button', { name: /Importar/ }).click()
    const dialog = page.getByRole('dialog', { name: 'Importar consignatarios (CSV o Excel)' })
    await dialog.getByLabel('Plantilla', { exact: true }).selectOption({ label: templateName })
    await dialog.getByLabel('Archivo (CSV o Excel)', { exact: true }).setInputFiles({ name: 'mezclado.csv', mimeType: 'text/csv', buffer: Buffer.from(mixedCsv(s), 'utf8') })
    await expect(dialog.getByText(/mezclado\.csv · 5 filas detectadas/)).toBeVisible()

    // ---- Editor: variante A (columna 9 vacía) y variante B (columna 9 = PUERTO RICO) con su propio mapeo ----
    await dialog.getByRole('button', { name: 'Editar' }).click()
    const editor = page.getByRole('dialog', { name: 'Editar plantilla de importación' })
    await expect(editor.locator('.cs-head', { hasText: 'C9' })).toBeVisible()
    await editor.getByRole('button', { name: /Agregar variante/ }).click()
    const tabs = editor.getByRole('tablist', { name: 'Variantes de mapeo' })
    await expect(tabs.getByRole('tab', { name: 'Variante 2' })).toHaveAttribute('aria-selected', 'true')
    await editor.getByLabel('Nombre de la variante 2').fill('Formato B')
    await editor.getByLabel('Columna de la condición 1').selectOption({ label: 'Columna 9 «C9»' })
    await editor.getByLabel('Valor de la condición 1').fill('PUERTO RICO')
    await editor.getByLabel('Columna 3', { exact: true }).selectOption({ label: 'Persona de contacto' })
    await editor.getByLabel('Columna 4', { exact: true }).selectOption({ label: 'Teléfono' })
    await editor.getByLabel('Columna 5', { exact: true }).selectOption({ label: '(No usar)' })
    await editor.getByLabel('Columna 6', { exact: true }).selectOption({ label: 'Dirección *' })
    await editor.getByLabel('Columna 7', { exact: true }).selectOption({ label: 'Dirección (línea 2)' })
    await editor.getByLabel('Columna 8', { exact: true }).selectOption({ label: 'Pueblo' })
    await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fb3-variante-b')

    await tabs.getByRole('tab', { name: 'Variante 1' }).click()
    await editor.getByLabel('Nombre de la variante 1').fill('Formato A')
    await editor.getByRole('button', { name: 'Agregar condición' }).click()
    await editor.getByLabel('Columna de la condición 1').selectOption({ label: 'Columna 9 «C9»' })
    await editor.getByLabel('Condición 1', { exact: true }).selectOption({ label: 'está vacía' })
    await expect(editor.getByLabel('Valor de la condición 1')).toHaveCount(0)
    await expect(editor.getByLabel('Columna 5', { exact: true })).toHaveValue('postalCode')
    await shot(page, isMobile, 'fb3-variante-a', editor.locator('.cs-variants'))
    await editor.getByRole('button', { name: 'Guardar plantilla' }).click()
    await expect(editor).toBeHidden()

    // ---- Vista previa ----
    await expect(dialog.getByText('Mapeo en 2 variantes (se usa la primera que se cumpla):')).toBeVisible()
    await expect(dialog.getByText(/Columna 9 es igual a «PUERTO RICO»/)).toBeVisible()
    await expect(dialog.getByRole('columnheader', { name: 'Variante' })).toBeVisible()
    const rowOf = (text: string) => dialog.locator('tbody tr', { hasText: text })
    await expect(rowOf(`A${s}1`)).toContainText('Formato A')
    await expect(rowOf(`A${s}1`)).toContainText('Toa Baja')
    await expect(rowOf(`A${s}1`)).toContainText('Pueblo tomado del código postal 00952: Toa Baja.')
    await expect(rowOf(`B${s}1`)).toContainText('Formato B')
    await expect(rowOf(`B${s}1`)).toContainText('00956')
    await expect(rowOf(`B${s}1`)).toContainText('Código postal asignado: 00956, el primero de entrega de Bayamón.')
    await expect(rowOf(`A${s}2`).getByText('Código postal ajustado', { exact: true })).toBeVisible()
    await expect(rowOf(`A${s}2`).locator('s', { hasText: '00734' })).toBeVisible()
    await expect(rowOf(`A${s}2`)).toContainText('Código postal ajustado: 00734 es de apartados postales (P.O. Box); se usó 00715, de entrega en Ponce.')
    await expect(rowOf(`B${s}2`)).toContainText("Pueblo: El pueblo 'Ciudad Gótica' no está en el catálogo de códigos postales; corríjalo o indique el código postal.")
    await expect(rowOf(`X${s}1`)).toContainText('Variante: Ninguna variante de la plantilla aplica a esta fila; revise las condiciones de las variantes.')
    await expect(rowOf(`X${s}1`)).toContainText(`Celdas de la fila: X${s}1 | Fila Rara ${s} | a | b | c | d | e | f | USA`)
    await expect(dialog.getByText('3 válidas')).toBeVisible()
    await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fb3-previa')

    // ---- Importar y resultado ----
    await dialog.getByRole('button', { name: 'Importar', exact: true }).click()
    const result = dialog.locator('.cs-result')
    await expect(result).toContainText('3 creados')
    await expect(result.getByRole('table', { name: /completados o ajustados con el catálogo de códigos postales: 3/ })).toBeVisible()
    await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fb3-resultado')
    await dialog.getByRole('button', { name: 'Cerrar' }).last().click()
    await expect(dialog).toBeHidden()

    // ---- Lo guardado ----
    const find = async (code: string) => {
      const list = await ok<LocationDto[]>(request.get(`${API_URL}/api/v1/locations?search=${encodeURIComponent(code)}&locationType=DELIVERY`, { headers }), code)
      expect(list).toHaveLength(1)
      return list[0]
    }
    expect(await find(`A${s}1`)).toMatchObject({ city: 'Toa Baja', state: 'PR', postalCode: '00952', line2: 'Urb. Las Flores', contactName: 'Ana Prueba' })
    expect(await find(`B${s}1`)).toMatchObject({ name: `Farmacia Dos ${s}`, city: 'Bayamón', state: 'PR', postalCode: '00956', line1: 'Carr. 2 km 3', line2: 'Bo. Pueblo', contactName: 'Beto Prueba' })
    expect(await find(`A${s}2`)).toMatchObject({ city: 'Ponce', postalCode: '00715' })
    const tpl = await ok<ImportTemplateDto>(request.get(`${API_URL}/api/v1/locations/import-templates/${base.publicId}`, { headers }), 'plantilla')
    expect(tpl.variants?.map((v) => [v.name, v.conditions?.[0]?.operator])).toEqual([
      ['Formato A', 'EMPTY'],
      ['Formato B', 'EQUALS'],
    ])

    // ---- Alta manual: el selector de pueblo/ZIP solo ofrece ZIP de entrega ----
    await page.getByRole('button', { name: /Nuevo consignatario/ }).click()
    const form = page.getByRole('dialog', { name: /Nuevo consignatario/ })
    await form.getByRole('combobox', { name: /Pueblo/ }).fill('0073')
    const options = form.getByRole('option')
    await expect(options.first()).toBeVisible()
    await expect(form.getByRole('option', { name: /00734/ })).toHaveCount(0)
    await expect(form.getByRole('option', { name: /00731/ })).toBeVisible()
    await shot(page, isMobile, 'fb3-alta-zip', form)
  })
})
