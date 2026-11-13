// Escena 7 — Análisis de ventas (ENSAYO). Parte del estado E06p (datos de P07 ya cargados).
import { test } from '@playwright/test'
import { clicEn, nuevoReloj, paso, finEscena, prepararCursor, señalar, tokensFueraDeCamara, ponerSesion, expect } from '../ayudas'

const ZOOM = Number(process.env.VIDEO_ZOOM ?? '1.25')
test.use({ launchOptions: { args: [`--force-device-scale-factor=${ZOOM}`] } })

test('E07 - análisis de ventas', async ({ browser, baseURL }) => {
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
  const reloj = nuevoReloj('E07-analisis', tPagina)
  const shot = (n: string) => page.screenshot({ path: `F:/Download/TeikemVideo/solutions-proceso/cuadros/E07-${n}.png` })

  await page.goto('/')
  await paso(page, reloj, 'Entrar al análisis', { l: 1 }, async () => {
    await page.waitForTimeout(1500)
    const grp = page.locator('button.grp-h', { hasText: 'Análisis' })
    if ((await grp.getAttribute('aria-expanded')) !== 'true') await clicEn(page, grp)
    await clicEn(page, page.getByRole('link', { name: 'Análisis de ventas' }))
    await expect(page.getByRole('heading', { name: 'Análisis de ventas', level: 1 })).toBeVisible()
    await expect(page.getByRole('group', { name: 'Cifras del mes' })).toBeVisible({ timeout: 30_000 })
  })
  await paso(page, reloj, 'Tarjetas del mes', { l: 4 }, async () => { await señalar(page, page.getByRole('group', { name: 'Cifras del mes' }), 4500) })
  await shot('tarjetas')
  await paso(page, reloj, 'Comparativo', { l: 5 }, async () => {
    const t = page.getByRole('table', { name: 'Comparativo por ruta y representante' })
    await señalar(page, t, 6500)
  })
  await shot('comparativo')
  await paso(page, reloj, 'Conciliación', { l: 7 }, async () => { await señalar(page, page.getByRole('table', { name: 'Totales y conciliación' }), 4000) })
  await paso(page, reloj, 'Movimiento de productos', { l: 8 }, async () => {
    await clicEn(page, page.getByRole('tab', { name: 'Movimiento de productos' }))
    await expect(page.getByRole('group', { name: 'Cifras del rango' })).toBeVisible({ timeout: 30_000 })
    await señalar(page, page.getByRole('group', { name: 'Cifras del rango' }), 3500)
  })
  await paso(page, reloj, 'Cobertura', { l: 10 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Inventario y cobertura' }))
    await señalar(page, page.getByRole('columnheader', { name: /Semanas de cobertura/ }), 3000)
  })
  await paso(page, reloj, 'Reorden', { l: 11 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Reorden' }))
    await señalar(page, page.getByRole('columnheader', { name: /Punto de reorden/ }).first(), 3000)
  })
  await shot('cobertura')
  await paso(page, reloj, 'Gráficos', { l: 12 }, async () => {
    await clicEn(page, page.getByRole('tab', { name: 'Gráficos' }))
    const reg = page.getByRole('region', { name: 'Presentación de ventas' })
    await expect(reg).toBeVisible({ timeout: 30_000 })
    await señalar(page, page.getByText('Venta por mes', { exact: false }).first(), 3500)
  })
  await paso(page, reloj, 'Venta contra cuota', { l: 13 }, async () => {
    await señalar(page, page.getByText('Venta contra cuota por representante', { exact: false }).first(), 3500)
  })
  await paso(page, reloj, 'Participación por línea', { l: 14 }, async () => {
    await señalar(page, page.getByText('Participación por línea', { exact: false }).first(), 3500)
  })
  await shot('graficos')
  await paso(page, reloj, 'Cuotas', { l: 15 }, async () => {
    await clicEn(page, page.getByRole('tab', { name: 'Configuración' }))
    await clicEn(page, page.getByRole('tab', { name: 'Cuotas' }))
    await expect(page.getByText(/Cuota de .* en /).first().or(page.getByLabel(/Cuota de/).first())).toBeVisible({ timeout: 20_000 })
    await page.waitForTimeout(2500)
  })
  await shot('cuotas')
  await paso(page, reloj, 'Importar y simular', { l: 16 }, async () => {
    await clicEn(page, page.getByRole('tab', { name: 'Importar historial' }))
    await page.getByLabel('Reporte de ventas (Sales by Item Detail, CSV)').setInputFiles('F:/Download/TeikemVideo/solutions-proceso/datos/ventas-demo.csv')
  })
  await paso(page, reloj, 'Simular', { l: 17 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Simular' }))
    await expect(page.getByText(/no se guardó nada/i).first()).toBeVisible({ timeout: 60_000 })
    const inf = page.getByRole('heading', { name: 'Simulación' }).first()
    await inf.scrollIntoViewIfNeeded()
    await page.mouse.wheel(0, 380)
    await page.waitForTimeout(2500)
  })
  await shot('simulacion')
  await paso(page, reloj, 'Cierre', { l: 18 }, async () => {
    await clicEn(page, page.getByRole('tab', { name: 'Por representante' }))
  })
  await finEscena(page, reloj, 'E07-analisis')
  await ctx.close()
})
