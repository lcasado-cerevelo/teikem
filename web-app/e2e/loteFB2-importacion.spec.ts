// Recorrido de la importación de consignatarios mejorada (Lote F-B2) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo, admin con todos los permisos, módulo CATALOG encendido).
//
// 1) Excel (.xlsx) con dos hojas, columnas genéricas que no se usan, ZIP guardado como número (901), teléfonos en formatos mezclados
//    (uno con dos números en la celda): se escoge la hoja, se arma el mapeo en el editor viendo encabezados y muestras («(No usar)»,
//    la columna del número también va al nombre, Nombre = Número + « - » + Nombre, persona de contacto, cliente por defecto) →
//    vista previa (00901, aviso del segundo teléfono) → Importar → ficha con la persona de contacto.
// 2) CSV en Windows-1252 (como lo guarda Excel en Windows) con la misma plantilla: la ñ llega bien; un número repetido da el error
//    con la fila; «Recordar este cliente en la plantilla».
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos (sintéticos).
// Capturas: docs/manual/frontend/img/fb2-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import * as XLSX from 'xlsx'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type LocationDto = components['schemas']['LocationDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type ImportTemplateDto = components['schemas']['ImportTemplateDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
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
  const manualShot1 = await manualTarget(target)
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  if (manualShot1) await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
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

const HEADER = ['Número', 'Nombre', 'Column3', 'Dirección', 'Pueblo', 'ZIP', 'Contacto', 'Teléfono']

/** Libro con una hoja de notas primero y los datos en la segunda (ZIP y teléfono como NÚMEROS, como los deja Excel). */
function workbook(suffix: string): Buffer {
  const wb = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet([['Lista de consignatarios'], ['Exportada del sistema anterior']]), 'Notas')
  XLSX.utils.book_append_sheet(
    wb,
    XLSX.utils.aoa_to_sheet([
      HEADER,
      [`X${suffix}1`, 'Farmacia Uno', '', 'Calle Sol 1', 'San Juan', 901, 'Ana Rivera', 7875550101],
      [`X${suffix}2`, 'Colmado Dos', 'basura', 'Calle Luna 2', 'Ponce', 716, '', '787 555-0102 / (939)555-0103'],
    ]),
    'Consignatarios',
  )
  return XLSX.write(wb, { type: 'buffer', bookType: 'xlsx' }) as Buffer
}

