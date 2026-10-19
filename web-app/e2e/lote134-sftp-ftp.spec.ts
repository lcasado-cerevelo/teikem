// Recorrido del Lote 134 (P11) contra el API real: conectores SFTP y FTP para cualquier cliente. Siempre: validaciones del alta (400), la contraseña y la llave nunca vuelven, «Probar conexión» y correr
// contra un servidor que no existe (Falló sin romper nada). Con servidores de prueba locales (variables REMOTE_TEST_SFTP_ROOT y REMOTE_TEST_FTP_ROOT = carpeta raíz de cada uno; SFTP en 127.0.0.1:2222
// y FTP en 127.0.0.1:2121): la huella SHA-256 del servidor SFTP se FIJA (sin ella o distinta no se conecta), se leen los archivos, se crean las órdenes y los archivos se MUEVEN a «procesados»
// (nunca se borran); el mismo contenido otra vez es «Repetido». Escritorio y móvil. Capturas: docs/manual/frontend/img/l134-<paso>.png.
import { mkdirSync, readdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'

import { contractData, withRates } from './rates'
import { API_URL, apiHeaders, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

const SFTP_ROOT = process.env.REMOTE_TEST_SFTP_ROOT
const FTP_ROOT = process.env.REMOTE_TEST_FTP_ROOT

type Integration = { publicId: string; name: string; hasRemoteSecret: boolean; remote?: Record<string, unknown>; connectorKind: string }
type Run = { status: string; filesRead: number; ordersCreated: number; rejected: number; message?: string; files: { fileName: string; result: string }[] }
type Test = { ok: boolean; message: string; count: number; fingerprint?: string }

async function setup(request: import('@playwright/test').APIRequestContext, s: string) {
  await enableModule(request, 'LTL_GROUND')
  await enableModule(request, 'INTEGRATIONS')
  const headers = await apiHeaders(request)
  const client = await ok<{ publicId: string; name: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Remoto ${s}`, contract: contractData() } }), 'cliente')
  await withRates(request, headers, client as never)
  await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { clientAssignsOrderNumber: true, clientAssignsInvoiceNumber: true } }), 'numeración')
  const columns = [{ position: 1, field: 'orderNumber' }, { position: 3, field: 'pieceNumber' }, { position: 4, field: 'description' }, { position: 5, field: 'consigneeName' }, { position: 6, field: 'line1' }, { position: 7, field: 'city' }, { position: 8, field: 'weightKg' }]
  const rules = [{ kind: 'INVOICE', position: 2, operator: 'EQUALS', value: 'Invoice' }, { kind: 'DESCRIPTION', position: 2, operator: 'IN', value: 'Drum | Item' }]
  const template = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/import-templates`, {
    headers, data: { name: `Plantilla remoto ${s}`, columns, defaults: { serviceType: 'STANDARD', packageType: 'BOX' }, clientPublicId: client.publicId, multiLine: true, lineRules: rules },
  }), 'plantilla')
  return { headers, client, template }
}

const csvOf = (s: string, tag: string) => [
  'orden,tipo,label,descripcion,consignatario,direccion,pueblo,peso',
  `${tag}-${s},Box,PCS-${tag}-${s}-1,,Colmado ${tag} ${s},Calle 1,Ponce,2.5`,
  `${tag}-${s},Drum,,Dron,,,,`,
  `${tag}-${s},Invoice,INV-${tag}-${s},,,,,`,
].join('\n')

test('SFTP y FTP: validaciones, secretos que no vuelven, probar conexión y correr contra un servidor inexistente', async ({ request }, testInfo) => {
  const s = `${testInfo.project.name.startsWith('movil') ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const { headers, client, template } = await setup(request, s)
  const remote = { host: '127.0.0.1', port: 1, username: 'teikem', dir: '/entrada', processedDir: 'procesados', errorDir: 'errores', authKind: 'PASSWORD', useFtps: false }
  const base = { name: `SFTP ${s}`, clientPublicId: client.publicId, connectorKind: 'SFTP', templatePublicId: template.publicId, scopes: ['files.orders_import'], includeMe: true, remote, secret: { password: 'clave-super-secreta' } }
  const post = (data: object) => request.post(`${API_URL}/api/v1/integrations`, { headers, data })
  const bad = async (patch: object, text: string) => {
    const res = await post({ ...base, ...patch })
    expect(res.status(), await res.text()).toBe(400)
    expect(await res.text()).toContain(text)
  }

  await bad({ remote: { ...remote, host: '' } }, 'Indique el servidor (host) del conector')
  await bad({ remote: { ...remote, port: 70000 } }, 'El puerto debe estar entre 1 y 65535')
  await bad({ remote: { ...remote, username: '' } }, 'Indique el usuario del conector')
  await bad({ secret: undefined }, 'Indique la contraseña del conector')
  await bad({ remote: { ...remote, authKind: 'KEY' }, secret: { password: 'x' } }, 'Indique la llave privada del conector')
  await bad({ connectorKind: 'FTP', remote: { ...remote, authKind: 'KEY' }, secret: { privateKey: 'x' } }, 'La llave privada solo se usa con SFTP')
  await bad({ remote: { ...remote, dir: '../fuera' } }, 'no pueden contener «..»')
  await bad({ remote: { ...remote, hostFingerprint: 'no-es-huella' } }, 'La huella debe ser la SHA-256 del servidor')
  await bad({ remote: { ...remote, fileName: { operator: 'RARO', value: 'x' } } }, 'Condición desconocida')
  await bad({ remote: undefined }, 'Indique el servidor (host) del conector')

  const created = await ok<Integration>(post(base), 'alta SFTP')
  expect(created).toMatchObject({ connectorKind: 'SFTP', hasRemoteSecret: true })
  expect(created.remote?.host).toBe('127.0.0.1')
  for (const text of [JSON.stringify(created), await (await request.get(`${API_URL}/api/v1/integrations/${created.publicId}`, { headers })).text(), await (await request.get(`${API_URL}/api/v1/integrations`, { headers })).text()])
    expect(text).not.toContain('clave-super-secreta')

  const tested = await ok<Test>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/test-connection`, { headers }), 'probar')
  expect(tested.ok).toBe(false)
  expect(tested.message).toContain('No se pudo conectar con el servidor')
  expect(tested.message).not.toContain('clave-super-secreta')
  const failed = await ok<Run>(request.post(`${API_URL}/api/v1/integrations/${created.publicId}/run`, { headers }), 'correr')
  expect(failed.status).toBe('FAILED')
  expect(failed.message).toContain('No se pudo conectar con el servidor')

  const ftp = await ok<Integration>(post({ ...base, name: `FTP ${s}`, connectorKind: 'FTP', remote: { ...remote, port: 1 } }), 'alta FTP')
  expect(ftp.connectorKind).toBe('FTP')
  const ftpTest = await ok<Test>(request.post(`${API_URL}/api/v1/integrations/${ftp.publicId}/test-connection`, { headers }), 'probar FTP')
  expect(ftpTest.ok).toBe(false)

  // editar sin mandar la contraseña la conserva; una configuración mala no se guarda
  const patched = await ok<Integration>(request.patch(`${API_URL}/api/v1/integrations/${created.publicId}`, { headers, data: { remote: { ...remote, dir: '/otra' } } }), 'editar')
  expect(patched.hasRemoteSecret).toBe(true)
  expect(patched.remote?.dir).toBe('/otra')
  expect((await request.patch(`${API_URL}/api/v1/integrations/${created.publicId}`, { headers, data: { remote: { ...remote, port: 0 } } })).status()).toBe(400)
  // una integración de carpeta no tiene conexión que probar
  const manual = await ok<Integration>(post({ ...base, name: `Manual ${s}`, connectorKind: 'MANUAL', remote: undefined, secret: undefined }), 'manual')
  expect((await request.post(`${API_URL}/api/v1/integrations/${manual.publicId}/test-connection`, { headers })).status()).toBe(400)
})

