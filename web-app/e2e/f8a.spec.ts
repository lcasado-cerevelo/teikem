// Recorrido del Lote F8a (docs/frontend/loteF8-plan.json, "recorrido") contra el API real (db-init hecho, API en API_URL,
// por defecto http://localhost:5000). Proyecto 'escritorio': pasos 1-9, en serie (comparten el rol/usuario "Almacén E2E"
// del beforeAll); proyecto 'movil' (Pixel 7 a 360 px): paso 10.
// El rol y el usuario E2E (Almacén E2E {timestamp} / e2e+{timestamp}@cerevelo.com) son nuevos en cada corrida: el
// afterAll les quita el rol, suspende al usuario y borra el rol (no falla el recorrido si algo ya no aplica). El
// aparato que crea el paso 7 se desactiva en el afterAll. El ajuste del catálogo (paso 8) se restaura dentro del propio
// paso. El idioma solo vive en el localStorage del contexto del navegador.
// Capturas para el manual (docs/manual/frontend/img/f8a-<pantalla>.png).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type RoleDto = components['schemas']['RoleDto']
type WarehouseDto = components['schemas']['WarehouseDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }

const STAMP = Date.now()
const ROLE_NAME = `Almacén E2E ${STAMP}`
const E2E = { email: `e2e+${STAMP}@cerevelo.com`, password: 'E2eAlmacen_2026!', fullName: 'Usuario E2E Almacén' }
const DEVICE_NAME = `Tablet E2E ${STAMP}`
const CATALOG_LABEL = `Acomodar E2E ${STAMP}`

test.use({ locale: 'es-PR' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

/** Captura de la pantalla actual para el manual, cuando ya no hay peticiones pendientes. */
async function shot(page: Page, name: string, opts: { mask?: Locator[] } = {}) {
  await page.waitForLoadState('networkidle')
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({
    path: `${IMG_DIR}f8a-${name}.png`,
    animations: 'disabled',
    caret: 'hide',
    mask: opts.mask,
    maskColor: '#2a3346',
  })
}

const data = {
  token: '',
  roleId: 0,
  userId: 0,
  warehouseLabel: '',
  devicePublicId: '',
}

/** Token de acceso del admin por el API (para preparar y limpiar datos del recorrido). */
async function apiToken(request: APIRequestContext, creds: { email: string; password: string } = ADMIN): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: creds })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { ...creds, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

const auth = () => ({ Authorization: `Bearer ${data.token}` })

/** Reautenticación AAL2 del admin (rol/permisos/PIN de otros usuarios la exigen al guardar). */
async function reauth(request: APIRequestContext) {
  const res = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers: auth(), data: { password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
}

/** Rol "Almacén E2E {STAMP}" (inventory.view, warehouse.receive, analytics.view, pulse.warehouse/indicators/activity) y su usuario. */
async function prepareData(request: APIRequestContext) {
  data.token = await apiToken(request)

  const whRes = await request.get(`${API_URL}/api/v1/warehouses`, { headers: auth() })
  expect(whRes.ok()).toBeTruthy()
  const wh = ((await whRes.json()) as WarehouseDto[])[0]
  expect(wh?.publicId).toBeTruthy()
  data.warehouseLabel = `${wh?.code} · ${wh?.name}`

  const roleRes = await request.post(`${API_URL}/api/v1/roles`, {
    headers: auth(),
    data: {
      name: ROLE_NAME,
      descriptions: { es: ROLE_NAME, en: ROLE_NAME },
      permissions: ['inventory.view', 'warehouse.receive', 'analytics.view', 'pulse.warehouse', 'pulse.indicators', 'pulse.activity'],
    },
  })
  expect(roleRes.ok()).toBeTruthy()
  data.roleId = ((await roleRes.json()) as RoleDto).id ?? 0

  const userRes = await request.post(`${API_URL}/api/v1/users`, {
    headers: auth(),
    data: { email: E2E.email, fullName: E2E.fullName, password: E2E.password, roles: [ROLE_NAME] },
  })
  expect(userRes.ok()).toBeTruthy()
  data.userId = ((await userRes.json()) as { user: { id: number } }).user.id ?? 0
}

/** Deshace lo creado (rol, usuario, aparato); no falla el recorrido si algo ya no aplica. */
async function cleanupData(request: APIRequestContext) {
  if (!data.token) return
  if (data.devicePublicId)
    await request.post(`${API_URL}/api/v1/devices/${data.devicePublicId}/deactivate`, { headers: auth() }).catch(() => {})
  if (data.userId) {
    await reauth(request)
    await request.put(`${API_URL}/api/v1/users/${data.userId}/roles`, { headers: auth(), data: { roles: [] } }).catch(() => {})
    await request.put(`${API_URL}/api/v1/users/${data.userId}/membership`, { headers: auth(), data: { status: 'SUSPENDED' } }).catch(() => {})
  }
  if (data.roleId) {
    await reauth(request)
    await request.delete(`${API_URL}/api/v1/roles/${data.roleId}`, { headers: auth() }).catch(() => {})
  }
}

async function login(page: Page, user: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(user.email)
  // "Contraseña" (exacto): sin acotar, también matchea el botón "Mostrar contraseña" del login
  await page.getByLabel('Contraseña', { exact: true }).fill(user.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/**
 * El cliente del API reintenta sola ante un 403 `aal2_required` mostrando el modal global de `ReauthProvider`
 * ("Confirme su identidad"), que puede tardar un instante en aparecer tras el "Guardar" — a diferencia de
 * `isVisible()` (una sola lectura, sin esperar), `waitFor` sondea hasta el timeout antes de darlo por ausente.
 */
async function maybeReauth(page: Page, password: string) {
  const reauthDialog = page.getByRole('dialog', { name: 'Confirme su identidad' })
  const appeared = await reauthDialog
    .waitFor({ state: 'visible', timeout: 4000 })
    .then(() => true)
    .catch(() => false)
  if (appeared) {
    await reauthDialog.getByLabel('Contraseña', { exact: true }).fill(password)
    await reauthDialog.getByRole('button', { name: 'Confirmar' }).click()
  }
}

/**
 * Sección de Pulso con ese título (h2): los paneles Almacén/Actividad son `section.panel` (kit `Panel`), pero
 * Indicadores/Gráficos son `<section aria-labelledby>` con un `<h2 className="streamlabel">` (PulseSections.tsx) —
 * ambos son `<section>`, así que no hace falta distinguir por clase.
 */
function pulseSection(page: Page, title: string | RegExp): Locator {
  return page.locator('section').filter({ has: page.getByRole('heading', { level: 2, name: title, exact: typeof title === 'string' }) })
}

/** Fila de una tabla (DataTable) que contiene ese texto. */
function rowWith(page: Page, text: string): Locator {
  return page.getByRole('row').filter({ hasText: text })
}

/**
 * Celda de la fila en la columna con ese encabezado (la tabla de usuarios tiene chips 'Sí/No' en MFA y en 'PIN app':
 * se mira la columna por su posición en el encabezado).
 */
async function cellInColumn(page: Page, row: Locator, header: string): Promise<Locator> {
  // textContent (no innerText: el encabezado va en mayúsculas por CSS); el orden activo agrega ▲/▼
  const headers = await page.getByRole('columnheader').allTextContents()
  const idx = headers.findIndex((h) => h.replace(/[▲▼]/g, '').trim() === header)
  expect(idx).toBeGreaterThanOrEqual(0)
  return row.getByRole('cell').nth(idx)
}

test.describe('Lote F8a — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })

  test.beforeAll(async ({ request }, testInfo) => {
    if (testInfo.project.use.isMobile) return
    await prepareData(request)
  })

  test.afterAll(async ({ request }) => {
    await cleanupData(request)
  })

  test('1. Marca y shell: tema oscuro sin recargar, colapsar, paleta (/, "usu", Enter) y persistencia', async ({ page }) => {
    await login(page, ADMIN)
    await expect(page.getByRole('link', { name: 'Teikem' })).toBeVisible()

    await page.getByRole('button', { name: 'Oscuro' }).click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
    await expect(page).toHaveURL('/')
    await shot(page, 'shell-oscuro')
    await page.getByRole('button', { name: 'Claro' }).click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')

    // colapsar la barra lateral: no desmonta la pantalla actual (colapsada, la etiqueta del botón no queda en el árbol
    // de accesibilidad — se localiza por clase, no por su texto "Contraer"/"Expandir")
    const collapseBtn = page.locator('button.collapse')
    await collapseBtn.click()
    await expect(page.locator('.app.col')).toHaveCount(1)
    await expect(collapseBtn).toHaveAttribute('aria-pressed', 'true')
    await collapseBtn.click()
    await expect(page.locator('.app.col')).toHaveCount(0)
    await expect(collapseBtn).toHaveAttribute('aria-pressed', 'false')

    // paleta: '/' la abre, "usu" filtra a Roles y usuarios, Enter navega
    await page.keyboard.press('/')
    const dialog = page.getByRole('dialog', { name: 'Paleta de comandos' })
    await expect(dialog).toBeVisible()
    await shot(page, 'paleta')
    await dialog.getByRole('combobox').fill('usu')
    await expect(dialog.getByRole('option', { name: /Roles y usuarios/ })).toBeVisible()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL('/system/users')
    await expect(page.getByRole('heading', { level: 1, name: 'Roles y usuarios' })).toBeVisible()

    // el tema (Claro) persiste al recargar
    await page.reload()
    await expect(page.getByRole('button', { name: 'Claro' })).toHaveAttribute('aria-pressed', 'true')
  })

  test('2. Menú completo: los 7 grupos de la maqueta y "Sala de despacho" como pantalla pendiente', async ({ page }) => {
    await login(page, ADMIN)
    const rail = page.locator('nav.rnav')
    for (const label of ['Operación', 'Almacén', 'Contabilidad', 'Catálogo', 'Análisis', 'Sistema', 'Portal de clientes']) {
      await expect(rail.getByRole('button', { name: label })).toBeVisible()
    }
    await shot(page, 'menu-completo')

    await page.goto('/ops/dispatch')
    await expect(page.getByRole('heading', { level: 1, name: 'Sala de despacho' })).toBeVisible()
    await expect(page.getByText('armar y despachar rutas')).toBeVisible()
    await expect(page.getByText('Esta pantalla llega en un lote posterior.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Abrir otra pantalla' })).toBeVisible()
    await shot(page, 'pendiente-sala-despacho')
  })

  test('3. Usuario solo almacén: menú y Pulso acotados a Operación/Almacén/Análisis, sin "Tus gráficos"; /system/users → Sin permiso', async ({
    page,
  }) => {
    await login(page, E2E)
    const rail = page.locator('nav.rnav')
    for (const label of ['Operación', 'Almacén', 'Análisis']) await expect(rail.getByRole('button', { name: label })).toBeVisible()
    for (const label of ['Contabilidad', 'Catálogo', 'Sistema', 'Portal de clientes']) await expect(rail.getByRole('button', { name: label })).toHaveCount(0)

    await expect(pulseSection(page, 'Tus indicadores')).toBeVisible()
    await expect(pulseSection(page, 'Almacén')).toBeVisible()
    await expect(pulseSection(page, 'Actividad reciente')).toBeVisible()
    await expect(pulseSection(page, 'Tus gráficos')).toHaveCount(0)
    await shot(page, 'pulso-usuario-almacen')

    await page.goto('/system/users')
    await expect(page.getByRole('heading', { name: 'Sin permiso' })).toBeVisible()
    await expect(page.getByText('Su usuario no tiene permiso para ver esta pantalla.')).toBeVisible()
    await shot(page, 'sin-permiso')
  })

  test('4. Indicadores: apagar "Mostrar en Pulso" de Productos bajo mínimo lo quita del Pulso; encenderlo lo regresa', async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/analytics/indicators')
    // tarjeta más cercana (no el panel del módulo de negocio, que también es .panel y envuelve varias tarjetas)
    const card = page
      .getByText('Productos bajo mínimo', { exact: true })
      .locator('xpath=ancestor::section[contains(concat(" ", normalize-space(@class), " "), " panel ")][1]')
    const toggle = card.getByRole('switch', { name: 'Mostrar en Pulso del día' })
    // por si una corrida anterior lo dejó apagado: partir siempre de "encendido"
    if (!(await toggle.isChecked())) await Promise.all([page.waitForResponse((r) => r.url().includes('/my-pulse') && r.ok()), toggle.click()])
    await expect(toggle).toBeChecked()
    await Promise.all([page.waitForResponse((r) => r.url().includes('/my-pulse') && r.ok()), toggle.click()])
    await expect(toggle).not.toBeChecked()
    await shot(page, 'indicadores')

    await page.goto('/')
    await expect(pulseSection(page, 'Tus indicadores').getByText('Productos bajo mínimo', { exact: true })).toHaveCount(0)

    await page.goto('/analytics/indicators')
    const toggle2 = card.getByRole('switch', { name: 'Mostrar en Pulso del día' })
    await Promise.all([page.waitForResponse((r) => r.url().includes('/my-pulse') && r.ok()), toggle2.click()])
    await expect(toggle2).toBeChecked()
    await page.goto('/')
    await expect(pulseSection(page, 'Tus indicadores').getByText('Productos bajo mínimo', { exact: true })).toBeVisible()
  })

  test('5. Organizar: mi Pulso (subir Actividad reciente, ocultar Almacén, Listo, persiste, "Volver al de la compañía")', async ({ page }) => {
    await login(page, ADMIN)
    await expect(pulseSection(page, 'Actividad reciente')).toBeVisible()
    // por si una corrida anterior dejó un Pulso personal a medio organizar: partir siempre del de la compañía
    if ((await page.getByText('Pulso personal').count()) > 0) {
      await page.getByRole('button', { name: 'Volver al de la compañía' }).click()
      await page.getByRole('dialog').getByRole('button', { name: 'Volver al de la compañía' }).click()
      await expect(page.getByText('Pulso personal')).toHaveCount(0)
    }
    // por si una corrida anterior de la prueba 6 dejó el panel Almacén oculto para toda la compañía: mostrarlo
    if ((await pulseSection(page, 'Almacén').count()) === 0) {
      await page.getByRole('button', { name: 'Organizar el de la compañía' }).click()
      await page.getByRole('button', { name: 'Mostrar Almacén', exact: true }).click()
      await page.getByRole('button', { name: 'Listo' }).click()
    }
    await expect(pulseSection(page, 'Almacén')).toBeVisible()
    const titlesOf = async () => page.getByRole('heading', { level: 2 }).allTextContents()
    const before = await titlesOf()
    const activityIdx = before.findIndex((t) => t === 'Actividad reciente')
    expect(activityIdx).toBeGreaterThan(0)

    await page.getByRole('button', { name: 'Organizar mi Pulso' }).click()
    await page.getByRole('button', { name: /^Subir Actividad reciente/ }).click()
    await page.getByRole('button', { name: 'Ocultar Almacén', exact: true }).click()
    await shot(page, 'organizar-mi-pulso')
    await page.getByRole('button', { name: 'Listo' }).click()
    await expect(page.getByText('Pulso guardado')).toBeVisible()

    await expect(pulseSection(page, 'Almacén')).toHaveCount(0)
    const after = await titlesOf()
    expect(after.indexOf('Actividad reciente')).toBeLessThan(activityIdx)
    await expect(page.getByText('Pulso personal')).toBeVisible()

    // recargar conserva el orden personal
    await page.reload()
    await expect(pulseSection(page, 'Almacén')).toHaveCount(0)
    await shot(page, 'pulso-personal')

    // el usuario E2E (sin tocar su Pulso) no ve este orden personal del admin
    const otherPage = await page.context().browser()!.newPage()
    await login(otherPage, E2E)
    await expect(pulseSection(otherPage, 'Almacén')).toBeVisible()
    await otherPage.close()

    // 'Volver al de la compañía': el orden original vuelve (el ConfirmDialog usa el mismo texto como confirmLabel)
    await page.getByRole('button', { name: 'Volver al de la compañía' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Volver al de la compañía' }).click()
    await expect(pulseSection(page, 'Almacén')).toBeVisible()
    await expect(page.getByText('Pulso personal')).toHaveCount(0)
  })

  test('6. Organizar el de la compañía: ocultar el panel Almacén lo quita también del Pulso del usuario E2E; se restaura', async ({ page }) => {
    await login(page, ADMIN)
    await expect(page.getByRole('button', { name: 'Organizar el de la compañía' })).toBeVisible()
    // por si una corrida anterior dejó el panel Almacén oculto para la compañía: mostrarlo antes de empezar
    if ((await pulseSection(page, 'Almacén').count()) === 0) {
      await page.getByRole('button', { name: 'Organizar el de la compañía' }).click()
      await page.getByRole('button', { name: 'Mostrar Almacén', exact: true }).click()
      await page.getByRole('button', { name: 'Listo' }).click()
      await expect(pulseSection(page, 'Almacén')).toBeVisible()
    }
    await page.getByRole('button', { name: 'Organizar el de la compañía' }).click()
    await page.getByRole('button', { name: 'Ocultar Almacén', exact: true }).click()
    await shot(page, 'organizar-compania')
    await page.getByRole('button', { name: 'Listo' }).click()
    await expect(page.getByText('Pulso guardado')).toBeVisible()
    await expect(pulseSection(page, 'Almacén')).toHaveCount(0)

    const e2ePage = await page.context().browser()!.newPage()
    await login(e2ePage, E2E)
    await expect(pulseSection(e2ePage, 'Almacén')).toHaveCount(0)
    await e2ePage.close()

    // se restaura (no dejar el panel Almacén oculto para el resto de la compañía)
    await page.getByRole('button', { name: 'Organizar el de la compañía' }).click()
    await page.getByRole('button', { name: 'Mostrar Almacén', exact: true }).click()
    await page.getByRole('button', { name: 'Listo' }).click()
    await expect(pulseSection(page, 'Almacén')).toBeVisible()
  })

  test('7. PIN: 2846 lo asigna (Sí), 1234 se rechaza por trivial, Quitar PIN lo devuelve a No; suspender bloquea su entrada', async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/system/users')
    await page.getByRole('tab', { name: 'Usuarios' }).click()
    // filtra a un solo usuario: sin esto, la fila puede caer en otra página con muchos usuarios de prueba acumulados;
    // "Incluir suspendidos" desde ahora, porque el usuario desaparece de la lista en cuanto se suspende
    await page.getByPlaceholder('Buscar por nombre o correo…').fill(E2E.email)
    // el interruptor personalizado (.sw/.tk) tapa visualmente el input nativo: clic forzado, como en la fila (abajo)
    await page.getByRole('checkbox', { name: 'Incluir suspendidos' }).click({ force: true })
    const row = rowWith(page, E2E.email)
    await expect(row.getByRole('button', { name: 'Asignar PIN' })).toBeVisible()
    await row.getByRole('button', { name: 'Asignar PIN' }).click()

    const pinDialog = page.getByRole('dialog', { name: /^Asignar PIN/ })
    await pinDialog.getByLabel(/^PIN \(4 a 6 dígitos\)/).fill('2846')
    await pinDialog.getByLabel(/^Confirmar PIN/).fill('2846')
    await pinDialog.getByRole('button', { name: 'Guardar' }).click()
    // AAL2: si la sesión no reautenticó hace poco, aparece el modal de confirmar identidad
    await maybeReauth(page, ADMIN.password)
    await expect(pinDialog).toHaveCount(0)
    const pinCell = await cellInColumn(page, row, 'PIN app')
    await expect(pinCell.locator('.chip')).toHaveText('Sí')
    await shot(page, 'usuarios-pin-asignado')

    await row.getByRole('button', { name: 'Restablecer PIN' }).click()
    const resetDialog = page.getByRole('dialog', { name: /^Restablecer PIN/ })
    await resetDialog.getByLabel(/^PIN \(4 a 6 dígitos\)/).fill('1234')
    await resetDialog.getByLabel(/^Confirmar PIN/).fill('1234')
    await resetDialog.getByRole('button', { name: 'Guardar' }).click()
    await maybeReauth(page, ADMIN.password)
    await expect(resetDialog.getByRole('alert')).toHaveText('El PIN no puede ser una secuencia trivial.')
    await shot(page, 'pin-trivial-rechazado')
    await resetDialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(resetDialog).toHaveCount(0)

    // "Quitar PIN" vive dentro del modal (PinModal.tsx), no como acción de fila; su ConfirmDialog es OTRO Modal con
    // portal propio (no queda anidado en el DOM del primero), así que "Confirmar" se busca sin acotar al diálogo.
    await row.getByRole('button', { name: 'Restablecer PIN' }).click()
    const resetDialog2 = page.getByRole('dialog', { name: /^Restablecer PIN/ })
    await resetDialog2.getByRole('button', { name: 'Quitar PIN' }).click()
    await page.getByRole('button', { name: 'Confirmar' }).click()
    await expect(resetDialog2).toHaveCount(0)
    await expect(pinCell.locator('.chip')).toHaveText('No')

    // suspender: ya no puede entrar (el switch es controlado por el estado del servidor: .click(), no .uncheck(),
    // para no pelear con la verificación propia de uncheck() mientras la mutación todavía está en vuelo)
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/membership') && r.ok()),
      row.locator('input[type="checkbox"]').click({ force: true }),
    ])
    await expect(row.getByText('Suspendido')).toBeVisible()
    const otherPage = await page.context().browser()!.newPage()
    await otherPage.goto('/login')
    await otherPage.getByLabel('Correo electrónico').fill(E2E.email)
    await otherPage.getByLabel('Contraseña', { exact: true }).fill(E2E.password)
    await otherPage.getByRole('button', { name: 'Entrar' }).click()
    await expect(otherPage.getByRole('alert')).toBeVisible()
    await expect(otherPage).toHaveURL(/\/login/)
    await otherPage.close()

    // se reactiva para el resto del recorrido (paso 6 ya corrió; este paso queda al final por el suspenso)
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/membership') && r.ok()),
      row.locator('input[type="checkbox"]').click({ force: true }),
    ])
    await expect(row.getByText('Activo')).toBeVisible()
  })

  test('8. Aparatos: alta con código de registro, "Nuevo código de registro" distinto, desactivar y reactivar', async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/system/devices')
    // "Mostrar: Solo activos" es el filtro por defecto: el aparato desaparece de la lista en cuanto se desactiva
    await page.getByLabel('Mostrar').selectOption('all')
    await page.getByRole('button', { name: 'Nuevo aparato' }).click()
    const createDialog = page.getByRole('dialog', { name: 'Nuevo aparato' })
    await createDialog.getByLabel('Nombre').fill(DEVICE_NAME)
    await createDialog.getByRole('combobox').click()
    await page.getByRole('option', { name: new RegExp(data.warehouseLabel.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')) }).click()
    await createDialog.getByRole('button', { name: 'Guardar' }).click()

    const codeDialog = page.getByRole('dialog', { name: 'Código de registro' })
    await expect(codeDialog).toBeVisible()
    const firstCode = (await codeDialog.locator('.secret').textContent())?.trim() ?? ''
    expect(firstCode).toMatch(/^[A-Z0-9]{8}$/)
    await shot(page, 'codigo-registro')
    await codeDialog.getByRole('button', { name: 'Listo' }).click()

    const row = rowWith(page, DEVICE_NAME)
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: 'Nuevo código de registro' }).click()
    await page.getByRole('button', { name: 'Nuevo código de registro' }).last().click()
    const codeDialog2 = page.getByRole('dialog', { name: 'Código de registro' })
    const secondCode = (await codeDialog2.locator('.secret').textContent())?.trim() ?? ''
    expect(secondCode).not.toBe(firstCode)
    await codeDialog2.getByRole('button', { name: 'Listo' }).click()

    await row.getByRole('button', { name: 'Desactivar' }).click()
    await page.getByRole('button', { name: 'Desactivar' }).last().click()
    await expect(row.getByRole('button', { name: 'Reactivar' })).toBeVisible()
    await row.getByRole('button', { name: 'Reactivar' }).click()
    await page.getByRole('button', { name: 'Reactivar' }).last().click()
    await expect(row.getByRole('button', { name: 'Desactivar' })).toBeVisible()
    await shot(page, 'aparatos')

    // publicId real para la limpieza (afterAll)
    const listRes = await page.request.get(`${API_URL}/api/v1/devices`, { headers: auth() })
    const device = ((await listRes.json()) as { publicId: string; name: string }[]).find((d) => d.name === DEVICE_NAME)
    data.devicePublicId = device?.publicId ?? ''
  })

  test('9. Catálogos: ajustar la etiqueta española de un tipo de tarea; el panel Almacén del Pulso la muestra sin recargar; restaurar', async ({
    page,
  }) => {
    await login(page, ADMIN)
    await page.goto('/system/catalogs')
    await page.getByRole('button', { name: /Tipo de tarea/ }).click()
    const row = rowWith(page, 'PUTAWAY')
    await row.getByRole('button', { name: 'Ajustar' }).click()
    const adjustDialog = page.getByRole('dialog', { name: /^Ajustar/ })
    await adjustDialog.getByLabel('Etiqueta (es)').fill(CATALOG_LABEL)
    await adjustDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(adjustDialog).toHaveCount(0)
    await shot(page, 'catalogos-ajustado')

    await page.goto('/')
    await expect(pulseSection(page, 'Almacén').getByText(CATALOG_LABEL)).toBeVisible()

    await page.goto('/system/catalogs')
    await page.getByRole('button', { name: /Tipo de tarea/ }).click()
    await rowWith(page, CATALOG_LABEL).getByRole('button', { name: 'Restaurar' }).click()
    await page.getByRole('button', { name: 'Restaurar' }).last().click()
    await expect(rowWith(page, 'Putaway')).toBeVisible()
  })

  test('10. Mi cuenta → PIN de la app: fijar con contraseña actual y quitar', async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/account')
    await page.getByRole('tab', { name: 'PIN de la app' }).click()
    await page.getByRole('button', { name: /^Fijar PIN|^Cambiar PIN/ }).click()
    const setDialog = page.getByRole('dialog', { name: /^Fijar PIN|^Cambiar PIN/ })
    await setDialog.getByLabel('Contraseña actual').fill(ADMIN.password)
    await setDialog.getByLabel(/^PIN nuevo/).fill('7391')
    await setDialog.getByLabel(/^Confirmar PIN/).fill('7391')
    await setDialog.getByRole('button', { name: 'Guardar' }).click()
    await expect(setDialog).toHaveCount(0)
    await shot(page, 'mi-cuenta-pin')

    await page.getByRole('button', { name: 'Quitar PIN' }).click()
    await page.getByRole('button', { name: 'Quitar PIN' }).last().click()
    await expect(page.getByRole('button', { name: 'Fijar PIN' })).toBeVisible()
  })
})

