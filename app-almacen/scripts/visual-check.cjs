// Verificación VISUAL de la app de almacén en el navegador con Playwright (2026-10-22): registra el aparato, entra con PIN y recorre las pantallas principales en un teléfono (390×844) contra un
// API simulado (fixtures aquí mismo; no toca ningún servidor). La app corre como web con sql.js en lugar de expo-sqlite y expo-secure-store sobre localStorage (ver metro.config.js y scripts/visual/).
// Uso:  npm run export:visual  y luego  npm run visual   →  capturas en visual-shots/ (o PORTAL_SHOTS_DIR).
const http = require('http')
const fs = require('fs')
const path = require('path')
const { chromium } = require(path.join(__dirname, '..', '..', 'web-app', 'node_modules', 'playwright'))

const ROOT = process.env.ALMACEN_EXPORT_DIR || path.join(__dirname, '..', 'dist-visual')
const OUT = process.env.ALMACEN_SHOTS_DIR || path.join(__dirname, '..', 'visual-shots')
fs.mkdirSync(OUT, { recursive: true })
const MIME = { '.html': 'text/html', '.js': 'application/javascript', '.json': 'application/json', '.png': 'image/png', '.wasm': 'application/wasm', '.ico': 'image/x-icon', '.ttf': 'font/ttf' }

const server = http.createServer((req, res) => {
  let f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]))
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) f = path.join(ROOT, 'index.html')
  res.writeHead(200, { 'content-type': MIME[path.extname(f)] ?? 'application/octet-stream' })
  fs.createReadStream(f).pipe(res)
})

// ------------------------------------------------------------------------------------------------ API simulado
const WH = '11111111-1111-4111-8111-111111111111'
const CLIENT = '22222222-2222-4222-8222-222222222222'
const now = new Date().toISOString()
const prod = (id, sku, name, barcode) => ({ id, publicId: `33333333-3333-4333-8333-${String(id).padStart(12, '0')}`, sku, name, barcode, trackingTypeCode: 'NONE', baseUomCode: 'EA', categoryId: null, ownerClientPublicId: CLIENT, ownerName: 'AxisCare', preferredBinId: null, isActive: true })
const products = [prod(1, '10270KDRSV-1', 'EXERCISE PEDDLE SILVER VEIN', '7501001'), prod(2, '171-AC-201-M', 'GUANTES DE EXAMEN M', '7501002'), prod(3, '1040P', 'CAMA 1040 P PEDRIATICA', '7501003')]
const bins = [
  { id: 1, code: 'R1', warehousePublicId: WH, zoneId: 1, zoneCode: 'STG', zoneName: 'Recepción', zoneTypeCode: 'STAGING', isActive: true, isProvisional: false, maxCapacityQty: 40 },
  { id: 2, code: '01-A-04', warehousePublicId: WH, zoneId: 2, zoneCode: 'PCK', zoneName: 'Picking', zoneTypeCode: 'PICKING', isActive: true, isProvisional: false, maxCapacityQty: 100 },
  { id: 3, code: '01-E-03', warehousePublicId: WH, zoneId: 2, zoneCode: 'PCK', zoneName: 'Picking', zoneTypeCode: 'PICKING', isActive: true, isProvisional: false, maxCapacityQty: 40 },
]
const asns = [
  { id: 5, number: 'LLE-00005', warehousePublicId: WH, clientPublicId: CLIENT, clientName: 'AxisCare', reference: 'PRUEBA-N1', expectedDate: now.slice(0, 10), statusCode: 'EXPECTED', isActive: true, assignedToUserId: 1, lines: [{ id: 51, productPublicId: products[0].publicId, sku: products[0].sku, productName: products[0].name, expectedQty: 15, postedQty: 0 }, { id: 52, productPublicId: products[1].publicId, sku: products[1].sku, productName: products[1].name, expectedQty: 6, postedQty: 0 }] },
  { id: 6, number: 'LLE-00006', warehousePublicId: WH, clientPublicId: CLIENT, clientName: 'AxisCare', reference: 'PRUEBA-N2', expectedDate: now.slice(0, 10), statusCode: 'EXPECTED', isActive: true, assignedToUserId: null, lines: [{ id: 61, productPublicId: products[2].publicId, sku: products[2].sku, productName: products[2].name, expectedQty: 4, postedQty: 0 }] },
]
const exits = [{ id: 9, publicId: '44444444-4444-4444-8444-000000000009', number: 'SAL-00009', warehousePublicId: WH, clientName: 'AxisCare', reference: 'PRUEBA-S1', destination: 'Cliente de prueba, San Juan', statusCode: 'REQUESTED', requiredDate: now.slice(0, 10), requestedAtUtc: now, isActive: true, assignedToUserId: 1, lines: [{ id: 91, productPublicId: products[0].publicId, sku: products[0].sku, productName: products[0].name, qtyOrdered: 5, qtyReserved: 5, qtyShipped: 0, qtyBackordered: 0, reservations: [{ id: 1, binId: 2, binCode: '01-A-04', quantity: 5 }] }] }]
const balances = [{ id: 1, warehousePublicId: WH, binId: 2, productId: 1, productPublicId: products[0].publicId, lotId: null, lotNumber: null, lotExpiryDate: null, qtyOnHand: 40, qtyReserved: 5, updatedAtUtc: now, isActive: true }, { id: 2, warehousePublicId: WH, binId: 3, productId: 2, productPublicId: products[1].publicId, lotId: null, lotNumber: null, lotExpiryDate: null, qtyOnHand: 120, qtyReserved: 0, updatedAtUtc: now, isActive: true }]
const tasks = [{ id: 70, typeCode: 'PUTAWAY', type: 'Acomodo', statusCode: 'PENDING', status: 'Pendiente', priority: 0, warehousePublicId: WH, warehouseCode: 'ALM-DEPOT', productPublicId: products[0].publicId, sku: products[0].sku, productName: products[0].name, quantity: 5, fromBinId: 1, fromBinCode: 'R1', toBinId: 2, toBinCode: '01-A-04', refEntityCode: 'RECEIPT', refId: 12, refLabel: 'REC-00005', assignedToUserId: 1 }]

