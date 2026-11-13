// Escena 3 — Recibo contra las dos órdenes de compra (ENSAYO). Parte del estado E02.
import { test } from '@playwright/test'
import { clicEn, modo, nuevoReloj, paso, finEscena, prepararCursor, señalar, taparColumna, tokensFueraDeCamara, ponerSesion, expect } from '../ayudas'

const ZOOM = Number(process.env.VIDEO_ZOOM ?? '1.25')
test.use({ launchOptions: { args: [`--force-device-scale-factor=${ZOOM}`] } })

test('E03 - recibo', async ({ browser, baseURL }) => {
  const tokens = await tokensFueraDeCamara(2)
  const ctx = await browser.newContext({
    baseURL: baseURL!, locale: 'es-PR', deviceScaleFactor: ZOOM,
    viewport: { width: Math.round(1920 / ZOOM), height: Math.round(1080 / ZOOM) },
    recordVideo: { dir: process.env.VIDEO_OUT ?? 'F:/Download/TeikemVideo/solutions-proceso/tomas/_pw', size: { width: 1920, height: 1080 } },
  })
  const tPagina = Date.now()
  const page = await ctx.newPage()
  await ponerSesion(page, tokens)
  await prepararCursor(page)
  const reloj = nuevoReloj('E03-recibo', tPagina)
  const dlg = () => page.getByRole('dialog')
  const shot = (n: string) => page.screenshot({ path: `F:/Download/TeikemVideo/solutions-proceso/cuadros/E03-${n}.png` })

  async function recibir(proveedor: RegExp, nombre: string, lineas: number, lento: boolean) {
    modo.rapido = !lento
    const c = (n: number): { l: number } => ({ l: lento ? n : 11 })
    await paso(page, reloj, `${nombre}: nuevo recibo`, c(2), async () => {
      await clicEn(page, page.getByRole('button', { name: 'Nuevo recibo' }))
      await expect(dlg().getByLabel(/^Origen/)).toBeVisible()
      await dlg().getByLabel(/^Origen/).selectOption('PO')
    })
    await paso(page, reloj, `${nombre}: almacén y orden`, c(4), async () => {
      const wh = dlg().getByRole('combobox', { name: /^Almacén/ })
      await clicEn(page, wh); await wh.fill('ALM-SOLUTIONS')
      await clicEn(page, dlg().getByRole('option', { name: /^ALM-SOLUTIONS/ }))
      const po = dlg().getByRole('combobox', { name: /^Orden de compra/ })
      await clicEn(page, po)
      await clicEn(page, dlg().getByRole('option', { name: proveedor }))
    })
    await paso(page, reloj, `${nombre}: crear`, c(5), async () => {
      await clicEn(page, dlg().getByRole('button', { name: 'Crear recibo' }))
      await expect(page.getByText(/Recibo REC-\d+ creado/)).toBeVisible()
      await expect(page.getByRole('table', { name: 'Líneas del recibo' })).toBeVisible()
    })
    if (lento) await shot('creado')
    const det = page.locator('.rcp-side')
    await paso(page, reloj, `${nombre}: posiciones sugeridas`, c(8), async () => {
      await clicEn(page, det.getByRole('button', { name: 'Usar posiciones sugeridas' }))
      await expect(page.getByText(new RegExp(`Se asignó posición a ${lineas} línea`))).toBeVisible()
    })
    await paso(page, reloj, `${nombre}: confirmar`, c(9), async () => {
      await clicEn(page, det.getByRole('button', { name: 'Confirmar recibo' }))
      await clicEn(page, dlg().getByRole('button', { name: 'Confirmar recibo' }))
      await expect(page.getByText(/Recibo REC-\d+ confirmado/)).toBeVisible()
      await taparColumna(page, 'Costo unitario')
    })
    if (lento) await shot('confirmado')
  }

  await page.goto('/')
  await paso(page, reloj, 'Abrir Recibo', { l: 1 }, async () => {
    await page.waitForTimeout(1500)
    const grp = page.locator('button.grp-h', { hasText: 'Almacén' })
    if ((await grp.getAttribute('aria-expanded')) !== 'true') await clicEn(page, grp)
    await clicEn(page, page.getByRole('link', { name: 'Recibo', exact: true }))
    await expect(page.getByRole('heading', { name: 'Recibo', level: 1 })).toBeVisible()
  })
  await recibir(/PO-\d+ · ADVANCE LOGISTICS/, 'Primera', 2, true)
  await recibir(/PO-\d+ · PADIMONT/, 'Segunda', 4, false)
  modo.rapido = false

  await paso(page, reloj, 'Compras: Recibida', { l: 12 }, async () => {
    await page.goto('/warehouse/purchase-orders')
    await expect(page.getByRole('table', { name: 'Órdenes de compra' }).getByText('Recibida')).toHaveCount(2)
    await señalar(page, page.getByRole('table', { name: 'Órdenes de compra' }), 3500)
  })
  await shot('compras')
  await paso(page, reloj, 'Productos: existencia', { l: 13 }, async () => {
    await page.goto('/warehouse/products')
    await expect(page.getByRole('group', { name: 'Resumen de la lista' }).getByText('…')).toHaveCount(0)
    await señalar(page, page.getByRole('group', { name: 'Resumen de la lista' }), 4000)
  })
  await shot('existencia')
  await finEscena(page, reloj, 'E03-recibo')
  await ctx.close()
})
