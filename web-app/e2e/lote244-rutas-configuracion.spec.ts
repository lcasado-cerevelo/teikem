// Lote 244 (P7b) — Configuración de rutas de venta: tabla de rutas (titular, apoyos, clientes, cuota), panel de personas de la ruta
// (agregar titular/apoyo, cerrar, anular, historial), representantes con usuario obligatorio, cuota por ruta o por persona, ajuste
// «La cuota se mide por», clientes sin ruta con asignación en bloque y «Pasar rutas desde representantes» (simular y aplicar).
// Mide el alto de las filas y que no haya scroll horizontal de la página; deja capturas claro/oscuro y es/en en SHOTS_DIR (si existe).
import { mkdirSync } from 'node:fs'
import { expect, test, type Page } from '@playwright/test'
import { API_URL, LETTERS, apiHeaders, login, ok } from './staffHelpers'

test.use({ locale: 'es-PR' })

const SHOTS = process.env.SHOTS_DIR ?? ''
const SA = `${API_URL}/api/v1/sales-analysis`

async function noHScroll(page: Page) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(over, 'scroll horizontal de la página').toBeLessThanOrEqual(1)
}

async function snap(page: Page, name: string) {
  if (!SHOTS) return
  mkdirSync(SHOTS, { recursive: true })
  await page.waitForLoadState('networkidle')
  // sin avisos emergentes ni cursor encima: la captura va limpia
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  await page.mouse.move(0, 0)
  await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: false })
}

async function setLook(page: Page, theme: 'light' | 'dark', lang: 'es' | 'en') {
  await page.evaluate(([th, lg]) => {
    localStorage.setItem('teikem.theme', th)
    localStorage.setItem('teikem.lang', lg)
  }, [theme, lang])
  await page.reload()
}

