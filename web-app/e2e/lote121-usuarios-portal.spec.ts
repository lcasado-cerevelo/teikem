// Recorrido de las pestañas «Clientes» y «Consignatarios» de Sistema → Roles y usuarios (Lote 121) contra el API real: la compañía ve y controla a TODAS las personas de portal y a los accesos de consignatarios (regla del dueño:
// los usuarios los controlan el administrador de la plataforma y el de la compañía). Datos propios por API: un cliente con persona administradora y un consignatario con acceso propio.
// Recorre: las cuatro pestañas (Roles, Usuarios, Clientes, Consignatarios), cada persona con su tipo y su cliente; suspender un acceso de consignatario (pide confirmar), reactivarlo, darlo de baja y verlo con «Incluir dados de baja».
// Escritorio y móvil. Capturas (solo escritorio): docs/manual/frontend/img/l121-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, createClientWithAdmin, createConsigneeAccess } from './portalHelpers'
import { login, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Roles y usuarios: las personas de los clientes y los accesos de consignatarios, con su tipo y sus acciones', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, 'Usuarios 121')
  const loc = await c.locations.post(`Colmado ${suffix}`)
  const access = await createConsigneeAccess(request, c, loc.publicId, `u${suffix}`)

  // por API: la lista de la compañía trae a la persona del cliente (tipo CLIENT) y al consignatario (tipo CONSIGNEE con su nombre)
  const list = async (kind: string, removed = false) => (await (await request.get(`${API_URL}/api/v1/portal-users?kind=${kind}&includeRemoved=${removed}`, { headers: c.staff })).json()) as { email: string; kind: string; status: string; consigneeName?: string; clientName: string }[]
  expect((await list('CLIENT')).find((u) => u.email === c.adminMail)).toMatchObject({ kind: 'CLIENT', status: 'ACTIVE', clientName: `Usuarios 121 ${suffix}` })
  expect((await list('CONSIGNEE')).find((u) => u.email === access.mail)).toMatchObject({ kind: 'CONSIGNEE', consigneeName: `Colmado ${suffix}` })
  expect((await request.get(`${API_URL}/api/v1/portal-users?kind=STAFF`, { headers: c.staff })).status()).toBe(400)
  expect((await request.get(`${API_URL}/api/v1/portal-users`, { headers: c.clientApi })).status()).toBe(403)       // una persona de portal no entra a la lista de la compañía

  await login(page)
  await page.goto('/system/users')
  const tabs = page.getByRole('tab')
  await expect(tabs.filter({ hasText: 'Roles' })).toBeVisible({ timeout: 30_000 })
  for (const name of ['Roles', 'Usuarios', 'Clientes', 'Consignatarios']) await expect(page.getByRole('tab', { name, exact: true })).toBeVisible()

  // Clientes: la persona del cliente con su tipo y su cliente
  await page.getByRole('tab', { name: 'Clientes', exact: true }).click()
  await page.getByRole('searchbox').fill(suffix)
  const clientRow = page.locator('tr, .dt-cards > li').filter({ hasText: c.adminMail }).first()
  await expect(clientRow).toBeVisible({ timeout: 30_000 })
  await expect(clientRow).toContainText(`Usuarios 121 ${suffix}`)
  await expect(clientRow).toContainText('Activo')
  await shot(page, isMobile, 'l121-clientes')

  // Consignatarios: el acceso con su consignatario; suspender pide confirmar, reactivar va directo, dar de baja pide confirmar
  await page.getByRole('tab', { name: 'Consignatarios', exact: true }).click()
  await page.getByRole('searchbox').fill(suffix)
  const row = () => page.locator('tr, .dt-cards > li').filter({ hasText: access.mail }).first()
  await expect(row()).toBeVisible({ timeout: 30_000 })
  await expect(row()).toContainText(`Colmado ${suffix}`)
  await expect(row()).toContainText('Consignatario')
  await shot(page, isMobile, 'l121-consignatarios')

  await row().getByRole('button', { name: 'Suspender' }).click()
  const dialog = page.getByRole('dialog', { name: 'Suspender el acceso' })
  await expect(dialog).toContainText('no podrá entrar al portal')
  await shot(page, isMobile, 'l121-suspender')
  await dialog.getByRole('button', { name: 'Suspender' }).click()
  await expect(row()).toContainText('Suspendido', { timeout: 30_000 })
  expect((await list('CONSIGNEE')).find((u) => u.email === access.mail)?.status).toBe('SUSPENDED')
  // el consignatario suspendido ya no entra al portal con su sesión
  expect((await request.get(`${API_URL}/api/v1/portal/consignee/me`, { headers: access.api })).status()).not.toBe(200)

  await row().getByRole('button', { name: 'Reactivar' }).click()
  await expect(row()).toContainText('Activo', { timeout: 30_000 })

  await row().getByRole('button', { name: 'Dar de baja' }).click()
  await page.getByRole('dialog', { name: 'Dar de baja el acceso' }).getByRole('button', { name: 'Dar de baja' }).click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: access.mail })).toHaveCount(0, { timeout: 30_000 })
  expect((await list('CONSIGNEE')).some((u) => u.email === access.mail)).toBe(false)
  expect((await list('CONSIGNEE', true)).find((u) => u.email === access.mail)?.status).toBe('DISABLED')
  await page.getByText('Incluir dados de baja').click()
  await expect(row()).toContainText('Inhabilitado', { timeout: 30_000 })
  await shot(page, isMobile, 'l121-baja')
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
