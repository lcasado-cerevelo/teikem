// PDF de las tablas de la web del personal (Lote 213, 2.ª tanda): lleva los COLORES de la marca de la compañía (Ajustes → Marca) y, si la compañía no subió logo, el de Teikem. Contra el API real.
import { expect, test } from '@playwright/test'

import { cssToRgb, pdfImageWidths, pdfText, pdfUsesFill } from './pdfHelpers'
import { API_URL, LETTERS, apiHeaders, createClient, createConsignee, createOrder, login } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('PDF de la lista de envíos del personal: colores de la compañía y logo de Teikem cuando no hay logo propio', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'la exportación de la lista es de escritorio')
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente PDF L213 ${LETTERS}`)
  const loc = await createConsignee(request, headers, client.publicId, `Colmado PDF ${LETTERS}`)
  await createOrder(request, headers, client.publicId, loc.publicId, 1)
  const setBrand = async (preset: string) => {
    const put = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers, data: { brandingJson: JSON.stringify({ preset, useCustom: false, custom: { flow: '#F37123', money: '#6C757D', neutral: '#03204B' } }) } })
    expect(put.ok(), await put.text()).toBeTruthy()
  }
  await setBrand('vino')
  try {
    await login(page)
    await page.goto('/orders')
    await expect(page.getByText(`Colmado PDF ${LETTERS}`).first()).toBeVisible({ timeout: 60_000 })
    const navbar = cssToRgb(await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--navbar')))
    expect(navbar, 'la marca vino cambió el color de la barra').not.toEqual([3, 31, 73])

    await page.getByRole('button', { name: 'Exportar', exact: true }).last().click()
    const [download] = await Promise.all([page.waitForEvent('download', { timeout: 60_000 }), page.getByRole('menuitem', { name: /PDF/ }).click()])
    const buf = (await import('node:fs')).readFileSync(await download.path())
    expect(buf.subarray(0, 5).toString()).toBe('%PDF-')
    expect(pdfText(buf)).toContain("Colmado")   // el nombre puede partirse en dos renglones de la celda
    expect(pdfUsesFill(buf, navbar), `el encabezado del PDF debe usar el color de la barra de la compañía (${navbar.join(',')})`).toBeTruthy()
    expect(pdfUsesFill(buf, [38, 50, 72]), 'ya no se usa el gris azulado fijo de antes').toBeFalsy()
    expect(pdfImageWidths(buf).length, 'sin logo propio, el PDF lleva el de Teikem').toBeGreaterThan(0)
    const out = process.env.L213_OUT
    if (out) (await import('node:fs')).copyFileSync(await download.path(), `${out}/personal-envios-marca-vino.pdf`)
  } finally {
    await setBrand('advance')
  }
})
