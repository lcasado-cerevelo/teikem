// Recorrido del almacén de archivos (Lote 127) contra el API real: la firma y las fotos del comprobante de entrega. Por API: a una orden entregada se le adjunta una foto PNG (multipart) y la firma;
// un texto que dice ser imagen responde 400 («Solo se admiten imágenes…»); una orden sin entrega registrada responde 422; el contenido se sirve con la sesión y sin sesión es 401.
// En la pantalla: la ficha de la orden muestra «Firma y fotos» con las miniaturas, «Agregar foto» sube otra y «Quitar» la saca. Escritorio y móvil. Capturas: docs/manual/frontend/img/l127-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

// PNG de 1×1 válido
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64')

test('Anejos del comprobante: la foto y la firma se guardan cifradas, se sirven con permiso y se ven en la ficha', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Anejos ${suffix}`)
  const consignee = await createConsignee(request, headers, client.publicId, `Tienda ${suffix}`)
  const order = await createOrder(request, headers, client.publicId, consignee.publicId)
  const pending = await createOrder(request, headers, client.publicId, consignee.publicId)

  // sin entrega registrada no se adjunta nada (422)
  const early = await request.post(`${API_URL}/api/v1/orders/${pending.publicId}/delivery/files`, { headers, multipart: { kind: 'DELIVERY_PHOTO', file: { name: 'foto.png', mimeType: 'image/png', buffer: PNG } } })
  expect(early.status()).toBe(422)
  expect(await early.text()).toContain('no tiene registrada su entrega')

  await ok(request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers, data: { recipientName: `Ana ${suffix}` } }), 'entrega')
  const photo = await ok<{ publicId: string; kind: string; fileName: string; contentType: string; sizeBytes: number }>(
    request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery/files`, { headers, multipart: { kind: 'DELIVERY_PHOTO', file: { name: 'puerta ../x.png', mimeType: 'image/png', buffer: PNG } } }), 'foto')
  expect(photo).toMatchObject({ kind: 'DELIVERY_PHOTO', fileName: 'x.png', contentType: 'image/png', sizeBytes: PNG.length })
  const signature = await ok<{ publicId: string; kind: string }>(
    request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery/files`, { headers, multipart: { kind: 'SIGNATURE', file: { name: 'firma.png', mimeType: 'image/png', buffer: PNG } } }), 'firma')
  expect(signature.kind).toBe('SIGNATURE')
  // un texto disfrazado de imagen no entra
  const fake = await request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery/files`, { headers, multipart: { kind: 'DELIVERY_PHOTO', file: { name: 'x.png', mimeType: 'image/png', buffer: Buffer.from('esto no es una imagen') } } })
  expect(fake.status()).toBe(400)
  expect(await fake.text()).toContain('Solo se admiten')
  // el comprobante lista los dos; el contenido se sirve con la sesión y sin sesión es 401
  const proof = await ok<{ files: { publicId: string; kind: string }[] }>(request.get(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers }), 'comprobante')
  expect(proof.files.map((f) => f.kind).sort()).toEqual(['DELIVERY_PHOTO', 'SIGNATURE'])
  const content = await request.get(`${API_URL}/api/v1/files/${photo.publicId}`, { headers })
  expect(content.status()).toBe(200)
  expect(content.headers()['content-type']).toContain('image/png')
  expect(Buffer.from(await content.body()).equals(PNG)).toBe(true)
  expect((await request.get(`${API_URL}/api/v1/files/${photo.publicId}`)).status()).toBe(401)

  await login(page)
  await page.goto(`/orders/${order.publicId}`)
  const files = page.getByTestId('proof-files')
  await expect(files).toBeVisible({ timeout: 30_000 })
  await expect(files.getByRole('img')).toHaveCount(2, { timeout: 30_000 })
  await shot(page, isMobile, 'l127-firma-y-fotos')

  // agregar una foto desde la ficha y quitarla
  await files.getByLabel('Foto o documento del comprobante').setInputFiles({ name: 'otra.png', mimeType: 'image/png', buffer: PNG })
  await expect(files.getByRole('img')).toHaveCount(3, { timeout: 30_000 })
  await files.getByRole('button', { name: /Quitar otra\.png/ }).click()
  await expect(files.getByRole('img')).toHaveCount(2, { timeout: 30_000 })
  await shot(page, isMobile, 'l127-agregar-quitar')
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