test('SFTP y FTP contra servidores de prueba: huella fijada, archivos leídos, órdenes creadas y archivos movidos', async ({ page, request }, testInfo) => {
  test.skip(!SFTP_ROOT || !FTP_ROOT, 'sin servidores de prueba (REMOTE_TEST_SFTP_ROOT y REMOTE_TEST_FTP_ROOT)')
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  const { headers, client, template } = await setup(request, s)
  const post = (data: object) => request.post(`${API_URL}/api/v1/integrations`, { headers, data })
  const run = (id: string) => ok<Run>(request.post(`${API_URL}/api/v1/integrations/${id}/run`, { headers }), 'correr')
  const orderNumbers = async () => (await ok<{ items: { orderNumber: string }[] }>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&take=50`, { headers }), 'órdenes')).items.map((o) => o.orderNumber).sort()

  // ---- SFTP: sin huella fijada no se conecta; la prueba la muestra; se fija y funciona; una distinta se rechaza
  const dir = join(SFTP_ROOT!, `entrada-${s}`)
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, 'ordenes.csv'), csvOf(s, 'SF'), 'utf-8')
  writeFileSync(join(dir, 'ignorar.pdf'), 'no es un csv', 'utf-8')
  const sftpRemote = { host: '127.0.0.1', port: 2222, username: 'teikem', dir: `/entrada-${s}`, processedDir: 'procesados', errorDir: 'errores', authKind: 'PASSWORD' }
  const sftp = await ok<Integration>(post({ name: `0 SFTP vivo ${s}`, clientPublicId: client.publicId, connectorKind: 'SFTP', templatePublicId: template.publicId, scopes: ['files.orders_import'], remote: sftpRemote, secret: { password: 'clave-sftp' } }), 'alta SFTP')
  const noPin = await ok<Test>(request.post(`${API_URL}/api/v1/integrations/${sftp.publicId}/test-connection`, { headers }), 'probar sin huella')
  expect(noPin.ok).toBe(false)
  expect(noPin.message).toContain('Falta confirmar la huella')
  expect(noPin.message).toContain('SHA256:')
  expect(noPin.fingerprint).toMatch(/^SHA256:[A-Za-z0-9+/]{43}$/)
  const noPinRun = await run(sftp.publicId)
  expect(noPinRun.status).toBe('FAILED')
  expect(noPinRun.message).toContain('Falta confirmar la huella')

  const wrong = 'SHA256:' + 'A'.repeat(43)
  await ok(request.patch(`${API_URL}/api/v1/integrations/${sftp.publicId}`, { headers, data: { remote: { ...sftpRemote, hostFingerprint: wrong } } }), 'huella distinta')
  const mismatch = await ok<Test>(request.post(`${API_URL}/api/v1/integrations/${sftp.publicId}/test-connection`, { headers }), 'probar huella distinta')
  expect(mismatch.ok).toBe(false)
  expect(mismatch.message).toContain('no coincide')

  await ok(request.patch(`${API_URL}/api/v1/integrations/${sftp.publicId}`, { headers, data: { remote: { ...sftpRemote, hostFingerprint: noPin.fingerprint } } }), 'fijar la huella')
  const good = await ok<Test>(request.post(`${API_URL}/api/v1/integrations/${sftp.publicId}/test-connection`, { headers }), 'probar')
  expect(good).toMatchObject({ ok: true, count: 2 })
  await new Promise((r) => setTimeout(r, 11_000))   // un archivo recién escrito espera 10 s por si todavía se está subiendo
  const sftpRun = await run(sftp.publicId)
  expect(sftpRun).toMatchObject({ status: 'OK', filesRead: 1, ordersCreated: 1, rejected: 0 })   // el pdf se ignora por tipo
  expect(await orderNumbers()).toEqual([`SF-${s}`])
  expect(readdirSync(dir).sort()).toEqual(['ignorar.pdf', 'procesados'])                          // el archivo se MOVIÓ, no se borró
  expect(readdirSync(join(dir, 'procesados'))).toEqual(['ordenes.csv'])

  // el mismo contenido en otro archivo: repetido
  writeFileSync(join(dir, 'copia.csv'), csvOf(s, 'SF'), 'utf-8')
  await new Promise((r) => setTimeout(r, 11_000))   // un archivo recién escrito espera 10 s por si todavía se está subiendo
  const dup = await run(sftp.publicId)
  expect(dup.files[0]).toMatchObject({ fileName: 'copia.csv', result: 'DUPLICATE' })
  expect(dup.ordersCreated).toBe(0)

  // ---- FTP (sin cifrar en el servidor de prueba): lee, crea y mueve
  const fdir = join(FTP_ROOT!, `entrada-${s}`)
  mkdirSync(fdir, { recursive: true })
  writeFileSync(join(fdir, 'ordenes-ftp.csv'), csvOf(s, 'FT'), 'utf-8')
  const ftpRemote = { host: '127.0.0.1', port: 2121, username: 'teikem', dir: `/entrada-${s}`, processedDir: 'procesados', errorDir: 'errores', authKind: 'PASSWORD', useFtps: false }
  const ftp = await ok<Integration>(post({ name: `0 FTP vivo ${s}`, clientPublicId: client.publicId, connectorKind: 'FTP', templatePublicId: template.publicId, scopes: ['files.orders_import'], remote: ftpRemote, secret: { password: 'clave-ftp' } }), 'alta FTP')
  const ftpTest = await ok<Test>(request.post(`${API_URL}/api/v1/integrations/${ftp.publicId}/test-connection`, { headers }), 'probar FTP')
  expect(ftpTest.ok).toBe(true)
  expect(ftpTest.message).toContain('FTP sin cifrar')
  expect(ftpTest.message, ftpTest.message).toContain('1 archivo(s)')
  await new Promise((r) => setTimeout(r, 11_000))
  const ftpRun = await run(ftp.publicId)
  expect(ftpRun).toMatchObject({ status: 'OK', filesRead: 1, ordersCreated: 1 })
  expect(await orderNumbers()).toEqual([`FT-${s}`, `SF-${s}`])
  expect(readdirSync(join(fdir, 'procesados'))).toEqual(['ordenes-ftp.csv'])

  // ---- pantalla: la lista muestra los conectores y «Probar conexión»
  await login(page)
  await page.goto('/system/integrations')
  const rowOf = (name: string) => page.locator('tr, li.dt-card, .dt-cards > li').filter({ has: page.getByText(name, { exact: true }) }).first()
  await expect(rowOf(`0 SFTP vivo ${s}`)).toContainText('SFTP', { timeout: 30_000 })
  await expect(rowOf(`0 FTP vivo ${s}`)).toContainText('FTP')
  await rowOf(`0 SFTP vivo ${s}`).getByRole('button', { name: 'Probar conexión' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('Conexión correcta', { timeout: 30_000 })
  await shot(page, isMobile, 'l134-lista', 900)
  await rowOf(`0 SFTP vivo ${s}`).getByRole('button', { name: 'Editar' }).click()
  const dialog = page.getByRole('dialog', { name: /Editar/ })
  await expect(dialog.getByLabel(/^Servidor/)).toHaveValue('127.0.0.1')
  await expect(dialog.getByLabel(/^Huella del servidor/)).toHaveValue(/^SHA256:/)
  await expect(dialog.getByText('Déjela vacía para conservar la guardada.').first()).toBeVisible()
  await shot(page, isMobile, 'l134-formulario', 1700)
  await dialog.getByLabel(/^Servidor/).fill('')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog.getByText('Indique el servidor (host) del conector.')).toBeVisible()
  await dialog.getByRole('button', { name: 'Cancelar' }).click()
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
