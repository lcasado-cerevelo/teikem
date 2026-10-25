// Recorrido del Lote 137 (P11) contra el API real: el formato del archivo de una integración — hoja de Excel (.xlsx), JSON y XML con mapeo por ruta — que se convierte al CSV de la plantilla de importación
// (con varias líneas por orden y las reglas de «no pieza» del Lote 131) y sigue el camino de siempre: hash (el mismo archivo no se procesa dos veces), importador con la cuenta de servicio y bandeja.
// Cubre: subida manual de cada formato, formato automático por la extensión, carpeta de entrada con JSON, errores con el mensaje exacto (sin mapeo, JSON roto, DTD, hoja inexistente, no es Excel) que quedan en
// la bandeja, validaciones del alta, vista previa de un archivo de muestra y la pantalla. Escritorio y móvil. Capturas: docs/manual/frontend/img/l137-<paso>.png.
import { mkdirSync, readdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { expect, test, type APIRequestContext } from '@playwright/test'
import * as XLSX from 'xlsx'

import { contractData, withRates } from './rates'
import { API_URL, apiHeaders, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

const FOLDER_ROOT = resolve(process.env.INTEGRATIONS_ROOT ?? 'test-results/integrations')
const TENANT_ID = process.env.TEIKEM_TENANT_ID ?? '1'

type Integration = { publicId: string; name: string }
type Run = { status: string; filesRead: number; ordersCreated: number; rejected: number; message?: string; files: { fileName: string; result: string; message?: string }[] }
type Preview = { kind: string; sheets: string[]; header: string[]; rows: string[][]; totalRows: number }

const JSON_COLUMNS = [
  { header: 'Orden', path: '^.ref' }, { header: 'Tipo', path: '@.type' }, { header: 'Label', path: '@.label' }, { header: 'Descripcion', path: '@.description' },
  { header: 'Consignatario', path: '^.to.name' }, { header: 'Direccion', path: '^.to.address' }, { header: 'Pueblo', path: '^.to.city' }, { header: 'Peso', path: '@.weight' },
]
const XML_COLUMNS = [
  { header: 'Orden', path: '^.@ref' }, { header: 'Tipo', path: '@.@tipo' }, { header: 'Label', path: '@.@label' }, { header: 'Descripcion', path: '@.@descripcion' },
  { header: 'Consignatario', path: '^.Destino.@nombre' }, { header: 'Direccion', path: '^.Destino.@direccion' }, { header: 'Pueblo', path: '^.Destino.@pueblo' }, { header: 'Peso', path: '@.@peso' },
]

const jsonOf = (s: string, tag: string) =>
  JSON.stringify({
    orders: [{ ref: `${tag}-${s}`, to: { name: `Colmado ${tag} ${s}`, address: 'Calle 1', city: 'Ponce' }, items: [{ type: 'Box', label: `PCS-${tag}-${s}-1`, weight: 2.5 }, { type: 'Drum', description: 'Dron de reparto' }, { type: 'Invoice', label: `INV-${tag}-${s}` }] }],
  })

const xmlOf = (s: string, tag: string) =>
  `<?xml version="1.0" encoding="utf-8"?><Pedidos><Orden ref="${tag}-${s}"><Destino nombre="Colmado ${tag} ${s}" direccion="Calle 2" pueblo="Ponce"/><Item tipo="Box" label="PCS-${tag}-${s}-1" peso="2.5"/><Item tipo="Drum" descripcion="Dron de reparto"/><Item tipo="Invoice" label="INV-${tag}-${s}"/></Orden></Pedidos>`

/** Libro con una hoja de notas y los datos en «Pedidos» (el peso como NÚMERO, como lo deja Excel). */
function workbook(s: string, tag: string, notesFirst = true): Buffer {
  const wb = XLSX.utils.book_new()
  if (notesFirst) XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet([['Pedidos de la semana']]), 'Notas')
  XLSX.utils.book_append_sheet(
    wb,
    XLSX.utils.aoa_to_sheet([
      ['orden', 'tipo', 'label', 'descripcion', 'consignatario', 'direccion', 'pueblo', 'peso'],
      [`${tag}-${s}`, 'Box', `PCS-${tag}-${s}-1`, '', `Colmado ${tag} ${s}`, 'Calle 3', 'Ponce', 2.5],
      [`${tag}-${s}`, 'Drum', '', 'Dron de reparto', '', '', '', ''],
      [`${tag}-${s}`, 'Invoice', `INV-${tag}-${s}`, '', '', '', '', ''],
    ]),
    'Pedidos',
  )
  return XLSX.write(wb, { type: 'buffer', bookType: 'xlsx' }) as Buffer
}

async function setup(request: APIRequestContext, s: string) {
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)
  const client = await ok<{ publicId: string; name: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Formatos ${s}`, contract: contractData() } }), 'cliente')
  await withRates(request, headers, client as never)
  await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { clientAssignsOrderNumber: true, clientAssignsInvoiceNumber: true } }), 'numeración')
  const columns = [{ position: 1, field: 'orderNumber' }, { position: 3, field: 'pieceNumber' }, { position: 4, field: 'description' }, { position: 5, field: 'consigneeName' }, { position: 6, field: 'line1' }, { position: 7, field: 'city' }, { position: 8, field: 'weightKg' }]
  const rules = [{ kind: 'INVOICE', position: 2, operator: 'EQUALS', value: 'Invoice' }, { kind: 'DESCRIPTION', position: 2, operator: 'IN', value: 'Drum | Item' }]
  const template = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/import-templates`, {
    headers, data: { name: `Plantilla formatos ${s}`, columns, defaults: { serviceType: 'STANDARD', packageType: 'BOX' }, clientPublicId: client.publicId, multiLine: true, lineRules: rules },
  }), 'plantilla')
  return { headers, client, template }
}

