// Recorrido de «¿Cómo va mi paquete?» (Lotes 103 y 116) contra el API real: la página pública (app del cliente, sin sesión) se abre con el ENLACE con token que trae el aviso al consignatario y muestra SOLO el avance con la barra de
// cuatro etapas, sin nombres ni direcciones; la orden detenida (intento fallido) se ve en alerta y la cancelada apagada; el enlace que no existe responde el mensaje genérico; tema claro/oscuro e idioma se recuerdan.
// (Desde el Lote 116 ya no hay búsqueda por número de conduce ni el ajuste «Seguimiento público» de la compañía: este recorrido era de esa versión y se reescribió para el enlace.)
//
// Requiere el API (API_URL) con Portal:ReturnInviteTokenInResponse y Auth:TrustedBrowsers:ReturnLinkInResponse, y la app del cliente (CLIENT_APP_URL, `npm run web -- --port 8081` en app-cliente). Escritorio y móvil.
// Capturas (solo escritorio): docs/manual/frontend/img/l103-<paso>.png.
import { expect, test, type Page } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, createConsigneeAccess, post, shot } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

/** El color de fondo que de verdad se ve: el del primer ascendiente con fondo propio del elemento. */
async function paintedBackground(page: Page, testId: string): Promise<string> {
  return page.getByTestId(testId).evaluate((el) => {
    let n: Element | null = el
    while (n) {
      const bg = getComputedStyle(n).backgroundColor
      if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') return bg
      n = n.parentElement
    }
    return 'transparent'
  })
}

