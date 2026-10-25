// Recorrido del acceso del consignatario (Lote 117) en la app del cliente (versión web) contra el API real.
//
// Requiere: el API con Auth:TrustedBrowsers:ReturnLinkInResponse=true, Portal:ReturnInviteTokenInResponse=true y Auth:PasswordReset:WebBaseUrl = la app (CLIENT_APP_URL), y la app del cliente en CLIENT_APP_URL
// (`npm run web -- --port 8081` en app-cliente, con EXPO_PUBLIC_API_URL = el API); API_URL apunta al mismo API.
// Recorre: el cliente (persona con MFA) abre «Configurar avisos → Acceso» y le da acceso a un consignatario → el consignatario acepta la invitación, entra SIN MFA a sus cuatro pestañas (Envíos, Avisos, Mensajes y Ajustes), ve
// su envío (solo lectura, sin precios), le escribe a su cliente y apaga los avisos por correo → el cliente ve el contador, la lista de conversaciones y contesta → el consignatario ve la respuesta → tema oscuro.
// Capturas (solo escritorio): docs/manual/frontend/img/l117-<paso>.png.
import { expect, test, type Page } from '@playwright/test'

import { API_URL, APP_URL, type AuthResultDto, confirmBrowserByApi, createClientWithAdmin, loginAsClientAdmin, loginConfirmingTheComputer, newConsigneeContext, post, shot } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

