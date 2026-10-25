// Capturas del manual que faltaban (2026-10-24): f6-almacen-plataformas (ficha del almacén, pestaña Plataformas/«Docks» con
// «Cambiar estatus») y f8a-movil-listas (Listas de valores en el teléfono). Escritorio: la del muelle; móvil de 360 px: la de listas.
// Con MANUAL_LANG=en salen en inglés en MANUAL_IMG_DIR; sin ella, en español en docs/manual/frontend/img.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import { login } from './staffHelpers'

test.use({ locale: 'es-PR' })
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await page.mouse.move(0, 0)
  mkdirSync(IMG_DIR, { recursive: true })
  const target = await manualTarget(page)
  await manualLang(page, 'en', name)
  await target.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
}

test('escritorio: ficha del almacén, pestaña Plataformas con Cambiar estatus', async ({ page, isMobile }) => {
  test.skip(isMobile, 'solo escritorio')
  await login(page)
  await page.goto('/warehouse/warehouses')
  const row = page.getByRole('row').filter({ hasText: 'ALM-01' })
  await row.getByText('ALM-01', { exact: true }).click()
  await page.getByRole('button', { name: 'Editar almacén' }).click()
  await expect(page).toHaveURL(/\/warehouse\/warehouses\/[0-9a-f-]+$/)
  await page.getByRole('tab', { name: 'Plataformas' }).click()
  const table = page.getByRole('table', { name: 'Plataformas' })
  await expect(table).toBeVisible()
  await expect(table.getByRole('row').nth(1)).toBeVisible()
  await shot(page, 'f6-almacen-plataformas')
})

test('móvil: Listas de valores con dominios como selector y valores como tarjetas', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'solo móvil')
  await login(page)
  await page.goto('/system/catalogs')
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
  const sel = page.locator('select').first()
  await sel.selectOption({ label: 'Tipo de tarea' })
  await expect(page.getByText('PUTAWAY').first()).toBeVisible()
  await shot(page, 'f8a-movil-listas')
})
