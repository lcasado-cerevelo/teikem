// Recorrido de «Impresoras y labels» (Lote F-G1) contra el API real (db-init hecho; API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos). En este entorno QZ Tray no está instalado: la pantalla lo dice («QZ Tray no detectado»,
// «Instalar QZ Tray») y la impresión cae al PDF del navegador; la impresión directa se prueba con un QZ Tray falso en vitest.
//
// Nombres ÚNICOS por corrida (sufijo aleatorio); al final la impresora y la plantilla creadas se desactivan por API.
//
// 1) La pantalla de la maqueta: nota por estación, dos paneles de estación, constructor de 9 elementos con vista previa, estado de QZ Tray.
// 2) Alta de «Zebra ZD420 — Mostrador 1» 4×6 (conexión QZ Tray, nombre en el sistema) y hacerla predeterminada.
// 3) Plantilla «4×6 — COD»: nueva, ocultar la factura y COD grande abajo a la derecha (la vista previa cambia), «Actualizar» y sigue igual al recargar.
// 4) Esta estación: elegir la impresora y la plantilla, «Confirmar antes», Guardar; sigue al recargar (localStorage del navegador).
// 5) Prueba → vista del label → «Descargar PDF»: un PDF de 1 página de 288×432 pt con el número del paquete.
// 6) El despachador (sin printers.manage) no ve el menú y la ruta le dice «Sin permiso».
// 7) Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/fg1-<paso>.png (solo escritorio).
import { mkdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Download, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type PrinterDto = components['schemas']['PrinterDto']
type LabelTemplateDto = components['schemas']['LabelTemplateDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const DISPATCH = { email: process.env.TEIKEM_DISPATCH_EMAIL ?? 'teikem+dispatch@cerevelo.com', password: ADMIN.password }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const RAND = Math.random().toString(36).slice(2, 7)

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

async function shot(page: Page, isMobile: boolean, name: string, height = 900, only?: Locator) {
  if (isMobile) {
    if (process.env.FG1_MOBILE_DIR) await page.screenshot({ path: `${process.env.FG1_MOBILE_DIR}/${name}.png`, fullPage: true })
    return
  }
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height })
  await (only ?? page).screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  if (size) await page.setViewportSize(size)
}

async function apiToken(request: APIRequestContext, who = ADMIN): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password } })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function login(page: Page, who = ADMIN) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(who.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(who.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F-F1). */
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

async function readPdf(download: Download) {
  const raw = readFileSync((await download.path())!).toString('latin1')
  expect(raw.slice(0, 5)).toBe('%PDF-')
  return {
    pages: (raw.match(/\/Type \/Page(?!s)/g) ?? []).length,
    mediaBoxes: [...raw.matchAll(/\/MediaBox \[([^\]]+)\]/g)].map((m) => m[1].trim().split(/\s+/).map(Number)),
  }
}

