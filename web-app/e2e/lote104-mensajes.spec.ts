// Recorrido de los mensajes del portal en la web de la compañía (Lote 104) contra el API real.
//
// Requiere el API con Portal:ReturnInviteTokenInResponse=true. Se crea un cliente con dos personas de portal; la compañía designa el equipo, le escribe primero a una persona desde la ficha,
// ve la conversación en «Mensajes de clientes», responde, y la otra persona (misma empresa) NO tiene conversación. Capturas (solo escritorio): docs/manual/frontend/img/l104-<paso>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForTimeout(400)
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
}

async function adminToken(request: APIRequestContext): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok(), `login: ${await res.text()}`).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    const again = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await again.json()) as AuthResultDto
  }
  const token = body.tokens?.accessToken ?? ''
  expect(token).toBeTruthy()
  const headers = { Authorization: `Bearer ${token}` }
  const status = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (status.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: status.currentVersion, lang: 'es' } })
  return token
}

async function portalPerson(request: APIRequestContext, headers: Record<string, string>, clientId: string, name: string, email: string) {
  const inv = await request.post(`${API_URL}/api/v1/clients/${clientId}/portal-users/invite`, { headers, data: { email, fullName: name, role: 'CLIENT_READONLY' } })
  expect(inv.ok(), await inv.text()).toBeTruthy()
  const token = ((await inv.json()) as { inviteToken: string }).inviteToken
  const acc = await request.post(`${API_URL}/api/v1/portal-users/accept-invite`, { data: { email, token, password: `Msg_${Date.now()}_Clave-2026!` } })
  expect(acc.status()).toBe(204)
}

test('mensajes del portal: equipo, escribir primero, bandeja y responder', async ({ page, request, isMobile }) => {
  const suffix = String(Date.now()).slice(-6)
  const token = await adminToken(request)
  const headers = { Authorization: `Bearer ${token}` }
  const res = await request.put(`${API_URL}/api/v1/modules/CLIENT_PORTAL`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { isEnabled: true } })
  // encender el módulo pide AAL2: en una base ya preparada está encendido y el 403 no importa
  if (!res.ok()) expect((await request.get(`${API_URL}/api/v1/client-messages/unread`, { headers })).ok()).toBeTruthy()
  const clientName = `Mensajes ${suffix}`
  const c = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } })
  expect(c.ok(), await c.text()).toBeTruthy()
  const clientId = ((await c.json()) as { publicId: string }).publicId
  await portalPerson(request, headers, clientId, `Ana ${suffix}`, `ana${suffix}@demo.test`)
  await portalPerson(request, headers, clientId, `Beto ${suffix}`, `beto${suffix}@demo.test`)

  await page.setViewportSize(isMobile ? { width: 412, height: 915 } : { width: 1280, height: 900 })
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  const noHScroll = () => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)

  // 1) la ficha del cliente: sin equipo designado → lo atiende quien tenga el permiso
  await page.goto(`/catalog/clients?client=${clientId}`)
  const panel = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: 'Equipo de servicio al cliente' }) })
  await expect(panel).toBeVisible({ timeout: 30_000 })
  await panel.scrollIntoViewIfNeeded()
  const toggle = panel.locator('header button, button[aria-expanded]').first()
  if ((await toggle.getAttribute('aria-expanded')) === 'false') await toggle.click()   // los paneles de la ficha vienen plegados
  await expect(panel.getByText(/no tiene equipo designado/)).toBeVisible()
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l104-equipo-sin-designar')

  // 2) designar el equipo
  await panel.getByRole('button', { name: 'Designar equipo' }).click()
  const dlg = page.getByRole('dialog')
  await expect(dlg).toBeVisible()
  await dlg.locator('input[type=checkbox]').first().check()
  await shot(page, isMobile, 'l104-designar-equipo')
  await dlg.getByRole('button', { name: 'Guardar equipo' }).click()
  await expect(panel.getByText(/no tiene equipo designado/)).toHaveCount(0, { timeout: 30_000 })   // ya hay equipo: la nota del respaldo desaparece

  // 3) escribirle primero a Ana
  await panel.getByRole('button', { name: 'Escribirle a una persona' }).click()
  const w = page.getByRole('dialog')
  await expect(w).toBeVisible()
  const opt = await w.getByLabel('Persona').locator('option', { hasText: `Ana ${suffix}` }).first().getAttribute('value')
  await w.getByLabel('Persona').selectOption(opt ?? '')
  await w.getByLabel('Mensaje').fill('Hola Ana, hay un problema con tu envío; ¿puedes confirmarnos la dirección?')
  await shot(page, isMobile, 'l104-escribir-primero')
  await w.getByRole('button', { name: 'Enviar' }).click()

  // 4) la bandeja con la conversación abierta
  await expect(page).toHaveURL(/\/portal\/messages\?conversation=/, { timeout: 30_000 })
  await expect(page.getByRole('heading', { name: 'Mensajes de clientes' })).toBeVisible()
  await expect(page.locator('.cm-msg.staff', { hasText: '¿puedes confirmarnos la dirección?' })).toBeVisible({ timeout: 30_000 })
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l104-bandeja')

  // 5) responder; vacío no se manda, y el contador
  const draft = page.getByRole('textbox', { name: 'Escriba su respuesta…' })
  await draft.fill('   ')
  await page.getByRole('button', { name: 'Enviar' }).click()
  await expect(page.getByText('Escriba el mensaje.')).toBeVisible()
  await draft.fill('Quedamos pendientes de su confirmación.')
  await page.getByRole('button', { name: 'Enviar' }).click()
  await expect(page.locator('.cm-msg.staff', { hasText: 'Quedamos pendientes' })).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l104-respondida')

  // 6) Beto (misma empresa) no tiene conversación: la lista solo trae la de Ana
  const list = await request.get(`${API_URL}/api/v1/client-messages/conversations?take=100`, { headers })
  const names = ((await list.json()) as { items: { personName?: string; clientName?: string }[] }).items.filter((i) => i.clientName === clientName).map((i) => i.personName)
  expect(names).toEqual([`Ana ${suffix}`])

  // 7) tema oscuro de la web
  await page.getByRole('button', { name: 'Oscuro' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await shot(page, isMobile, 'l104-bandeja-oscuro')
})
