// Lote 244 (P7c) — Análisis de ventas → «Por ruta» (la vista por omisión), contra el API real de pruebas (API SIN correo: Brevo__ApiKey vacía,
// Auth__Onboarding__Enabled=false). Siembra rutas, representantes con usuario (titular automático), apoyos, clientes con ruta, el historial de ventas
// (CSV de QuickBooks inventado) y la cuota por ruta; después recorre la pestaña en claro/oscuro y es/en, mide la fila de ruta (≤ 48 px), que las personas
// plegadas no desborden y que no haya scroll horizontal de página (también a 360 px: proyecto «movil»).
// Capturas en SHOTS_DIR (por omisión la carpeta temporal de la sesión), NO en docs.
// Correr: WEB_URL=http://localhost:5176 API_URL=http://localhost:5102 npx playwright test lote244-ventas-por-ruta --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

import { API_URL, apiHeaders, login, ok } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)
test.describe.configure({ mode: 'serial' })

const SA = `${API_URL}/api/v1/sales-analysis`
const SHOTS = process.env.SHOTS_DIR ?? join(tmpdir(), 'lote244-p7c')

interface Named {
  id: number
  name: string
}

async function getOrCreateRoute(request: APIRequestContext, headers: Record<string, string>, name: string): Promise<number> {
  const routes = await ok<Named[]>(request.get(`${SA}/routes?includeInactive=true`, { headers }), 'rutas')
  const found = routes.find((r) => r.name === name)
  if (found) return found.id
  return (await ok<Named>(request.post(`${SA}/routes`, { headers, data: { name } }), `ruta ${name}`)).id
}

