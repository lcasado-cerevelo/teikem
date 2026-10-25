// Recorrido de «Vistas e informes» (Lote F-F2) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo,
// admin con todos los permisos, módulos ANALYTICS y CUSTOM_FIELDS encendidos).
//
// Nombres ÚNICOS por corrida (sufijo aleatorio): la base se reutiliza entre proyectos y corridas. Se siembra por API: tres clientes, un consignatario
// por cliente, seis órdenes (borrador) con piezas distintas y un campo personalizado de texto en las órdenes con un valor por orden. Al final se
// eliminan las vistas creadas y se desactiva el campo personalizado.
//
// 1) La galería: paneles por fuente (abiertos), vista de sistema «Por defecto» sin editar/eliminar.  2) Vista simple de Clientes: validaciones del
// constructor, columnas, filtro, orden, vista previa; crearla, abrirla, buscar, ordenar, exportar CSV.  3) Vista agrupada de Órdenes con
// agregados, totales y orden por un agregado; rango de fechas (7 días y personalizado vacío).  4) Vista combinada (Órdenes + Cliente) con el campo
// personalizado.  5) Editar (nombre y columna) y el 409 de nombre repetido con el mensaje exacto.  6) Eliminar con confirmación.  7) La vista de
// sistema solo se ve: sin Editar ni Eliminar y el API responde 403 con el mensaje exacto.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/ff2-<paso>.png (solo en escritorio).
import { mkdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'
import { contractData, withRates } from './rates'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type LocationDto = components['schemas']['LocationDto']
type OrderDetailDto = components['schemas']['OrderDetailDto']
type ReportDto = components['schemas']['ReportDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
const RAND = Math.random().toString(36).slice(2, 7)

test.use({ locale: 'es-PR', actionTimeout: 20_000 })
test.setTimeout(420_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string, height = 900, only?: Locator, width?: number) {
  if (isMobile) {
    if (process.env.FF2_MOBILE_DIR) await page.screenshot({ path: `${process.env.FF2_MOBILE_DIR}/${name}.png`, fullPage: true })
    return
  }
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: width ?? size.width, height })
  const manualShot1 = await manualTarget((only ?? page))
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F18). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      if (el.parentElement?.closest('.dt-scroll')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      if (el.matches('.sw input')) return
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

test.describe('Vistas e informes (Lote F-F2)', () => {
  test('galería, vista simple, agrupada con totales, combinada con campo personalizado, rango, buscar, ordenar, exportar, editar, 409, eliminar y vista de sistema', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const s = `${isMobile ? 'm' : 'd'}${RAND}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const cfKey = `ff2_${s}`
    const cfLabel = `Referencia FF2 ${s}`
    const created: number[] = []
    let cfId = 0

    // ---------- Datos: tres clientes con un consignatario cada uno y seis órdenes (borrador) con piezas 2,3,5 · 4,1 · 7 ----------
    const clientNames = ['Alfa', 'Bravo', 'Condor'].map((l) => `Cliente FF2 ${s} ${l}`)
    const pieces = [[2, 3, 5], [4, 1], [7]]
    const cf = await ok<{ id: number }>(
      request.post(`${API_URL}/api/v1/custom-fields/definitions/TRANSPORT_ORDER`, {
        headers,
        data: { fieldKey: cfKey, labels: { es: cfLabel, en: cfLabel }, descriptions: {}, dataType: 'TEXT', isRequired: false, isUnique: false, showInList: false, sortOrder: 0, options: [] },
      }),
      'campo personalizado',
    )
    cfId = cf.id
    const orders: OrderDetailDto[] = []
    for (const [i, name] of clientNames.entries()) {
      const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name, contract: contractData() } }), `cliente ${name}`)
      await withRates(request, headers, client)
      const cons = await ok<LocationDto>(
        request.post(`${API_URL}/api/v1/locations`, {
          headers,
          data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name: `Consignatario FF2 ${s} ${i}`, line1: 'Calle Sol 1', city: 'Ponce', postalCode: '00716', country: 'PR', allowDupInvoice: true },
        }),
        'consignatario',
      )
      for (const [j, pcs] of pieces[i].entries()) {
        const order = await ok<OrderDetailDto>(
          request.post(`${API_URL}/api/v1/orders`, {
            headers,
            data: { clientPublicId: client.publicId, consigneeLocationPublicId: cons.publicId, serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: pcs, description: 'Ropa' }] },
          }),
          'orden',
        )
        await ok(request.put(`${API_URL}/api/v1/custom-fields/values/TRANSPORT_ORDER/${order.id}`, { headers, data: { values: { [cfKey]: `REF-${'ABC'[i]}${j + 1}` } } }), 'valor del campo')
        orders.push(order)
      }
    }
    expect(orders).toHaveLength(6)

    const names = { simple: `Clientes FF2 ${s}`, simple2: `Clientes FF2 ${s} renombrada`, grouped: `Piezas por cliente FF2 ${s}`, combined: `Órdenes con cliente FF2 ${s}` }
    const dialog = () => page.getByRole('dialog')
    const toastOf = (text: string | RegExp) => expect(page.locator('.toast.on').filter({ hasText: text })).toBeVisible()
    const ROW = isMobile ? 'li.dt-card' : 'tbody tr'
    const panel = (label: string) => page.locator(`section.vw-panel[aria-label="${label}"]`)
    const saveView = () => dialog().getByRole('button', { name: 'Guardar vista' }).click()
    const heading = (name: string) => expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
    const allViews = async () => ok<ReportDto[]>(request.get(`${API_URL}/api/v1/analytics/reports`, { headers }), 'vistas')
    const addFilter = async (field: string, op: string, value: string) => {
      await dialog().getByRole('button', { name: 'Agregar filtro' }).click()
      const rows = dialog().locator('.fr-row')
      const last = rows.last()
      await last.getByLabel('Campo', { exact: true }).selectOption(field)
      await last.getByLabel('Operador', { exact: true }).selectOption(op)
      await last.getByLabel('Valor', { exact: true }).fill(value)
    }

    try {
      await login(page)
      await page.goto('/analytics/reports')

      // ================= 1) La galería =================
      await heading('Vistas e informes')
      await expect(panel('Clientes')).toBeVisible()
      await expect(panel('Envíos')).toBeVisible()
      // abiertos por defecto: la vista de sistema «Clientes» se ve con su insignia y sin Editar/Eliminar
      const sysRow = panel('Clientes').locator('.vw-view').filter({ hasText: '6 columnas' })
      await expect(sysRow).toContainText('Por defecto')
      await expect(sysRow.getByRole('button', { name: /^Editar la vista/ })).toHaveCount(0)
      await expect(sysRow.getByRole('button', { name: /^Eliminar la vista/ })).toHaveCount(0)
      // una fuente sin informes dice cómo crear el primero
      await expect(page.getByText('Todavía no hay informes para esta fuente — crea el primero con "Nueva vista".').first()).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-galeria', 1000)

      // ================= 2) Vista simple de Clientes =================
      await panel('Clientes').getByRole('button', { name: 'Nueva vista de Clientes' }).click()
      await expect(dialog().getByRole('heading', { name: 'Nueva vista' })).toBeVisible()
      await expect(dialog().getByText('Esta fuente de datos no tiene otras fuentes asociadas para combinar.')).toBeVisible()
      // validaciones de la maqueta
      await saveView()
      await expect(dialog().getByText('Ponle un nombre a la vista')).toBeVisible()
      await expect(dialog().getByText('Escoge al menos una columna')).toBeVisible()
      await dialog().getByLabel(/^Nombre de la vista/).fill(names.simple)
      const mainCols = dialog().getByRole('group', { name: 'Clientes' })
      await mainCols.getByLabel('Código', { exact: true }).check()
      await mainCols.getByLabel('Nombre', { exact: true }).check()
      await mainCols.getByLabel('Estatus', { exact: true }).check()
      await addFilter('Name', 'contains', `FF2 ${s}`)
      await dialog().getByRole('button', { name: 'Agregar orden' }).click()
      await dialog().getByLabel('Ordenar por 1').selectOption('Name')
      await dialog().getByLabel('Dirección del orden 1').selectOption('desc')
      // vista previa con datos reales
      await expect(dialog().getByText(clientNames[0], { exact: true })).toBeVisible()
      await expect(dialog().getByText(clientNames[2], { exact: true })).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-constructor-simple', 1500)
      await saveView()
      await toastOf(`Vista guardada — disponible para uso futuro: ${names.simple}`)
      await expect(dialog()).toHaveCount(0)
      const mine = (await allViews()).find((v) => v.name === names.simple)
      expect(mine).toMatchObject({ baseEntityType: 'CLIENT', columns: ['Code', 'Name', 'Status'], canEdit: true, isSystem: false, visibility: 'PRIVATE' })
      expect(JSON.parse(mine?.sortJson ?? '[]')).toEqual([{ field: 'Name', dir: 'desc' }])
      created.push(mine?.id ?? 0)

      // abrirla desde la galería: resultado con las tres filas, orden descendente por nombre (el que guardó la vista)
      await expect(panel('Clientes').locator('.vw-view').filter({ hasText: names.simple })).toContainText('3 columnas')
      await panel('Clientes').locator('.vw-open', { hasText: names.simple }).click()
      await heading(names.simple)
      await expect(page.getByRole('button', { name: 'Volver a Vistas' })).toBeVisible()
      await expect(page.getByText('1 filtro(s) aplicado(s)')).toBeVisible()
      await expect(page.locator(ROW)).toHaveCount(3)
      await expect(page.locator(ROW).first()).toContainText(clientNames[2])
      // sin campo de fecha en Clientes: no hay rango
      await expect(page.getByLabel('Rango de fechas')).toHaveCount(0)
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-resultado-simple', 800)

      // buscar sobre lo cargado
      await page.getByRole('searchbox', { name: 'Buscar en las filas cargadas' }).fill('Bravo')
      await expect(page.locator(ROW)).toHaveCount(1)
      await expect(page.locator(ROW).first()).toContainText(clientNames[1])
      await page.getByRole('searchbox', { name: 'Buscar en las filas cargadas' }).fill('no-existe-zz')
      await expect(page.getByText('Ninguna fila coincide con la búsqueda.')).toBeVisible()
      await page.getByRole('searchbox', { name: 'Buscar en las filas cargadas' }).fill('')
      await expect(page.locator(ROW)).toHaveCount(3)

      // ordenar por una columna (escritorio: clic en el encabezado; móvil: el selector «Ordenar por»)
      if (isMobile) {
        await page.getByLabel('Ordenar por', { exact: true }).selectOption({ label: 'Nombre' })
      } else {
        await page.getByRole('columnheader', { name: /Nombre/ }).getByRole('button').click()
      }
      await expect(page.locator(ROW).first()).toContainText(clientNames[0])

      // exportar lo cargado (CSV)
      await page.getByRole('button', { name: 'Exportar' }).click()
      const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('menuitem', { name: /CSV/ }).click()])
      expect(download.suggestedFilename()).toMatch(/\.csv$/)
      const csv = readFileSync((await download.path())!, 'utf8').replace(/^﻿/, '')
      for (const n of clientNames) expect(csv).toContain(n)
      expect(csv.split('\n')[0]).toMatch(/Código/)
      await shot(page, isMobile, 'ff2-resultado-orden', 800)

      // ================= 3) Vista agrupada: piezas por cliente =================
      await page.getByRole('button', { name: 'Volver a Vistas' }).click()
      await panel('Envíos').getByRole('button', { name: 'Nueva vista de Envíos' }).click()
      await dialog().getByLabel(/^Nombre de la vista/).fill(names.grouped)
      await addFilter('ClientName', 'contains', `FF2 ${s}`)
      await dialog().getByRole('button', { name: 'Agregar campo de agrupación' }).click()
      await dialog().getByLabel('Campo de agrupación 1', { exact: true }).selectOption('ClientName')
      await dialog().getByRole('button', { name: 'Agregar agregado' }).click()
      await dialog().getByRole('button', { name: 'Agregar agregado' }).click()
      await dialog().getByLabel('Función del agregado 2').selectOption('SUM')
      await dialog().getByLabel('Campo del agregado 2').selectOption('TotalPieces')
      await dialog().getByLabel('Mostrar fila de totales').check()
      await dialog().getByRole('button', { name: 'Agregar orden' }).click()
      await dialog().getByLabel('Ordenar por 1').selectOption('sum_TotalPieces')
      await dialog().getByLabel('Dirección del orden 1').selectOption('desc')
      // la vista previa agrupa y rotula el agregado
      await expect(isMobile ? dialog().locator('li.dt-card').first() : dialog().getByRole('columnheader', { name: /Suma · Piezas/ })).toContainText(isMobile ? 'Suma · Piezas' : '')
      await expect(dialog().getByText('Escoge al menos una columna')).toHaveCount(0)
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-constructor-agrupada', 1700)
      await saveView()
      await toastOf(`Vista guardada — disponible para uso futuro: ${names.grouped}`)
      const grouped = (await allViews()).find((v) => v.name === names.grouped)
      created.push(grouped?.id ?? 0)
      expect(grouped?.columns).toEqual([])
      expect(JSON.parse(grouped?.groupJson ?? '{}')).toEqual({ by: ['ClientName'], aggregates: [{ fn: 'COUNT' }, { fn: 'SUM', field: 'TotalPieces' }], totals: true })

      await panel('Envíos').locator('.vw-open', { hasText: names.grouped }).click()
      await heading(names.grouped)
      await expect(page.getByText('1 filtro(s) aplicado(s) · Agrupado por Cliente')).toBeVisible()
      await expect(page.locator(ROW)).toHaveCount(3)
      // orden guardado: 10, 7, 5 piezas
      await expect(page.locator(ROW).nth(0)).toContainText(clientNames[0])
      await expect(page.locator(ROW).nth(0)).toContainText('10')
      await expect(page.locator(ROW).nth(1)).toContainText(clientNames[2])
      await expect(page.locator(ROW).nth(2)).toContainText(clientNames[1])
      // totales de la vista: 6 órdenes y 22 piezas
      const totals = page.getByRole('group', { name: 'Totales' })
      await expect(totals).toContainText('Cantidad')
      await expect(totals).toContainText('6')
      await expect(totals).toContainText('22')
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-resultado-agrupada', 800)

      // rango de fechas: la fuente tiene fecha de creación
      const range = page.getByLabel('Rango de fechas')
      await expect(range).toHaveValue('ALL')
      await range.selectOption({ label: 'Últimos 7 días' })
      await expect(page.locator(ROW)).toHaveCount(3)
      await range.selectOption({ label: 'Rango personalizado' })
      await expect(page.getByText('Escoja las fechas Desde y Hasta para ver el resultado.')).toBeVisible()
      await page.getByLabel('Desde', { exact: true }).fill('2020-01-01')
      await page.getByLabel('Hasta', { exact: true }).fill('2020-01-31')
      await expect(page.getByText('Esta vista no tiene filas con las condiciones y el rango actuales.')).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-rango', 800)

      // ================= 4) Vista combinada con el campo personalizado =================
      await page.getByRole('button', { name: 'Volver a Vistas' }).click()
      await panel('Envíos').getByRole('button', { name: 'Nueva vista de Envíos' }).click()
      await dialog().getByLabel(/^Nombre de la vista/).fill(names.combined)
      await dialog().locator('.r2').first().getByLabel('Cliente', { exact: true }).check() // «Combinar con» Cliente
      const orderCols = dialog().getByRole('group', { name: 'Envíos' })
      await orderCols.getByLabel('Número de envío', { exact: true }).check()
      await dialog().getByRole('group', { name: 'Campos personalizados' }).getByLabel(cfLabel).check()
      await dialog().getByRole('group', { name: 'Cliente', exact: true }).getByLabel('Nombre', { exact: true }).check()
      await dialog().getByRole('group', { name: 'Cliente', exact: true }).getByLabel('Código', { exact: true }).check()
      await addFilter('ClientName', 'contains', `FF2 ${s}`)
      await dialog().getByRole('button', { name: 'Agregar orden' }).click()
      await dialog().getByLabel('Ordenar por 1').selectOption('cf.' + cfKey)
      await expect(dialog().getByText('REF-A1').first()).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-constructor-combinada', 1700)
      await saveView()
      await toastOf(`Vista guardada — disponible para uso futuro: ${names.combined}`)
      const combined = (await allViews()).find((v) => v.name === names.combined)
      created.push(combined?.id ?? 0)
      expect(combined).toMatchObject({ secondary: ['Client'], baseEntityType: 'TRANSPORT_ORDER' })
      expect(combined?.columns).toEqual(expect.arrayContaining(['OrderNumber', `cf.${cfKey}`, 'Client.Name', 'Client.Code']))

      await panel('Envíos').locator('.vw-view').filter({ hasText: names.combined }).locator('.vw-open').click()
      await heading(names.combined)
      await expect(page.locator(ROW)).toHaveCount(6)
      await expect(page.locator(ROW).first()).toContainText('REF-A1')
      await expect(page.locator(ROW).first()).toContainText(clientNames[0])
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-resultado-combinada', 900)

      // ================= 5) Editar y el 409 del nombre repetido =================
      await page.getByRole('button', { name: 'Volver a Vistas' }).click()
      await panel('Clientes').locator('.vw-open', { hasText: names.simple }).click()
      await page.getByRole('button', { name: 'Editar', exact: true }).click()
      await expect(dialog().getByRole('heading', { name: 'Editar vista' })).toBeVisible()
      // lo guardado vuelve al constructor
      await expect(dialog().getByLabel(/^Nombre de la vista/)).toHaveValue(names.simple)
      await expect(mainCols.getByLabel('Código', { exact: true })).toBeChecked()
      await expect(mainCols.getByLabel('Razón social', { exact: true })).not.toBeChecked()
      await expect(dialog().getByLabel('Valor', { exact: true })).toHaveValue(`FF2 ${s}`)
      await dialog().getByLabel(/^Nombre de la vista/).fill(names.simple2)
      await mainCols.getByLabel('Razón social', { exact: true }).check()
      await shot(page, isMobile, 'ff2-editar', 1500)
      await saveView()
      await toastOf(`Vista actualizada: ${names.simple2}`)
      await heading(names.simple2)
      await (isMobile ? expect(page.locator(ROW).first()).toContainText('Razón social') : expect(page.getByRole('columnheader', { name: /Razón social/ })).toBeVisible())

      // nombre repetido (otra vista con el mismo nombre en la misma fuente) → 409 exacto bajo el nombre
      await page.getByRole('button', { name: 'Volver a Vistas' }).click()
      await panel('Clientes').getByRole('button', { name: 'Nueva vista de Clientes' }).click()
      await dialog().getByLabel(/^Nombre de la vista/).fill(names.simple2)
      await dialog().getByRole('group', { name: 'Clientes' }).getByLabel('Código', { exact: true }).check()
      await saveView()
      await expect(dialog().getByText(`Ya existe una vista '${names.simple2}' para CLIENT.`)).toBeVisible()
      await toastOf(`Ya existe una vista '${names.simple2}' para CLIENT.`)
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-409', 1300)
      await dialog().getByRole('button', { name: 'Cancelar' }).click()
      await expect(dialog()).toHaveCount(0)

      // ================= 6) Eliminar =================
      await panel('Clientes').locator('.vw-open', { hasText: names.simple2 }).click()
      await page.getByRole('button', { name: 'Eliminar', exact: true }).click()
      await expect(dialog().getByText(`Se eliminará la vista «${names.simple2}»`)).toBeVisible()
      await shot(page, isMobile, 'ff2-eliminar', 700)
      await dialog().getByRole('button', { name: 'Eliminar', exact: true }).click()
      await toastOf(`Vista eliminada: ${names.simple2}`)
      await heading('Vistas e informes')
      await expect(panel('Clientes').locator('.vw-view').filter({ hasText: names.simple2 })).toHaveCount(0)
      expect((await allViews()).find((v) => v.name === names.simple2)).toBeUndefined()
      created.length = 0
      created.push(grouped?.id ?? 0, combined?.id ?? 0)
      // con el servidor corregido (Lote F-F2), el nombre de una vista eliminada vuelve a estar libre (el API que corre en :5000 puede ser anterior)
      if (process.env.FF2_SERVER_FIXED) {
        const again = await ok<ReportDto>(request.post(`${API_URL}/api/v1/analytics/reports/CLIENT`, { headers, data: { name: names.simple2, columns: ['Name'] } }), 'nombre de una vista eliminada')
        created.push(again.id ?? 0)
      }

      // ================= 7) La vista de sistema solo se ve =================
      const sys = (await allViews()).find((v) => v.isSystem && v.baseEntityType === 'CLIENT')
      await panel('Clientes').locator('.vw-open', { hasText: sys?.name ?? 'Clientes' }).first().click()
      await expect(page.getByText('Por defecto').first()).toBeVisible()
      await expect(page.getByRole('button', { name: 'Editar', exact: true })).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Eliminar', exact: true })).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Volver a Vistas' })).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff2-sistema', 800)
      const put = await request.put(`${API_URL}/api/v1/analytics/reports/${sys?.id}`, { headers, data: { name: 'x', columns: ['Name'] } })
      expect(put.status()).toBe(403)
      expect(((await put.json()) as { title: string }).title).toBe('Los elementos por default de la plataforma no se editan ni se eliminan.')
      const del = await request.delete(`${API_URL}/api/v1/analytics/reports/${sys?.id}`, { headers })
      expect(del.status()).toBe(403)
    } finally {
      for (const id of created) if (id) await request.delete(`${API_URL}/api/v1/analytics/reports/${id}`, { headers })
      if (cfId) await request.delete(`${API_URL}/api/v1/custom-fields/definitions/${cfId}`, { headers })
    }
  })
})
