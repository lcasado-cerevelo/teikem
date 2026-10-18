// Recorrido de «Labels y documentos» (Lote F-G2) contra el API real (db-init hecho; API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos, módulos LTL_GROUND y CATALOG). QZ Tray no está en este entorno: todo sale por el PDF del
// navegador; el recorrido usa «Confirmar antes» y «Descargar PDF» (el diálogo de impresión del sistema no se puede pulsar).
//
// Datos propios por corrida (sufijo): un cliente, un consignatario y dos órdenes Sometidas: A con un paquete de 2 piezas (número
// tecleado PKG-<sufijo>) y otro de 1 (PQT automático); B con 1 pieza. Al final las dos órdenes y la de la entrada rápida se eliminan.
// NO se toca el ajuste de la compañía «Imprimir labels directo» (otros recorridos guardan órdenes a la vez): se enciende el «Label al
// guardar» de la pantalla.
//
// 1) Esta estación con «Confirmar antes».  2) Lista: Elegir A y B → «Imprimir label» → vista «Imprimir 4 labels» → PDF de 4 páginas de
// 288×432 pt.  3) «Todos los labels» con la búsqueda del sufijo.  4) Waybill summary (carta apaisada) y Packing list (carta).
// 5) Ficha de A → «Imprimir label» (3 labels).  6) Entrada rápida con «Label al guardar» → vista del label de la orden nueva.
// 7) Escaneo: el número de paquete encuentra la orden («Encontrada por número de paquete.») e «Informe de faltantes».
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal). Capturas: docs/manual/frontend/img/fg2-<paso>.png (solo escritorio).
import { mkdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Download, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'
import { contractData, withRates } from './rates'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type LocationDto = components['schemas']['LocationDto']
type OrderDetailDto = components['schemas']['OrderDetailDto']
type OrderPageDto = components['schemas']['OrderPageDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const RAND = Math.random().toString(36).slice(2, 7).toUpperCase()

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

async function shot(page: Page, isMobile: boolean, name: string, height = 900, only?: Locator) {
  if (isMobile) {
    if (process.env.FG2_MOBILE_DIR) await page.screenshot({ path: `${process.env.FG2_MOBILE_DIR}/${name}.png`, fullPage: true })
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

async function readPdf(download: Download) {
  const raw = readFileSync((await download.path())!).toString('latin1')
  expect(raw.slice(0, 5)).toBe('%PDF-')
  return {
    pages: (raw.match(/\/Type \/Page(?!s)/g) ?? []).length,
    mediaBoxes: [...raw.matchAll(/\/MediaBox \[([^\]]+)\]/g)].map((m) => m[1].trim().split(/\s+/).map((n) => Math.round(Number(n)))),
  }
}

/** Abre la vista (Confirmar antes) con `open` y descarga su PDF. */
async function downloadFromPreview(page: Page, open: () => Promise<void>, title: string | RegExp) {
  await open()
  const dialog = page.getByRole('dialog', { name: title })
  await expect(dialog).toBeVisible()
  const [download] = await Promise.all([page.waitForEvent('download'), dialog.getByRole('button', { name: 'Descargar PDF' }).click()])
  await expect(dialog).toHaveCount(0)
  return readPdf(download)
}

test.describe('Labels y documentos (Lote F-G2)', () => {
  test('labels de las marcadas y de todo lo filtrado, waybill summary, packing list, ficha, label al guardar y escaneo por número de paquete', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const s = `${isMobile ? 'M' : 'D'}${RAND}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const created: string[] = []
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FG2 ${s}`, contract: contractData() } }), 'cliente')
    await withRates(request, headers, client)
    const cons = await ok<LocationDto>(
      request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name: `Farmacia FG2 ${s}`, line1: 'Carr. 2 km 14.5', city: 'Toa Baja', postalCode: '00949', country: 'PR' } }),
      'consignatario',
    )
    const order = (packages: unknown[], cod?: number) =>
      ok<OrderDetailDto>(request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: client.publicId, consigneeLocationPublicId: cons.publicId, serviceType: 'STANDARD', packages, codAmount: cod ?? null } }), 'orden')
    const a = await order([{ packageType: 'BOX', pieces: 2, packageNumber: `PKG-${s}` }, { packageType: 'ENVELOPE', pieces: 1 }], 125.82)
    const b = await order([{ packageType: 'BOX', pieces: 1 }])
    created.push(a.publicId!, b.publicId!)
    const ROW = isMobile ? 'li.dt-card' : 'tbody tr'

    try {
      await login(page)

      // 1) esta estación con «Confirmar antes» (labels; los documentos ya lo traen)
      await page.goto('/system/printers')
      const station = page.locator('.panel').filter({ has: page.getByRole('heading', { name: 'Esta estación — labels', exact: true }) })
      const confirm = station.getByRole('switch', { name: 'Confirmar antes' })
      if (!(await confirm.isChecked())) {
        await confirm.check()
        await station.getByRole('button', { name: 'Guardar' }).click()
      }

      // 2) lista: marcar A y B → Imprimir label
      await page.goto('/orders')
      await page.getByRole('searchbox', { name: 'Buscar…' }).fill(`FG2 ${s}`)
      await expect(page.getByText('1–2 de 2')).toBeVisible() // solo las dos órdenes de la corrida (la búsqueda va al servidor)
      const pick = (o: OrderDetailDto) => page.locator(ROW).filter({ hasText: cons.name ?? '' }).filter({ hasText: o.orderNumber ?? '' }).getByRole('checkbox')
      const print = page.getByRole('button', { name: 'Imprimir label' })
      await expect(print).toBeDisabled()
      await pick(a).check()
      await pick(b).check()
      await expect(page.getByText('2 marcadas')).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'fg2-lista', 1100)
      await print.click()
      const preview = page.getByRole('dialog', { name: 'Imprimir 4 labels' })
      await expect(preview.getByTestId('print-count')).toHaveText('Labels: 4 · órdenes: 2 (uno por pieza).')
      await expect(preview.getByTestId('label-preview')).toContainText(`PKG-${s}1 · Paquete 1 de 3`)   // Lote 109: cada pieza con su número (la línea tecleada PKG-x numera sus 2 piezas PKG-x1 y PKG-x2)
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'fg2-labels-vista', 1000, preview)
      const [labelsPdf] = await Promise.all([page.waitForEvent('download'), preview.getByRole('button', { name: 'Descargar PDF' }).click()])
      const pdf = await readPdf(labelsPdf)
      expect(pdf.pages).toBe(4)
      expect(pdf.mediaBoxes).toEqual(Array(4).fill([0, 0, 288, 432]))

      // 3) «Todos los labels» con el filtro (búsqueda del sufijo): las mismas 4
      const all = await downloadFromPreview(page, () => page.getByRole('button', { name: 'Todos los labels' }).click(), 'Imprimir 4 labels')
      expect(all.pages).toBe(4)

      // 4) waybill summary y packing list de las marcadas, en carta
      const waybill = await downloadFromPreview(page, () => page.getByRole('button', { name: 'Waybill summary' }).click(), 'Waybill summary')
      expect(waybill.mediaBoxes[0]).toEqual([0, 0, 792, 612])
      const packing = await downloadFromPreview(page, () => page.getByRole('button', { name: 'Packing list' }).click(), 'Packing list')
      expect(packing.mediaBoxes[0]).toEqual([0, 0, 612, 792])

      // 5) ficha de A → Imprimir label (3 piezas)
      await page.goto(`/orders/${a.publicId}`)
      const one = await downloadFromPreview(page, () => page.getByRole('button', { name: 'Imprimir label' }).click(), 'Imprimir 3 labels')
      expect(one.pages).toBe(3)

      // 6) entrada rápida con «Label al guardar»: al guardar sale la vista del label de la orden nueva
      await page.goto('/orders')
      await page.getByRole('button', { name: /Nueva orden/ }).click()
      if ((await page.getByRole('button', { name: /Rápida/ }).getAttribute('aria-pressed')) !== 'true') await page.getByRole('button', { name: /Rápida/ }).click()
      const qk = page.locator('.qk')
      const sw = qk.getByRole('switch', { name: 'Label al guardar' })
      if (!(await sw.isChecked())) await sw.check()
      const clientBox = qk.getByRole('combobox', { name: 'Cliente', exact: true })
      await clientBox.click()
      await clientBox.fill(`Cliente FG2 ${s}`)
      await page.getByRole('option', { name: new RegExp(`Cliente FG2 ${s}`) }).first().click()
      await qk.getByRole('combobox', { name: 'Consignatario' }).click()
      await page.getByRole('option', { name: new RegExp(`Farmacia FG2 ${s}`) }).click()
      for (const label of ['Paquete', 'Servicio']) if ((await qk.getByLabel(label, { exact: true }).inputValue()) === '') await qk.getByLabel(label, { exact: true }).selectOption({ index: 1 })
      await qk.getByRole('button', { name: 'Guardar fila' }).click()
      const quickPreview = page.getByRole('dialog', { name: 'Imprimir 1 label' })
      await expect(quickPreview).toBeVisible()
      await shot(page, isMobile, 'fg2-label-al-guardar', 1000)
      await quickPreview.getByRole('button', { name: 'Cancelar' }).click()
      const quick = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&take=50`, { headers }), 'órdenes')
      for (const o of quick.items ?? []) if (o.publicId && !created.includes(o.publicId)) created.push(o.publicId)

      // 7) escaneo: el número de paquete encuentra la orden; informe de faltantes
      await page.goto('/ops/scan')
      const input = page.getByRole('textbox', { name: 'Código a escanear' })
      await input.fill(`PKG-${s}2`)   // el número de UNA pieza (Lote 109)
      await input.press('Enter')
      await expect(page.getByRole('status', { name: 'Resultado del último escaneo' })).toContainText('Encontrada por número de paquete.')
      const missing = await downloadFromPreview(page, () => page.getByRole('button', { name: 'Informe de faltantes' }).click(), 'Informe de faltantes')
      expect(missing.pages).toBeGreaterThanOrEqual(1)
      expect(missing.mediaBoxes[0]).toEqual([0, 0, 792, 612])
      if (isMobile) await expectNoHorizontalScroll(page)
    } finally {
      for (const id of created) await request.delete(`${API_URL}/api/v1/orders/${id}`, { headers })
    }
  })
})
