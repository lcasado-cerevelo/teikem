// Escena 5 — Almacén: Recolección y empaque y salida de la orden (ENSAYO). Parte del estado E04. Usa «Despachar» de la ficha (Lote 242).
import { test } from '@playwright/test'
import { clicEn, modo, escribir, nuevoReloj, paso, finEscena, prepararCursor, señalar, taparColumna, tokensFueraDeCamara, ponerSesion, expect } from '../ayudas'

const ZOOM = Number(process.env.VIDEO_ZOOM ?? '1.25')
test.use({ launchOptions: { args: [`--force-device-scale-factor=${ZOOM}`] } })

test('E05 - recolección y salida', async ({ browser, baseURL }) => {
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
  const reloj = nuevoReloj('E05-despacho', tPagina)
  modo.rapido = true
  const shot = (n: string) => page.screenshot({ path: `F:/Download/TeikemVideo/solutions-proceso/cuadros/E05-${n}.png` })
  const fila = () => page.locator('tr, .dt-cards > li').filter({ hasText: /OV-\d+/ }).filter({ hasText: 'Solicitada' }).first()

  await page.goto('/')
  await paso(page, reloj, 'Abrir Recolección y empaque', { l: 1 }, async () => {
    await page.waitForTimeout(1500)
    const grp = page.locator('button.grp-h', { hasText: 'Almacén' })
    if ((await grp.getAttribute('aria-expanded')) !== 'true') await clicEn(page, grp)
    await clicEn(page, page.getByRole('link', { name: 'Recolección y empaque' }))
    await expect(page.getByRole('heading', { name: 'Recolección y empaque', level: 1 })).toBeVisible()
    await expect(fila()).toBeVisible()
  })
  await shot('lista')
  await paso(page, reloj, 'Señalar la orden', { l: 3 }, async () => { await señalar(page, fila(), 1800) })
  await paso(page, reloj, 'Abrir la ficha', { l: 4 }, async () => {
    await clicEn(page, fila(), { resaltar: false })
    await expect(page.getByRole('dialog').getByText('Lo que hay que recolectar')).toBeVisible()
    await señalar(page, page.getByRole('dialog').getByText('Lo que hay que recolectar'), 2500)
  })
  await shot('ficha')
  await paso(page, reloj, 'Despachar', { l: 6 }, async () => {
    await clicEn(page, page.getByRole('dialog').getByRole('button', { name: 'Despachar', exact: true }))
    await expect(page.getByRole('dialog', { name: /Despachar la orden/ })).toBeVisible()
    await señalar(page, page.getByRole('dialog', { name: /Despachar la orden/ }), 1500)
  })
  await shot('despachar')
  await paso(page, reloj, 'Confirmar y retiro', { l: 8 }, async () => {
    await clicEn(page, page.getByRole('dialog', { name: /Despachar la orden/ }).getByRole('button', { name: 'Despachar', exact: true }))
    await expect(page.getByRole('dialog', { name: /^Despacho EMP-/ })).toBeVisible()
    await señalar(page, page.getByRole('dialog', { name: /^Despacho EMP-/ }), 1200)
  })
  await paso(page, reloj, 'Retiro', { l: 9 }, async () => {
    await clicEn(page, page.getByRole('dialog', { name: /^Despacho EMP-/ }).getByRole('button', { name: /Retiro/ }))
  })
  await shot('retiro')
  await paso(page, reloj, 'Documento despachado', { l: 10 }, async () => {
    await page.goto('/warehouse/pick-batches')
    const f = page.locator('tr, .dt-cards > li').filter({ hasText: /OV-\d+/ }).filter({ hasText: 'Despachada' }).first()
    await expect(f).toBeVisible()
    await señalar(page, f, 2500)
  })
  await shot('despachada')
  await paso(page, reloj, 'Kárdex', { l: 11 }, async () => {
    await page.goto('/warehouse/kardex')
    await expect(page.locator('tr').filter({ hasText: /Despacho/ }).first()).toBeVisible()
    await taparColumna(page, 'Costo')
    await señalar(page, page.locator('tr').filter({ hasText: /Despacho/ }).first(), 3000)
  })
  await shot('kardex')
  await finEscena(page, reloj, 'E05-despacho')
  await ctx.close()
})
