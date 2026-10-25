// Recorrido del portal del cliente (Lote 119, P10) en la app del cliente contra el API real: el comprobante de entrega en la ficha de la orden, las facturas (lista, filtro y ficha, solo lectura), «Mis avisos» (la persona apaga lo que
// no quiere; lo que la compañía no encendió no se puede encender) y «Mis consignatarios».
//
// Datos propios por API: un cliente con persona administradora (MFA y documento legal ya hechos), un consignatario, una orden entregada por el personal desde la web (comprobante), una corrida de facturación aprobada (factura
// emitida) y dos avisos encendidos por la compañía. Requiere el API con Portal:ReturnInviteTokenInResponse y Auth:TrustedBrowsers:ReturnLinkInResponse, y la app del cliente (CLIENT_APP_URL).
// Escritorio y móvil. Capturas (solo escritorio): docs/manual/frontend/img/l119-<paso>.png.
import { expect, test } from '@playwright/test'

import { ADMIN, API_URL, createClientWithAdmin, loginAsClientAdmin, post, shot, APP_URL } from './portalHelpers'
import { createOrder, scan } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Portal del cliente: comprobante de entrega, facturas de solo lectura, Mis avisos y Mis consignatarios', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, 'Portal 119')
  const consigneeName = `Colmado Sur ${suffix}`
  const loc = await c.locations.post(consigneeName)

  // una orden que el personal entrega desde la web: queda el comprobante
  const order = await createOrder(request, c.staff, c.client.publicId, loc.publicId, 1)
  await scan(request, c.staff, 'DEPART', order.pieceNumbers[0])
  const delivery = await request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers: c.staff, data: { recipientName: `Ana Rivera ${suffix}`, notes: 'nota interna que el cliente no ve' } })
  expect(delivery.ok(), await delivery.text()).toBeTruthy()

  // la compañía enciende «Salida a entrega» (correo y bandeja) y «Entregas» (solo bandeja)
  const settings = await request.put(`${API_URL}/api/v1/clients/${c.client.publicId}/notifications/settings`, {
    headers: c.staff,
    data: { settings: [{ kind: 'OUT_FOR_DELIVERY', emailEnabled: true, appEnabled: true, smsEnabled: false }, { kind: 'DELIVERED', emailEnabled: false, appEnabled: true, smsEnabled: false }] },
  })
  expect(settings.ok(), await settings.text()).toBeTruthy()

  // una factura emitida: corrida de lo entregado, revisada y aprobada (la aprobación pide reautenticarse)
  const run = await post<{ id: number; invoiceCount: number }>(request, '/api/v1/billing/runs', c.staff, { clientPublicId: c.client.publicId, periodStart: '2020-01-01', periodEnd: '2099-12-31' })
  expect(run.invoiceCount).toBeGreaterThanOrEqual(1)
  await post(request, `/api/v1/billing/runs/${run.id}/review`, c.staff, {})
  const reauth = await post<{ accessToken: string }>(request, '/api/v1/auth/reauth', c.staff, { password: ADMIN.password })
  await post(request, `/api/v1/billing/runs/${run.id}/approve`, { Authorization: `Bearer ${reauth.accessToken}` }, {})

  // por API: la orden del portal trae el comprobante SIN el nombre del usuario ni la nota interna
  const portalOrder = (await (await request.get(`${API_URL}/api/v1/portal/orders/${order.publicId}`, { headers: c.clientApi })).json()) as { deliveryProof?: Record<string, unknown> }
  expect(portalOrder.deliveryProof).toMatchObject({ recipientName: `Ana Rivera ${suffix}`, registeredByStaff: true, signedByRecipient: false })
  expect(JSON.stringify(portalOrder.deliveryProof)).not.toContain('nota interna')
  expect(Object.keys(portalOrder.deliveryProof ?? {}).join(',')).not.toMatch(/registeredByName|signedByName|notes/i)

  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)

  // 1) el comprobante en la ficha de la orden
  await page.goto(`${APP_URL}/order/${order.publicId}`)
  await expect(page.getByText('Comprobante de entrega')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(`Ana Rivera ${suffix}`)).toBeVisible()
  await expect(page.getByText('La entrega la registró el personal de la compañía; el consignatario no firmó.')).toBeVisible()
  await expect(page.getByText('nota interna')).toHaveCount(0)
  await shot(page, isMobile, 'l119-comprobante')

  // 2) las facturas: el inicio las ofrece, la lista, el filtro y la ficha; ninguna acción de pago
  await page.goto(`${APP_URL}/home`)
  await page.getByTestId('home-invoices').click()
  await expect(page.getByRole('heading', { name: 'Mis facturas' }).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(/Aquí solo puede consultarlas/)).toBeVisible()
  await expect(page.getByText(/^FAC-/).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Emitida', { exact: true }).first()).toBeVisible()
  await expect(page.getByText(/Pagar/i)).toHaveCount(0)
  await shot(page, isMobile, 'l119-facturas')
  await page.getByTestId('invoices-filter-PAID').click()
  await expect(page.getByText('No hay facturas.')).toBeVisible({ timeout: 30_000 })          // ninguna pagada todavía
  await page.getByTestId('invoices-filter-all').click()
  await page.getByText(/^FAC-/).first().click()
  await expect(page.getByText('Detalle', { exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Saldo', { exact: true }).last()).toBeVisible()
  await expect(page.getByText(/Pagar|Marcar/i)).toHaveCount(0)
  await shot(page, isMobile, 'l119-factura')

  // por API: la factura de otro cliente o inexistente responde 404 y los borradores no salen
  expect((await request.get(`${API_URL}/api/v1/portal/invoices/${crypto.randomUUID()}`, { headers: c.clientApi })).status()).toBe(404)

  // 3) Mis avisos: lo que la compañía no encendió no se puede encender; lo que sí, la persona lo apaga
  await page.goto(`${APP_URL}/settings`)
  await page.getByTestId('settings-notice-prefs').click()
  await expect(page.getByRole('heading', { name: 'Mis avisos' }).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Salida a entrega')).toBeVisible()
  await expect(page.getByText('La compañía no tiene este aviso encendido para su empresa.').first()).toBeVisible()
  await shot(page, isMobile, 'l119-mis-avisos')
  await page.getByTestId('prefs-email-OUT_FOR_DELIVERY').click()
  await expect.poll(async () => {
    const prefs = (await (await request.get(`${API_URL}/api/v1/portal/notice-prefs`, { headers: c.clientApi })).json()) as { prefs: { kind: string; emailEnabled: boolean; emailAvailable: boolean }[] }
    const mine = prefs.prefs.find((p) => p.kind === 'OUT_FOR_DELIVERY')
    return `${mine?.emailEnabled}/${mine?.emailAvailable}`
  }, { timeout: 30_000 }).toBe('false/true')                                                       // la persona lo apagó y la compañía lo sigue teniendo encendido
  await shot(page, isMobile, 'l119-mis-avisos-apagado')

  // 4) Mis consignatarios: la lista del cliente
  await page.goto(`${APP_URL}/settings`)
  await page.getByTestId('settings-consignee-notices').click()
  await expect(page.getByRole('heading', { name: 'Mis consignatarios' }).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(consigneeName)).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l119-mis-consignatarios')
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
