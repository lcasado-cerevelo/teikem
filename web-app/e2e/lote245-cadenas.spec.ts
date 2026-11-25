// Lote 245 (P7) — Cadenas de clientes: lista, ficha, «Agregar por nombre» (simulación con un excluido), filtro de Clientes, filtro y columna de Órdenes de venta y la pestaña
// «Por cadena» del Análisis de ventas con un renglón expandido, contra el API real de pruebas (API SIN correo: Brevo__ApiKey vacía, Auth__Onboarding__Enabled=false).
// Mide: sin scroll horizontal de página, filas de la lista ≤ 56 px y que la ficha no se desborde; claro/oscuro, es/en, 1440 y 390 px (deviceScaleFactor 2).
// Capturas en SHOTS_DIR (por omisión la carpeta temporal), NO en docs.
// Correr: WEB_URL=http://localhost:5175 API_URL=http://localhost:5101 npx playwright test lote245-cadenas --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test, type Page } from '@playwright/test'

import { API_URL, apiHeaders, login, ok } from './staffHelpers'

test.use({ locale: 'es-PR', deviceScaleFactor: 2 })
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const SA = `${API_URL}/api/v1/sales-analysis`
const SHOTS = process.env.SHOTS_DIR ?? join(tmpdir(), 'lote245-p7')
const CHAIN_A = 'Walgreens P7B'
const CHAIN_B = 'CVS P7B'
const CLIENTS_A = ['WALGREENS P7B 101', 'WALGREENS P7B 102']
const CLIENTS_FREE = ['WALGREENS P7B 103', 'WALGREENS P7B 104']
const CLIENT_B = 'CVS P7B CENTRO'
const CLIENT_NONE = 'FARMACIA P7B SIN CADENA'

interface Chain { publicId: string; name: string; isActive: boolean }
interface ClientRow { publicId: string; name: string }
let chainAId = ''

test.beforeAll(async ({ request }) => {
  const headers = await apiHeaders(request)
  const chains = await ok<Chain[]>(request.get(`${API_URL}/api/v1/client-chains?includeInactive=true`, { headers }), 'cadenas')
  const chain = async (name: string, notes: string) => {
    const found = chains.find((c) => c.name === name && c.isActive)
    if (found) return found.publicId
    return (await ok<Chain>(request.post(`${API_URL}/api/v1/client-chains`, { headers, data: { name, notes } }), `cadena ${name}`)).publicId
  }
  chainAId = await chain(CHAIN_A, 'Farmacias de la cadena de prueba')
  const chainB = await chain(CHAIN_B, '')
  const client = async (name: string) => {
    const find = async () => (await ok<ClientRow[]>(request.get(`${API_URL}/api/v1/clients?search=${encodeURIComponent(name)}`, { headers }), 'clientes')).find((c) => c.name === name)
    let c = await find()
    if (!c) {
      await ok(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name } }), `cliente ${name}`)
      c = await find()
    }
    return c!.publicId
  }
  const idsA = await Promise.all(CLIENTS_A.map(client))
  const idsFree = await Promise.all(CLIENTS_FREE.map(client))
  const idB = await client(CLIENT_B)
  await client(CLIENT_NONE)
  await ok(request.post(`${API_URL}/api/v1/client-chains/${chainAId}/clients`, { headers, data: { clientPublicIds: idsA } }), 'asignar A')
  await ok(request.post(`${API_URL}/api/v1/client-chains/${chainB}/clients`, { headers, data: { clientPublicIds: [idB] } }), 'asignar B')
  // los libres empiezan sin cadena (el recorrido simula el alta masiva sobre ellos)
  for (const id of idsFree) await ok(request.put(`${API_URL}/api/v1/client-chains/by-client/${id}`, { headers, data: { chainPublicId: null } }), 'liberar')
  // historial de ventas de septiembre y octubre de 2026 solo si todavía no hay venta de la cadena
  const before = await ok<{ rows: { sales: number }[] }>(request.get(`${SA}/by-chain?month=2026-10&chainPublicId=${chainAId}`, { headers }), 'by-chain')
  if ((before.rows[0]?.sales ?? 0) === 0) {
    const csv =
      ',"Type","Date","Num","Memo","Name","Qty","Sales Price","Amount","Balance"\r\n' +
      '"Inventory",,,,,,,,,""\r\n' +
      '"AB-100 (TIRAS DE PRUEBA 1/50CT.)",,,,,,,,,""\r\n' +
      `,"Invoice","09/10/2026","P7B-0001","TIRAS","${CLIENTS_A[0]}",100,4.00,400.00,400.00\r\n` +
      `,"Invoice","10/05/2026","P7B-0002","TIRAS","${CLIENTS_A[0]}",300,4.00,1200.00,1600.00\r\n` +
      `,"Invoice","10/06/2026","P7B-0003","TIRAS","${CLIENTS_A[1]}",150,4.00,600.00,2200.00\r\n` +
      `,"Invoice","09/12/2026","P7B-0004","TIRAS","${CLIENT_B}",50,4.00,200.00,2400.00\r\n` +
      `,"Invoice","10/07/2026","P7B-0005","TIRAS","${CLIENT_B}",90,4.00,360.00,2760.00\r\n` +
      `,"Invoice","10/09/2026","P7B-0006","TIRAS","${CLIENT_NONE}",25,4.00,100.00,2860.00\r\n` +
      '"Total AB-100 (TIRAS DE PRUEBA 1/50CT.)",,,,,,715,,2860.00,2860.00\r\n' +
      '"Total Inventory",,,,,,715,,2860.00,2860.00\r\n' +
      '"TOTAL",,,,,,715,,2860.00,2860.00\r\n'
    const res = await request.post(`${SA}/import`, { headers, multipart: { salesFile: { name: 'ventas-p7b.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'latin1') }, dryRun: 'false' } })
    expect(res.ok(), await res.text()).toBeTruthy()
  }
})

async function noHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth, 'scroll horizontal de la página').toBeLessThanOrEqual(innerWidth)
}

