// Compañía nueva y sus datos (2026-10-26) con el API real, como administrador de la plataforma: el botón «Nueva compañía» en Ajustes, el alta con lo base (nombre, teléfono, domicilio, dirección
// postal distinta, subdominio), la entrada automática a la compañía nueva con los módulos base (sin transporte ni almacén), editar teléfono y direcciones, dominio, y desactivar / activar
// (el selector de compañías la marca «desactivada»). Capturas: docs/manual/frontend/img/compania-<paso>.png.
import { expect, test, type Page } from '@playwright/test'
import { LETTERS, shot } from './staffHelpers'

const PLATFORM = { email: process.env.TEIKEM_PLATFORM_EMAIL ?? 'teikem+support@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

async function loginAsPlatform(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(PLATFORM.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(PLATFORM.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login', { timeout: 30_000 })
  if (new URL(page.url()).pathname === '/select-tenant') await page.locator('.tenant-list button').first().click()
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
}

test('Nueva compañía desde Ajustes: alta con lo base, entra a ella, se edita y se desactiva y activa', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'Recorrido largo con capturas: escritorio.')
  const name = `Compañía E2E ${LETTERS}`
  await page.setViewportSize({ width: 1366, height: 900 })
  await loginAsPlatform(page)
  await page.goto('/system/settings')

  // el botón solo lo ve la plataforma; el nombre de la compañía actual es editable para ella
  await expect(page.getByTestId('new-company')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByLabel(/^Nombre/)).toBeEnabled()
  await expect(page.getByText('Solo la plataforma').first()).toBeVisible()

  // ---- alta: primero lo obligatorio
  await page.getByTestId('new-company').click()
  const dialog = page.getByRole('dialog')
  await dialog.getByTestId('new-company-save').click()
  await expect(dialog.getByText('El nombre es obligatorio.')).toBeVisible()
  await expect(dialog.getByText('El teléfono de la compañía es obligatorio.')).toBeVisible()
  await expect(dialog.getByText('La dirección (línea 1) es obligatoria.')).toBeVisible()

  await dialog.getByTestId('new-company-name').fill(name)
  await dialog.getByLabel(/^Teléfono/).fill('787-555-0100')
  await dialog.getByLabel(/Dirección \(línea 1\)/).fill('Calle Sol 123')
  await dialog.getByLabel(/Ciudad/).fill('San Juan')
  await dialog.getByRole('switch').click()   // dirección postal distinta
  await dialog.getByLabel(/Dirección \(línea 1\)/).nth(1).fill('PO Box 99')
  await dialog.getByLabel(/Ciudad/).nth(1).fill('Ponce')
  await dialog.getByLabel('Subdominio').fill(`e2e${LETTERS.toLowerCase()}`)
  await shot(page, false, 'compania-nueva', 1100, dialog)
  await dialog.getByTestId('new-company-save').click()
  // crear una compañía exige una reautenticación reciente: si hace falta, el sistema pide la contraseña y sigue solo
  const reauth = page.getByRole('dialog').filter({ hasText: 'Confirme su identidad' })
  await reauth.locator('#reauth-password').fill(PLATFORM.password)
  await reauth.getByRole('button', { name: 'Confirmar' }).click()

  // ---- entra solo a la compañía nueva, en Ajustes
  await expect(page.getByTestId('new-company-name')).toHaveCount(0, { timeout: 60_000 })   // la ventana se cerró: la compañía se creó
  await expect(page.getByLabel(/^Nombre/)).toHaveValue(name, { timeout: 30_000 })
  await expect(page.getByLabel('Teléfono')).toHaveValue('787-555-0100')
  await expect(page.getByLabel(/Dirección \(línea 1\)/).first()).toHaveValue('Calle Sol 123')
  await expect(page.getByLabel(/Dirección \(línea 1\)/).nth(1)).toHaveValue('PO Box 99')
  await expect(page.getByLabel('Subdominio')).toHaveValue(`e2e${LETTERS.toLowerCase()}`)
  await shot(page, false, 'compania-general', 1500)

  // módulos base: el menú no trae transporte ni almacén
  await expect(page.getByRole('link', { name: 'Almacén' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Envíos' })).toHaveCount(0)

  // ---- editar: teléfono y volver la postal a «la misma»
  await page.getByLabel('Teléfono').fill('(787) 555 0199')
  await page.getByRole('switch').click()   // postal: la misma que el domicilio
  await page.getByRole('button', { name: 'Guardar cambios' }).first().click()
  await expect(page.getByText('Ajustes guardados').first()).toBeVisible()
  await page.reload()
  await expect(page.getByLabel('Teléfono')).toHaveValue('(787) 555 0199')
  await expect(page.getByLabel(/Dirección \(línea 1\)/)).toHaveCount(1)   // sin dirección postal propia

  // un teléfono corto es 400 debajo del campo
  await page.getByLabel('Teléfono').fill('123')
  await page.getByRole('button', { name: 'Guardar cambios' }).first().click()
  await expect(page.getByText(/El teléfono debe tener entre 7 y 15 dígitos/)).toBeVisible()
  await page.getByLabel('Teléfono').fill('(787) 555 0199')

  // ---- desactivar y activar
  await page.getByTestId('company-active-toggle').click()
  await shot(page, false, 'compania-desactivar', 900, page.getByRole('dialog'))
  await page.getByRole('dialog').getByRole('button', { name: 'Desactivar compañía' }).click()
  await expect(page.getByTestId('company-status')).toContainText('DESACTIVADA', { timeout: 30_000 })
  await expect(page.locator('select[aria-label="Compañía"] option', { hasText: `${name} (desactivada)` })).toHaveCount(1)
  await shot(page, false, 'compania-desactivada', 1500)
  await page.getByTestId('company-active-toggle').click()
  await page.getByRole('dialog').getByRole('button', { name: 'Activar compañía' }).click()
  await expect(page.getByTestId('company-status')).toContainText('activa', { timeout: 30_000 })
  await expect(page.locator('select[aria-label="Compañía"] option', { hasText: `${name} (desactivada)` })).toHaveCount(0)
})
