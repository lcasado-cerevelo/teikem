// Recorrido del Lote F1 (docs/frontend/loteF1-plan.md, "Recorrido Playwright") contra el API real (db-init hecho,
// API en API_URL, por defecto http://localhost:5000). Proyecto 'escritorio': pasos 1-7; proyecto 'movil' (Pixel 7 a 360 px):
// pasos 8 y 9.
// Nada de este recorrido deja cambios que rompan otra corrida: la contraseña y el MFA solo se intentan con datos inválidos.
// Capturas para el manual (docs/manual/frontend/img/f1-<pantalla>.png): las del proyecto 'escritorio' y dos de 'movil'.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type AnalyticsDefinitionDto = components['schemas']['AnalyticsDefinitionDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const DISPATCH = { email: process.env.TEIKEM_DISPATCH_EMAIL ?? 'teikem+dispatch@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }

// Interfaz en español (el idioma inicial sale del navegador si el usuario no eligió otro).
test.use({ locale: 'es-PR' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

/** Captura de la pantalla actual para el manual, cuando ya no hay peticiones pendientes. `mask` tapa datos variables o secretos. */
async function shot(page: Page, name: string, opts: { mask?: Locator[] } = {}) {
  await page.waitForLoadState('networkidle')
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({
    path: `${IMG_DIR}f1-${name}.png`,
    animations: 'disabled',
    caret: 'hide',
    mask: opts.mask,
    maskColor: '#2a3346',
  })
}

/** El control con esa etiqueta queda inválido y su descripción accesible es exactamente el mensaje (el `.ferr` bajo el campo). */
async function expectFieldError(field: Locator, message: string) {
  await expect(field).toHaveAttribute('aria-invalid', 'true')
  await expect(field).toHaveAccessibleDescription(message)
}

/** Token de acceso del admin por el API (para preparar datos del recorrido). */
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

/** Garantiza al menos un gráfico en el Pulso del admin (los indicadores de sistema ya vienen en Pulso). */
async function ensureChartInPulse(request: APIRequestContext): Promise<void> {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const res = await request.get(`${API_URL}/api/v1/analytics/charts`, { headers })
  expect(res.ok()).toBeTruthy()
  const charts = (await res.json()) as AnalyticsDefinitionDto[]
  if (charts.some((c) => c.showInPulse)) return
  let id = charts[0]?.id
  if (id == null) {
    const created = await request.post(`${API_URL}/api/v1/analytics/charts`, {
      headers,
      data: { name: `Cambios por entidad (e2e)`, dataSource: 'AUDIT_LOG', groupByField: 'EntityType', aggregateFn: 'COUNT', chartType: 'DONUT', dateRangeMode: 'ALL' },
    })
    expect(created.ok()).toBeTruthy()
    id = ((await created.json()) as AnalyticsDefinitionDto).id
  }
  const marked = await request.put(`${API_URL}/api/v1/analytics/charts/${id}/my-pulse`, { headers, data: { showInPulse: true } })
  expect(marked.ok()).toBeTruthy()
}

async function login(page: Page, user: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(user.email)
  await page.getByLabel('Contraseña').fill(user.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  // Si el usuario pertenece a varias compañías, se elige la predeterminada.
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/**
 * Sin scroll horizontal de página y sin contenido recortado. El shell recorta (`.app`/`.main` overflow hidden,
 * `.stage` overflow-x hidden, `body` overflow-x hidden): el scrollWidth del documento nunca pasa del viewport aunque algo
 * desborde. Por eso se mide cada contenedor que recorta y cada elemento visible dentro de `.stage` y `.bar` (o de `body`
 * en las pantallas sin shell): ninguno puede salirse del viewport. Los contenedores con scroll horizontal propio a
 * propósito (`.seg`, pestañas) se miden ellos mismos, no su contenido.
 */
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
        if (el.parentElement?.closest(OWN_SCROLL)) return // dentro de un contenedor con scroll propio
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

test.describe('Lote F1 — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')

  test('1. admin entra a Pulso; el menú muestra los grupos según módulos y permisos', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByRole('button', { name: 'Entrar' })).toBeVisible()
    await shot(page, 'login')
    await login(page, ADMIN)
    // Lote F8a: el título del Pulso es la fecha del día; 'Organizar mi Pulso' confirma que es la pantalla de inicio
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Organizar mi Pulso' })).toBeVisible()
    const menu = page.getByRole('complementary', { name: 'Menú principal' })
    await expect(menu.getByRole('button', { name: 'Operación' })).toBeVisible()
    await expect(menu.getByRole('link', { name: 'Pulso del día' })).toBeVisible()
  })

  test('2. despacho: el menú no muestra Sistema; Mi cuenta está disponible', async ({ page }) => {
    await login(page, DISPATCH)
    const menu = page.getByRole('complementary', { name: 'Menú principal' })
    await expect(menu.getByRole('button', { name: 'Operación' })).toBeVisible()
    await expect(menu.getByRole('button', { name: 'Sistema' })).toHaveCount(0)
    await page.getByRole('link', { name: /Carlos Rivera|despacho@teikem\.local/ }).click()
    await expect(page).toHaveURL(/\/account$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeVisible()
    await shot(page, 'mi-cuenta')
  })

  test('3. cambiar el idioma conserva la pantalla, el grupo del menú y lo escrito; los textos cambian', async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/account?tab=password')
    const current = page.getByLabel(/Contraseña actual/)
    await current.fill('a-medias')
    const group = page.getByRole('button', { name: 'Operación' })
    await group.click() // el usuario cierra el grupo
    await expect(group).toHaveAttribute('aria-expanded', 'false')

    await page.getByRole('button', { name: 'Idioma' }).click()
    await page.getByRole('menuitemradio', { name: 'English' }).click()

    await expect(page.getByRole('heading', { level: 1, name: 'My account' })).toBeVisible()
    await expect(page).toHaveURL(/\/account\?tab=password$/)
    await expect(page.getByRole('button', { name: 'Operations' })).toHaveAttribute('aria-expanded', 'false')
    await expect(page.getByLabel(/Current password/)).toHaveValue('a-medias')
    await shot(page, 'idioma-ingles')
  })

  test('4. Pulso muestra al menos un indicador y un gráfico del tenant demo', async ({ page, request }) => {
    await ensureChartInPulse(request)
    await login(page, ADMIN)
    await expect(page.getByRole('heading', { level: 2, name: 'Tus indicadores' })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: 'Tus gráficos' })).toBeVisible()
    await expect(page.locator('.river > .node').first()).toBeVisible()
    await expect(page.locator('.pulse-charts > *').first()).toBeVisible()
    await shot(page, 'pulso')
    // El shell desplaza dentro de `.stage` (una captura de página completa no lo incluye): segunda captura con los gráficos.
    await page.getByRole('heading', { level: 2, name: 'Tus gráficos' }).evaluate((el) => el.scrollIntoView({ block: 'start' }))
    await page.waitForTimeout(1500) // la animación de entrada de los gráficos es de la librería (JS), no CSS
    await shot(page, 'pulso-graficos')
  })

  test('5. Mi cuenta: sesiones lista la actual; contraseña actual incorrecta muestra el mensaje del API', async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/account?tab=sessions')
    await expect(page.getByText('Esta sesión').first()).toBeVisible()
    await shot(page, 'mi-cuenta-sesiones')

    await page.goto('/account?tab=password')
    const fields: Record<string, Locator> = {
      currentPassword: page.getByLabel(/Contraseña actual/),
      newPassword: page.getByLabel(/^Nueva contraseña/),
      confirmPassword: page.getByLabel(/^Repita la nueva contraseña/),
    }
    const newPassword = `Otra_Clave_e2e_${Date.now()}!`
    await fields.currentPassword.fill('No_Es_La_Clave_2026!')
    await fields.newPassword.fill(newPassword)
    await fields.confirmPassword.fill(newPassword)
    const [res] = await Promise.all([
      page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/password') && r.request().method() === 'PUT'),
      page.getByRole('button', { name: 'Cambiar contraseña' }).click(),
    ])
    // El API rechaza el cambio (400 validation) y su mensaje queda bajo el campo que nombra.
    expect(res.status()).toBe(400)
    const problem = (await res.json()) as { errors?: Record<string, string[]> }
    const entries = Object.entries(problem.errors ?? {})
    expect(entries.length).toBeGreaterThan(0)
    for (const [name, messages] of entries) {
      expect(fields[name], `campo del API sin control en la pantalla: ${name}`).toBeDefined()
      await expectFieldError(fields[name], messages.join(' '))
    }
    await expect(page).toHaveURL(/\/account\?tab=password$/)
    await shot(page, 'mi-cuenta-contrasena')
  })

  test('6. MFA: activar muestra la clave y pide código; uno inválido muestra el error; cancelar la deja desactivada', async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/account?tab=mfa')
    await expect(page.getByText('Desactivada')).toBeVisible()
    await shot(page, 'mi-cuenta-mfa')
    await page.getByRole('button', { name: 'Activar', exact: true }).click()
    await expect(page.getByTestId('mfa-secret')).not.toBeEmpty()
    const code = page.getByLabel(/Código de verificación/)
    await code.fill('000000')
    const [res] = await Promise.all([
      page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/mfa/totp/confirm')),
      page.getByRole('button', { name: 'Confirmar y activar' }).click(),
    ])
    // El API responde 400 validation con el error en `code`; la pantalla lo pinta bajo el campo del código.
    expect(res.status()).toBe(400)
    const problem = (await res.json()) as { errors?: Record<string, string[]> }
    expect(problem.errors?.code?.length).toBeGreaterThan(0)
    await expectFieldError(code, problem.errors?.code?.join(' ') ?? '')
    // La clave y el URI son de un enrolamiento sin confirmar, pero no se publican en el manual.
    await shot(page, 'mi-cuenta-mfa-activar', { mask: [page.getByTestId('mfa-secret'), page.getByTestId('mfa-uri')] })
    await page.getByRole('button', { name: 'Cancelar' }).click()
    await expect(page.getByText('Desactivada')).toBeVisible()
  })

  test("7. pantallas 'Sin permiso' y 'Módulo apagado'", async ({ page }) => {
    await login(page, ADMIN)
    await page.goto('/forbidden')
    await expect(page.getByTestId('forbidden-screen')).toContainText('Sin permiso')
    await shot(page, 'sin-permiso')
    await page.goto('/module-off')
    await expect(page.getByTestId('module-off-screen')).toContainText('Módulo apagado')
    await shot(page, 'modulo-apagado')
  })
})

