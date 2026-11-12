// Lote 240 — fotos de productos contra el API real (SIN correo: Brevo__ApiKey vacía). Comprueba EN PANTALLA que:
//  1) la lista de «Productos e inventario» trae la miniatura CHIQUITA y la fila NO crece (con foto y sin foto);
//  2) un clic en la miniatura la amplía ahí mismo (el mouse encima no) y Esc la cierra;
//  3) con el ajuste «La compañía maneja fotos de productos» apagado desaparece la columna y el bloque «Foto» del producto, y subir foto es 422; al encender vuelve todo.
// Correr: WEB_URL=http://localhost:5174 API_URL=http://localhost:5733 npx playwright test lote240 --project=escritorio --no-deps --trace off --workers=1
import { expect, test, type APIRequestContext } from '@playwright/test'
import { deflateSync } from 'node:zlib'

import { API_URL, IMG_DIR, LETTERS, apiHeaders, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

// PNG de prueba de 240×240 (franjas de colores, armado aquí mismo: sin archivos aparte)
function makePng(size: number): Buffer {
  const raw = Buffer.alloc((size * 3 + 1) * size)
  for (let y = 0; y < size; y++) {
    raw[y * (size * 3 + 1)] = 0
    for (let x = 0; x < size; x++) {
      const o = y * (size * 3 + 1) + 1 + x * 3
      const band = Math.floor(x / 40) % 3
      raw[o] = band === 0 ? 230 : 40
      raw[o + 1] = band === 1 ? 190 : 80
      raw[o + 2] = band === 2 ? 220 : 90
    }
  }
  const crcTable = Array.from({ length: 256 }, (_, n) => {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    return c >>> 0
  })
  const crc = (b: Buffer) => {
    let c = 0xffffffff
    for (const byte of b) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8)
    return (c ^ 0xffffffff) >>> 0
  }
  const chunk = (type: string, data: Buffer) => {
    const len = Buffer.alloc(4)
    len.writeUInt32BE(data.length)
    const body = Buffer.concat([Buffer.from(type), data])
    const sum = Buffer.alloc(4)
    sum.writeUInt32BE(crc(body))
    return Buffer.concat([len, body, sum])
  }
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(size, 0)
  ihdr.writeUInt32BE(size, 4)
  ihdr[8] = 8
  ihdr[9] = 2
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))])
}
const PNG = makePng(240)

let withPhoto = ''
let withoutPhoto = ''
let productId = ''

async function setPhotos(request: APIRequestContext, enabled: boolean) {
  await ok(request.put(`${API_URL}/api/v1/tenant/settings`, { headers: await apiHeaders(request), data: { productPhotosEnabled: enabled } }), 'ajuste de fotos')
}

test.beforeAll(async ({ request }) => {
  const headers = await apiHeaders(request)
  await setPhotos(request, true)
  withPhoto = `0L240-F-${LETTERS}`
  withoutPhoto = `0L240-S-${LETTERS}`
  const a = await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: withPhoto, name: `Guantes con foto ${LETTERS}`, trackingType: 'NONE' } }), 'producto con foto')
  await ok(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: withoutPhoto, name: `Guantes sin foto ${LETTERS}`, trackingType: 'NONE' } }), 'producto sin foto')
  productId = a.product.publicId
  const up = await request.post(`${API_URL}/api/v1/products/${productId}/image`, { headers, multipart: { file: { name: 'rojo.png', mimeType: 'image/png', buffer: PNG } } })
  expect(up.ok(), await up.text()).toBeTruthy()
})

test.afterAll(async ({ request }) => {
  await setPhotos(request, true) // deja la compañía como estaba
})

test('La miniatura es chiquita, la fila no crece y un clic la amplía', async ({ page, isMobile }) => {
  await login(page)
  await page.goto('/warehouse/products')
  const search = page.getByPlaceholder(/SKU o nombre/i).first()
  if (await search.count()) await search.fill(`L240-`)
  const rowOf = (sku: string) => page.locator('tr', { hasText: sku })
  await expect(rowOf(withPhoto)).toBeVisible({ timeout: 30_000 })
  await expect(rowOf(withoutPhoto)).toBeVisible()

  // cada fila (con y sin foto) mide lo mismo que cualquier fila normal: la foto NO la agranda
  const hWith = (await rowOf(withPhoto).boundingBox())!.height
  const hWithout = (await rowOf(withoutPhoto).boundingBox())!.height
  expect(hWith, 'fila con foto').toBeLessThanOrEqual(64)
  expect(hWithout, 'fila sin foto').toBeLessThanOrEqual(64)
  expect(Math.abs(hWith - hWithout), 'las dos filas miden casi lo mismo').toBeLessThanOrEqual(4)
  const thumb = rowOf(withPhoto).locator('img.pl-rowphoto')
  await expect(thumb).toBeVisible()
  const box = (await thumb.boundingBox())!
  expect(box.width).toBeLessThanOrEqual(28)
  expect(box.height).toBeLessThanOrEqual(28)
  await rowOf(withPhoto).scrollIntoViewIfNeeded()
  const rb = (await rowOf(withPhoto).boundingBox())!
  await page.screenshot({ path: `${IMG_DIR}l240-fila-con-foto.png`, clip: { x: Math.max(0, rb.x), y: Math.max(0, rb.y - 70), width: Math.min(rb.width, 1100), height: rb.height + 140 } })
  await shot(page, isMobile, 'l240-lista-miniatura', 700)

  // el mouse encima NO la amplía; el clic SÍ, y Esc la cierra
  await thumb.hover()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await rowOf(withPhoto).getByRole('button', { name: /Ampliar la foto/ }).click()
  const big = page.getByRole('dialog')
  await expect(big).toBeVisible()
  const bigBox = (await big.locator('img').boundingBox())!
  expect(bigBox.width).toBeGreaterThan(100)
  await shot(page, isMobile, 'l240-foto-ampliada', 700)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('Con el ajuste apagado no hay columna ni bloque de foto, subir es 422; al encender vuelve', async ({ page, request, isMobile }) => {
  await setPhotos(request, false)
  const headers = await apiHeaders(request)
  const up = await request.post(`${API_URL}/api/v1/products/${productId}/image`, { headers, multipart: { file: { name: 'rojo.png', mimeType: 'image/png', buffer: PNG } } })
  expect(up.status()).toBe(422)
  expect(await up.text()).toContain('La compañía no maneja fotos de productos')

  await login(page)
  await page.goto('/warehouse/products')
  const search = page.getByPlaceholder(/SKU o nombre/i).first()
  if (await search.count()) await search.fill(`L240-`)
  await expect(page.locator('tr', { hasText: withPhoto })).toBeVisible({ timeout: 30_000 })
  await expect(page.locator('img.pl-rowphoto')).toHaveCount(0)
  await expect(page.locator('.pl-rowphoto')).toHaveCount(0)
  await shot(page, isMobile, 'l240-lista-sin-fotos', 700)

  await page.locator('tr', { hasText: withPhoto }).getByText(withPhoto).click()
  const dlg = page.getByRole('dialog')
  await expect(dlg).toBeVisible({ timeout: 20_000 })
  await expect(dlg.getByText('Foto', { exact: true })).toHaveCount(0)
  await page.keyboard.press('Escape')

  await setPhotos(request, true)
  await page.reload()
  if (await page.getByPlaceholder(/SKU o nombre/i).count()) await page.getByPlaceholder(/SKU o nombre/i).first().fill(`L240-`)
  await expect(page.locator('tr', { hasText: withPhoto }).locator('img.pl-rowphoto')).toBeVisible({ timeout: 30_000 })
})
