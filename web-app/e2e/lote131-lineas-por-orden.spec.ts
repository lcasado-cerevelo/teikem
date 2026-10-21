// Recorrido del Lote 131 contra el API real: importación de órdenes con VARIAS líneas por orden (caso AxisCare, para cualquier cliente). La plantilla agrupa las filas por número de envío y dice qué NO
// es pieza (descripción o factura) con reglas por columna; todo lo demás es pieza (puede haber varias por orden). Por API: validaciones de la plantilla (400), vista previa con las líneas, orden sin
// pieza, número de pieza repetido en el archivo, confirmar (la orden nace con sus piezas, descripción y factura) y volver a subir (orden y piezas ya existen). En pantalla: el editor de plantillas
// con «El archivo trae varias líneas por orden» y la vista previa con el resumen de las líneas. Escritorio y móvil. Capturas: docs/manual/frontend/img/l131-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, enableModule, login, ok, shot } from './staffHelpers'
import { contractData, withRates } from './rates'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Varias líneas por orden: piezas, descripciones y factura por reglas de la plantilla', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'LTL_GROUND')
  const headers = await apiHeaders(request)

  // cliente que numera sus órdenes y facturas (el archivo trae el número de envío y la factura)
  const client = await ok<{ publicId: string; name: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Lineas ${s}`, contract: contractData() } }), 'cliente')
  await withRates(request, headers, client as never)
  await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { clientAssignsOrderNumber: true, clientAssignsInvoiceNumber: true } }), 'numeración')

  // plantilla: 1 orden · 2 tipo de línea · 3 label/número · 4 descripción · 5 consignatario · 6 dirección · 7 pueblo · 8 peso
  const columns = [
    { position: 1, field: 'orderNumber' }, { position: 3, field: 'pieceNumber' }, { position: 4, field: 'description' }, { position: 5, field: 'consigneeName' },
    { position: 6, field: 'line1' }, { position: 7, field: 'city' }, { position: 8, field: 'weightKg' },
  ]
  const rules = [{ kind: 'INVOICE', position: 2, operator: 'EQUALS', value: 'Invoice' }, { kind: 'DESCRIPTION', position: 2, operator: 'IN', value: 'Drum | Item' }]
  const base = { name: `Lineas ${s}`, columns, defaults: { serviceType: 'STANDARD', packageType: 'BOX' }, clientPublicId: client.publicId }

  // validaciones de la plantilla
  const noOrder = await request.post(`${API_URL}/api/v1/import-templates`, { headers, data: { ...base, columns: columns.slice(1), multiLine: true, lineRules: rules } })
  expect(noOrder.status()).toBe(400)
  expect(await noOrder.text()).toContain('necesita la columna orderNumber')
  const badRule = await request.post(`${API_URL}/api/v1/import-templates`, { headers, data: { ...base, multiLine: true, lineRules: [{ kind: 'PIECE', position: 2, operator: 'EQUALS', value: 'x' }, { kind: 'INVOICE', position: 2, operator: 'EQUALS' }] } })
  expect(badRule.status()).toBe(400)
  const badText = await badRule.text()
  expect(badText).toContain('Clase de línea desconocida: PIECE')
  expect(badText).toContain('necesita un valor')
  const orphanRules = await request.post(`${API_URL}/api/v1/import-templates`, { headers, data: { ...base, multiLine: false, lineRules: rules } })
  expect(orphanRules.status()).toBe(400)
  expect(await orphanRules.text()).toContain('solo se usan en una plantilla con varias líneas por envío')
  const template = await ok<{ publicId: string; multiLine: boolean; lineRules: { kind: string; operator: string }[] }>(
    request.post(`${API_URL}/api/v1/import-templates`, { headers, data: { ...base, multiLine: true, lineRules: rules } }), 'plantilla')
  expect(template.multiLine).toBe(true)
  expect(template.lineRules.map((r) => `${r.kind}:${r.operator}`)).toEqual(['INVOICE:EQUALS', 'DESCRIPTION:IN'])

  const p = (n: number) => `PCS-${s}-${n}`
  const csv = [
    'orden,tipo,label,descripcion,consignatario,direccion,pueblo,peso',
    `A-${s},Box,${p(1)},,Colmado Uno ${s},Calle 1,Ponce,2.5`,
    `A-${s},Box,${p(2)},,Colmado Uno ${s},Calle 1,Ponce,3`,
    `A-${s},Drum,,Dron de reparto,,,,`,
    `A-${s},Invoice,INV-${s},,,,,`,
    `B-${s},Box,${p(3)},,Colmado Dos ${s},Calle 2,Ponce,1`,
    `B-${s},Item,,Equipo medico,,,,`,
    `C-${s},Drum,,Solo descripcion,Colmado Tres ${s},Calle 3,Ponce,`,
    `D-${s},Box,${p(1)},,Colmado Cuatro ${s},Calle 4,Ponce,1`,
  ].join('\n')

  type Line = { row: number; kind: string; errors?: Record<string, string> }
  type Preview = { batchPublicId: string; rowCount: number; validRows: number; rows: { row: number; isValid: boolean; errors: Record<string, string>; warnings: string[]; values: Record<string, string>; lines?: Line[]; orderNumber?: string }[] }
  const validate = () => ok<Preview>(request.post(`${API_URL}/api/v1/orders/import/validate`, { headers, data: { templatePublicId: template.publicId, clientPublicId: client.publicId, content: csv, fileName: 'axis.csv' } }), 'validar')
  const preview = await validate()
  expect(preview.rowCount).toBe(4)   // una fila de vista previa por ORDEN
  expect(preview.validRows).toBe(2)
  const [a, b, c, d] = preview.rows
  expect(a.isValid).toBe(true)
  expect(a.lines?.map((l) => l.kind)).toEqual(['PIECE', 'PIECE', 'DESCRIPTION', 'INVOICE'])
  expect(a.values.clientInvoiceNumber).toBe(`INV-${s}`)
  expect(b.isValid).toBe(true)
  expect(b.lines?.map((l) => l.kind)).toEqual(['PIECE', 'DESCRIPTION'])
  expect(c.isValid).toBe(false)
  expect(c.errors.lines).toContain('no trae ninguna pieza')
  expect(d.isValid).toBe(false)
  expect(d.lines?.[0].errors?.pieceNumber).toBe(`El número de pieza ${p(1)} se repite en la fila ${a.row} del archivo.`)

  // en pantalla: el editor de plantillas con las reglas y la vista previa con el resumen de líneas
  await login(page)
  await page.goto('/orders')
  await page.getByRole('button', { name: 'Importar', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Importar envíos desde CSV' })
  const clientValue = await dialog.locator('#oi-client option', { hasText: client.name }).first().getAttribute('value')
  await dialog.getByLabel('Cliente', { exact: true }).selectOption(clientValue ?? '')
  await dialog.getByRole('button', { name: 'Nueva plantilla' }).click()
  const editor = page.getByRole('dialog', { name: 'Nueva plantilla de importación de envíos' })
  await expect(editor.getByLabel('El archivo trae varias líneas por envío')).toBeVisible()
  await editor.getByLabel('El archivo trae varias líneas por envío').check()
  await expect(editor.getByText('¿Qué líneas NO son pieza?')).toBeVisible()
  await editor.getByRole('button', { name: 'Agregar regla' }).click()
  await expect(editor.getByLabel('Columna de la regla 1')).toBeVisible()
  await shot(page, isMobile, 'l131-plantilla', 1500)
  await editor.getByRole('button', { name: 'Cancelar' }).click()
  await expect(editor).toBeHidden()
  const tplValue = await dialog.locator('select option', { hasText: `Lineas ${s}` }).first().getAttribute('value')
  await dialog.getByLabel('Plantilla', { exact: true }).selectOption(tplValue ?? '')
  await dialog.getByLabel('Archivo CSV', { exact: true }).setInputFiles({ name: 'axis.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'utf-8') })
  await expect(dialog.getByText('2 pieza(s) · 1 descripción(es) · factura INV-' + s)).toBeVisible({ timeout: 30_000 })
  await expect(dialog.getByText(/El envío no trae ninguna pieza/)).toBeVisible()
  await expect(dialog.getByText(new RegExp(`El número de pieza ${p(1)} se repite`))).toBeVisible()
  await shot(page, isMobile, 'l131-previa', 1500)
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  await page.keyboard.press('Escape')

  // confirmar por API: solo las dos válidas; la orden A nace con sus piezas, la descripción de lo que se envía y la factura
  const result = await ok<{ created: number; failed: number; skipped: number; rows: { orderPublicId?: string; orderNumber?: string }[] }>(
    request.post(`${API_URL}/api/v1/orders/import/${preview.batchPublicId}/confirm`, { headers, data: {} }), 'confirmar')
  expect(result.created).toBe(2)
  const orderA = await ok<{ clientInvoiceNumber: string; packages: { pieces: number; description: string; pieceNumbers: string[] }[] }>(
    request.get(`${API_URL}/api/v1/orders/${result.rows[0].orderPublicId}`, { headers }), 'orden A')
  expect(orderA.clientInvoiceNumber).toBe(`INV-${s}`)
  expect(orderA.packages).toHaveLength(2)                                 // dos piezas, no una
  expect(orderA.packages.flatMap((x) => x.pieceNumbers).sort()).toEqual([p(1), p(2)])
  expect(orderA.packages.every((x) => x.description.includes('Dron de reparto'))).toBe(true)

  // volver a subir el archivo: las órdenes y los números de pieza ya existen (no se duplica la pieza)
  const again = await validate()
  expect(again.validRows).toBe(0)
  expect(again.rows[0].errors.orderNumber).toBeTruthy()
  expect(again.rows[0].lines?.some((l) => l.errors?.pieceNumber?.includes('ya existe'))).toBe(true)
})
