// Recorrido de los avisos (Lote 52) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000, con
// Notifications__PollSeconds corto para no esperar; compañía demo, admin con todos los permisos).
//
// 1) Sistema → Reglas de aviso: se crea una regla sobre el aviso de prueba con destinatario el propio admin (buscador de personas con nombre y
//    correo) y la campanita encendida; correo, SMS y push salen apagados con «Este canal todavía no está configurado en Teikem.».
// 2) Registro → «Disparar aviso de prueba»: el evento pasa por la cola real y el registro muestra el envío a la campanita como Enviado.
// 3) Al abrir la aplicación con avisos sin leer, Kemi saluda con su globo «Te llegó(aron) … → Ver»; «Ver» abre la campanita con la lista; un
//    clic en un aviso lo marca leído; «Marcar todo como leído» deja el contador en cero.
// 4) Kemi: un clic abre sus opciones; «Dormir a Kemi» la esconde en la cabecera (sin globos) y un clic la despierta. La campanita no cambia.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l52-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type NotificationRuleDto = components['schemas']['NotificationRuleDto']
type NotificationLogDtoPagedResult = components['schemas']['NotificationLogDtoPagedResult']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
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
  const manualShot1 = await manualTarget(target)
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  if (manualShot1) await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
}

async function apiToken(request: APIRequestContext): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

test('Avisos: reglas, aviso de prueba por la cola real, campanita y Kemi', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const ruleName = `Regla FE52 ${suffix}`
  const token = await apiToken(request)
  const headers = { Authorization: `Bearer ${token}` }
  const noHScroll = () => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)

  await login(page)

  // 1) la regla
  await page.goto('/system/notifications')
  await expect(page.getByRole('heading', { level: 1, name: 'Reglas de aviso' })).toBeVisible({ timeout: 20_000 })
  await shot(page, isMobile, 'l52-reglas-vacio')
  await page.getByRole('button', { name: 'Nueva regla' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: 'Nueva regla' })).toBeVisible()
  await dialog.getByLabel('Evento', { exact: true }).selectOption('TEST')
  await dialog.getByLabel('Nombre de la regla', { exact: true }).fill(ruleName)
  await dialog.getByLabel('Tipo', { exact: true }).selectOption('USER')
  await dialog.getByRole('combobox', { name: 'Escoja un usuario…' }).fill('teikem')
  const options = dialog.getByRole('listbox').getByRole('option')
  await expect(options.first()).toContainText('teikem+admin@cerevelo.com')   // el buscador muestra nombre y correo
  await shot(page, isMobile, 'l52-buscador-personas', dialog)
  await options.first().click()
  for (const channel of ['Correo', 'SMS', 'Push']) await expect(dialog.getByRole('switch', { name: `Enviar por ${channel}` })).toBeDisabled()
  await expect(dialog.getByText('Este canal todavía no está configurado en Teikem.').first()).toBeVisible()
  const campanita = dialog.getByRole('switch', { name: 'Enviar por Campanita' })
  await dialog.locator('[data-channel="APP"] label.sw').click()
  await expect(campanita).toBeChecked()
  await dialog.getByRole('switch', { name: 'Avisar cuando pase el evento' }).check({ force: true })
  await shot(page, isMobile, 'l52-regla-nueva', dialog)
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog).toBeHidden()
  const row = page.locator('tr, .dt-cards > li').filter({ has: page.getByText(ruleName, { exact: true }) }).first()
  await expect(row).toContainText('Campanita')
  await expect(row.getByRole('switch')).toBeChecked()
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l52-reglas-lista')
  const rules = (await (await request.get(`${API_URL}/api/v1/notifications/rules`, { headers })).json()) as NotificationRuleDto[]
  const rule = rules.find((r) => r.name === ruleName)!
  expect(rule).toMatchObject({ eventCode: 'TEST', isActive: true })
  expect(rule.recipients).toHaveLength(1)

  // 2) el aviso de prueba por la cola real
  await page.getByRole('tab', { name: 'Registro' }).click()
  await page.getByRole('button', { name: 'Disparar aviso de prueba' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('Aviso de prueba enviado a la cola')
  await expect
    .poll(async () => {
      const log = (await (await request.get(`${API_URL}/api/v1/notifications/log?channel=APP&status=SENT&take=50`, { headers })).json()) as NotificationLogDtoPagedResult
      return (log.items ?? []).some((l) => l.rulePublicId === rule.publicId)
    }, { timeout: 60_000 })
    .toBe(true)
  await page.reload()
  await page.getByRole('tab', { name: 'Registro' }).click()
  await expect(page.locator('main')).toContainText('Enviado')
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l52-registro')

  // 3) la campanita y el globo de Kemi al abrir la aplicación con avisos sin leer
  await page.evaluate(() => sessionStorage.removeItem('teikem.kemi.greeted'))   // el saludo de Kemi es una vez por sesión: aquí se vuelve a probar
  await page.goto('/')
  const bubble = page.getByRole('status').filter({ hasText: /Te lleg/ })
  await expect(bubble).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l52-kemi-globo')
  await bubble.getByRole('button', { name: /Ver/ }).click()
  const pop = page.getByRole('dialog', { name: 'Avisos' })
  await expect(pop).toBeVisible()
  await expect(pop.getByRole('button', { name: /Aviso de prueba de Teikem/ }).first()).toBeVisible()
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l52-campanita')
  await pop.getByRole('button', { name: /Aviso de prueba de Teikem/ }).first().click()
  await expect(pop).toBeVisible()   // sin enlace, el aviso se marca leído y la campanita sigue abierta
  await pop.getByRole('button', { name: 'Marcar todo como leído' }).click()
  await expect(page.getByRole('button', { name: 'Avisos', exact: true })).toBeVisible()
  expect((await (await request.get(`${API_URL}/api/v1/inbox/unread-count`, { headers })).json()).count).toBe(0)
  await page.keyboard.press('Escape')

  // 4) Kemi: opciones, dormir y despertar (la campanita sigue donde estaba)
  await page.getByRole('button', { name: 'Kemi, asistente de Teikem' }).click()
  const panel = page.getByRole('dialog', { name: 'Kemi' })
  await expect(panel.getByRole('button', { name: 'Ver mis avisos' })).toBeVisible()
  await expect(panel.getByRole('button', { name: 'Dormir a Kemi' })).toBeVisible()
  await shot(page, isMobile, 'l52-kemi-opciones')
  await panel.getByRole('button', { name: 'Dormir a Kemi' }).click()
  await expect(page.getByRole('button', { name: 'Kemi, asistente de Teikem' })).toHaveCount(0)
  const wake = page.getByRole('button', { name: 'Despertar a Kemi' })
  await expect(wake).toBeVisible()
  await expect(page.getByRole('button', { name: 'Avisos', exact: true })).toBeVisible()
  await shot(page, isMobile, 'l52-kemi-dormida', page.locator('header.bar'))
  // recargar: sigue dormida en este navegador
  await page.reload()
  await expect(page.getByRole('button', { name: 'Despertar a Kemi' })).toBeVisible()
  await page.getByRole('button', { name: 'Despertar a Kemi' }).click()
  await expect(page.getByRole('button', { name: 'Kemi, asistente de Teikem' })).toBeVisible()
  expect(await noHScroll()).toBe(true)

  // el API: apagar la regla (para no dejar avisos en las otras corridas)
  const off = await request.patch(`${API_URL}/api/v1/notifications/rules/${rule.publicId}`, { headers, data: { isActive: false } })
  expect(off.ok(), await off.text()).toBeTruthy()
})
