// Recorrido de los avisos al consignatario (Lote 116) en la app del cliente (versión web) contra el API real.
//
// Requiere lo mismo que el del Lote 117 (API con los enlaces de desarrollo y la app del cliente en CLIENT_APP_URL).
// Recorre: el cliente prende el aviso, marca el correo de un consignatario (advertencia con Continuar/Cancelar; queda marcado) → el consignatario recibe el enlace de seguimiento por token cuando la orden sale En ruta → la página
// pública /track/<token> muestra solo el avance (sin sesión) y un enlace que no existe responde el mensaje genérico → la página de baja pide confirmar y un enlace que no sirve se explica.
// Capturas (solo escritorio): docs/manual/frontend/img/l116-<paso>.png.
import { expect, test, type Page } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, createConsigneeAccess, loginAsClientAdmin, post, shot } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

test('Avisos al consignatario: marcar con advertencia, seguimiento por enlace y página de baja', async ({ page, request, browser, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)

  // 0) cliente con administrador; un consignatario con correo y teléfono; una orden
  const c0 = await createClientWithAdmin(request, suffix, 'Cliente 116')
  const consigneeName = `Tienda Sur ${suffix}`
  const loc = await c0.locations.post(consigneeName)
  await post(request, `/api/v1/contacts/LOCATION/${loc.id}`, c0.staff, { contactType: 'EMAIL', value: `tienda.${suffix.toLowerCase()}@sur.test`, isPrimary: true })
  await post(request, `/api/v1/contacts/LOCATION/${loc.id}`, c0.staff, { contactType: 'MOBILE', value: '787-555-0142', isPrimary: false })
  const order = await post<{ packages: { pieceNumbers: string[] }[] }>(request, '/api/v1/orders', c0.staff, { clientPublicId: c0.client.publicId, consigneeLocationPublicId: loc.publicId, packages: [{ pieces: 1, description: 'Caja' }] })

  // 1) EL CLIENTE: Configurar avisos → marcar el correo (advertencia) y cancelar / continuar
  if (!isMobile) await page.setViewportSize({ width: 1100, height: 760 })
  await loginAsClientAdmin(page, c0)
  await page.goto(`${APP_URL}/consignee-notices`)
  await expect(page.getByText(consigneeName)).toBeVisible({ timeout: 30_000 })
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l116-configurar-avisos')
  const email = page.getByRole('checkbox', { name: /^tienda\..*@sur\.test$/ })
  await email.click()
  await expect(page.getByText('Antes de continuar')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText(/aceptó recibir avisos sobre sus envíos/)).toBeVisible()
  await shot(page, isMobile, 'l116-advertencia')
  await page.getByTestId('notices-warning-cancel').click()
  await expect(page.getByText('Antes de continuar')).toHaveCount(0)
  await expect(email).not.toBeChecked()                                   // cancelar no marca nada
  await email.click()
  await page.getByTestId('notices-warning-continue').click()
  await expect(email).toBeChecked()
  await shot(page, isMobile, 'l116-marcado')

  // el aviso solo se encola si el consignatario está marcado: ahora la orden sale En ruta (se escanea «Salir») y se crea el enlace de seguimiento
  await post(request, '/api/v1/scan/action', c0.staff, { action: 'DEPART', code: order.packages[0].pieceNumbers[0] })
  // el enlace de seguimiento sale en los avisos; aquí se toma de la bandeja de un consignatario con acceso (el correo no está configurado en desarrollo)
  const access = await createConsigneeAccess(request, c0, loc.publicId, `s${suffix}`)
  const inbox = (await (await request.get(`${API_URL}/api/v1/portal/consignee/orders`, { headers: access.api })).json()) as { items: { trackToken?: string }[] }
  const token = inbox.items[0].trackToken
  expect(token, 'la orden que sale En ruta debe tener su enlace de seguimiento').toBeTruthy()

  // 2) la página PÚBLICA de seguimiento por enlace (sin sesión: contexto nuevo): solo el avance
  const ctx = await browser.newContext({ locale: 'es-PR', viewport: isMobile ? { width: 412, height: 915 } : { width: 1100, height: 760 } })
  const pub = await ctx.newPage()
  await pub.goto(`${APP_URL}/track/${token}`)
  await expect(pub.getByTestId('track-result-waybill')).toBeVisible({ timeout: 45_000 })
  await expect(pub.getByText(/nunca nombres, direcciones ni teléfonos/)).toBeVisible()
  await expect(pub.getByText(/Paso 3 de 4/)).toBeVisible()                // En ruta es la tercera de las cuatro etapas
  expect(await noHScroll(pub)).toBe(true)
  await shot(pub, isMobile, 'l116-seguimiento')
  await pub.goto(`${APP_URL}/track/no-existe-${suffix}`)
  await expect(pub.getByText('No encontramos ese enlace de seguimiento.')).toBeVisible({ timeout: 30_000 })
  await shot(pub, isMobile, 'l116-seguimiento-no-existe')

  // 3) la página de baja: abrir el enlace NO da de baja; un enlace que no sirve se explica
  await pub.goto(`${APP_URL}/unsubscribe/no-existe-${suffix}`)
  await expect(pub.getByText('Darse de baja de los avisos')).toBeVisible({ timeout: 30_000 })
  await shot(pub, isMobile, 'l116-baja')
  await pub.getByTestId('unsubscribe-confirm').click()
  await expect(pub.getByText('El enlace de baja no es válido.')).toBeVisible({ timeout: 30_000 })
  await ctx.close()
})