const page = (items) => ({ items, nextCursor: null, serverTimeUtc: new Date().toISOString() })

function api(method, url, body) {
  const u = new URL(url)
  const p = u.pathname
  const json = (b, status = 200) => ({ status, body: JSON.stringify(b) })
  if (p === '/api/v1/devices/enroll') return json({ devicePublicId: '55555555-5555-4555-8555-555555555555', deviceSecret: 'secreto', tenantName: 'Advance Depot', defaultWarehousePublicId: WH, theme: null, defaultWarehouseReceivingMode: 'PUTAWAY', receiptRequirement: 'FREE' })
  if (p === '/api/v1/auth/device/users') return json([{ userId: 1, fullName: 'Operador Uno', initials: 'OU' }, { userId: 2, fullName: 'Supervisor Almacén', initials: 'SA' }])
  if (p === '/api/v1/auth/device/login') return json({ accessToken: 'a.b.c', accessExpiresAtUtc: '2099-01-01T00:00:00Z', refreshToken: 'r', refreshExpiresAtUtc: '2099-01-01T00:00:00Z', tenantId: 1 })
  if (p === '/api/v1/devices/heartbeat') return json({ isActive: true, defaultWarehousePublicId: WH, theme: null, defaultWarehouseReceivingMode: 'PUTAWAY', receiptRequirement: 'FREE' })
  if (p === '/api/v1/me') return json({ userId: 1, fullName: 'Operador Uno', tenantId: 1, tenantName: 'Advance Depot', permissions: ['warehouse.receive', 'warehouse.pick', 'warehouse.count', 'warehouse.count.capture', 'warehouse.damage', 'warehouse.transfer', 'warehouse.issue', 'inventory.view'], enabledModules: ['WMS_LOTSERIAL'] })
  if (p === '/api/v1/tenant/settings') return json({ name: 'Advance Depot', regionCode: 'PR', timeZoneId: 'America/Puerto_Rico', currencyCode: 'USD', currencySymbol: '$', currencySymbolPosition: 'BEFORE', currencyDecimals: 2, dateOrder: 'MDY', dateSeparator: '/' })
  if (p === '/api/v1/warehouses') return json([{ id: 1, publicId: WH, code: 'ALM-DEPOT', name: 'Advance Depot', statusCode: 'ACTIVE', isActive: true, receivingModeCode: 'PUTAWAY', defaultReceivingBinId: 1, defaultReceivingBinCode: 'R1' }])
  if (p === '/api/v1/sync/products') return json(page(products))
  if (p === '/api/v1/sync/asns') return json(page(asns))
  if (p === '/api/v1/sync/exit-notices') return json(page(exits))
  if (p === '/api/v1/sync/bins') return json(page(bins))
  if (p === '/api/v1/sync/balances') return json(page(balances))
  if (p.startsWith('/api/v1/sync/')) return json(page([]))
  if (p === '/api/v1/warehouse-tasks') return json({ items: tasks, total: tasks.length })
  if (p === '/api/v1/warehouse-tasks/assignees') return json([])
  if (p === '/api/v1/warehouse-tasks/putaway-suggestions') return json([])
  if (p === '/api/v1/inventory/exit-options' || p === '/api/v1/manual-issues/reasons') return json([])
  if (p === '/api/v1/receipts' && method === 'GET') return json({ items: [], total: 0 })
  return json({})
}