test('Configuración de rutas de venta', async ({ page, request, isMobile }) => {
  test.setTimeout(240_000)
  const headers = await apiHeaders(request)
  const routeA = await ok<{ id: number; name: string }>(request.post(`${SA}/routes`, { headers, data: { name: `Ruta E2E ${LETTERS}`, sortOrder: 900 } }), 'ruta A')
  const routeB = await ok<{ id: number; name: string }>(request.post(`${SA}/routes`, { headers, data: { name: `Zona E2E ${LETTERS}`, sortOrder: 901 } }), 'ruta B')
  const cands = await ok<{ userId: number; name: string }[]>(request.get(`${SA}/sellers/candidates?take=5`, { headers }), 'candidatos')
  expect(cands.length).toBeGreaterThan(0)
  const me = cands[0]
  const reps = await ok<{ userId?: number; isActive?: boolean }[]>(request.get(`${SA}/reps?includeInactive=true`, { headers }), 'representantes')
  const free = cands.find((c) => !reps.some((r) => r.isActive && r.userId === c.userId))
  expect(free, 'un usuario que aún no sea representante').toBeTruthy()

  await login(page)
  await page.goto('/analytics/sales?tab=setup')
  if (isMobile) {
    // a 360 px la tabla pasa a tarjetas: aquí solo se comprueba que cada sección se vea sin scroll horizontal de la página
    await expect(page.getByText(routeA.name).first()).toBeVisible()
    for (const sec of ['routes', 'reps', 'quotas', 'clients', 'settings']) {
      await page.goto(`/analytics/sales?tab=setup&sec=${sec}`)
      await page.waitForLoadState('networkidle')
      await noHScroll(page)
      await snap(page, `${sec}-movil-es-claro`)
    }
    for (const [th, lg] of [['dark', 'en']] as const) {
      await setLook(page, th, lg)
      await page.waitForLoadState('networkidle')
      await noHScroll(page)
      await snap(page, `settings-movil-${lg}-${th}`)
    }
    await setLook(page, 'light', 'es')
    return
  }
  const routesTable = page.getByRole('table', { name: 'Rutas de venta' })
  const row = routesTable.locator('tr', { hasText: routeA.name })
  await expect(row).toBeVisible()
  // sin titular: chip de aviso
  await expect(row.getByText('Sin titular')).toBeVisible()
  if (!isMobile) {
    const h = (await row.boundingBox())?.height ?? 999
    expect(h, 'alto de la fila de ruta').toBeLessThanOrEqual(48)
  }
  await noHScroll(page)
  await snap(page, `rutas-${isMobile ? 'movil' : 'escritorio'}-es-claro`)

  // personas de la ruta: agregar titular
  const open = async () => {
    const r = routesTable.locator('tr', { hasText: routeA.name })
    const btn = r.getByRole('button', { name: 'Personas', exact: true })
    if (await btn.count()) await btn.click()
    else {
      await r.getByRole('button', { name: /Acciones|Más/ }).click()
      await page.getByRole('menuitem', { name: 'Personas' }).click()
    }
    return page.getByRole('dialog', { name: new RegExp(`Personas de ${routeA.name}`) })
  }
  let dialog = await open()
  await expect(dialog.getByText('Esta ruta de venta no tiene personas.')).toBeVisible()
  await dialog.locator('#sa-route-user').click()
  await page.getByRole('option', { name: new RegExp(me.name) }).click()
  await dialog.locator('#sa-route-role').selectOption('PRIMARY')
  await dialog.getByRole('button', { name: 'Agregar persona' }).click()
  await expect(dialog.getByRole('cell', { name: me.name }).first()).toBeVisible()
  await expect(dialog.getByText('Vigente').first()).toBeVisible()
  await noHScroll(page)
  await snap(page, `personas-${isMobile ? 'movil' : 'escritorio'}-es-claro`)
  // segundo titular a la misma persona: el servidor responde con el mensaje exacto
  await dialog.locator('#sa-route-user').click()
  await page.getByRole('option', { name: new RegExp(me.name) }).click()
  await dialog.locator('#sa-route-role').selectOption('PRIMARY')
  await dialog.getByRole('button', { name: 'Agregar persona' }).click()
  await expect(dialog.getByRole('alert')).toContainText('La persona ya es titular de esta ruta.')
  // anular (registrada por error) con confirmación
  await dialog.getByRole('button', { name: new RegExp(`Anular la asignación de ${me.name}`) }).click()
  await page.getByRole('dialog', { name: /Anular la asignación/ }).getByRole('button', { name: 'Anular' }).click()
  await expect(dialog.getByText('Esta ruta de venta no tiene personas.')).toBeVisible()
  await dialog.getByRole('button', { name: 'Cerrar', exact: true }).last().click()
  await expect(dialog).toBeHidden()

  // la ruta vuelve a «Sin titular» y la cuota por ruta se edita
  await expect(routesTable.locator('tr', { hasText: routeA.name }).getByText('Sin titular')).toBeVisible()
  await page.goto('/analytics/sales?tab=setup&sec=quotas')
  await page.locator('#sa-q-by').selectOption('ROUTE')
  const cell = page.getByLabel(new RegExp(`Cuota de ${routeA.name}`)).first()
  await expect(cell).toBeVisible()
  await cell.fill('1500')
  await page.getByRole('button', { name: /^Guardar \(/ }).click()
  await expect(page.locator('.toast.on')).toContainText('Cuotas guardadas')
  await noHScroll(page)
  await snap(page, `cuotas-${isMobile ? 'movil' : 'escritorio'}-es-claro`)

  // ajuste: la cuota se mide por ruta
  await page.goto('/analytics/sales?tab=setup&sec=settings')
  await page.locator('#sa-set-quotaby').selectOption('ROUTE')
  await page.getByRole('button', { name: 'Guardar', exact: true }).click()
  await expect(page.locator('.toast.on')).toContainText('Ajustes del análisis guardados')
  await noHScroll(page)

  // clientes sin ruta: asignación en bloque
  const cl = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente sin ruta ${LETTERS}` } }), 'cliente')
  expect(cl.publicId).toBeTruthy()
  await page.goto('/analytics/sales?tab=setup&sec=clients')
  const panel = page.locator('.panel', { hasText: 'Clientes sin ruta de venta' }).first()
  await expect(panel).toBeVisible()
  const filtered = page.waitForResponse((r) => r.url().includes('/sales-analysis/clients?') && r.url().includes('q='))
  await panel.getByPlaceholder(/Buscar cliente/).fill(`sin ruta ${LETTERS}`)
  await filtered
  await page.waitForLoadState('networkidle')
  await panel.getByRole('checkbox', { name: new RegExp(`Elegir Cliente sin ruta ${LETTERS}`) }).or(panel.getByRole('checkbox', { name: new RegExp(`Cliente sin ruta ${LETTERS}`) })).first().check()
  await panel.locator('#sa-route-bulk').selectOption({ label: routeB.name })
  await noHScroll(page)
  await snap(page, `clientes-${isMobile ? 'movil' : 'escritorio'}-es-claro`)
  await panel.getByRole('button', { name: 'Asignar ruta' }).click()
  await expect(page.locator('.toast.on')).toContainText('Se asignó la ruta a 1 clientes')
  await noHScroll(page)

  // un representante de antes con ruta y un cliente que aún no la tiene: la simulación lo pasa a la ruta y deja a su usuario como titular
  const sur = await ok<{ id: number }>(request.post(`${SA}/reps`, { headers, data: { name: `Representante Sur ${LETTERS}`, userId: free!.userId, routeId: routeB.id } }), 'representante Sur')
  const cs = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente del representante Sur ${LETTERS}` } }), 'cliente del representante')
  await ok(request.put(`${SA}/clients/${cs.id}/rep`, { headers, data: { repId: sur.id } }), 'representante del cliente')

  // pasar rutas desde representantes: simula y se queda sin escribir
  await page.goto('/analytics/sales?tab=setup')
  await page.getByRole('button', { name: 'Pasar rutas desde representantes' }).click()
  const sim = page.getByRole('dialog', { name: /Crear las rutas desde los representantes/ })
  await sim.getByRole('button', { name: 'Simular' }).click()
  await expect(sim.getByText('Simulación: no se guardó nada.')).toBeVisible()
  await page.waitForLoadState('networkidle')
  await noHScroll(page)
  await snap(page, `desde-representantes-${isMobile ? 'movil' : 'escritorio'}-es-claro`)
  await sim.getByRole('button', { name: 'Cerrar', exact: true }).last().click()
  await ok(request.put(`${SA}/clients/${cs.id}/rep`, { headers, data: { repId: null } }), 'quitar representante del cliente')
  await ok(request.post(`${SA}/reps/${sur.id}/deactivate`, { headers }), 'desactivar representante Sur')   // libera el usuario para el paso siguiente

  // representantes: el usuario es obligatorio
  await page.goto('/analytics/sales?tab=setup&sec=reps')
  await page.getByRole('button', { name: 'Nuevo representante' }).click()
  const repDlg = page.getByRole('dialog', { name: 'Nuevo representante' })
  await repDlg.getByLabel('Nombre').fill(`Rep E2E ${LETTERS}`)
  await repDlg.getByRole('button', { name: 'Guardar' }).click()
  await expect(repDlg.getByRole('alert')).toContainText('El usuario es obligatorio.')
  await repDlg.getByLabel(/^Usuario/).selectOption(String(free?.userId))
  await repDlg.getByLabel('Ruta').selectOption({ label: routeA.name })
  await snap(page, `representante-${isMobile ? 'movil' : 'escritorio'}-es-claro`)
  await repDlg.getByRole('button', { name: 'Guardar' }).click()
  // el representante con usuario en una ruta sin titular la deja con titular automático
  await expect(page.locator('.toast.on')).toContainText('quedó como titular de su ruta')
  await noHScroll(page)

  // claro/oscuro y es/en
  for (const [th, lg] of [['dark', 'es'], ['light', 'en'], ['dark', 'en']] as const) {
    await page.goto('/analytics/sales?tab=setup')
    await setLook(page, th, lg)
    await expect(page.locator('table').first()).toBeVisible()
    await noHScroll(page)
    await snap(page, `rutas-${isMobile ? 'movil' : 'escritorio'}-${lg}-${th === 'dark' ? 'oscuro' : 'claro'}`)
    await page.goto('/analytics/sales?tab=setup&sec=quotas')
    await expect(page.locator('#sa-q-by')).toBeVisible()
    await noHScroll(page)
    await snap(page, `cuotas-${isMobile ? 'movil' : 'escritorio'}-${lg}-${th === 'dark' ? 'oscuro' : 'claro'}`)
  }
  await setLook(page, 'light', 'es')

  // limpieza: el representante de la prueba se desactiva para liberar su usuario en la siguiente corrida
  const after = await ok<{ id: number; name: string }[]>(request.get(`${SA}/reps?includeInactive=true`, { headers }), 'representantes')
  const mine = after.find((r) => r.name === `Rep E2E ${LETTERS}`)
  if (mine) await ok(request.post(`${SA}/reps/${mine.id}/deactivate`, { headers }), 'desactivar representante')
})

// «Necesita tu atención» (Pulso): la ruta sin titular que ya tiene clientes sale como fila de peligro con «Revisar» hacia Configuración → Rutas.
test('Necesita tu atención: ruta de venta sin titular con clientes', async ({ page, request, isMobile }) => {
  test.setTimeout(120_000)
  const headers = await apiHeaders(request)
  const route = await ok<{ id: number; name: string }>(request.post(`${SA}/routes`, { headers, data: { name: `Ruta atención ${LETTERS}`, sortOrder: 902 } }), 'ruta')
  const cl = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente atención ${LETTERS}` } }), 'cliente')
  await ok(request.put(`${SA}/clients/${cl.id}/sales`, { headers, data: { routeId: route.id } }), 'ruta del cliente')
  await login(page)
  await page.goto('/')
  const att = page.locator('section.inbox', { has: page.getByRole('heading', { name: 'Necesita tu atención' }) })
  await expect(att).toBeVisible({ timeout: 30_000 })
  await expect(att.getByText(route.name).first(), 'la fila de la ruta sin titular debe salir en Necesita tu atención').toBeVisible({ timeout: 30_000 })
  await noHScroll(page)
  await snap(page, `atencion-${isMobile ? 'movil' : 'escritorio'}-es-claro`)
})
