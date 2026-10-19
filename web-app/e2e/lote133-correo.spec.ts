// Recorrido del Lote 133 (P11) contra el API real: integración por CORREO. Sin un buzón real, la configuración se prueba subiendo un .eml a la integración (se procesa EXACTAMENTE igual que uno que
// llegara por IMAP). Por API: validaciones del alta (400), la contraseña nunca sale (solo hasMailPassword), «Probar conexión» y una corrida contra un servidor que no existe (Falló, sin romper nada),
// y tres correos: uno bueno (remitente ORIGINAL permitido por X-Original-Sender; el cuerpo en HTML se lee por etiqueta y bloque de renglones y la plantilla de varias líneas crea una orden con
// 2 piezas), el mismo otra vez (repetido), uno reenviado de un remitente no permitido (bandeja: «Remitente no permitido») y uno sin el campo obligatorio (bandeja). Un asunto que no es de la
// integración se ignora. En pantalla: la sección Correo del formulario de edición y «Probar conexión». Escritorio y móvil. Capturas: docs/manual/frontend/img/l133-<paso>.png.
import { expect, test } from '@playwright/test'

import { contractData, withRates } from './rates'
import { API_URL, apiHeaders, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

type Run = { id: number; status: string; filesRead: number; ordersCreated: number; rejected: number; message?: string; files: { fileName: string; result: string; message?: string }[] }
type Integration = { publicId: string; name: string; hasMailPassword: boolean; email?: { host: string; port: number; allowedSenders: string[] } & Record<string, unknown>; connectorKind: string }
type Item = { id: number; status: string; reason: string; referenceKey?: string }

/** Un correo MIME mínimo (cuerpo HTML en base64 y, si se pide, un adjunto de texto). */
function eml(o: { from: string; subject: string; id: string; headers?: Record<string, string>; html?: string; text?: string; attachment?: { name: string; content: string } }) {
  const b64 = (s: string) => Buffer.from(s, 'utf-8').toString('base64').replace(/(.{76})/g, '$1\r\n')
  const boundary = `----b${o.id}`
  const head = [`From: ${o.from}`, 'To: ordenes@advance.com', `Subject: ${o.subject}`, `Message-ID: <${o.id}@test>`, 'Date: Mon, 19 Oct 2026 10:00:00 +0000', 'MIME-Version: 1.0', ...Object.entries(o.headers ?? {}).map(([k, v]) => `${k}: ${v}`)]
  const parts: string[] = []
  parts.push(o.html ? `Content-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n${b64(o.html)}` : `Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n${b64(o.text ?? '')}`)
  if (o.attachment) parts.push(`Content-Type: text/csv; name="${o.attachment.name}"\r\nContent-Disposition: attachment; filename="${o.attachment.name}"\r\nContent-Transfer-Encoding: base64\r\n\r\n${b64(o.attachment.content)}`)
  return Buffer.from([...head, `Content-Type: multipart/mixed; boundary="${boundary}"`, '', ...parts.map((p) => `--${boundary}\r\n${p}`), `--${boundary}--`, ''].join('\r\n'), 'utf-8')
}

test('Correo: remitente original, adjuntos, cuerpo por etiqueta y bloque, bandeja y prueba de conexión', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)

  const client = await ok<{ publicId: string; name: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Correo ${s}`, contract: contractData() } }), 'cliente')
  await withRates(request, headers, client as never)
  await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { clientAssignsOrderNumber: true, clientAssignsInvoiceNumber: true } }), 'numeración')
  // plantilla de varias líneas por orden: 1 orden · 2 label de la pieza · 3 descripción · 4 consignatario · 5 dirección · 6 pueblo · 7 peso
  const columns = [{ position: 1, field: 'orderNumber' }, { position: 2, field: 'pieceNumber' }, { position: 3, field: 'description' }, { position: 4, field: 'consigneeName' }, { position: 5, field: 'line1' }, { position: 6, field: 'city' }, { position: 7, field: 'weightKg' }]
  const template = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/import-templates`, {
    headers, data: { name: `Plantilla correo ${s}`, columns, defaults: { serviceType: 'STANDARD', packageType: 'BOX' }, clientPublicId: client.publicId, multiLine: true, lineRules: [] },
  }), 'plantilla')

  const bodyFields = [
    { position: 1, kind: 'LABEL', label: 'Reference #', required: true, transform: 'UPPER' },
    { position: 4, kind: 'LABEL', label: 'Nombre' },
    { position: 5, kind: 'LABEL', label: 'Dirección' },
    { position: 6, kind: 'LABEL', label: 'Pueblo' },
    { position: 2, kind: 'BLOCK', start: 'Order Summary', end: 'Total', separator: '\t', columns: [{ position: 2, index: 1 }, { position: 3, index: 2 }, { position: 7, index: 3 }] },
  ]
  const email = { host: '127.0.0.1', port: 1, useSsl: false, username: 'ordenes@advance.com', mailbox: 'INBOX', processedFolder: 'Procesados', subject: { operator: 'CONTAINS', value: 'PR Orden' }, allowedSenders: ['truss.com'], requireAttachment: false, attachmentMode: 'ALL', attachmentTypes: ['csv'], readBody: true, bodyFields }
  const base = { name: `Correo de Truss ${s}`, clientPublicId: client.publicId, connectorKind: 'EMAIL', templatePublicId: template.publicId, scopes: ['files.orders_import'], includeMe: true, email, mailPassword: 'clave-de-aplicacion' }
  const post = (data: object) => request.post(`${API_URL}/api/v1/integrations`, { headers, data })
  const bad = async (patch: object, text: string) => {
    const res = await post({ ...base, ...patch })
    expect(res.status(), await res.text()).toBe(400)
    expect(await res.text()).toContain(text)
  }

  // ---- validaciones del alta
  await bad({ email: { ...email, host: '' } }, 'Indique el servidor del buzón')
  await bad({ email: { ...email, port: 0 } }, 'El puerto debe estar entre 1 y 65535')
  await bad({ mailPassword: '' }, 'Indique la contraseña del buzón')
  await bad({ email: { ...email, allowedSenders: ['no es correo'] } }, 'no es un correo ni un dominio válido')
  await bad({ email: { ...email, subject: { operator: 'REGEX', value: '(' } } }, 'no es válida')
  await bad({ email: { ...email, bodyFields: [] } }, 'indique al menos un campo a leer')
  await bad({ email: { ...email, bodyFields: [{ position: 1, kind: 'BLOCK' }] } }, 'necesita el texto de inicio y el de fin')
  await bad({ email: { ...email, attachmentMode: 'BY_NAME' } }, 'indique la condición del nombre del adjunto')
  await bad({ email: undefined }, 'Indique el servidor del buzón')

  // ---- alta: la contraseña no vuelve nunca
  const created = await ok<Integration>(post(base), 'alta')
  expect(created.connectorKind).toBe('EMAIL')
  expect(created.hasMailPassword).toBe(true)
  expect(JSON.stringify(created)).not.toContain('clave-de-aplicacion')
  expect(created.email?.host).toBe('127.0.0.1')
  const read = await ok<Integration>(request.get(`${API_URL}/api/v1/integrations/${created.publicId}`, { headers }), 'leer')
  expect(JSON.stringify(read)).not.toContain('clave-de-aplicacion')
  const listed = await request.get(`${API_URL}/api/v1/integrations`, { headers })
  expect(await listed.text()).not.toContain('clave-de-aplicacion')

  // ---- probar la conexión y correr contra un servidor que no existe: Falló, sin romper nada
  const test1 = await ok<{ ok: boolean; message: string }>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/email/test`, { headers }), 'probar')
  expect(test1.ok).toBe(false)
  expect(test1.message).toContain('No se pudo conectar con el buzón')
  expect(test1.message).not.toContain('clave-de-aplicacion')
  const failedRun = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/run`, { headers }), 'correr')
  expect(failedRun.status).toBe('FAILED')
  expect(failedRun.message).toContain('No se pudo conectar con el buzón')

  const upload = (name: string, content: Buffer) => ok<Run>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/upload`, { headers, multipart: { file: { name, mimeType: 'message/rfc822', buffer: content } } }), `subir ${name}`)
  const html = (ref: string, withRef = true) =>
    `<html><body>${withRef ? `<p>Reference #: ${ref}</p>` : ''}<p>Nombre: Colmado Correo ${s}</p><p>Dirección: Calle Luna 9</p><p>Pueblo: Ponce</p><table><tr><td>Order Summary</td></tr>` +
    `<tr><td>PCS-${s}-1</td><td>Caja</td><td>2.5</td></tr><tr><td>PCS-${s}-2</td><td>Sobre</td><td>1</td></tr><tr><td>Total</td></tr></table></body></html>`

  // ---- correo bueno: el remitente ORIGINAL (X-Original-Sender) está permitido aunque el From sea de otro dominio
  const good = eml({ from: 'dalyshamel@advance.com', subject: `PR Orden ${s}`, id: `bueno-${s}`, headers: { 'X-Original-Sender': 'Rep Truss <rep@truss.com>' }, html: html(`pr-${s}`) })
  const ran = await upload('bueno.eml', good)
  expect(ran).toMatchObject({ status: 'OK', filesRead: 1, ordersCreated: 1, rejected: 0 })
  const orders = await ok<{ items: { publicId: string; orderNumber: string }[] }>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&take=50`, { headers }), 'órdenes')
  expect(orders.items.map((o) => o.orderNumber)).toEqual([`PR-${s}`.toUpperCase()])   // la transformación MAYÚSCULAS del campo
  const order = await ok<{ packages: { pieceNumbers: string[]; description: string }[] }>(request.get(`${API_URL}/api/v1/orders/${orders.items[0].publicId}`, { headers }), 'orden')
  expect(order.packages.flatMap((p) => p.pieceNumbers).sort()).toEqual([`PCS-${s}-1`, `PCS-${s}-2`])   // dos piezas del bloque de renglones
  expect(order.packages.map((p) => p.description).sort()).toEqual(['Caja', 'Sobre'])

  // ---- el mismo correo otra vez: repetido (mismo contenido), no se duplica nada
  const again = await upload('bueno-otra-vez.eml', good)
  expect(again.files[0].result).toBe('DUPLICATE')
  expect(again.ordersCreated).toBe(0)

  // ---- reenviado de un remitente no permitido (el From es de la compañía, el original no): bandeja con el motivo
  const forwarded = eml({ from: 'dalyshamel@advance.com', subject: `PR Orden ${s} B`, id: `reenviado-${s}`, text: `Ver abajo\r\n\r\n---------- Mensaje reenviado ----------\r\nDe: Alguien <alguien@otro.com>\r\nAsunto: PR Orden\r\n\r\nReference #: PR-B-${s}` })
  const blocked = await upload('reenviado.eml', forwarded)
  expect(blocked).toMatchObject({ status: 'FAILED', ordersCreated: 0, rejected: 1 })

  // ---- sin el campo obligatorio: bandeja
  const missing = eml({ from: 'rep@truss.com', subject: `PR Orden ${s} C`, id: `sin-ref-${s}`, html: html('x', false) })
  const noRef = await upload('sin-ref.eml', missing)
  expect(noRef).toMatchObject({ ordersCreated: 0, rejected: 1 })

  // ---- otro asunto: no es de esta integración, se ignora
  const other = await upload('otro.eml', eml({ from: 'rep@truss.com', subject: 'Cumpleaños', id: `otro-${s}`, html: html('z') }))
  expect(other).toMatchObject({ filesRead: 0, ordersCreated: 0, rejected: 0 })

  const inbox = await ok<{ items: Item[]; pending: number }>(request.get(`${API_URL}/api/v1/integrations/inbox?integration=${created.publicId}&status=PENDING`, { headers }), 'bandeja')
  expect(inbox.pending).toBe(2)
  const reasons = inbox.items.map((i) => i.reason).join(' || ')
  expect(reasons).toContain('Remitente no permitido: alguien@otro.com.')
  expect(reasons).toContain('No se encontró el campo obligatorio de la columna 1 (Reference #).')

  // ---- editar: sin cambiar la contraseña se conserva (hasMailPassword sigue true); una configuración mala no se guarda
  const patched = await ok<Integration>(request.patch(`${API_URL}/api/v1/integrations/${created.publicId}`, { headers, data: { email: { ...email, allowedSenders: ['truss.com', 'advance.com'] } } }), 'editar')
  expect(patched.hasMailPassword).toBe(true)
  expect(patched.email?.allowedSenders).toEqual(['truss.com', 'advance.com'])
  const badPatch = await request.patch(`${API_URL}/api/v1/integrations/${created.publicId}`, { headers, data: { email: { ...email, port: 99999 } } })
  expect(badPatch.status()).toBe(400)

  // ---- en pantalla: la sección Correo del formulario de edición y «Probar conexión»
  await login(page)
  await page.goto('/system/integrations')
  const rowOf = (name: string) => page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()
  await expect(rowOf(base.name)).toBeVisible({ timeout: 30_000 })
  await expect(rowOf(base.name)).toContainText('Correo')
  await rowOf(base.name).getByRole('button', { name: 'Probar conexión' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('No se pudo conectar con el buzón', { timeout: 30_000 })
  await rowOf(base.name).getByRole('button', { name: 'Editar' }).click()
  const dialog = page.getByRole('dialog', { name: /Editar/ })
  await expect(dialog.getByLabel(/^Servidor IMAP/)).toHaveValue('127.0.0.1')
  await expect(dialog.getByText('Déjela vacía para conservar la guardada.')).toBeVisible()
  await expect(dialog.getByLabel(/^Etiqueta 1/)).toHaveValue('Reference #')
  await shot(page, isMobile, 'l133-correo', 1800)
  await dialog.getByLabel(/^Servidor IMAP/).fill('')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog.getByText('Indique el servidor del buzón (IMAP).')).toBeVisible()
  await dialog.getByRole('button', { name: 'Cancelar' }).click()
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