async function main() {
  await new Promise((r) => server.listen(8097, r))
  const browser = await chromium.launch()
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 })
  const pg = await ctx.newPage()
  const errors = []
  pg.on('pageerror', (e) => errors.push(String(e).slice(0, 200)))
  await pg.route('http://api.test/**', async (route) => {
    const req = route.request()
    const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'content-type': 'application/json' }
    if (req.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: h })
    const r = api(req.method(), req.url(), req.postData())
    return route.fulfill({ status: r.status, headers: h, body: r.body })
  })
  const shot = async (name) => { await pg.waitForTimeout(700); await pg.screenshot({ path: path.join(OUT, `${name}.png`) }); console.log('captura', name) }
  const tryStep = async (name, fn) => { try { await fn() } catch (e) { console.log('FALLO', name, String(e.message).split('\n')[0]) } }

  await pg.goto('http://localhost:8097/')
  await pg.waitForTimeout(2500)
  await shot('01-registrar')
  await tryStep('registrar', async () => {
    await pg.getByTestId('server-url-input').fill('http://api.test/')
    await pg.getByTestId('enroll-code-input').fill('ABCD1234')
    await pg.getByText('Registrar', { exact: true }).last().click()
    await pg.waitForTimeout(2500)
  })
  await shot('02-quien-eres')
  await tryStep('usuario', async () => {
    await pg.getByText('Operador Uno').first().click()
    await pg.waitForTimeout(800)
  })
  await shot('03-pin')
  await tryStep('pin', async () => {
    for (const d of ['1', '2', '3', '4']) await pg.getByText(d, { exact: true }).first().click()
    await pg.waitForTimeout(500)
    const enter = pg.getByText('Entrar', { exact: true })
    if (await enter.count()) await enter.first().click()
    await pg.waitForTimeout(4000)
  })
  await shot('04-menu')

  const open = async (label, name, extra) => {
    await tryStep(name, async () => {
      await pg.getByText(label, { exact: false }).first().click()
      await pg.waitForTimeout(1500)
      if (extra) await extra()
      await shot(name)
    })
  }
  const back = async () => { await tryStep('volver', async () => { await pg.goBack(); await pg.waitForTimeout(900) }) }

  const home = async () => { for (let i = 0; i < 6; i++) { if ((await pg.locator('body').innerText()).includes('Teikem Almacén')) return; await pg.goBack(); await pg.waitForTimeout(700) } }
  // Primero lo que no deja un documento abierto; el recibo en curso bloquea los demás mosaicos, así que va al final.
  await open('Mis tareas', '05-mis-tareas')
  await home()
  await open('Acomodar', '10-acomodar')
  await home()
  await open('Despacho', '11-despacho')
  await home()
  await open('Consultar', '12-consultar')
  await home()
  await open('Conteo', '13-conteo')
  await home()
  await open('Recibir', '06-recibir')
  await tryStep('abrir aviso', async () => {
    await pg.getByText('PRUEBA-N1').first().click()
    await pg.waitForTimeout(1500)
    const mode = pg.getByText('Con acomodo', { exact: true })
    if (await mode.count()) { await mode.first().click(); await pg.waitForTimeout(1200) }
    await shot('07-recibir-aviso')
  })
  await tryStep('capturar linea', async () => {
    await pg.getByText('EXERCISE PEDDLE SILVER VEIN').first().click()
    await pg.waitForTimeout(1000)
    await shot('08-recibir-linea')
    await pg.keyboard.press('Control+A')
    await pg.keyboard.type('5')
    await pg.getByText('Agregar', { exact: true }).first().click()
    await pg.waitForTimeout(1200)
    await shot('09-recibir-parte')
  })
  console.log('errores de página:', errors.length ? errors.slice(0, 3).join(' | ') : 'ninguno')
  await browser.close()
  server.close()
}
main().catch((e) => { console.error(e); process.exit(1) })