test('Seguimiento por enlace: solo el avance con la barra, en alerta o apagada, sin datos privados', async ({ browser, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)

  // 0) un cliente que avisa a su consignatario (marcado por correo) y tres órdenes: en ruta, con intento fallido y cancelada
  const c0 = await createClientWithAdmin(request, suffix, 'Seguimiento')
  const consigneeName = `Sucursal Secreta ${suffix}`
  const loc = await c0.locations.post(consigneeName)
  await post(request, `/api/v1/contacts/LOCATION/${loc.id}`, c0.staff, { contactType: 'EMAIL', value: `secreta.${suffix.toLowerCase()}@demo.test`, isPrimary: true })
  const marked = await request.put(`${API_URL}/api/v1/portal/consignee-notices/consignees/${loc.publicId}`, { headers: c0.clientApi, data: { channel: 'EMAIL', accepted: true, warningAccepted: true } })
  expect(marked.ok(), await marked.text()).toBeTruthy()
  const newOrder = async () => post<{ publicId: string; packages: { pieceNumbers: string[] }[] }>(request, '/api/v1/orders', c0.staff, { clientPublicId: c0.client.publicId, consigneeLocationPublicId: loc.publicId, packages: [{ pieces: 1 }] })
  const depart = (o: { packages: { pieceNumbers: string[] }[] }) => post(request, '/api/v1/scan/action', c0.staff, { action: 'DEPART', code: o.packages[0].pieceNumbers[0] })
  const onRoute = await newOrder()
  const held = await newOrder()
  const cancelled = await newOrder()
  await depart(onRoute)
  await depart(held)
  await post(request, `/api/v1/orders/${held.publicId}/status`, c0.staff, { toCode: 'FAILED' })                          // Lote 106: «Intento fallido» es el lateral
  await post(request, `/api/v1/orders/${cancelled.publicId}/cancel`, c0.staff, { comment: 'prueba' })

  // los enlaces: los de las órdenes del consignatario (con acceso propio; el correo no está configurado en desarrollo)
  const access = await createConsigneeAccess(request, c0, loc.publicId, `t${suffix}`)
  const list = (await (await request.get(`${API_URL}/api/v1/portal/consignee/orders?take=20`, { headers: access.api })).json()) as { items: { publicId: string; trackToken?: string }[] }
  const tokenOf = (o: { publicId: string }) => list.items.find((i) => i.publicId === o.publicId)?.trackToken
  expect(tokenOf(onRoute), 'la orden En ruta tiene enlace').toBeTruthy()
  expect(tokenOf(held), 'la orden con intento fallido tiene enlace').toBeTruthy()
  expect(tokenOf(cancelled), 'la orden cancelada tiene enlace').toBeTruthy()

  // 1) la página pública, sin sesión (contexto nuevo): En ruta
  const ctx = await browser.newContext({ locale: 'es-PR', viewport: isMobile ? { width: 412, height: 915 } : { width: 1100, height: 760 } })
  const page = await ctx.newPage()
  await page.goto(`${APP_URL}/track/${tokenOf(onRoute)}`)
  await expect(page.getByTestId('track-result-waybill')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('heading', { name: '¿Cómo va mi paquete?' })).toBeVisible()
  await expect(page.getByText('Paso 3 de 4 · En ruta')).toBeVisible()
  const total = await page.getByTestId(/^segment-/).count()
  expect(total).toBe(4)                                                                                    // Sometida, Recibida, En ruta, Completada
  await expect(page.getByTestId('segment-current')).toHaveCount(1)
  await expect(page.getByTestId('segment-done')).toHaveCount(2)
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l103-publica')
  // nada de personas, direcciones ni del cliente
  const text = (await page.locator('body').innerText()) + (await page.content())
  for (const secret of [consigneeName, 'Calle 5 #12', c0.client.name, '00716']) expect(text).not.toContain(secret)

  // 1b) los dos temas y los dos idiomas, con su opción en la misma página (por omisión: automático y español)
  await expect(page.getByTestId('theme-auto')).toBeChecked()
  await expect(page.getByTestId('lang-es')).toBeChecked()
  await page.emulateMedia({ colorScheme: 'light' })
  expect(await paintedBackground(page, 'track-result-waybill')).toBe('rgb(255, 255, 255)')                  // la tarjeta, en claro
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect.poll(() => paintedBackground(page, 'track-result-waybill')).toBe('rgb(21, 29, 46)')          // Automático sigue al sistema: en oscuro
  await shot(page, isMobile, 'l103-publica-oscuro')
  await page.getByTestId('theme-light').click()                                                             // Claro manda sobre el sistema
  await expect.poll(() => paintedBackground(page, 'track-result-waybill')).toBe('rgb(255, 255, 255)')
  await page.getByTestId('theme-dark').click()
  await expect.poll(() => paintedBackground(page, 'track-result-waybill')).toBe('rgb(21, 29, 46)')
  await page.reload()                                                                                       // la elección se recuerda
  await expect(page.getByTestId('theme-dark')).toBeChecked({ timeout: 30_000 })
  await page.getByTestId('theme-auto').click()
  await page.getByTestId('lang-en').click()
  await expect(page.getByRole('heading', { name: 'How is my package doing?' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: 'How is my package doing?' })).toBeVisible({ timeout: 30_000 })   // el idioma también se recuerda
  await shot(page, isMobile, 'l103-publica-ingles')
  await page.getByTestId('lang-es').click()
  await expect(page.getByRole('heading', { name: '¿Cómo va mi paquete?' })).toBeVisible()
  await page.emulateMedia({ colorScheme: 'light' })

  // 2) con intento fallido: la barra se queda en «En ruta» y avisa; cancelada: toda apagada
  await page.goto(`${APP_URL}/track/${tokenOf(held)}`)
  await expect(page.getByTestId('track-result-waybill')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(/^Intentada · va por el paso 3 de 4$/)).toBeVisible()                         // Intento fallido = etapa «En ruta» en alerta
  await expect(page.getByTestId('segment-alert')).toHaveCount(1)
  await shot(page, isMobile, 'l103-publica-espera')
  await page.goto(`${APP_URL}/track/${tokenOf(cancelled)}`)
  await expect(page.getByTestId('track-result-waybill')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('segment-cancelled')).toHaveCount(total)

  // 3) un enlace que no existe: el mismo mensaje genérico, sin pista de qué falló
  await page.goto(`${APP_URL}/track/no-existe-${suffix.toLowerCase()}`)
  await expect(page.getByText('No encontramos ese enlace de seguimiento.')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('track-result-waybill')).toHaveCount(0)
  await ctx.close()
})