test('Formatos de archivo: Excel, JSON y XML con mapeo por ruta (subida, carpeta, automático, errores, vista previa y pantalla)', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const { headers, client, template } = await setup(request, s)
  const post = (data: object) => request.post(`${API_URL}/api/v1/integrations`, { headers, data })
  const make = (name: string, extra: object) =>
    ok<Integration>(post({ name: `0 ${name} ${s}`, clientPublicId: client.publicId, templatePublicId: template.publicId, scopes: ['files.orders_import'], connectorKind: 'MANUAL', ...extra }), name)
  const upload = async (integration: Integration, name: string, buffer: Buffer | string, mimeType = 'application/octet-stream') => {
    const res = await request.post(`${API_URL}/api/v1/integrations/${integration.publicId}/upload`, { headers, multipart: { file: { name, mimeType, buffer: Buffer.from(buffer) } } })
    expect(res.ok(), await res.text()).toBeTruthy()
    return (await res.json()) as Run
  }

  // ---- validaciones del alta con el mensaje exacto
  const bad = async (format: object, text: string, extra: object = {}) => {
    const res = await post({ name: `Mala ${Date.now()}`, clientPublicId: client.publicId, templatePublicId: template.publicId, scopes: ['files.orders_import'], connectorKind: 'MANUAL', format, ...extra })
    expect(res.status()).toBe(400)
    expect(await res.text()).toContain(text)
  }
  await bad({ kind: 'PDF' }, 'Formato desconocido: PDF. Use AUTO, CSV, EXCEL, JSON, XML.')
  await bad({ kind: 'EXCEL', sheet: 'h'.repeat(32) }, 'El nombre de la hoja no puede pasar de 31 caracteres.')
  await bad({ kind: 'JSON', columns: JSON_COLUMNS }, 'Indique la ruta de los envíos (por ejemplo $.orders[*]).')
  await bad({ kind: 'JSON', recordsPath: 'orders', columns: JSON_COLUMNS }, 'La ruta «orders» no es válida. Use el formato $.campo.otro[*] (también @.campo o ^.campo).')
  await bad({ kind: 'JSON', recordsPath: '$.orders[*]' }, 'Agregue al menos una columna al mapeo.')
  await bad({ kind: 'JSON', recordsPath: '$.orders[*]', columns: [{ header: 'A', path: '^.x' }] }, 'La ruta «^.x» usa ^ (el envío), que solo se puede cuando hay una ruta de líneas.')
  await bad({ kind: 'JSON', recordsPath: '$.orders[*]', linesPath: '$.items[*]', columns: [{ header: 'A', path: '@.x' }] }, 'La ruta de las líneas debe ser relativa al envío: empiece con @ (por ejemplo @.lines[*]).')
  await bad({ kind: 'JSON', recordsPath: '$.orders[*]', columns: [{ header: 'A', path: '@.x' }, { header: 'a', path: '@.y' }] }, 'Hay dos columnas llamadas «a».')
  const api = await ok<Integration>(post({ name: `Api formato ${s}`, connectorKind: 'API', clientPublicId: client.publicId, scopes: ['orders:read'] }), 'integración API')
  const apiFormat = await request.patch(`${API_URL}/api/v1/integrations/${api.publicId}`, { headers, data: { format: { kind: 'JSON' } } })
  expect(apiFormat.status()).toBe(400)
  expect(await apiFormat.text()).toContain('Una integración de tipo API no tiene formato de archivo.')

  // ---- JSON: una orden con tres líneas (pieza, descripción y factura) → una orden con su pieza, su descripción y su factura
  const intJson = await make('Json', { format: { kind: 'JSON', recordsPath: '$.orders[*]', linesPath: '@.items[*]', columns: JSON_COLUMNS } })
  const jsonRun = await upload(intJson, 'pedidos.json', jsonOf(s, 'J'))
  expect(jsonRun).toMatchObject({ status: 'OK', filesRead: 1, ordersCreated: 1, rejected: 0 })
  expect(jsonRun.files[0]).toMatchObject({ fileName: 'pedidos.json', result: 'PROCESSED' })
  const dup = await upload(intJson, 'pedidos-copia.json', jsonOf(s, 'J'))
  expect(dup.files[0].result).toBe('DUPLICATE')                                           // el hash es el del archivo original
  const created = await ok<{ items: { orderNumber: string; pieces?: number }[] }>(request.get(`${API_URL}/api/v1/orders?q=J-${s}&take=5`, { headers }), 'orden de JSON')
  expect(created.items.map((o) => o.orderNumber)).toContain(`J-${s}`)

  // ---- XML con atributos
  const intXml = await make('Xml', { format: { kind: 'XML', recordsPath: '$.Pedidos.Orden[*]', linesPath: '@.Item[*]', columns: XML_COLUMNS } })
  const xmlRun = await upload(intXml, 'pedidos.xml', xmlOf(s, 'X'))
  expect(xmlRun).toMatchObject({ status: 'OK', ordersCreated: 1, rejected: 0 })

  // ---- Excel: la hoja «Pedidos» (la primera es «Notas»), con el peso como número
  const intXls = await make('Excel', { format: { kind: 'EXCEL', sheet: 'pedidos' } })
  const xlsRun = await upload(intXls, 'pedidos.xlsx', workbook(s, 'E'))
  expect(xlsRun).toMatchObject({ status: 'OK', ordersCreated: 1, rejected: 0 })

  // ---- automático: por la extensión (.json con su mapeo, .xlsx la primera hoja)
  const intAuto = await make('Auto', { format: { kind: 'AUTO', recordsPath: '$.orders[*]', linesPath: '@.items[*]', columns: JSON_COLUMNS } })
  expect((await upload(intAuto, 'a.json', jsonOf(s, 'AJ'))).ordersCreated).toBe(1)
  expect((await upload(intAuto, 'a.xlsx', workbook(s, 'AE', false))).ordersCreated).toBe(1)
  expect((await upload(intAuto, 'a.csv', ['orden,tipo,label,descripcion,consignatario,direccion,pueblo,peso', `AC-${s},Box,PCS-AC-${s}-1,,Colmado AC ${s},Calle 4,Ponce,1`, `AC-${s},Invoice,INV-AC-${s},,,,,`].join('\n'))).ordersCreated).toBe(1)

  // ---- errores: el archivo queda anotado y en la bandeja con el motivo exacto
  const intNoMap = await make('Sin mapeo', {})
  const noMap = await upload(intNoMap, 'sin-mapeo.json', jsonOf(s, 'N'))
  expect(noMap.files[0].result).toBe('FAILED')
  expect(noMap.files[0].message).toBe('El archivo es JSON, pero la integración no tiene el mapeo de rutas: configure «Formato del archivo».')
  expect((await upload(intJson, 'roto.json', '{"orders": [')).files[0].message).toMatch(/^No se pudo leer el archivo JSON: /)
  expect((await upload(intJson, 'vacio-de-ordenes.json', '{"orders": []}')).files[0].message).toBe('La ruta de los envíos «$.orders[*]» no encontró ningún registro en el archivo.')
  const xxe = '<?xml version="1.0"?><!DOCTYPE p [<!ENTITY e SYSTEM "file:///etc/passwd">]><Pedidos><Orden ref="&e;"/></Pedidos>'
  expect((await upload(intXml, 'xxe.xml', xxe)).files[0].message).toBe('El archivo XML trae una declaración DTD, que no se admite.')
  const intBadSheet = await make('Hoja mala', { format: { kind: 'EXCEL', sheet: 'Nada' } })
  expect((await upload(intBadSheet, 'pedidos2.xlsx', workbook(s, 'B'))).files[0].message).toBe("El libro no tiene la hoja 'Nada'.")
  expect((await upload(intXls, 'no-es-excel.txt', 'a,b\n1,2')).files[0].message).toBe('El archivo no es un libro de Excel (.xlsx).')
  const inbox = await ok<{ total: number; items: { reason: string }[] }>(request.get(`${API_URL}/api/v1/integrations/inbox?integration=${intJson.publicId}&status=PENDING`, { headers }), 'bandeja')
  expect(inbox.items.some((i) => i.reason.startsWith('Archivo roto.json: No se pudo leer el archivo JSON: '))).toBe(true)

  // ---- carpeta de entrada con formato JSON: solo toma los .json (el .csv de al lado se queda donde está)
  const folder = `fmt_${s}`
  const intFolder = await make('Carpeta json', { connectorKind: 'FOLDER', folder, scheduleKind: 'MANUAL', format: { kind: 'JSON', recordsPath: '$.orders[*]', linesPath: '@.items[*]', columns: JSON_COLUMNS } })
  const dir = join(FOLDER_ROOT, TENANT_ID, folder)
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, 'pedidos.json'), jsonOf(s, 'F'), 'utf-8')
  writeFileSync(join(dir, 'ignorado.csv'), 'a,b\n1,2', 'utf-8')
  const folderRun = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${intFolder.publicId}/run`, { headers }), 'correr carpeta')
  expect(folderRun).toMatchObject({ status: 'OK', filesRead: 1, ordersCreated: 1 })
  expect(readdirSync(join(dir, 'procesados')).map((f) => f.replace(/^\d+-/, ''))).toEqual(['pedidos.json'])
  expect(readdirSync(dir)).toContain('ignorado.csv')

  // ---- vista previa de un archivo de muestra (no guarda nada)
  const previewOf = async (name: string, buffer: Buffer | string, format: object | null) => {
    const res = await request.post(`${API_URL}/api/v1/integrations/format/preview`, { headers, multipart: { file: { name, mimeType: 'application/octet-stream', buffer: Buffer.from(buffer) }, ...(format ? { format: JSON.stringify(format) } : {}) } })
    return res
  }
  const pJson = await previewOf('m.json', jsonOf(s, 'P'), { kind: 'JSON', recordsPath: '$.orders[*]', linesPath: '@.items[*]', columns: JSON_COLUMNS })
  expect(pJson.status()).toBe(200)
  const pj = (await pJson.json()) as Preview
  expect(pj).toMatchObject({ kind: 'JSON', totalRows: 3 })
  expect(pj.header).toEqual(JSON_COLUMNS.map((c) => c.header))
  expect(pj.rows[0]).toEqual([`P-${s}`, 'Box', `PCS-P-${s}-1`, '', `Colmado P ${s}`, 'Calle 1', 'Ponce', '2.5'])
  expect(pj.rows[1][1]).toBe('Drum')
  const pXls = (await (await previewOf('m.xlsx', workbook(s, 'P'), null)).json()) as Preview
  expect(pXls).toMatchObject({ kind: 'EXCEL', totalRows: 0 })                             // la primera hoja del libro es «Notas» (sin encabezado real)
  expect(pXls.sheets).toEqual(['Notas', 'Pedidos'])
  const pXls2 = (await (await previewOf('m.xlsx', workbook(s, 'P'), { kind: 'EXCEL', sheet: 'Pedidos' })).json()) as Preview
  expect(pXls2).toMatchObject({ kind: 'EXCEL', totalRows: 3 })
  expect(pXls2.header[0]).toBe('orden')
  const pBad = await previewOf('m.json', '{"x": 1}', { kind: 'JSON', recordsPath: '$.orders[*]', linesPath: '@.items[*]', columns: JSON_COLUMNS })
  expect(pBad.status()).toBe(400)
  expect(await pBad.text()).toContain('La ruta de los envíos «$.orders[*]» no encontró ningún registro en el archivo.')

  // ---- pantalla: editar la integración JSON, probar con un archivo de muestra y las validaciones del formulario
  await login(page)
  await page.goto('/system/integrations')
  const rowOf = (name: string) => page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()
  await expect(rowOf(intJson.name)).toBeVisible({ timeout: 30_000 })
  await rowOf(intJson.name).getByRole('button', { name: 'Editar' }).click()
  const dialog = page.getByRole('dialog', { name: /Editar/ })
  const section = dialog.locator('fieldset', { hasText: 'Formato del archivo' })
  await expect(section.getByLabel(/^Ruta de los envíos/)).toHaveValue('$.orders[*]')
  await expect(section.getByLabel(/^Ruta de las líneas/)).toHaveValue('@.items[*]')
  await expect(section.getByLabel('Nombre de la columna')).toHaveCount(8)
  await section.getByLabel('Archivo de muestra').setInputFiles({ name: 'muestra.json', mimeType: 'application/json', buffer: Buffer.from(jsonOf(s, 'U')) })
  await expect(section.getByTestId('format-preview')).toContainText('Se leyó como JSON: 3 fila(s)', { timeout: 15_000 })
  await expect(section.getByTestId('format-preview')).toContainText(`PCS-U-${s}-1`)
  await shot(page, isMobile, 'l137-formato-json', 1900, section)
  await section.getByLabel('Archivo de muestra').setInputFiles({ name: 'roto.json', mimeType: 'application/json', buffer: Buffer.from('{"orders": [') })
  await expect(section.getByTestId('format-error')).toContainText('No se pudo leer el archivo JSON: ', { timeout: 15_000 })
  await section.getByLabel(/^Ruta de los envíos/).fill('')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog.getByText('Indique la ruta de los envíos (por ejemplo $.orders[*]).')).toBeVisible()
  await section.getByLabel(/^Ruta de los envíos/).fill('orders')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog.getByText('La ruta «orders» no es válida. Use el formato $.campo.otro[*] (también @.campo o ^.campo).')).toBeVisible()
  await dialog.getByRole('button', { name: 'Cancelar' }).click()

  // Excel: la hoja y la prueba con un libro
  await rowOf(intXls.name).getByRole('button', { name: 'Editar' }).click()
  const dialog2 = page.getByRole('dialog', { name: /Editar/ })
  const section2 = dialog2.locator('fieldset', { hasText: 'Formato del archivo' })
  await expect(section2.getByLabel('Hoja de Excel')).toHaveValue('pedidos')
  await section2.getByLabel('Archivo de muestra').setInputFiles({ name: 'muestra.xlsx', mimeType: 'application/octet-stream', buffer: workbook(s, 'V') })
  await expect(section2.getByTestId('format-preview')).toContainText('Se leyó como Excel (.xlsx): 3 fila(s)', { timeout: 15_000 })
  await expect(section2.getByTestId('format-preview')).toContainText('Hojas del libro: Notas, Pedidos.')
  await shot(page, isMobile, 'l137-formato-excel', 1500, section2)
  await dialog2.getByRole('button', { name: 'Cancelar' }).click()
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