test('Acceso del consignatario: el cliente invita, el consignatario entra sin MFA, ve sus envíos, se escriben y apaga los avisos', async ({ page, request, browser, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)

  // 0) cliente con contrato y tarifa y su administrador con MFA; un consignatario con una orden
  const c0 = await createClientWithAdmin(request, suffix, 'Cliente 117')
  const { staff, client, password, clientApi } = c0
  const legal = { currentVersion: c0.legalVersion }
  const consigneeName = `Tienda Norte ${suffix}`
  const loc = await c0.locations.post(consigneeName)
  await post(request, '/api/v1/orders', staff, { clientPublicId: client.publicId, consigneeLocationPublicId: loc.publicId, packages: [{ pieces: 2, description: 'Caja de pruebas' }] })

  // 1) EL CLIENTE: Configurar avisos → Acceso → invita al consignatario
  if (!isMobile) await page.setViewportSize({ width: 1100, height: 760 })
  await loginAsClientAdmin(page, c0)
  await page.goto(`${APP_URL}/consignee-notices`)
  await expect(page.getByText(consigneeName)).toBeVisible({ timeout: 30_000 })
  await page.getByTestId(/^access-open-/).first().click()
  const consigneeMail = `c117.tienda.${suffix.toLowerCase()}@demo.test`
  await expect(page.getByText('Nadie tiene acceso todavía.')).toBeVisible({ timeout: 30_000 })
  await page.getByTestId('access-email').fill(consigneeMail)
  await page.getByTestId('access-name').fill('Doña Tienda')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l117-acceso-invitar')
  await page.getByTestId('access-invite').click()
  await expect(page.getByText(`Invitación enviada a ${consigneeMail}.`)).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Invitación pendiente')).toBeVisible()
  await shot(page, isMobile, 'l117-acceso-pendiente')

  // 2) el consignatario acepta la invitación (el enlace vuelve a emitirse por API: la pantalla no lo muestra)
  const access = (await (await request.get(`${API_URL}/api/v1/portal/consignee-access/${loc.publicId}`, { headers: clientApi })).json()) as { id: number }[]
  const resent = await post<{ inviteToken?: string }>(request, `/api/v1/portal/consignee-access/${loc.publicId}/${access[0].id}/resend`, clientApi)
  expect((await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email: consigneeMail, token: resent.inviteToken, password } })).status()).toBe(204)
  // el documento legal del consignatario se acepta por API (la pantalla ya se recorrió en el Lote 102)
  const cBrowser = (crypto.randomUUID() + crypto.randomUUID()).replace(/-/g, '')
  await confirmBrowserByApi(request, consigneeMail, password, cBrowser)
  const cLogin = (await (await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email: consigneeMail, password, trustedBrowserToken: cBrowser } })).json()) as AuthResultDto
  expect(cLogin.status).toBe('ok')   // el consignatario NO pasa por el MFA
  const cApi = { Authorization: `Bearer ${cLogin.tokens?.accessToken}` }
  await post(request, '/api/v1/legal/accept', cApi, { version: legal.currentVersion, lang: 'es' })

  // 3) EL CONSIGNATARIO en otra computadora (contexto nuevo): entra sin MFA y aterriza en Avisos con sus cuatro pestañas
  const c = await newConsigneeContext(browser, isMobile)
  const cp = c.page
  await loginConfirmingTheComputer(cp, consigneeMail, password)
  await expect(cp.getByRole('heading', { name: 'Mis avisos' })).toBeVisible({ timeout: 30_000 })
  for (const tab of ['Envíos', 'Avisos', 'Mensajes', 'Ajustes']) await expect(cp.getByRole('link', { name: tab })).toBeVisible()
  await expect(cp.getByRole('link', { name: 'Inicio' })).toHaveCount(0)
  expect(await noHScroll(cp)).toBe(true)
  await shot(cp, isMobile, 'l117-consignatario-avisos')

  // 4) sus envíos: solo lectura, sin precios, sin botón de crear
  await cp.getByRole('link', { name: 'Envíos' }).click()
  await expect(cp.getByRole('heading', { name: 'Mis envíos' })).toBeVisible({ timeout: 30_000 })
  await expect(cp.getByText(/^Conduce /)).toHaveCount(1)
  await expect(cp.getByTestId('orders-new')).toHaveCount(0)
  await shot(cp, isMobile, 'l117-consignatario-envios')
  await cp.getByText(/^Conduce /).click()
  await expect(cp.getByText('Calle 5 #12')).toBeVisible({ timeout: 30_000 })
  await expect(cp.getByText('2 × Caja de pruebas')).toBeVisible()
  await expect(cp.getByText(/Factura|COD/)).toHaveCount(0)
  await shot(cp, isMobile, 'l117-consignatario-envio')

  // 5) le escribe a su cliente
  await cp.goto(`${APP_URL}/messages`)
  await expect(cp.getByText('Su conversación con su cliente. Solo usted y las personas de su cliente la ven.')).toBeVisible({ timeout: 30_000 })
  await cp.getByTestId('messages-input').fill('Hola, ¿cuándo llega mi paquete?')
  await cp.getByTestId('messages-send').click()
  await expect(cp.getByText('Hola, ¿cuándo llega mi paquete?')).toBeVisible({ timeout: 30_000 })
  await shot(cp, isMobile, 'l117-consignatario-mensajes')

  // 6) su cuenta: apaga el correo y se le dice cómo volver a prenderlo
  await cp.goto(`${APP_URL}/settings`)
  await expect(cp.getByText('Cómo recibo los avisos')).toBeVisible({ timeout: 30_000 })
  await expect(cp.getByText(consigneeName)).toBeVisible()
  await shot(cp, isMobile, 'l117-consignatario-ajustes')

  // 7) EL CLIENTE ve el contador, la conversación, y contesta
  await page.goto(`${APP_URL}/messages`)
  await expect(page.getByRole('link', { name: 'Mensajes 1' })).toBeVisible({ timeout: 45_000 })   // el contador de sin leer va en la pestaña
  await shot(page, isMobile, 'l117-cliente-mensajes')
  await expect(page.getByText(consigneeName)).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l117-cliente-conversaciones')
  await page.getByText(consigneeName).click()
  await expect(page.getByTestId('bubble-team').getByText('Hola, ¿cuándo llega mi paquete?')).toBeVisible({ timeout: 30_000 })
  // la pestaña Mensajes queda montada debajo: el hilo abierto es el último
  await page.getByTestId('messages-input').last().fill('Mañana por la mañana.')
  await page.getByTestId('messages-send').last().click()
  await expect(page.getByTestId('bubble-mine').getByText('Mañana por la mañana.')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l117-cliente-conversacion')

  // 8) el consignatario ve la respuesta
  await cp.goto(`${APP_URL}/messages`)
  await expect(cp.getByTestId('bubble-team').getByText('Mañana por la mañana.')).toBeVisible({ timeout: 30_000 })

  // 9) lo que no le toca: las rutas del cliente le responden 403 (el servidor limita, no la interfaz)
  const forbidden = await request.get(`${API_URL}/api/v1/portal/orders`, { headers: cApi })
  expect(forbidden.status()).toBe(403)
  const create = await request.post(`${API_URL}/api/v1/portal/orders`, { headers: cApi, data: { packages: [{ pieces: 1 }] } })
  expect(create.status()).toBe(403)   // el consignatario no crea órdenes

  // 10) tema oscuro desde sus ajustes: se aplica al instante
  await cp.goto(`${APP_URL}/settings`)
  await cp.getByTestId('theme-dark').click()
  await expect(cp.getByTestId('theme-dark')).toBeChecked()
  await shot(cp, isMobile, 'l117-consignatario-ajustes-oscuro')
  await c.context.close()
})
