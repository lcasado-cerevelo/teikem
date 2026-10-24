// Verificación VISUAL del portal (web) con Playwright: sirve el export web (npm run export:visual) con un API simulado y guarda capturas de las pantallas principales en visual-shots/.
// Uso: npm run visual  (o node scripts/visual-check.cjs). El API simulado vive en este archivo (api()); no toca ningún servidor real.
// Recorre el portal compilado (web) con Playwright contra un API simulado y guarda capturas. Uso: node run.js
const http = require('http')
const fs = require('fs')
fs.mkdirSync(process.env.PORTAL_SHOTS_DIR || require('path').join(__dirname, '..', 'visual-shots'), { recursive: true })
const path = require('path')
const { chromium } = require(path.join(__dirname, '..', '..', 'web-app', 'node_modules', 'playwright'))

const ROOT = process.env.PORTAL_EXPORT_DIR || path.join(__dirname, '..', 'dist-visual')
const OUT = process.env.PORTAL_SHOTS_DIR || path.join(__dirname, '..', 'visual-shots')
const MIME = { '.html': 'text/html', '.js': 'application/javascript', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.ttf': 'font/ttf', '.css': 'text/css', '.svg': 'image/svg+xml' }

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0])
  let f = path.join(ROOT, p)
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) f = path.join(ROOT, 'index.html')
  res.writeHead(200, { 'content-type': MIME[path.extname(f)] ?? 'application/octet-stream' })
  fs.createReadStream(f).pipe(res)
})

const uuid = (n) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const consignees = [
  { publicId: uuid(1), name: 'Farmacia Norte', line1: 'Calle 1 #12', city: 'Bayamón', state: 'PR' },
  { publicId: uuid(2), name: 'Colmado Sur', line1: 'Av. Principal 22', city: 'Ponce', state: 'PR' },
  { publicId: uuid(3), name: 'Farmacia del Sur', line1: 'Calle 9', city: 'Ponce', state: 'PR' },
  { publicId: uuid(4), name: 'Clínica San Pablo', line1: 'Carr. 2 km 10', city: 'Caguas', state: 'PR' },
]
const statuses = [['DELIVERED', 'Entregada'], ['IN_TRANSIT', 'En tránsito'], ['FAILED', 'Intento fallido'], ['PICKED_UP', 'Recogida']]
const orders = Array.from({ length: 9 }, (_, i) => ({
  publicId: uuid(100 + i), waybillNumber: `W-${1000 + i}`, orderNumber: `ORD-${200 + i}`, clientInvoiceNumber: `F-${50 + i}`,
  consigneeName: consignees[i % 4].name, consigneeCity: consignees[i % 4].city, consigneeState: 'PR', serviceTypeLabel: 'Entrega estándar',
  totalPieces: (i % 3) + 1, codAmount: i % 2 ? 25.5 * (i + 1) : 0, status: statuses[i % 4][0], statusLabel: statuses[i % 4][1], createdAtUtc: `2026-10-${String(10 + i).padStart(2, '0')}T14:00:00Z`,
}))
const page = (items) => ({ items, total: items.length, skip: 0, take: 20 })
const stock = Array.from({ length: 8 }, (_, i) => ({ productPublicId: uuid(300 + i), sku: `SKU-${100 + i}`, productName: `Producto de ejemplo ${i + 1}`, warehousePublicId: uuid(9), warehouseCode: 'ALM-DEPOT', warehouseName: 'Advance Depot', lotNumber: i % 3 ? null : `L${i}`, expiryDate: i % 3 ? null : '2027-03-01', onHand: 100 + i * 7, reserved: i, available: 100 + i * 6 }))

const svgLogo = '<svg xmlns="http://www.w3.org/2000/svg" width="180" height="36" viewBox="0 0 180 36"><rect width="36" height="36" rx="8" fill="#F37123"/><text x="46" y="25" font-family="Arial" font-weight="700" font-size="20" fill="#ffffff">ADVANCE</text></svg>'