test.describe('Impresoras y labels (Lote F-G1)', () => {
  test('pantalla, alta de impresora predeterminada, plantilla «4×6 — COD», estación guardada, prueba en PDF 4×6 y despachador sin permiso', async ({ page, request, browser }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const s = `${isMobile ? 'm' : 'd'}${RAND}`
    const printerName = `Zebra ZD420 — Mostrador 1 ${s}`
    const templateName = `4×6 — COD ${s}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const ROW = isMobile ? 'li.dt-card' : 'tbody tr'
    const toastOf = (text: string | RegExp) => expect(page.locator('.toast.on').filter({ hasText: text }).first()).toBeVisible()
    const panelOf = (title: string) => page.locator('.panel').filter({ has: page.getByRole('heading', { name: title, exact: true }) })

    try {
      await login(page)
      await page.goto('/system/printers')

      // 1) la pantalla
      await expect(page.getByRole('heading', { level: 1, name: 'Impresoras y labels' })).toBeVisible()
      await expect(page.getByText(/cada estación de trabajo guarda su propia configuración/)).toBeVisible()
      await expect(page.getByText('QZ Tray no detectado')).toBeVisible()
      await expect(page.getByRole('link', { name: 'Instalar QZ Tray' })).toBeVisible()
      const builder = panelOf('Construir el label')
      await expect(builder.locator('tbody tr')).toHaveCount(9)
      await expect(builder.getByTestId('label-preview')).toContainText('PQT-00012 · Paquete 1 de 3')
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'fg1-pantalla', 1500)

      // 2) alta de la impresora y predeterminada
      const printers = panelOf('Impresoras de la compañía')
      await printers.getByRole('button', { name: 'Nueva impresora' }).click()
      const dialog = page.getByRole('dialog')
      await dialog.getByLabel(/^Nombre/).first().fill(printerName)
      await dialog.getByLabel('Tamaño').selectOption('4X6')
      await dialog.getByLabel('Conexión').selectOption('QZ_TRAY')
      await dialog.getByLabel('Nombre en el sistema').fill('ZDesigner ZD420-203dpi ZPL')
      await dialog.getByLabel('Modelo').fill('ZD420')
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'fg1-impresora-nueva', 900, dialog)
      await dialog.getByRole('button', { name: 'Guardar' }).click()
      await toastOf(`Impresora «${printerName}» creada.`)
      const row = printers.locator(ROW).filter({ hasText: printerName })
      await expect(row).toBeVisible()
      if (!(await row.locator('.chip', { hasText: 'Predeterminada' }).isVisible())) {
        await row.getByRole('button', { name: 'Hacer predeterminada' }).click()
        await toastOf(`«${printerName}» es ahora la predeterminada de su uso.`)
      }
      await expect(row.locator('.chip', { hasText: 'Predeterminada' })).toBeVisible()
      await shot(page, isMobile, 'fg1-impresoras', 900, printers)

      // 3) plantilla «4×6 — COD»: nueva, sin factura, COD grande abajo a la derecha
      await builder.getByRole('button', { name: 'Nueva plantilla' }).click()
      await page.getByRole('dialog').getByLabel('Nombre de la plantilla').fill(templateName)
      await page.getByRole('dialog').getByRole('button', { name: 'Guardar' }).click()
      await toastOf(`Plantilla «${templateName}» creada.`)
      await expect(builder.getByLabel('Plantilla')).toHaveValue(/.+/)
      await expect(builder.getByLabel('Plantilla').locator('option:checked')).toHaveText(new RegExp(templateName))
      const preview = builder.getByTestId('label-preview')
      await expect(preview).toContainText('Factura AX-48217')
      await builder.getByRole('checkbox', { name: 'Mostrar Invoice' }).uncheck()
      await builder.getByLabel('Tamaño de Monto COD').selectOption('lg')
      await builder.getByLabel('Posición de Monto COD').selectOption('br')
      await expect(preview).not.toContainText('Factura AX-48217')
      await expect(preview.locator('.lbl-row-b .lbl-cell-r [data-el="cod"]')).toContainText('COD $125.82')
      await shot(page, isMobile, 'fg1-constructor', 1200, builder)
      await builder.getByRole('button', { name: 'Actualizar construcción del label' }).click()
      await toastOf('Construcción del label actualizada.')

      // 4) esta estación: impresora y plantilla, «Confirmar antes», Guardar
      const station = panelOf('Esta estación — labels')
      await station.getByLabel('Impresora por defecto').selectOption({ label: `${printerName} · 4×6 · Directo (QZ Tray)` })
      const templateOption = station.getByLabel('Template de label').locator('option:not([value=""])', { hasText: templateName })
      await station.getByLabel('Template de label').selectOption((await templateOption.getAttribute('value'))!)
      const confirm = station.getByRole('switch', { name: 'Confirmar antes' })
      if (!(await confirm.isChecked())) await confirm.check()
      await station.getByRole('button', { name: 'Guardar' }).click()
      await toastOf('Configuración de esta estación guardada.')
      await expect(station.getByText('Imprime directo con QZ Tray en «ZDesigner ZD420-203dpi ZPL».')).toBeVisible()
      await shot(page, isMobile, 'fg1-estacion', 900, station)

      await page.reload()
      await expect(page.getByRole('heading', { level: 1, name: 'Impresoras y labels' })).toBeVisible()
      await expect(station.getByLabel('Impresora por defecto').locator('option:checked')).toHaveText(new RegExp(printerName))
      await expect(station.getByLabel('Template de label').locator('option:checked')).toHaveText(templateName)
      await expect(station.getByRole('switch', { name: 'Confirmar antes' })).toBeChecked()
      // la plantilla guardada sigue sin factura y con el COD grande
      await builder.getByLabel('Plantilla').selectOption((await builder.getByLabel('Plantilla').locator('option', { hasText: templateName }).getAttribute('value'))!)
      await expect(builder.getByRole('checkbox', { name: 'Mostrar Invoice' })).not.toBeChecked()
      await expect(builder.getByLabel('Tamaño de Monto COD')).toHaveValue('lg')

      // 5) Prueba → vista del label → Descargar PDF de 1 página 4×6
      await station.getByRole('button', { name: 'Prueba' }).click()
      const pv = page.getByRole('dialog', { name: 'Vista del label' })
      await expect(pv.getByTestId('label-preview')).not.toContainText('Factura AX-48217')
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'fg1-prueba', 1000, pv)
      const [download] = await Promise.all([page.waitForEvent('download'), pv.getByRole('button', { name: 'Descargar PDF' }).click()])
      expect(download.suggestedFilename()).toBe('label-de-prueba.pdf')
      const pdf = await readPdf(download)
      expect(pdf.pages).toBe(1)
      expect(pdf.mediaBoxes).toEqual([[0, 0, 288, 432]])
      if (isMobile) await expectNoHorizontalScroll(page)

      // 6) el despachador no ve el menú ni la pantalla
      const ctx = await browser.newContext({ ...(isMobile ? { viewport: { width: 360, height: 780 } } : {}), locale: 'es-PR' })
      const other = await ctx.newPage()
      await login(other, DISPATCH)
      if (!isMobile) await expect(other.locator('nav.rnav').getByText('Impresoras y labels')).toHaveCount(0)
      await other.goto('/system/printers')
      await expect(other.getByRole('heading', { name: 'Sin permiso' })).toBeVisible()
      await ctx.close()
    } finally {
      // limpieza: la impresora y la plantilla de la corrida quedan inactivas (y la estación del navegador se borra con el contexto)
      const list = (await (await request.get(`${API_URL}/api/v1/printers`, { headers })).json()) as PrinterDto[]
      for (const p of list.filter((x) => x.name === printerName)) await request.post(`${API_URL}/api/v1/printers/${p.publicId}/deactivate`, { headers })
      const tpls = (await (await request.get(`${API_URL}/api/v1/label-templates`, { headers })).json()) as LabelTemplateDto[]
      for (const tp of tpls.filter((x) => x.name === templateName)) await request.post(`${API_URL}/api/v1/label-templates/${tp.publicId}/deactivate`, { headers })
    }
  })
})
