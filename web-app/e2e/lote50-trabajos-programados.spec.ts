// Recorrido de Sistema → Trabajos programados (Lote 50, P2.2) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000, con Jobs__PollSeconds corto para no esperar; compañía demo, admin con todos los permisos).
//
// 1) La lista trae los tres trabajos de fábrica con su horario: la limpieza del historial y la revisión diaria encendidas, el de prueba apagado.
// 2) «Ejecutar ahora» sobre el trabajo de prueba (apagado): el sistema lo toma en unos segundos y queda «Bien» con su resumen.
// 3) Cambiar el horario: el rango se valida con el mensaje del servidor; «Todos los días a una hora» y «Cada cierto tiempo» se guardan.
// 4) El interruptor enciende y apaga (con próxima corrida al encender, sin ella al apagar).
// 5) El historial muestra la corrida pedida a mano.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l50-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ScheduledJobDto = components['schemas']['ScheduledJobDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

async function shot(page: Page, isMobile: boolean, name: string, target?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  if (target) await target.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** La fila (escritorio) o la tarjeta (móvil) del trabajo. */
const rowOf = (page: Page, name: string) => page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()

test('Trabajos programados: lista, Ejecutar ahora, horario, interruptor e historial', async ({ page, request, isMobile }) => {
  const token = await apiToken(request)
  const headers = { Authorization: `Bearer ${token}` }
  const jobs = async () => (await (await request.get(`${API_URL}/api/v1/jobs`, { headers })).json()) as ScheduledJobDto[]
  const testJob = async () => (await jobs()).find((j) => j.key === 'JOB_TEST')!

  // punto de partida: el trabajo de prueba apagado y cada hora (los dos proyectos del recorrido comparten la compañía)
  const initial = await testJob()
  const reset = await request.patch(`${API_URL}/api/v1/jobs/JOB_TEST`, { headers, data: { isEnabled: false, scheduleKind: 'INTERVAL', intervalSeconds: 3600, rowVersion: initial.rowVersion } })
  expect(reset.ok(), await reset.text()).toBeTruthy()

  await login(page)
  await page.goto('/system/jobs')
  await expect(page.getByRole('heading', { level: 1, name: 'Trabajos programados' })).toBeVisible({ timeout: 20_000 })

  // 1) los tres trabajos de fábrica con su horario
  const purge = rowOf(page, 'Limpiar el historial de trabajos')
  const sweep = rowOf(page, 'Revisión diaria de inventario')
  const test = rowOf(page, 'Trabajo de prueba')
  await expect(purge).toBeVisible()
  await expect(sweep).toBeVisible()
  await expect(test).toBeVisible()
  await expect(purge.getByRole('switch')).toBeChecked()
  await expect(sweep.getByRole('switch')).toBeChecked()
  await expect(test.getByRole('switch')).not.toBeChecked()
  await expect(purge).toContainText(/Todos los días a las/)
  await expect(test).toContainText('Cada hora')
  const noHScroll = () => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l50-lista')

  // 2) «Ejecutar ahora» sobre el trabajo apagado: lo toma el sistema y queda «Bien» con su resumen
  await test.getByRole('button', { name: 'Ejecutar ahora' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('se ejecutará en unos segundos')
  // mientras está pedido o corriendo el botón no se ofrece; vuelve cuando terminó (rowVersion ya al día)
  await expect(rowOf(page, 'Trabajo de prueba').getByRole('button', { name: 'Ejecutar ahora' })).toBeHidden()
  await expect(rowOf(page, 'Trabajo de prueba').getByRole('button', { name: 'Ejecutar ahora' })).toBeVisible({ timeout: 60_000 })
  await expect(rowOf(page, 'Trabajo de prueba')).toContainText('Prueba completada.', { timeout: 60_000 })
  await expect(rowOf(page, 'Trabajo de prueba').getByText('Bien')).toBeVisible()
  await expect(rowOf(page, 'Trabajo de prueba').getByRole('switch')).not.toBeChecked() // sigue apagado
  await shot(page, isMobile, 'l50-ejecutado')

  // 3) cambiar el horario: validación del rango y los dos tipos
  await rowOf(page, 'Trabajo de prueba').getByRole('button', { name: 'Cambiar horario' }).click()
  let dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: /Horario de/ })).toBeVisible()
  await dialog.getByRole('spinbutton').fill('0')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog.getByText('El intervalo debe estar entre 60 segundos y 7 días (604800).')).toBeVisible()
  await shot(page, isMobile, 'l50-horario-error', dialog)
  await dialog.getByLabel('Tipo de horario').selectOption('DAILY')
  await dialog.getByLabel('Hora (24 horas)').fill('04:30')
  await shot(page, isMobile, 'l50-horario-diario', dialog)
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog).toBeHidden()
  await expect(rowOf(page, 'Trabajo de prueba')).toContainText(/Todos los días a las/)
  expect(await testJob()).toMatchObject({ scheduleKind: 'DAILY', dailyAt: '04:30' })

  await rowOf(page, 'Trabajo de prueba').getByRole('button', { name: 'Cambiar horario' }).click()
  dialog = page.getByRole('dialog')
  await dialog.getByLabel('Tipo de horario').selectOption('INTERVAL')
  await dialog.getByRole('spinbutton').fill('15')
  await dialog.getByLabel('Unidad').selectOption('m')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog).toBeHidden()
  await expect(rowOf(page, 'Trabajo de prueba')).toContainText('Cada 15 minutos')
  expect(await testJob()).toMatchObject({ scheduleKind: 'INTERVAL', intervalSeconds: 900 })

  // 4) el interruptor: al encender queda con próxima corrida; al apagar, sin ella
  await rowOf(page, 'Trabajo de prueba').locator('label.sw').click()
  await expect(page.locator('.toast.on').first()).toContainText('quedó encendido')
  await expect.poll(async () => (await testJob()).isEnabled).toBe(true)
  expect((await testJob()).nextRunUtc).toBeTruthy()
  await expect(rowOf(page, 'Trabajo de prueba').getByRole('switch')).toBeChecked()
  await rowOf(page, 'Trabajo de prueba').locator('label.sw').click()
  await expect.poll(async () => (await testJob()).isEnabled).toBe(false)
  expect((await testJob()).nextRunUtc).toBeFalsy()

  // 5) el historial muestra la corrida pedida a mano
  await rowOf(page, 'Trabajo de prueba').getByRole('button', { name: 'Ver historial' }).click()
  dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: /Historial de/ })).toBeVisible()
  await expect(dialog.getByText('Prueba completada.').first()).toBeVisible()
  await expect(dialog.getByText('Ejecutar ahora').first()).toBeVisible()
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l50-historial', dialog)
  await dialog.getByRole('button', { name: 'Cerrar', exact: true }).last().click()

  // el API: un horario inválido responde 400 con el mensaje exacto
  const bad = await request.patch(`${API_URL}/api/v1/jobs/JOB_TEST`, { headers, data: { scheduleKind: 'INTERVAL', intervalSeconds: 30 } })
  expect(bad.status()).toBe(400)
  expect(await bad.text()).toContain('El intervalo debe estar entre 60 segundos y 7 días (604800).')
  const missing = await request.get(`${API_URL}/api/v1/jobs/NO_EXISTE`, { headers })
  expect(missing.status()).toBe(404)
  expect(await missing.text()).toContain('Trabajo no encontrado.')
})
