// Recorrido de la mensajería (Lote 53) contra el API real con DOS personas a la vez (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo: el administrador y Carlos Rivera, el despachador).
//
// 1) El administrador abre Mensajes, «Nuevo mensaje»: el buscador lista a Carlos con su NOMBRE y su CORREO; le escribe.
// 2) Carlos, con la aplicación abierta, recibe EN VIVO (conexión SignalR): el contador de la cabecera sube y Kemi avisa «Te llegó un mensaje de …
//    → Ver». «Ver» abre el hilo y contesta con Enter.
// 3) El administrador ve la respuesta EN VIVO sin recargar, y Carlos aparece «En línea» (presencia).
// 4) Un mensaje a un rol crea un grupo con quienes tienen el rol hoy; Carlos lo ve y ve a las personas del grupo.
// 5) Carlos no puede leer el hilo del otro por la API (404), y en teléfono (360 px) se ve una cosa a la vez, sin scroll horizontal de página.
//
// Escritorio y móvil. Capturas: docs/manual/frontend/img/l53-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { devices, expect, test, type APIRequestContext, type Browser, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type MessageThreadSummaryDto = components['schemas']['MessageThreadSummaryDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const CARLOS = { email: 'teikem+dispatch@cerevelo.com', password: PASSWORD }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

async function shot(page: Page, isMobile: boolean, name: string, target?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  if (target) await target.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
}