async function setLook(page: Page, lang: 'es' | 'en', theme: 'light' | 'dark') {
  await page.evaluate(([l, th]) => {
    localStorage.setItem('teikem.lang', l)
    localStorage.setItem('teikem.theme', th)
    // los paneles recuerdan abierto/cerrado: el recorrido parte siempre de lo de fábrica
    Object.keys(localStorage).filter((k) => k.startsWith('teikem.panel.chains')).forEach((k) => localStorage.removeItem(k))
  }, [lang, theme] as const)
  await page.reload()
}

const LOOKS = [
  { lang: 'es', theme: 'light', width: 1440, height: 1100, tag: 'escritorio' },
  { lang: 'en', theme: 'dark', width: 1440, height: 1100, tag: 'escritorio' },
  { lang: 'es', theme: 'dark', width: 390, height: 900, tag: 'movil' },
  { lang: 'en', theme: 'light', width: 390, height: 900, tag: 'movil' },
] as const

for (const look of LOOKS) {
  test(`Cadenas ${look.tag} ${look.lang}/${look.theme}`, async ({ page, isMobile }) => {
    test.skip(isMobile, 'el recorrido fija sus propios anchos (1440 y 390)')
    mkdirSync(SHOTS, { recursive: true })
    const { lang, theme, tag } = look
    const L = lang === 'es'
    const shot = (name: string) => page.screenshot({ path: join(SHOTS, `${name}-${tag}-${lang}-${theme}.png`), fullPage: true })
    await login(page)
    await page.setViewportSize({ width: look.width, height: look.height })

    // ---- lista y ficha
    await page.goto('/catalog/chains')
    await setLook(page, lang, theme)
    await expect(page.getByRole('heading', { name: 'Cadenas', exact: true }).or(page.getByRole('heading', { name: 'Chains', exact: true })).first()).toBeVisible({ timeout: 30_000 })
    await page.goto(`/catalog/chains?id=${chainAId}`)
    await expect(page.getByRole('heading', { name: CHAIN_A })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('WALGREENS P7B 101').first()).toBeVisible()
    await noHorizontalScroll(page)
    if (look.width >= 720) {
      const rows = page.locator('.chn-split table.lst').first().locator('tbody tr')
      const n = await rows.count()
      expect(n).toBeGreaterThan(0)
      for (let i = 0; i < n; i++) expect((await rows.nth(i).boundingBox())!.height, `alto de la fila ${i} de la lista`).toBeLessThanOrEqual(56)
    }
    const detail = (await page.locator('.chn-detail').boundingBox())!
    const overflow = await page.locator('.chn-detail').evaluate((el) => el.scrollWidth - el.clientWidth)
    expect(overflow, 'la ficha no se desborda').toBeLessThanOrEqual(1)
    expect(detail.x + detail.width).toBeLessThanOrEqual(look.width + 1)
    await shot('lista-ficha')

    // ---- agregar por nombre: simulación con un excluido (no se aplica: el recorrido se puede repetir)
    await page.getByRole('heading', { name: L ? 'Agregar por nombre' : 'Add by name' }).click()
    await page.getByLabel(L ? 'El nombre empieza con' : 'Name starts with').fill('Walgreens P7B')
    await page.getByRole('button', { name: L ? 'Simular' : 'Simulate' }).click()
    await expect(page.getByText('WALGREENS P7B 103').first()).toBeVisible({ timeout: 30_000 })
    await page.getByRole('checkbox', { name: (L ? 'Incluir a ' : 'Include ') + 'WALGREENS P7B 104' }).uncheck()
    await expect(page.getByText(L ? 'Excluido' : 'Excluded').first()).toBeVisible()
    await expect(page.getByRole('button', { name: L ? 'Aplicar a 1 clientes' : 'Apply to 1 clients' })).toBeEnabled()
    await noHorizontalScroll(page)
    await shot('simulacion')

    // ---- Clientes: chip y filtro de cadena
    await page.goto('/catalog/clients')
    await expect(page.getByRole('combobox', { name: L ? 'Cadena' : 'Chain' })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('combobox', { name: L ? 'Cadena' : 'Chain' }).locator('option', { hasText: CHAIN_A })).toHaveCount(1)
    await page.getByRole('combobox', { name: L ? 'Cadena' : 'Chain' }).selectOption({ label: CHAIN_A })
    await expect(page.locator('.cl-list').getByText('WALGREENS P7B 101')).toBeVisible({ timeout: 30_000 })
    await expect(page.locator('.cl-list').getByText(CLIENT_B)).toHaveCount(0)
    await noHorizontalScroll(page)
    await shot('clientes-filtro')

    // ---- Órdenes de venta: columna y filtro «Cadena»
    await page.goto('/sales-orders')
    await expect(page.getByText(L ? 'Cadena' : 'Chain', { exact: true }).first()).toBeVisible({ timeout: 30_000 })
    if (look.width >= 1000) {
      await expect(page.locator('tbody tr').first()).toBeVisible({ timeout: 30_000 })
      await expect(page.getByRole('columnheader', { name: L ? 'Cadena' : 'Chain' })).toBeVisible()
    }
    await page.waitForLoadState('networkidle')
    await noHorizontalScroll(page)
    await shot('ordenes-filtro')

    // ---- Análisis de ventas → Por cadena, con un renglón expandido
    await page.goto('/analytics/sales?tab=chain')
    const table = page.getByRole('table', { name: L ? 'Comparativo por cadena' : 'Comparison by chain' })
    if (look.width >= 720) {
      await expect(table).toBeVisible({ timeout: 30_000 })
      await expect(table.getByText(CHAIN_A, { exact: true })).toBeVisible()
    } else {
      await expect(page.getByText(CHAIN_A, { exact: true }).first()).toBeVisible({ timeout: 30_000 })
    }
    await page.getByRole('button', { name: (L ? 'Ver los clientes de ' : 'Show the clients of ') + CHAIN_A }).click()
    await expect(page.getByText('WALGREENS P7B 101').first()).toBeVisible()
    await noHorizontalScroll(page)
    await shot('por-cadena')
  })
}