test.describe('Lote F1 — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  test('8. menú en cajón, Pulso apila las tarjetas y no hay scroll horizontal', async ({ page, request }) => {
    await ensureChartInPulse(request)
    await login(page, ADMIN)
    expect(page.viewportSize()?.width).toBe(360)
    // Lote F8a: el título del Pulso es la fecha del día; 'Organizar mi Pulso' confirma que es la pantalla de inicio
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Organizar mi Pulso' })).toBeVisible()

    // Cajón: oculto hasta que se abre con el botón de menú
    const rail = page.locator('#app-rail')
    await expect(rail).not.toBeInViewport()
    await page.getByRole('button', { name: 'Abrir menú' }).click()
    await expect(rail).toBeInViewport()
    await expect(rail.getByRole('link', { name: 'Pulso del día' })).toBeVisible()
    await shot(page, 'menu-movil')
    // Tocar fuera del cajón (el velo, a la derecha del cajón de 280 px) lo cierra
    await page.locator('.drawer-scrim').click({ position: { x: 340, y: 400 } })
    await expect(rail).not.toBeInViewport()

    // Tarjetas apiladas: todas en una sola columna (mismo borde izquierdo)
    const cards = page.locator('.river > .node')
    await expect(cards.first()).toBeVisible()
    const lefts = await cards.evaluateAll((els) => els.map((el) => Math.round(el.getBoundingClientRect().left)))
    expect(new Set(lefts).size).toBe(1)
    await expect(page.locator('.pulse-charts > *').first()).toBeVisible()
    await expectNoHorizontalScroll(page)
    await shot(page, 'pulso-movil')

    // Diálogo "Mi rango de fecha" de una tarjeta: cabe a 360 px; se cancela sin guardar
    await cards.first().getByRole('button', { name: /^Cambiar mi rango de fecha de / }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    const box = await dialog.boundingBox()
    expect((box?.x ?? -1) >= 0 && (box?.x ?? 0) + (box?.width ?? 999) <= 360).toBeTruthy()
    await dialog.getByRole('button', { name: 'Cancelar' }).click()
    await expect(dialog).toHaveCount(0)
  })

  test('9. a 360 px: login, cabecera y todas las pestañas de Mi cuenta sin scroll horizontal', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByRole('button', { name: 'Entrar' })).toBeVisible()
    await expectNoHorizontalScroll(page)

    // Selección de compañía (si el admin tiene varias membresías) y cabecera con el selector de compañía
    await page.getByLabel('Correo electrónico').fill(ADMIN.email)
    await page.getByLabel('Contraseña').fill(ADMIN.password)
    await page.getByRole('button', { name: 'Entrar' }).click()
    await page.waitForURL((url) => url.pathname !== '/login')
    if (new URL(page.url()).pathname === '/select-tenant') {
      await expectNoHorizontalScroll(page)
      const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
      await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
    }
    await page.waitForURL((url) => url.pathname === '/')
    await expect(page.locator('.bar')).toBeVisible()
    await expectNoHorizontalScroll(page)

    for (const [tab, check] of [
      ['', () => page.getByRole('heading', { level: 1, name: 'Mi cuenta' })],
      ['?tab=password', () => page.getByLabel(/Contraseña actual/)],
      ['?tab=mfa', () => page.getByRole('button', { name: /^(Activar|Desactivar)$/ })],
      ['?tab=sessions', () => page.getByText('Esta sesión').first()],
    ] as const) {
      await page.goto(`/account${tab}`)
      await expect(check()).toBeVisible()
      await expectNoHorizontalScroll(page)
    }
  })
})