test.describe('Lote F8a — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  test('11. Pulso, Roles y usuarios y Catálogos sin scroll horizontal; paleta desde la lupa', async ({ page }) => {
    await login(page, ADMIN)
    expect(page.viewportSize()?.width).toBe(360)
    await expect(pulseSection(page, 'Almacén')).toBeVisible()
    await expectNoHorizontalScroll(page)
    await shot(page, 'movil-pulso')

    await page.getByRole('button', { name: 'Abrir menú' }).click()
    await page.getByRole('button', { name: 'Sistema' }).click()
    await page.getByRole('link', { name: 'Roles y usuarios' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Roles y usuarios' })).toBeVisible()
    await expectNoHorizontalScroll(page)

    await page.goto('/system/catalogs')
    await expectNoHorizontalScroll(page)
    await shot(page, 'movil-catalogos')

    await page.getByRole('button', { name: 'Buscar o ejecutar' }).click()
    await expect(page.getByRole('dialog', { name: 'Paleta de comandos' })).toBeVisible()
    await expectNoHorizontalScroll(page)
  })
})

/** Sin scroll horizontal de página y sin contenido recortado (mismo criterio que loteF1.spec.ts/f7a.spec.ts). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    const describe = (el: Element) => `${el.tagName.toLowerCase()}${el.id ? `#${el.id}` : ''}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')}`
    for (const sel of ['.stage', '.main', '.bar']) {
      document.querySelectorAll<HTMLElement>(sel).forEach((el) => {
        if (el.scrollWidth > el.clientWidth + 1) out.push(`${describe(el)} scrollWidth ${el.scrollWidth} > ${el.clientWidth}`)
      })
    }
    const OWN_SCROLL = '.seg'
    const roots = document.querySelectorAll('.stage, .bar')
    const scope: Element[] = roots.length > 0 ? Array.from(roots) : [document.body]
    for (const root of scope) {
      root.querySelectorAll('*').forEach((el) => {
        if (el.parentElement?.closest(OWN_SCROLL)) return
        const style = getComputedStyle(el)
        if (style.visibility === 'hidden' || style.display === 'none') return
        const r = el.getBoundingClientRect()
        if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${describe(el)} [${Math.round(r.left)}, ${Math.round(r.right)}] fuera de 0..${vw}`)
      })
    }
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}
