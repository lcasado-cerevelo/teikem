// Escena 1 — Productos e inventario (ENSAYO: tiempos objetivo provisionales; se ajustan al audio de Luis).
// Corre con: npx playwright test --config e2e/video.config.ts E01 --workers=1   (API en :5733 sobre Teikem_video, sin correo; Vite en :5174)
import { test } from '@playwright/test'
import { clicEn, escribir, nuevoReloj, paso, finEscena, prepararCursor, señalar, tokensFueraDeCamara, ponerSesion, expect } from '../ayudas'

const ZOOM = Number(process.env.VIDEO_ZOOM ?? '1.25')
// zoom de navegador de verdad (no CSS): el viewport en px CSS se reduce y el video sale a 1920×1080
test.use({ launchOptions: { args: [`--force-device-scale-factor=${ZOOM}`] } })

test('E01 - productos e inventario', async ({ browser, baseURL }) => {
  const tokens = await tokensFueraDeCamara(2)  // Advance Solutions
  const ctx = await browser.newContext({
    baseURL: baseURL!, locale: 'es-PR',
    viewport: { width: Math.round(1920 / ZOOM), height: Math.round(1080 / ZOOM) },
    deviceScaleFactor: ZOOM,
    recordVideo: { dir: process.env.VIDEO_OUT ?? 'F:/Download/TeikemVideo/solutions-proceso/tomas/_pw', size: { width: 1920, height: 1080 } },
  })
  const tPagina = Date.now()
  const page = await ctx.newPage()
  await ponerSesion(page, tokens)
  await prepararCursor(page)
  const reloj = nuevoReloj('E01-productos', tPagina)

  await page.goto('/')   // la frase 1 se dice sobre el Pulso del día
  await paso(page, reloj, 'Abrir la pantalla', { l: 2 }, async () => {
    await page.goto('/warehouse/products')
    await expect(page.getByRole('heading', { name: 'Productos e inventario', level: 1 })).toBeVisible()
    await expect(page.getByRole('group', { name: 'Resumen de la lista' })).toBeVisible()
    await expect(page.getByRole('group', { name: 'Resumen de la lista' }).getByText('…')).toHaveCount(0)
  })

  await paso(page, reloj, 'Indicadores', { l: 4 }, async () => {
    await señalar(page, page.getByRole('group', { name: 'Resumen de la lista' }), 5000)
  })

  await paso(page, reloj, 'Balance: Disponible/Reservado/Total', { l: 5 }, async () => {
    await señalar(page, page.getByRole('columnheader', { name: 'Disponible' }), 1500)
    await señalar(page, page.getByRole('columnheader', { name: 'Total' }).first(), 1200)
  })
  await paso(page, reloj, 'Fila del monitor', { l: 6 }, async () => {
    await señalar(page, page.locator('tr', { hasText: '070120' }).first(), 4500)
  })

  await paso(page, reloj, 'Buscar', { l: 7 }, async () => {
    await clicEn(page, page.getByText('SKU o nombre').first())
    const caja = page.getByPlaceholder(/buscar|SKU o nombre/i).last()
    await escribir(caja, 'PRODIGY')
    await page.waitForTimeout(1200)
  })
  await paso(page, reloj, 'Elegir 070120', { l: 8 }, async () => {
    await clicEn(page, page.getByRole('checkbox', { name: /070120/ }))
    await page.keyboard.press('Escape')
    await expect(page.locator('tbody tr')).toHaveCount(1)
  })

  await paso(page, reloj, 'Limpiar', { l: 9 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Limpiar' }))
    await expect(page.locator('tbody tr').nth(5)).toBeVisible()
  })

  await paso(page, reloj, 'Abrir producto', { l: 10 }, async () => {
    await clicEn(page, page.locator('tbody tr', { hasText: '070120' }).first().getByText('PRODIGY MONITOR 1EA.'), { resaltar: false })
    await expect(page.getByRole('dialog').getByText('Editar producto')).toBeVisible()
  })
  await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E01-editor.png' })

  await paso(page, reloj, 'Dónde está', { l: 12 }, async () => {
    await clicEn(page, page.getByText('Dónde está (existencia por posición)'))
    await expect(page.getByRole('dialog').getByText('PISO').first()).toBeVisible()
  })
  await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E01-donde.png' })

  await paso(page, reloj, 'Editar reorden', { l: 13 }, async () => {
    const campo = page.getByRole('dialog').getByLabel('Punto de reorden')
    await clicEn(page, campo)
    await campo.press('Control+A')
    await escribir(campo, '900')
    console.log('reorden=', await campo.inputValue())
  })
  await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E01-antes-guardar.png' })
  await paso(page, reloj, 'Guardar', { l: 14 }, async () => {
    const g = page.getByRole('dialog').getByRole('button', { name: 'Guardar' })
    await g.scrollIntoViewIfNeeded(); await page.waitForTimeout(600)
    console.log('guardar box', JSON.stringify(await g.boundingBox()), 'vp', JSON.stringify(page.viewportSize()), 'dlg', JSON.stringify(await page.getByRole('dialog').boundingBox()))
    await clicEn(page, page.getByRole('dialog').getByRole('button', { name: 'Guardar' }))
    await page.waitForTimeout(1200)
    await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E01-tras-guardar.png' })
    console.log('toasts=', JSON.stringify(await page.locator('.toast, [role=status], [role=alert]').allInnerTexts()))
    await expect(page.getByText('Producto actualizado.')).toBeVisible()
  })

  await paso(page, reloj, 'Nuevo producto', { l: 15 }, async () => {
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await clicEn(page, page.getByRole('button', { name: 'Nuevo producto' }))
    await expect(page.getByRole('dialog').getByText('Nuevo producto').first()).toBeVisible()
  })
  await paso(page, reloj, 'Datos del nuevo', { l: 16 }, async () => {
    const d = page.getByRole('dialog')
    const sku = d.getByRole('textbox', { name: 'SKU', exact: true })
    await clicEn(page, sku); await escribir(sku, '151515')
    const nombre = d.getByRole('textbox', { name: 'Nombre', exact: true })
    await clicEn(page, nombre); await escribir(nombre, 'CAJA CORRUGADA 15 X 15 X 15')
    const cat = d.getByRole('combobox', { name: 'Categoría' })
    await clicEn(page, cat); await escribir(cat, 'BOXES'); await page.keyboard.press('Enter')
    const costo = d.getByLabel('Costo de compra'); await costo.evaluate((e) => ((e as HTMLElement).style.filter = 'blur(7px)')); await clicEn(page, costo); await escribir(costo, '0.62')
    const precio = d.getByLabel('Precio de venta'); await clicEn(page, precio); await escribir(precio, '1.50')
  })
  await page.screenshot({ path: 'F:/Download/TeikemVideo/solutions-proceso/cuadros/E01-nuevo.png' })
  await paso(page, reloj, 'Guardar nuevo', { l: 17 }, async () => {
    await clicEn(page, page.getByRole('dialog').getByRole('button', { name: 'Guardar' }))
    await expect(page.getByText('Producto creado.')).toBeVisible()
  })

  await finEscena(page, reloj, 'E01-productos')
  await ctx.close()
})