async function apiToken(request: APIRequestContext, who: { email: string; password: string }): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password } })
  expect(res.ok(), `login ${who.email}: ${await res.text()}`).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function login(page: Page, who: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(who.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(who.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

async function secondPerson(browser: Browser, isMobile: boolean) {
  const context = await browser.newContext(isMobile ? { ...devices['Pixel 7'], viewport: { width: 360, height: 780 }, locale: 'es-PR' } : { locale: 'es-PR', viewport: { width: 1280, height: 720 } })
  return { context, page: await context.newPage() }
}

test('Mensajes: dos personas, en vivo, con hilo, presencia y un grupo por rol', async ({ page, request, browser, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const first = `Hola Carlos, ¿listo el cierre? ${suffix}`
  const reply = `Listo, ya lo cierro ${suffix}`
  const group = `Cierre ${suffix}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const carlosToken = await apiToken(request, CARLOS)
  const adminToken = await apiToken(request, ADMIN)
  const asCarlos = { Authorization: `Bearer ${carlosToken}` }
  const asAdmin = { Authorization: `Bearer ${adminToken}` }

  // punto de partida limpio: lo que Carlos tuviera pendiente de otras corridas se marca leído
  const pending = (await (await request.get(`${API_URL}/api/v1/messages/threads`, { headers: asCarlos })).json()) as MessageThreadSummaryDto[]
  for (const t of pending.filter((x) => (x.unread ?? 0) > 0)) await request.post(`${API_URL}/api/v1/messages/threads/${t.publicId}/read`, { headers: asCarlos })

  // Carlos entra primero y se queda con la aplicación abierta (recibe en vivo)
  const { context: carlosCtx, page: carlos } = await secondPerson(browser, isMobile)
  await login(carlos, CARLOS)
  await expect(carlos.getByRole('link', { name: /^Mensajes/ })).toBeVisible({ timeout: 20_000 })
  await login(page, ADMIN)

  // 1) el administrador escribe
  await page.goto('/messages')
  await expect(page.getByRole('complementary', { name: 'Conversaciones' })).toBeVisible({ timeout: 20_000 })
  await shot(page, isMobile, 'l53-lista-vacia')
  await page.getByRole('button', { name: 'Nuevo mensaje' }).click()
  const dialog = page.getByRole('dialog', { name: 'Nuevo mensaje' })
  await dialog.getByRole('combobox', { name: 'Para' }).fill('carlos')
  const option = dialog.getByRole('listbox').getByRole('option').filter({ hasText: 'Carlos Rivera' })
  await expect(option).toContainText('teikem+dispatch@cerevelo.com')   // nombre y correo
  await shot(page, isMobile, 'l53-nuevo-buscador', dialog)
  await option.click()
  await dialog.getByLabel('Mensaje', { exact: true }).fill(first)
  await shot(page, isMobile, 'l53-nuevo-mensaje', dialog)
  await dialog.getByRole('button', { name: 'Enviar' }).click()
  await expect(dialog).toBeHidden()
  await expect(page).toHaveURL(/\/messages\?thread=/)
  const pane = page.getByRole('region', { name: 'Carlos Rivera' })
  await expect(pane.getByText(first)).toBeVisible()
  await expect(pane.locator('.chat-group.mine').filter({ hasText: first })).toBeVisible()
  expect(await noHScroll(page)).toBe(true)

  // 2) Carlos lo recibe EN VIVO: contador y Kemi (sin recargar)
  await expect(carlos.getByRole('link', { name: /^Mensajes: [1-9]/ })).toBeVisible({ timeout: 15_000 })
  const bubble = carlos.locator('.kemi-bubble').filter({ hasText: /mensaje/ })
  await expect(bubble).toBeVisible({ timeout: 15_000 })
  await expect(bubble).toContainText('Administrador Advance')
  await shot(carlos, isMobile, 'l53-kemi-globo')
  await bubble.getByRole('button', { name: /Ver/ }).click()
  await expect(carlos).toHaveURL(/\/messages\?thread=/)
  await expect(carlos.getByRole('region', { name: 'Administrador Advance' }).getByText(first)).toBeVisible()
  await expect(carlos.getByRole('region', { name: 'Administrador Advance' }).getByText('En línea')).toBeVisible({ timeout: 20_000 })   // presencia del administrador
  await carlos.getByRole('textbox', { name: 'Escriba un mensaje…' }).fill(reply)
  await carlos.keyboard.press('Enter')
  await expect(carlos.locator('.chat-group.mine').filter({ hasText: reply })).toBeVisible()
  await expect(carlos.getByRole('link', { name: 'Mensajes', exact: true })).toBeVisible()   // al verlo se marcó leído: sin contador

  // 3) el administrador ve la respuesta en vivo, sin recargar, y a Carlos «En línea»
  await expect(pane.getByText(reply)).toBeVisible({ timeout: 15_000 })
  await expect(pane.getByText('En línea')).toBeVisible({ timeout: 20_000 })
  await shot(page, isMobile, 'l53-conversacion')
  expect(await noHScroll(page)).toBe(true)

  // 4) un mensaje a un rol: grupo con quienes lo tienen hoy
  await page.goto('/messages')
  await page.getByRole('button', { name: 'Nuevo mensaje' }).click()
  const d2 = page.getByRole('dialog', { name: 'Nuevo mensaje' })
  await d2.getByRole('combobox', { name: 'Para' }).fill('dispatch')   // el mismo campo busca personas y roles
  const roleOption = d2.getByRole('listbox').getByRole('option').filter({ hasText: /Dispatcher|Despachador/ }).first()
  await expect(roleOption).toContainText('Rol ·')
  await roleOption.click()
  await expect(d2.getByText(/Se arma un grupo con las personas que tienen ese rol hoy/)).toBeVisible()
  await d2.getByLabel('Nombre del grupo (opcional)').fill(group)
  await d2.getByLabel('Mensaje', { exact: true }).fill(`Reunión de cierre ${suffix}`)
  await shot(page, isMobile, 'l53-nuevo-rol', d2)
  await d2.getByRole('button', { name: 'Enviar' }).click()
  await expect(d2).toBeHidden()
  const groupPane = page.getByRole('region', { name: group })
  await expect(groupPane.getByText(`Reunión de cierre ${suffix}`)).toBeVisible()
  await groupPane.getByRole('button', { name: 'Personas' }).click()
  const members = groupPane.getByRole('region', { name: 'Personas' })
  await expect(members.getByText('Carlos Rivera')).toBeVisible()
  await expect(members.getByText('(creó el grupo)')).toBeVisible()
  await shot(page, isMobile, 'l53-grupo', groupPane)

  // Carlos ve el grupo en su lista (en vivo)
  await carlos.getByRole('link', { name: /^Mensajes/ }).click()
  await expect(carlos.getByRole('complementary', { name: 'Conversaciones' }).getByText(group, { exact: true })).toBeVisible({ timeout: 15_000 })
  if (!isMobile) await shot(carlos, isMobile, 'l53-lista-carlos')

  // 5) la API: Carlos no puede leer un hilo del que no es parte, y las listas coinciden
  const adminThreads = (await (await request.get(`${API_URL}/api/v1/messages/threads`, { headers: asAdmin })).json()) as MessageThreadSummaryDto[]
  const carlosThreads = (await (await request.get(`${API_URL}/api/v1/messages/threads`, { headers: asCarlos })).json()) as MessageThreadSummaryDto[]
  const direct = adminThreads.find((t) => t.kind === 'DIRECT' && t.name === 'Carlos Rivera')!
  expect(carlosThreads.some((t) => t.publicId === direct.publicId)).toBe(true)
  const fake = await request.get(`${API_URL}/api/v1/messages/threads/00000000-0000-0000-0000-000000000000`, { headers: asCarlos })
  expect(fake.status()).toBe(404)
  expect(await fake.text()).toContain('Conversación no encontrada.')
  const empty = await request.post(`${API_URL}/api/v1/messages/threads/${direct.publicId}/messages`, { headers: asCarlos, data: { text: '   ' } })
  expect(empty.status()).toBe(400)
  expect(await empty.text()).toContain('Escriba el mensaje.')
  const self = await request.post(`${API_URL}/api/v1/messages/threads`, { headers: asAdmin, data: { userIds: [0], text: 'x' } })
  expect(self.status()).toBe(400)

  if (isMobile) {
    // teléfono: una cosa a la vez (lista ↔ conversación), sin scroll horizontal
    await page.goto('/messages')
    await expect(page.getByRole('complementary', { name: 'Conversaciones' })).toBeVisible()
    await expect(page.locator('.chat-pane')).toBeHidden()
    await page.getByText('Carlos Rivera').first().click()
    await expect(page.locator('.chat-pane')).toBeVisible()
    await expect(page.getByRole('complementary', { name: 'Conversaciones' })).toBeHidden()
    expect(await noHScroll(page)).toBe(true)
    await page.getByRole('button', { name: 'Volver a la lista' }).click()
    await expect(page.getByRole('complementary', { name: 'Conversaciones' })).toBeVisible()
  }
  await carlosCtx.close()
})