function api(method, url) {
  const u = new URL(url)
  const p = u.pathname.replace('/api/v1/portal', '')
  const json = (b, status = 200) => ({ status, body: JSON.stringify(b), ctype: 'application/json' })
  if (u.pathname.endsWith('/brand/logos/lockup-inverted')) return { status: 200, body: svgLogo, ctype: 'image/svg+xml' }
  if (p === '/me') return json({ fullName: 'Luis Casado', email: 'luis@axiscare.com', mfaEnabled: true, currentClient: { publicId: uuid(7), name: 'AxisCare', tenantName: 'Advance Logistics', isCurrent: true }, clients: [{ publicId: uuid(7), name: 'AxisCare', tenantName: 'Advance Logistics', isCurrent: true }], capabilities: { transport: true, warehouse: true, canSeeValue: true, canCreateProducts: false, canCreateArrivals: false, canCreateExits: false, companyName: 'Advance Depot' } })
  if (p === '/brand') return json({ brandingJson: process.env.BRAND_JSON ?? null, logoSlots: ['lockup-inverted'] })
  if (p === '/summary') return json({ totalOrders: 128, byStatus: statuses.map(([s, l], i) => ({ status: s, statusLabel: l, count: [90, 20, 6, 12][i] })), recentOrders: orders.slice(0, 6) })
  if (p === '/indicators') return json({ created: 40, delivered: 30, onTimePct: 96, onTime: 29, deliveredWithPromise: 30, avgDeliveryHours: 22.5, failedAttempts: 3, openNow: 12, inTransitNow: 4, invoiceBalance: 1520.5, overdueInvoices: 1, codToRemit: 350 })
  if (p === '/orders' && method === 'GET') return json(page(orders))
  if (p === '/orders' && method === 'POST') return json({ publicId: uuid(555), waybillNumber: `W-${2000 + Math.floor(Math.random() * 900)}`, orderNumber: 'ORD-900', status: 'SUBMITTED', statusLabel: 'Sometida' })
  if (p === '/order-form') return json({ canCreate: true, consignees: consignees.map((c, i) => ({ ...c, code: `C-${100 + i}`, postalCode: '00959', phone: i % 2 ? '+1 787 555 0100' : null })), pickups: [], maxPackages: 50, clientTypesInvoiceNumber: true, clientAssignsOrderNumber: false, serviceTypes: [{ code: 'STD', label: 'Entrega estándar' }, { code: 'EXP', label: 'Expreso' }], packageTypes: [{ code: 'BOX', label: 'Caja' }, { code: 'ENV', label: 'Sobre' }], defaultServiceType: 'STD', defaultPackageType: 'BOX' })
  if (p === '/consignee-notices/consignees/bulk') return json({ done: 2, failed: 1, items: [{ publicId: uuid(1), name: 'Farmacia Norte', ok: true }, { publicId: uuid(2), name: 'Colmado Sur', ok: true }, { publicId: uuid(3), name: 'Farmacia del Sur', ok: false, message: 'El consignatario no tiene correo registrado.' }] })
  if (u.pathname.endsWith('/auth/trusted-browsers')) return json([])
  if (p === '/invoices') return json(page([{ publicId: uuid(401), number: 'F-0001', status: 'ISSUED', statusLabel: 'Emitida', issueDate: '2026-10-05', dueDate: '2026-11-04', currency: 'USD', total: 1200, paid: 200, balance: 1000 }, { publicId: uuid(402), number: 'F-0002', status: 'OVERDUE', statusLabel: 'Vencida', issueDate: '2026-09-01', dueDate: '2026-09-30', currency: 'USD', total: 520.5, paid: 0, balance: 520.5 }, { publicId: uuid(403), number: 'F-0003', status: 'PAID', statusLabel: 'Pagada', issueDate: '2026-08-01', dueDate: '2026-08-31', currency: 'USD', total: 300, paid: 300, balance: 0 }]))
  if (p === '/warehouse/stock') return json({ ...page(stock), total: stock.length })
  if (p === '/warehouse/products') return json(page(stock.map((s, i) => ({ publicId: s.productPublicId, sku: s.sku, name: s.productName, brand: 'Marca', category: 'General', barcode: `77700${i}`, onHand: s.onHand, available: s.available, trackingType: i % 3 ? 'NONE' : 'LOT' }))))
  if (p === '/warehouse/movements') return json(page(stock.map((s, i) => ({ id: i + 1, type: i % 2 ? 'Salida' : 'Entrada', sku: s.sku, productName: s.productName, quantity: i % 2 ? -5 : 20, warehouseCode: 'ALM-DEPOT', lotNumber: null, reference: `REC-0000${i}`, reason: null, createdAtUtc: `2026-10-${String(10 + i).padStart(2, '0')}T12:00:00Z` }))))
  if (p === '/warehouse/valuation') return json({ ...page(stock.map((s) => ({ productPublicId: s.productPublicId, sku: s.sku, productName: s.productName, onHand: s.onHand, unitCost: 4.25, value: s.onHand * 4.25 }))), totalValue: 5200, totalQty: 900, unvaluedQty: 0, method: 'AVERAGE' })
  if (p.startsWith('/orders/')) return json({ ...orders[0], promisedDate: '2026-10-12', stepLabels: ['Sometida','Recogida','En tránsito','Entregada'], delivery: { name: 'Farmacia Norte', line1: 'Calle 1 #12', city: 'Bayamón', state: 'PR', postalCode: '00959' }, packages: [{ description: 'Cajas de medicina', pieces: 2, weightKg: 4.5 }], timeline: [] })
  if (p === '/messages/unread') return json({ unread: 2 })
  if (p === '/messages' && method === 'GET') return json({ messages: [{ id: 1, body: 'Buenas, ¿cuándo sale el envío W-1003?', mine: true, senderName: 'Luis Casado', createdAtUtc: '2026-10-21T14:00:00Z' }, { id: 2, body: 'Sale hoy en la ruta de la tarde. Le avisamos al entregar.', mine: false, senderName: 'María', createdAtUtc: '2026-10-21T14:20:00Z' }], hasMore: false, unread: 0 })
  if (p === '/consignee-messages/unread') return json({ unread: 1 })
  if (p === '/consignee-messages' && method === 'GET') return json(page([{ publicId: uuid(1), consigneeName: 'Farmacia Norte', personName: 'Ana Rivera', lastMessagePreview: '¿A qué hora llega hoy?', lastMessageAtUtc: '2026-10-22T13:00:00Z', unread: 1 }, { publicId: uuid(2), consigneeName: 'Colmado Sur', personName: 'Pedro Díaz', lastMessagePreview: 'Recibido, gracias.', lastMessageAtUtc: '2026-10-20T10:00:00Z', unread: 0 }]))
  if (p === '/consignee-notices' && method === 'GET') return json({ enabled: true, lang: 'es', emailConfigured: true, canEdit: true })
  if (p === '/consignee-notices/consignees') return json(page(consignees.map((c, i) => ({ publicId: c.publicId, name: c.name, city: c.city, email: i % 3 === 2 ? null : `contacto${i}@ejemplo.com`, phone: i % 2 ? '+17875550100' : null, acceptsEmail: i === 0, acceptsSms: false }))))
  if (p === '/auth/config') return json({ captchaRequired: false })
  if (u.pathname.endsWith('/legal/status')) return json({ required: false, accepted: true })
  if (u.pathname.endsWith('/legal/current')) return json({ version: '1', lang: 'es', privacy: '<p>Política de privacidad de ejemplo</p>', terms: '<p>Términos de ejemplo</p>', title: 'Legal' })
  return json({})
}

