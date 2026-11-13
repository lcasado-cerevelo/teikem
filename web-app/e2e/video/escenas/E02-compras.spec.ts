// Escena 2 — Importar compras de QuickBooks (ENSAYO). Parte del estado E00.
import { test } from '@playwright/test'
import { clicEn, nuevoReloj, paso, finEscena, prepararCursor, señalar, tokensFueraDeCamara, ponerSesion, expect } from '../ayudas'

const ZOOM = Number(process.env.VIDEO_ZOOM ?? '1.25')
const ARCHIVO = 'F:/Download/TeikemVideo/solutions-proceso/datos/compras-quickbooks-video.csv'
test.use({ launchOptions: { args: [`--force-device-scale-factor=${ZOOM}`] } })

test('E02 - compras de QuickBooks', async ({ browser, baseURL }) => {
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
  const reloj = nuevoReloj('E02-compras', tPagina)
  const dlg = () => page.getByRole('dialog')

  await page.goto('/')
  await paso(page, reloj, 'Abrir Compras', { l: 2 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Contabilidad' }))
    await clicEn(page, page.getByRole('link', { name: 'Compras', exact: true }))
    await expect(page.getByRole('heading', { name: 'Órdenes de compra', level: 1 })).toBeVisible()
  })
  await paso(page, reloj, 'Importar', { l: 4 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Importar', exact: true }))
    await expect(dlg().getByText('Importar órdenes de compra').first()).toBeVisible()
  })
  await paso(page, reloj, 'Origen', { l: 5 }, async () => {
    await señalar(page, dlg().getByLabel('Origen del archivo'), 2500)
  })
  await paso(page, reloj, 'Estatus Completada', { l: 6 }, async () => {
    await dlg().getByLabel('Estatus de las órdenes importadas').selectOption('SENT')
    await señalar(page, dlg().getByLabel('Estatus de las órdenes importadas'), 2500)
  })
  await paso(page, reloj, 'Elegir archivo', { l: 8 }, async () => {
    const [fc] = await Promise.all([page.waitForEvent('filechooser'), clicEn(page, dlg().locator('label.cs-pick'))])
    await fc.setFiles(ARCHIVO)
    await expect(dlg().getByText(/compras-quickbooks-video/)).toBeVisible()
  })
  await paso(page, reloj, 'Revisar', { l: 9 }, async () => {
    await clicEn(page, dlg().getByRole('button', { name: 'Revisar' }))
    await expect(dlg().getByTestId('poi-summary')).toContainText('2 órdenes en el archivo')
  })
  await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E02-revisar.png' })
  await paso(page, reloj, 'Importar 2', { l: 12 }, async () => {
    await clicEn(page, dlg().getByRole('button', { name: 'Importar 2' }))
    await expect(dlg().locator('tr[data-status="CREATED"]')).toHaveCount(2)
  })
  await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E02-creadas.png' })
  await paso(page, reloj, 'Cerrar y lista', { l: 14 }, async () => {
    await clicEn(page, dlg().locator('footer').getByRole('button', { name: 'Cerrar' }))
    await expect(page.getByRole('table', { name: 'Órdenes de compra' }).getByRole('row')).toHaveCount(3)
    await señalar(page, page.getByRole('table', { name: 'Órdenes de compra' }), 3500)
  })
  await paso(page, reloj, 'Abrir una orden', { l: 15 }, async () => {
    await clicEn(page, page.getByRole('table', { name: 'Órdenes de compra' }).getByRole('row').filter({ hasText: 'PADIMONT' }), { resaltar: false })
    await expect(page.getByText(/PO-\d+ · PADIMONT/).first()).toBeVisible()
    // costos de compra: se tapan en pantalla (regla del guion)
    await page.getByLabel('Costo unitario').evaluateAll((els) => els.forEach((e) => ((e as HTMLElement).style.filter = 'blur(7px)')))
    await señalar(page, page.getByLabel('Notas').first(), 2500)
  })
  await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E02-ficha.png' })
  await finEscena(page, reloj, 'E02-compras')
  await ctx.close()
})
