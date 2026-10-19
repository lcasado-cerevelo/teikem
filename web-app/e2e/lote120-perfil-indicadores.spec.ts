// Recorrido de «Mi perfil» e «Indicadores» del portal del cliente (Lote 120, P10) en la app del cliente contra el API real: los indicadores del Inicio con datos reales (órdenes creadas y entregadas, en curso, intentos), el cambio de
// período; el perfil (mi nombre se cambia y queda en el API; mi correo y los datos de mi empresa son solo lectura) y el cambio de contraseña con sus validaciones.
//
// Datos propios por API: un cliente con persona administradora (MFA y documento legal ya hechos), tres órdenes (una entregada por el personal con fecha prometida de hoy, una en tránsito y una Sometida).
// Escritorio y móvil. Capturas (solo escritorio): docs/manual/frontend/img/l120-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, loginAsClientAdmin, shot } from './portalHelpers'
import { createOrder, scan } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Portal del cliente: indicadores con datos reales, mi perfil y el cambio de contraseña', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, 'Perfil 120')
  const loc = await c.locations.post(`Tienda ${suffix}`)

  // tres órdenes: entregada por el personal, en tránsito y Sometida
  const delivered = await createOrder(request, c.staff, c.client.publicId, loc.publicId, 1)
  await scan(request, c.staff, 'DEPART', delivered.pieceNumbers[0])
  const done = await request.post(`${API_URL}/api/v1/orders/${delivered.publicId}/delivery`, { headers: c.staff, data: { recipientName: `Ana ${suffix}` } })
  expect(done.ok(), await done.text()).toBeTruthy()
  const transit = await createOrder(request, c.staff, c.client.publicId, loc.publicId, 1)
  await scan(request, c.staff, 'DEPART', transit.pieceNumbers[0])
  await createOrder(request, c.staff, c.client.publicId, loc.publicId, 1)

  // por API: los números de los 30 días de ESTE cliente
  const ind = (await (await request.get(`${API_URL}/api/v1/portal/indicators?days=30`, { headers: c.clientApi })).json()) as { created: number; delivered: number; openNow: number; inTransitNow: number }
  expect(ind).toMatchObject({ created: 3, delivered: 1, openNow: 2, inTransitNow: 1 })
  expect((await request.get(`${API_URL}/api/v1/portal/indicators?days=15`, { headers: c.clientApi })).status()).toBe(400)

  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)

  // 1) los indicadores en el Inicio
  await expect(page.getByTestId('ind-created')).toHaveText('3', { timeout: 30_000 })
  await expect(page.getByTestId('ind-delivered')).toHaveText('1')
  await expect(page.getByTestId('ind-open')).toContainText('2 · 1 en tránsito')
  await expect(page.getByTestId('ind-failed')).toHaveText('0')
  await shot(page, isMobile, 'l120-indicadores')
  await page.getByTestId('indicators-days-7').click()
  await expect(page.getByTestId('ind-created')).toHaveText('3')                      // todo es de esta semana
  await page.getByTestId('indicators-days-90').click()
  await expect(page.getByTestId('ind-created')).toHaveText('3')

  // 2) mi perfil: mi nombre se cambia; mi correo y mi empresa son de solo lectura
  await page.goto(`${APP_URL}/settings`)
  await page.getByTestId('settings-profile').click()
  await expect(page.getByRole('heading', { name: 'Mi perfil' }).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(`Correo: ${c.adminMail}`)).toBeVisible()
  await expect(page.getByText(`Perfil 120 ${suffix}`).last()).toBeVisible()            // los datos de mi empresa
  await expect(page.getByText(/^Código: /)).toBeVisible()
  await expect(page.getByText(/Estos datos los cambia la compañía/)).toBeVisible()
  await expect(page.getByTestId('profile-save')).toBeDisabled()
  await shot(page, isMobile, 'l120-perfil')
  await page.getByTestId('profile-name').fill(`Ana María ${suffix}`)
  await page.getByTestId('profile-save').click()
  await expect(page.getByText('Su nombre quedó guardado.')).toBeVisible({ timeout: 30_000 })
  const me = (await (await request.get(`${API_URL}/api/v1/portal/profile`, { headers: c.clientApi })).json()) as { fullName: string; email: string; company: { name: string } }
  expect(me.fullName).toBe(`Ana María ${suffix}`)
  expect(me.email).toBe(c.adminMail)                                                    // el correo no cambia
  expect((await request.put(`${API_URL}/api/v1/portal/profile`, { headers: c.clientApi, data: { fullName: '   ' } })).status()).toBe(400)

  // 3) la contraseña: las validaciones de la pantalla, el rechazo del servidor y el cambio
  await page.getByTestId('profile-pw-save').click()
  await expect(page.getByText('Escriba su contraseña actual y la nueva.')).toBeVisible()
  await page.getByTestId('profile-pw-current').fill(c.password)
  await page.getByTestId('profile-pw-new').fill(`Nueva-${suffix}-Clave-2026!`)
  await page.getByTestId('profile-pw-again').fill('no-coincide')
  await page.getByTestId('profile-pw-save').click()
  await expect(page.getByText('Las contraseñas nuevas no coinciden.')).toBeVisible()
  await page.getByTestId('profile-pw-again').fill(`Nueva-${suffix}-Clave-2026!`)
  await page.getByTestId('profile-pw-current').fill('contraseña-actual-equivocada')
  await page.getByTestId('profile-pw-save').click()
  await expect(page.getByRole('alert').filter({ hasText: /contraseña|incorrecta|inválid/i }).first()).toBeVisible({ timeout: 30_000 })
  await page.getByTestId('profile-pw-current').fill(c.password)
  await page.getByTestId('profile-pw-save').click()
  await expect(page.getByText('Su contraseña se cambió.')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l120-contrasena')
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