async function main() {
  await new Promise((r) => server.listen(8099, r))
  const browser = await chromium.launch()
  const notes = []
  async function open(viewport, name, route, { brand = null, run, noSession = false } = {}) {
    process.env.BRAND_JSON = brand ?? ''
    const ctx = await browser.newContext({ viewport })
    const pg = await ctx.newPage()
    const errors = []
    pg.on('pageerror', (e) => errors.push(String(e)))
    pg.on('console', (m) => m.type() === 'error' && errors.push(m.text()))
    if (!noSession) await ctx.addInitScript(() => {
      localStorage.setItem('teikem.cliente.session', JSON.stringify({ accessToken: 'a', refreshToken: 'r', accessExpiresAtUtc: '2099-01-01T00:00:00Z', refreshExpiresAtUtc: '2099-01-01T00:00:00Z', tenantId: 1 }))
      localStorage.setItem('teikem.cliente.lang', 'es')
    })
    await pg.route('**/api/v1/**', async (route) => {
      const req = route.request()
      const h = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' }
      if (req.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: h })
      const r = api(req.method(), req.url())
      return route.fulfill({ status: r.status, headers: { ...h, 'content-type': r.ctype }, body: r.body })
    })
    await pg.goto(`http://localhost:8099${route}`)
    await pg.waitForTimeout(1500)
    try { if (run) await run(pg) } catch (e) { notes.push(name + ' FALLO: ' + String(e.message).split(String.fromCharCode(10))[0]) }
    await pg.waitForTimeout(500)
    await pg.screenshot({ path: path.join(OUT, `${name}.png`), fullPage: false })
    notes.push(`${name}: ${errors.length ? 'ERRORES ' + errors.slice(0, 3).join(' | ') : 'sin errores de consola'}`)
    await ctx.close()
  }

  const D = { width: 1440, height: 900 }
  const M = { width: 390, height: 844 }
  await open(D, '01-home', '/home')
  await open(D, '02-orders', '/orders')
  await open(D, '03-new-order-quick', '/order/new')
  await open(D, '04-new-order-quick-filled', '/order/new', {
    run: async (pg) => {
      // todo con el teclado: Tab recorre, flechas y Enter escogen el consignatario, Enter guarda
      await pg.getByTestId('quick-invoice').focus()
      await pg.keyboard.press('Tab')   // consignatario (desde la fecha, Tab recorre primero mes/día/año del calendario del navegador)
      await pg.keyboard.type('farm')
      await pg.waitForTimeout(300)
      await pg.keyboard.press('Enter')   // toma el primero y el foco pasa al desplegable Paquete
      await pg.waitForTimeout(200)
      await pg.keyboard.press('ArrowDown')   // en el desplegable: cambia a «Sobre»
      await pg.keyboard.press('Tab')   // COD
      await pg.keyboard.type('40')
      await pg.keyboard.press('Tab')   // desplegable Servicio
      await pg.keyboard.press('ArrowDown')   // «Expreso»
      await pg.keyboard.press('Enter')   // Enter en el desplegable guarda la fila
      await pg.waitForTimeout(800)
      await pg.getByTestId('quick-consignee').fill('colmado')
      await pg.waitForTimeout(300)
    },
  })
  await open(D, '05-invoices', '/invoices')
  await open(D, '06-warehouse', '/warehouse')
  await open(D, '07-reports', '/reports', {
    run: async (pg) => {
      await pg.getByTestId('report-shipments').click()
      await pg.getByTestId('report-run').click()
      await pg.waitForTimeout(800)
    },
  })
  await open(D, '08-home-brand-carretera', '/home', { brand: '{"preset":"carretera"}' })
  await open(M, '09-mobile-home', '/home')
  await open(M, '10-mobile-new-order', '/order/new')
  await open(D, '11-accept-invite', '/accept-invite?email=ana%40cliente.co&token=abc', { noSession: true })
  await open(D, '12-order-detail', '/order/' + uuid(100))
  await open(D, '13-reports-bystatus', '/reports', { run: async (pg) => { await pg.getByTestId('report-byStatus').click(); await pg.getByTestId('report-run').click(); await pg.waitForTimeout(800) } })
  await open(D, '14-login', '/login', { noSession: true })
  await open(D, '15-messages', '/messages')
  await open(D, '15b-messages-consignee', '/messages', { run: async (pg) => { await pg.getByTestId('conv-' + uuid(1)).click(); await pg.waitForTimeout(600) } })
  await open(D, '16-consignee-notices', '/consignee-notices')
  await open(D, '16b-consignee-notices-bulk', '/consignee-notices', { run: async (pg) => { await pg.getByTestId('notices-select-visible').click(); await pg.getByTestId('notices-bulk-email-on').click(); await pg.waitForTimeout(300) } })
  await open(D, '17-new-order-detailed', '/order/new', { run: async (pg) => { await pg.getByTestId('neworder-mode-detailed').click(); await pg.waitForTimeout(400); await pg.getByTestId('consignee').fill('cl'); await pg.waitForTimeout(300); await pg.getByTestId('consignee-suggest-0').click(); await pg.waitForTimeout(300) } })
  await open(D, '02b-orders-range', '/orders', { run: async (pg) => { await pg.getByTestId('orders-range-preset').selectOption('custom'); await pg.waitForTimeout(300); await pg.getByTestId('orders-range-from').fill('2026-10-01'); await pg.waitForTimeout(300) } })
  await open(M, '21-mobile-new-order-detailed', '/order/new', { run: async (pg) => { await pg.getByTestId('consignee').fill('farm'); await pg.waitForTimeout(300); await pg.getByTestId('consignee-suggest-0').click(); await pg.waitForTimeout(300) } })
  await open(M, '22-mobile-consignee-notices', '/consignee-notices')
  await open(M, '18-mobile-orders', '/orders')
  await open(D, '19-settings', '/settings')
  await open(M, '20-mobile-messages', '/messages')
  console.log(notes.join('\n'))
  await browser.close()
  server.close()
}
main().catch((e) => { console.error(e); process.exit(1) })
