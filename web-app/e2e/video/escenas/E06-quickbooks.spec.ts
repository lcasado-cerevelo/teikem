// Escena 6 — Facturar y exportar a QuickBooks (ENSAYO). Parte del estado E05.
import { copyFileSync, mkdirSync } from 'node:fs'
import { test } from '@playwright/test'
import { clicEn, modo, escribir, nuevoReloj, paso, finEscena, prepararCursor, señalar, tokensFueraDeCamara, ponerSesion, expect } from '../ayudas'
import { ADMIN } from '../../staffHelpers'

const ZOOM = Number(process.env.VIDEO_ZOOM ?? '1.25')
test.use({ launchOptions: { args: [`--force-device-scale-factor=${ZOOM}`] } })

test('E06 - facturar y exportar a QuickBooks', async ({ browser, baseURL }) => {
  const tokens = await tokensFueraDeCamara(2)
  const ctx = await browser.newContext({
    baseURL: baseURL!, locale: 'es-PR', deviceScaleFactor: ZOOM, acceptDownloads: true,
    viewport: { width: Math.round(1920 / ZOOM), height: Math.round(1080 / ZOOM) },
    recordVideo: { dir: process.env.VIDEO_OUT ?? 'F:/Download/TeikemVideo/solutions-proceso/tomas/_pw', size: { width: 1920, height: 1080 } },
  })
  const tPagina = Date.now()
  const page = await ctx.newPage()
  await ponerSesion(page, tokens)
  await prepararCursor(page)
  const reloj = nuevoReloj('E06-quickbooks', tPagina)
  modo.rapido = true   // las acciones deben ir al paso de la voz: sin esperas de más
  const shot = (n: string) => page.screenshot({ path: `F:/Download/TeikemVideo/solutions-proceso/cuadros/E06-${n}.png` })

  await page.goto('/')   // la frase 1 se dice sobre el Pulso
  await paso(page, reloj, 'Facturación', { l: 2 }, async () => {
    await page.goto('/money/billing')
    await expect(page.getByRole('heading', { name: 'Facturación', level: 1 })).toBeVisible()
  })
  await paso(page, reloj, 'Nueva corrida', { l: 2 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Nueva corrida' }))
    const form = page.getByRole('dialog', { name: 'Nueva corrida' })
    const cb = form.getByRole('combobox').first()
    await clicEn(page, cb); await escribir(cb, 'FARMACIA LA ESP')
    await clicEn(page, page.getByRole('option').filter({ hasText: 'FARMACIA LA ESPERANZA' }).first())
    await señalar(page, form, 700)
    await clicEn(page, form.getByRole('button', { name: 'Generar' }))
  })
  const run = page.getByRole('dialog', { name: /^Corrida #/ })
  await paso(page, reloj, 'La corrida', { l: 4 }, async () => {
    await expect(run).toContainText('FARMACIA LA ESPERANZA', { timeout: 30_000 })
    await señalar(page, run, 2000)
  })
  await shot('corrida')
  await paso(page, reloj, 'Revisar y aprobar', { l: 5 }, async () => {
    await clicEn(page, run.getByRole('button', { name: 'Marcar como revisada' }))
    await expect(run).toContainText(/Revisada/)
    await clicEn(page, run.getByRole('button', { name: 'Aprobar y emitir' }))
    const dialog = page.getByRole('dialog', { name: 'Confirme su identidad' })
    await dialog.waitFor({ state: 'visible', timeout: 15_000 })
    const pwd = dialog.getByLabel('Contraseña', { exact: true })
    await clicEn(page, pwd); await escribir(pwd, ADMIN.password)
    await clicEn(page, dialog.getByRole('button', { name: 'Confirmar' }))
    await expect(run).toContainText(/Aprobada/, { timeout: 30_000 })
  })
  await shot('aprobada')
  await paso(page, reloj, 'Ir a QuickBooks', { l: 8 }, async () => {
    await clicEn(page, run.getByRole('button', { name: 'Cerrar' }).first())
    await page.goto('/sales-orders?tab=quickbooks')
    await expect(page.getByRole('button', { name: 'Exportar a QuickBooks' })).toBeVisible()
    await señalar(page, page.locator('tr, .dt-cards > li').filter({ hasText: 'FARMACIA LA ESPERANZA' }).first(), 2000)
  })
  await shot('lista')
  await paso(page, reloj, 'Exportar', { l: 10 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Exportar a QuickBooks' }))
    const ex = page.getByRole('dialog', { name: 'Exportar a QuickBooks' })
    await señalar(page, ex, 1200)
    const [download] = await Promise.all([page.waitForEvent('download', { timeout: 60_000 }), clicEn(page, ex.getByRole('button', { name: 'Exportar y bajar el archivo' }))])
    mkdirSync('F:/Download/TeikemVideo/solutions-proceso/datos', { recursive: true })
    copyFileSync(await download.path(), 'F:/Download/TeikemVideo/solutions-proceso/datos/export-quickbooks.iif')
    await expect(page.getByText(/Se bajó el archivo/).first()).toBeVisible()
  })
  await shot('exportado')
  await paso(page, reloj, 'Ya importé', { l: 13 }, async () => {
    const ask = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: /^¿Ya importaste estas \d+\?/ }) })
    await señalar(page, ask, 2000)
    await clicEn(page, ask.getByRole('button', { name: /^Sí, ya están/ }))
  })
  await shot('final')
  await finEscena(page, reloj, 'E06-quickbooks')
  await ctx.close()
})