test.describe('Importación de consignatarios mejorada (Lote F-B2)', () => {
  test('Excel con hojas, mapeo con «(No usar)», columnas unidas, persona de contacto y cliente por defecto; CSV en Windows-1252', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const clientName = `Cliente FB2 ${suffix}`
    const otherName = `Otro FB2 ${suffix}`
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } }), 'cliente')
    await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: otherName } }), 'otro cliente')
    // plantilla de partida (se rehace en el editor viendo el archivo)
    const templateName = `Plantilla FB2 ${suffix}`
    const base = await ok<ImportTemplateDto>(
      request.post(`${API_URL}/api/v1/locations/import-templates`, {
        headers,
        data: { name: templateName, columns: [{ position: 2, field: 'name' }, { position: 4, field: 'line1' }, { position: 5, field: 'city' }] },
      }),
      'plantilla',
    )

    await login(page)
    await page.goto('/catalog/consignees')
    await page.getByRole('button', { name: /Importar/ }).click()
    const dialog = page.getByRole('dialog', { name: 'Importar consignatarios (CSV o Excel)' })
    await dialog.getByLabel('Plantilla', { exact: true }).selectOption({ label: templateName })
    await expect(dialog.getByText(/Mapeo: 2\. Nombre · 4\. Dirección · 5\. Pueblo/)).toBeVisible()

    // ---- Excel: la primera hoja son notas; se escoge la de datos ----
    await dialog.getByLabel('Archivo (CSV o Excel)', { exact: true }).setInputFiles({
      name: 'consignatarios.xlsx',
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      buffer: workbook(suffix),
    })
    const sheet = dialog.getByLabel('Hoja del libro')
    await expect(sheet).toHaveValue('Notas')
    await sheet.selectOption('Consignatarios')
    await expect(dialog.getByText(/consignatarios\.xlsx · Consignatarios · 2 filas detectadas/)).toBeVisible()
    await shot(page, isMobile, 'fb2-hoja')

    // ---- Editor con los encabezados y muestras del archivo ----
    await dialog.getByRole('button', { name: 'Editar' }).click()
    const editor = page.getByRole('dialog', { name: 'Editar plantilla de importación' })
    await expect(editor.getByRole('columnheader', { name: 'En el archivo' })).toBeVisible()
    await expect(editor.locator('.cs-head', { hasText: 'Column3' })).toBeVisible()
    await editor.getByLabel('Columna 1', { exact: true }).selectOption({ label: 'Número' })
    await editor.getByRole('button', { name: /Usar también en otro campo/ }).first().click()
    await editor.getByLabel('Columna 1, campo 2').selectOption({ label: 'Nombre *' })
    await expect(editor.getByLabel('Columna 3', { exact: true })).toHaveValue('') // «(No usar)»
    await editor.getByLabel('Columna 6', { exact: true }).selectOption({ label: 'ZIP' })
    await editor.getByLabel('Columna 7', { exact: true }).selectOption({ label: 'Persona de contacto' })
    await editor.getByLabel('Columna 8', { exact: true }).selectOption({ label: 'Teléfono' })
    const joins = editor.getByRole('region', { name: 'Campos con varias columnas' })
    await joins.getByLabel('Separador 1 de Nombre').fill(' - ')
    await expect(joins.getByText(`X${suffix}1 - Farmacia Uno`)).toBeVisible()
    await editor.getByLabel('Cliente por defecto').selectOption({ label: clientName })
    await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fb2-editor')
    await joins.scrollIntoViewIfNeeded()
    await shot(page, isMobile, 'fb2-unir', joins)
    await editor.getByRole('button', { name: 'Guardar plantilla' }).click()
    await expect(editor).toBeHidden()

    // ---- Vista previa: nombre unido, ZIP con ceros, contacto, aviso del segundo teléfono, cliente de cada fila ----
    await expect(dialog.getByLabel('Cliente por defecto')).toHaveValue(client.publicId ?? '')
    await expect(dialog.getByText(`X${suffix}1 - Farmacia Uno`)).toBeVisible()
    await expect(dialog.getByRole('cell', { name: '00901' })).toBeVisible()
    await expect(dialog.getByRole('cell', { name: 'Ana Rivera' })).toBeVisible()
    await expect(dialog.getByText('La celda del teléfono trae más de un número; se tomó el primero (7875550102).')).toBeVisible()
    await expect(dialog.getByText('2 válidas')).toBeVisible()
    await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fb2-previa')
    await dialog.getByRole('button', { name: 'Importar', exact: true }).click()
    await expect(dialog.locator('.cs-result')).toContainText('2 creados')
    await dialog.getByRole('button', { name: 'Cerrar' }).last().click()
    await expect(dialog).toBeHidden()

    const created = await ok<LocationDto[]>(request.get(`${API_URL}/api/v1/locations?search=${encodeURIComponent(`X${suffix}1`)}&locationType=DELIVERY`, { headers }), 'importado')
    expect(created).toHaveLength(1)
    expect(created[0]).toMatchObject({ name: `X${suffix}1 - Farmacia Uno`, code: `X${suffix}1`, postalCode: '00901', contactName: 'Ana Rivera', clientPublicId: client.publicId })
    const tpl = await ok<ImportTemplateDto>(request.get(`${API_URL}/api/v1/locations/import-templates/${base.publicId}`, { headers }), 'plantilla')
    expect(tpl.defaultClientPublicId).toBe(client.publicId)
    expect(tpl.columns).toContainEqual(expect.objectContaining({ position: 3, field: 'ignore' }))

    // ---- Ficha con la persona de contacto ----
    await page.getByRole('searchbox').fill(`X${suffix}1`)
    await page.getByRole('list', { name: 'Consignatarios' }).getByText(`X${suffix}1 - Farmacia Uno`).click()
    const ficha = page.locator('section.panel', { has: page.getByRole('heading', { name: 'Ficha del consignatario', exact: true }) })
    await expect(ficha.getByText('Ana Rivera')).toBeVisible()
    await shot(page, isMobile, 'fb2-ficha')

    // ---- CSV en Windows-1252 con la misma plantilla: la ñ llega bien; número repetido; recordar otro cliente ----
    const csv = [
      HEADER.join(','),
      `Y${suffix}1,Farmacia Peñuelas,,Calle Ñ 3,Peñuelas,624,José Muñiz,787-555-0104`,
      `Y${suffix}1,Repetida,,Calle 4,Ponce,,,`,
    ].join('\r\n')
    await page.getByRole('button', { name: /Importar/ }).click()
    const again = page.getByRole('dialog', { name: 'Importar consignatarios (CSV o Excel)' })
    await again.getByLabel('Plantilla', { exact: true }).selectOption({ label: templateName })
    await expect(again.getByLabel('Cliente por defecto')).toHaveValue(client.publicId ?? '')
    await again.getByLabel('Cliente por defecto').selectOption({ label: otherName })
    await expect(again.getByRole('switch', { name: 'Recordar este cliente en la plantilla' })).toBeChecked()
    await again.getByLabel('Archivo (CSV o Excel)', { exact: true }).setInputFiles({ name: 'consignatarios.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'latin1') })
    await expect(again.getByText(`Y${suffix}1 - Farmacia Peñuelas`)).toBeVisible()
    await expect(again.getByRole('cell', { name: 'José Muñiz' })).toBeVisible()
    await expect(again.getByText(`Número: El código Y${suffix}1 se repite en la fila 1 del archivo.`)).toBeVisible()
    await expect(again.getByLabel('Hoja del libro')).toHaveCount(0)
    await shot(page, isMobile, 'fb2-csv')
    await again.getByRole('button', { name: 'Importar', exact: true }).click()
    await expect(again.locator('.cs-result')).toContainText('1 creados')
    const remembered = await ok<ImportTemplateDto>(request.get(`${API_URL}/api/v1/locations/import-templates/${base.publicId}`, { headers }), 'plantilla')
    expect(remembered.defaultClientName).toBe(otherName)
    await expectNoHorizontalScroll(page)
  })
})