test.beforeAll(async ({ request }) => {
  const headers = await apiHeaders(request)
  const users = await ok<{ id: number; fullName: string }[]>(request.get(`${API_URL}/api/v1/users`, { headers }), 'usuarios')
  const [u1, u2, u3] = users
  const cadenas = await getOrCreateRoute(request, headers, 'Cadenas')
  const metro = await getOrCreateRoute(request, headers, 'Metro')
  await getOrCreateRoute(request, headers, 'Norte')
  // representantes con usuario: el primero de cada ruta sin titular queda como titular automático
  const reps = await ok<{ id: number; name: string; userId?: number }[]>(request.get(`${SA}/reps?includeInactive=true`, { headers }), 'representantes')
  const ensureRep = async (name: string, routeId: number, userId: number) => {
    if (reps.some((r) => r.name === name)) return
    await ok(request.post(`${SA}/reps`, { headers, data: { name, routeId, userId } }), `representante ${name}`)
  }
  await ensureRep('Representante Cadenas', cadenas, u1.id)
  await ensureRep('Representante Metro', metro, u3.id)
  // un apoyo en Cadenas (si ya lo hay, el servidor responde 409 y se sigue)
  await request.post(`${SA}/routes/${cadenas}/people`, { headers, data: { userId: u2.id, role: 'SUPPORT' } })
  // clientes: dos con ruta propia y uno sin ruta
  const clients: { name: string; route: number | null }[] = [
    { name: 'FARMACIA P7C CADENAS', route: cadenas },
    { name: 'FARMACIA P7C METRO', route: metro },
    { name: 'FARMACIA P7C SIN RUTA', route: null },
  ]
  for (const c of clients) {
    const find = async () => (await ok<{ items: { clientId: number; name: string }[] }>(request.get(`${SA}/clients?q=${encodeURIComponent(c.name)}&take=5`, { headers }), 'clientes')).items.find((x) => x.name === c.name)
    let found = await find()
    if (!found) {
      await ok(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: c.name } }), `cliente ${c.name}`)
      found = await find()
    }
    if (found && c.route) await ok(request.put(`${SA}/clients/${found.clientId}/sales`, { headers, data: { routeId: c.route } }), `ruta de ${c.name}`)
  }
  // historial de ventas (septiembre y octubre de 2026) solo si todavía no hay venta
  const before = await ok<{ totals: { sales: number } }>(request.get(`${SA}/by-route?month=2026-10`, { headers }), 'by-route')
  if (before.totals.sales === 0) {
    const csv =
      ',"Type","Date","Num","Memo","Name","Qty","Sales Price","Amount","Balance"\r\n' +
      '"Inventory",,,,,,,,,""\r\n' +
      '"AB-100 (TIRAS DE PRUEBA 1/50CT.)",,,,,,,,,""\r\n' +
      ',"Invoice","09/10/2026","P7C-0001","TIRAS","FARMACIA P7C CADENAS",100,4.00,400.00,400.00\r\n' +
      ',"Invoice","10/05/2026","P7C-0002","TIRAS","FARMACIA P7C CADENAS",300,4.00,1200.00,1600.00\r\n' +
      ',"Invoice","09/12/2026","P7C-0003","TIRAS","FARMACIA P7C METRO",50,4.00,200.00,1800.00\r\n' +
      ',"Invoice","10/07/2026","P7C-0004","TIRAS","FARMACIA P7C METRO",90,4.00,360.00,2160.00\r\n' +
      ',"Invoice","10/09/2026","P7C-0005","TIRAS","FARMACIA P7C SIN RUTA",25,4.00,100.00,2260.00\r\n' +
      '"Total AB-100 (TIRAS DE PRUEBA 1/50CT.)",,,,,,565,,2260.00,2260.00\r\n' +
      '"Total Inventory",,,,,,565,,2260.00,2260.00\r\n' +
      '"TOTAL",,,,,,565,,2260.00,2260.00\r\n'
    const res = await request.post(`${SA}/import`, { headers, multipart: { salesFile: { name: 'ventas-p7c.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'latin1') }, dryRun: 'false' } })
    expect(res.ok(), await res.text()).toBeTruthy()
  }
  // cuota por ruta de octubre y el ajuste «por ruta»
  await ok(request.put(`${SA}/settings`, { headers, data: { quotaBy: 'ROUTE' } }), 'ajuste quotaBy')
  await ok(request.put(`${SA}/quotas/2026-10`, { headers, data: { items: [{ routeId: cadenas, amount: 1500 }, { routeId: metro, amount: 800 }] } }), 'cuotas por ruta')
})

async function noHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
}

async function setLook(page: Page, lang: 'es' | 'en', theme: 'light' | 'dark') {
  await page.evaluate(([l, th]) => {
    localStorage.setItem('teikem.lang', l)
    localStorage.setItem('teikem.theme', th)
  }, [lang, theme] as const)
  await page.reload()
}

test('Por ruta: filas de ruta ≤ 48 px, personas plegables sin desbordar y sin scroll horizontal (claro/oscuro, es/en)', async ({ page, isMobile }) => {
  mkdirSync(SHOTS, { recursive: true })
  const tag = isMobile ? 'movil' : 'escritorio'
  await login(page)
  const size = page.viewportSize()!
  await page.setViewportSize({ width: size.width, height: 2400 })   // el contenido va en un contenedor con desplazamiento propio: un viewport alto lo deja entero en la captura
  for (const [lang, theme] of [['es', 'light'], ['en', 'dark']] as const) {
    await page.goto('/analytics/sales')
    await setLook(page, lang, theme)
    const tableName = lang === 'es' ? 'Ruta de venta y sus personas' : 'Sales route and its people'
    const table = page.getByRole('table', { name: tableName })
    await expect(table).toBeVisible({ timeout: 30_000 })
    await expect(table.getByText('Cadenas', { exact: true })).toBeVisible()
    await expect(page.getByRole('table', { name: lang === 'es' ? 'Totales y conciliación' : 'Totals and reconciliation' })).toBeVisible()
    await page.screenshot({ path: join(SHOTS, `por-ruta-plegado-${tag}-${lang}-${theme}.png`), fullPage: true })
    // fila de la ruta: alto normal
    const row = table.locator('tr.sa-rt-route').first()
    const box = (await row.boundingBox())!
    expect(box.height, 'alto de la fila de ruta').toBeLessThanOrEqual(isMobile ? 64 : 48)
    await noHorizontalScroll(page)
    // desplegar todas: aparecen las personas y siguen sin desbordar
    await page.getByRole('button', { name: lang === 'es' ? 'Desplegar todas' : 'Expand all' }).click()
    await expect(table.locator('tr.sa-rt-person').first()).toBeVisible()
    await page.screenshot({ path: join(SHOTS, `por-ruta-desplegado-${tag}-${lang}-${theme}.png`), fullPage: true })
    const person = (await table.locator('tr.sa-rt-person').first().boundingBox())!
    expect(person.height, 'alto de la fila de persona').toBeLessThanOrEqual(isMobile ? 64 : 48)
    await noHorizontalScroll(page)
  }
})

test('Por representante sigue disponible y los gráficos usan la cuota por ruta', async ({ page, isMobile }) => {
  const tag = isMobile ? 'movil' : 'escritorio'
  await login(page)
  const size = page.viewportSize()!
  await page.setViewportSize({ width: size.width, height: 2400 })
  await page.goto('/analytics/sales?tab=rep')
  await setLook(page, 'es', 'light')
  // en móvil la tabla pasa a tarjetas (sin rol de tabla): se busca el título del panel
  await expect(page.getByText('Comparativo por ruta y representante').first()).toBeVisible({ timeout: 30_000 })
  await noHorizontalScroll(page)
  await page.goto('/analytics/sales?tab=charts')
  await expect(page.getByText('Venta contra cuota por ruta de venta')).toBeVisible({ timeout: 30_000 })
  await noHorizontalScroll(page)
  await page.screenshot({ path: join(SHOTS, `graficos-${tag}-es-light.png`), fullPage: true })
})
