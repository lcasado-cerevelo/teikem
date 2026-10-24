// Recorrido del consignatario nuevo EN LÍNEA (Lote 213) contra el API real (db-init hecho, API en API_URL; compañía demo, administrador con todos
// los permisos). Dictado de Luis: crear un consignatario desde la captura de un envío NO debe pedir el ratón.
//
// 1) Entrada rápida: se escribe un nombre que no existe en el combo del consignatario, Enter abre el formulario en línea (foco en Dirección),
//    Dirección, Pueblo (elegido con el teclado) y Enter graba; el nuevo queda elegido y el foco cae en «Paquete»; Enter guarda la orden.
//    Todo con `page.keyboard`. Se verifica que el consignatario existe en el catálogo y que la orden lo usa.
// 2) Entrada detallada: el mismo gesto dentro de la sección 02; al grabar queda elegido y el foco pasa al siguiente control.
//
// Solo escritorio: el recorrido es de teclado (en el móvil la rápida es una tarjeta táctil).
import { expect, test } from '@playwright/test'
import { API_URL, LETTERS, apiHeaders, createClient, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

interface LocationRow {
  publicId?: string
  name?: string
  line1?: string
  city?: string
  clientPublicId?: string | null
}

async function consigneesOf(request: Parameters<typeof apiHeaders>[0], headers: Record<string, string>, clientPublicId: string): Promise<LocationRow[]> {
  return ok<LocationRow[]>(
    request.get(`${API_URL}/api/v1/locations?clientId=${clientPublicId}&includeShared=false&locationType=DELIVERY&includeInactive=false`, { headers }),
    'consignatarios del cliente',
  )
}

/** Escribe el pueblo en el selector de pueblo/ZIP y lo elige con Enter cuando la lista ya se filtró. */
async function pickTown(page: import('@playwright/test').Page, town: string) {
  await page.keyboard.type(town)
  await expect(page.getByRole('listbox').getByRole('option').first()).toContainText(new RegExp(town, 'i'))
  await page.keyboard.press('Enter')
}

test.describe('Consignatario nuevo en línea — solo con teclado (Lote 213)', () => {
  test('entrada rápida: nombre nuevo, Enter, dirección, pueblo, Enter; el foco pasa a Paquete y Enter guarda la orden', async ({ page, request }, testInfo) => {
    test.skip(testInfo.project.name.startsWith('movil'), 'recorrido de teclado: solo escritorio')
    const suffix = `D${LETTERS}`
    const headers = await apiHeaders(request)
    const client = await createClient(request, headers, `Cliente L213 rápida ${suffix}`)
    const newName = `Tienda Nueva L213 ${suffix}`

    await login(page)
    await page.goto('/orders')
    await page.getByRole('button', { name: /Nuevo envío/ }).click()
    const qk = page.locator('.qk')
    await expect(qk.getByRole('button', { name: 'Guardar fila' })).toBeVisible()

    // el cliente se elige una vez (el consignatario depende de él)
    const clientBox = qk.getByRole('combobox', { name: 'Cliente', exact: true })
    await clientBox.click()
    await clientBox.fill(client.name)
    await page.getByRole('option', { name: new RegExp(client.name) }).first().click()
    // la compañía demo puede no tener servicio ni paquete predeterminados
    for (const label of ['Paquete', 'Servicio']) if ((await qk.getByLabel(label, { exact: true }).inputValue()) === '') await qk.getByLabel(label, { exact: true }).selectOption({ index: 1 })

    await page.keyboard.press('Escape') // cierra el calendario de la fecha que se abrió al escoger el cliente
    // ---- desde aquí, solo teclado ----
    const consBox = qk.getByRole('combobox', { name: 'Consignatario' })
    await consBox.focus()
    await page.keyboard.type(newName)
    await expect(page.getByRole('option', { name: /Nuevo consignatario/ })).toBeVisible()
    await page.keyboard.press('Enter')
    const panel = qk.getByTestId('consignee-inline')
    await expect(panel).toBeVisible()
    await expect(panel.getByLabel(/^Nombre/)).toHaveValue(newName)
    await expect(panel.getByLabel(/^Dirección/)).toBeFocused()

    // Enter con faltantes: el foco se queda en el primer faltante y sale el mensaje
    await page.keyboard.press('Enter')
    await expect(panel.getByText('La dirección (línea 1) es obligatoria.')).toBeVisible()
    await page.keyboard.type('Calle Luna 3')
    await page.keyboard.press('Enter')
    await expect(panel.getByText('La ciudad es obligatoria.')).toBeVisible()
    await expect(panel.getByRole('combobox')).toBeFocused()
    await pickTown(page, 'ponce')
    // con permiso de contactos el cursor pasa al teléfono; Enter graba
    await expect(panel.getByLabel('Teléfono')).toBeFocused()
    await page.keyboard.type('7875550142')
    await shot(page, false, 'l213-personal-rapida-consignatario-en-linea', 800)
    await page.keyboard.press('Enter')

    await expect(panel).toBeHidden()
    await expect(qk.getByLabel('Paquete', { exact: true })).toBeFocused()
    await expect(consBox).toHaveValue(newName)
    await page.keyboard.press('Enter')
    await expect(qk.locator('tr.saved').first()).toContainText(newName)
    await shot(page, false, 'l213-personal-rapida-guardada', 800)

    const created = (await consigneesOf(request, headers, client.publicId)).find((c) => c.name === newName)
    expect(created, 'el consignatario quedó en el catálogo').toBeTruthy()
    expect(created?.line1).toBe('Calle Luna 3')
    expect(created?.clientPublicId).toBe(client.publicId)
  })

  test('entrada detallada: el panel de la sección 02 graba, elige y pasa el foco al siguiente control', async ({ page, request }, testInfo) => {
    test.skip(testInfo.project.name.startsWith('movil'), 'recorrido de teclado: solo escritorio')
    const suffix = `D${LETTERS}`
    const headers = await apiHeaders(request)
    const client = await createClient(request, headers, `Cliente L213 detallada ${suffix}`)
    const newName = `Colmado Nuevo L213 ${suffix}`

    await login(page)
    await page.goto('/orders')
    await page.getByRole('button', { name: /Nuevo envío/ }).click()
    await page.getByRole('button', { name: /Detallada/ }).click()
    await expect(page.getByText('Ticket del envío')).toBeVisible()

    const clientBox = page.getByRole('combobox', { name: /^Cliente/ })
    await clientBox.click()
    await clientBox.fill(client.name)
    await page.getByRole('option', { name: new RegExp(client.name) }).first().click()

    const consBox = page.getByRole('combobox', { name: /^Nombre/ })
    await consBox.focus()
    await page.keyboard.type(newName)
    await page.keyboard.press('Enter')
    const panel = page.getByTestId('consignee-inline')
    await expect(panel).toBeVisible()
    await expect(panel.getByLabel(/^Dirección/)).toBeFocused()
    await page.keyboard.type('Calle Sol 9')
    await page.keyboard.press('Tab')
    await expect(panel.getByRole('combobox')).toBeFocused()
    await pickTown(page, 'ponce')
    await shot(page, false, 'l213-personal-detallada-consignatario-en-linea', 1100)
    await page.keyboard.press('Enter')

    await expect(panel).toBeHidden()
    await expect(consBox).toHaveValue(newName)
    // el foco pasa al tipo de paquete de la primera línea (se salta el interruptor de entrega especial) y la orden NO se envió
    await expect(page.locator('.oe-pkg select').first()).toBeFocused()
    await expect(page.getByText('Ticket del envío')).toBeVisible()
    await shot(page, false, 'l213-personal-detallada-guardado', 1100)

    const created = (await consigneesOf(request, headers, client.publicId)).find((c) => c.name === newName)
    expect(created, 'el consignatario quedó en el catálogo').toBeTruthy()
    expect(created?.city?.toLowerCase()).toContain('ponce')
  })
})
