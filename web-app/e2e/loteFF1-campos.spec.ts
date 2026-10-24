// Recorrido de «Campos personalizados» (Lote F-F1, administración de definiciones) contra el API real (db-init hecho, API en API_URL, por
// defecto http://localhost:5000; compañía demo, admin con todos los permisos, módulos CUSTOM_FIELDS y ANALYTICS encendidos).
//
// Claves ÚNICAS por corrida (sufijo aleatorio): la base se reutiliza entre proyectos y corridas. Al final cada campo creado se desactiva por API.
//
// 1) La pantalla: entidades con contador y la tabla de Cliente.  2) Crear un campo de texto con formato (regex), longitudes y único.  3) Uno de número con
// mínimo y máximo.  4) Uno de selección con «Nueva lista…» (nombre y opciones separadas por coma) y valor por defecto.  5) Clave repetida: 409 con el
// mensaje exacto bajo la clave, y la clave inválida se rechaza en el formulario.  6) La ficha de un cliente (creado por API) muestra los tres campos:
// un valor que no cumple el formato se rechaza, uno válido se guarda y sobrevive a recargar.  7) Editar un campo; cambiar el tipo de uno con valores:
// 409 exacto bajo el tipo.  8) Desactivar (confirmación) → desaparece de la ficha.  9) «Incluir inactivos» y Reactivar → vuelve a la ficha.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/ff1-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import { openClientPanels } from './staffHelpers'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type CustomFieldDefinitionDto = components['schemas']['CustomFieldDefinitionDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
const RAND = Math.random().toString(36).slice(2, 7)

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string, height = 900, only?: Locator, width?: number) {
  // Móvil: sin captura de documentación; con FF1_MOBILE_DIR se guardan fuera del repositorio para revisarlas a ojo.
  if (isMobile) {
    if (process.env.FF1_MOBILE_DIR) await page.screenshot({ path: `${process.env.FF1_MOBILE_DIR}/${name}.png`, fullPage: true })
    return
  }
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: width ?? size.width, height })
  const manualShot1 = await manualTarget((only ?? page))
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
  if (size) await page.setViewportSize(size)
}

async function apiToken(request: APIRequestContext): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F18). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      if (el.parentElement?.closest('.dt-scroll')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      if (el.matches('.sw input')) return
      const r = el.getBoundingClientRect()
      if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')} [${Math.round(r.left)}, ${Math.round(r.right)}]`)
    })
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

async function ok<T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> {
  const res = await p
  expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
  return (await res.json()) as T
}

test.describe('Campos personalizados (Lote F-F1)', () => {
  test('lista por entidad, alta de texto/número/selección con lista nueva, clave repetida, ficha del cliente, editar, cambio de tipo, desactivar y reactivar', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const s = `${isMobile ? 'm' : 'd'}${RAND}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const keys = { text: `cc_${s}`, num: `peso_${s}`, sel: `prio_${s}` }
    const labels = { text: `Centro de costo ${s}`, num: `Peso ${s}`, sel: `Prioridad ${s}` }
    const ROW = isMobile ? 'li.dt-card' : 'tbody tr'
    const row = (key: string) => page.locator(ROW).filter({ hasText: key })
    const dialog = () => page.getByRole('dialog')
    const toastOf = (text: string | RegExp) => expect(page.locator('.toast.on').filter({ hasText: text })).toBeVisible()
    const save = async () => dialog().getByRole('button', { name: 'Guardar campo' }).click()

    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FF1 ${s}` } }), 'cliente')
    const created: number[] = []
    let listKey = ''

    try {
      await login(page)
      await page.goto('/analytics/custom-fields')
      await expect(page.getByRole('heading', { level: 1, name: 'Campos personalizados' })).toBeVisible()

      // ================= 1) La pantalla =================
      const nav = page.getByRole('navigation', { name: 'Entidades' })
      await expect(nav.getByRole('button')).toHaveCount(6)
      for (const name of ['Cliente', 'Envío', 'Chofer', 'Vehículo', 'Producto', 'Ubicación (consignatario)']) await expect(nav.getByRole('button', { name: new RegExp(`^${name.replace(/[()]/g, '\\$&')}`) })).toBeVisible()
      await expect(page.getByText(/Cada campo se define una vez por entidad/)).toBeVisible()
      await expect(page.getByRole('table', { name: 'Cliente' }).or(page.getByRole('list', { name: 'Cliente' })).or(page.getByText('Todavía no hay campos personalizados para esta entidad.'))).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff1-lista', 760)

      // ================= 2) Texto con formato, longitudes y único =================
      await page.getByRole('button', { name: /Nuevo campo/ }).click()
      await expect(dialog().getByRole('heading', { name: 'Nuevo campo personalizado' })).toBeVisible()
      await shot(page, isMobile, 'ff1-nuevo', 1200)
      // clave inválida: lo rechaza el formulario con el mensaje exacto del servidor
      await dialog().getByLabel(/^Clave/).fill('Mal-Nombre')
      await save()
      await expect(dialog().getByText("Clave inválida: minúsculas, dígitos y '_' (2-60), empezando por letra.")).toBeVisible()
      await expect(dialog().getByText('La etiqueta es obligatoria.')).toBeVisible()
      await dialog().getByLabel(/^Clave/).fill(keys.text)
      await dialog().getByLabel(/^Etiqueta \(español\)/).fill(labels.text)
      await dialog().getByLabel(/^Etiqueta \(inglés\)/).fill(`Cost center ${s}`)
      await dialog().getByLabel(/^Formato/).fill('^CC-\\d{3}$')
      await dialog().getByLabel('Longitud máxima').fill('6')
      await dialog().getByRole('switch', { name: 'Único' }).check()
      await dialog().getByRole('switch', { name: 'Mostrar en listados' }).check()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff1-texto', 1250)
      await save()
      await toastOf(`Campo creado: ${labels.text}`)
      await expect(dialog()).toHaveCount(0)
      await expect(row(keys.text)).toContainText('Texto')

      // ================= 3) Número con mínimo y máximo =================
      await page.getByRole('button', { name: /Nuevo campo/ }).click()
      await dialog().getByLabel(/^Tipo/).selectOption('NUMBER')
      await expect(dialog().getByLabel(/^Formato/)).toHaveCount(0)
      await dialog().getByLabel(/^Clave/).fill(keys.num)
      await dialog().getByLabel(/^Etiqueta \(español\)/).fill(labels.num)
      await dialog().getByLabel('Valor mínimo').fill('0')
      await dialog().getByLabel('Valor máximo').fill('100')
      await save()
      await toastOf(`Campo creado: ${labels.num}`)
      await expect(row(keys.num)).toContainText('Número')

      // ================= 4) Selección con «Nueva lista…» =================
      await page.getByRole('button', { name: /Nuevo campo/ }).click()
      await dialog().getByLabel(/^Tipo/).selectOption('SELECT')
      await dialog().getByLabel(/^Clave/).fill(keys.sel)
      await dialog().getByLabel(/^Etiqueta \(español\)/).fill(labels.sel)
      await save()
      await expect(dialog().getByText('Escoge o crea una lista de valores')).toBeVisible()
      await dialog().getByRole('button', { name: /Nueva lista/ }).click()
      await dialog().getByLabel('Nombre de la lista').fill(`Prioridad ${s}`)
      await dialog().getByLabel('Opciones separadas por coma').fill('Alto, Medio, Bajo')
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff1-nueva-lista', 1150)
      await dialog().getByRole('button', { name: 'Crear lista' }).click()
      await toastOf(`Lista creada: Prioridad ${s}`)
      await expect(dialog().getByRole('combobox', { name: /Lista de valores/ })).toHaveValue(`Prioridad ${s}`)
      await expect(dialog().getByText('Opciones: Alto · Medio · Bajo')).toBeVisible()
      await dialog().getByLabel('Valor por defecto').selectOption({ label: 'Medio' })
      await shot(page, isMobile, 'ff1-seleccion', 1250)
      await save()
      await toastOf(`Campo creado: ${labels.sel}`)
      await expect(row(keys.sel)).toContainText(`Lista: Prioridad ${s} · 3 opciones`)

      const all = await ok<CustomFieldDefinitionDto[]>(request.get(`${API_URL}/api/v1/custom-fields/definitions?entityType=CLIENT&includeInactive=true`, { headers }), 'definiciones')
      for (const d of all.filter((x) => Object.values(keys).includes(x.fieldKey ?? ''))) created.push(d.id ?? 0)
      expect(created).toHaveLength(3)
      const sel = all.find((d) => d.fieldKey === keys.sel)
      listKey = sel?.refEntity ?? ''
      expect(sel).toMatchObject({ dataType: 'SELECT', defaultValue: 'MEDIO', labels: { es: labels.sel, en: labels.sel } })
      expect(all.find((d) => d.fieldKey === keys.text)).toMatchObject({ isUnique: true, showInList: true, labels: { en: `Cost center ${s}` } })
      expect(JSON.parse(all.find((d) => d.fieldKey === keys.text)?.validationJson ?? '{}')).toEqual({ regex: '^CC-\\d{3}$', maxLength: 6 })
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff1-tres-campos', 760)

      // ================= 5) Clave repetida: 409 exacto bajo la clave =================
      await page.getByRole('button', { name: /Nuevo campo/ }).click()
      await dialog().getByLabel(/^Clave/).fill(keys.text)
      await dialog().getByLabel(/^Etiqueta \(español\)/).fill('Otro')
      await save()
      await expect(dialog().getByText(`Ya existe el campo '${keys.text}' en CLIENT.`)).toBeVisible()
      await expect(dialog()).toBeVisible()
      await shot(page, isMobile, 'ff1-clave-repetida', 1150)
      await dialog().getByRole('button', { name: 'Cancelar' }).click()
      await expect(dialog()).toHaveCount(0)

      // ================= 6) La ficha del cliente: los tres campos, valor inválido y válido =================
      await page.goto(`/catalog/clients?client=${client.publicId}`)
      await openClientPanels(page)
      const panel = page.locator('section.panel').filter({ has: page.getByRole('heading', { name: 'Campos personalizados' }) })
      await expect(panel).toBeVisible()
      await expect(panel.getByLabel(labels.text)).toBeVisible()
      await expect(panel.getByLabel(labels.num)).toBeVisible()
      await expect(panel.getByLabel(labels.sel)).toHaveValue('MEDIO') // el valor por defecto de la definición
      await panel.getByLabel(labels.text).fill('XX-1')
      await panel.getByLabel(labels.num).fill('50')
      await panel.getByRole('button', { name: 'Guardar' }).click()
      await expect(panel.getByText('No cumple el formato requerido.')).toBeVisible()
      await panel.getByLabel(labels.text).fill('CC-123')
      await panel.getByLabel(labels.sel).selectOption({ label: 'Alto' })
      await panel.getByRole('button', { name: 'Guardar' }).click()
      await expect(page.locator('.toast.on').filter({ hasText: 'guardad' })).toBeVisible()
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff1-ficha', 1100, panel, 1100)
      await page.reload()
      await openClientPanels(page)
      const panel2 = page.locator('section.panel').filter({ has: page.getByRole('heading', { name: 'Campos personalizados' }) })
      await expect(panel2.getByLabel(labels.text)).toHaveValue('CC-123')
      await expect(panel2.getByLabel(labels.num)).toHaveValue('50')
      await expect(panel2.getByLabel(labels.sel)).toHaveValue('ALTO')

      // ================= 7) Editar y cambiar el tipo de un campo con valores =================
      await page.goto('/analytics/custom-fields')
      await row(keys.num).getByRole('button', { name: 'Editar' }).click()
      await expect(dialog().getByRole('heading', { name: 'Editar campo personalizado' })).toBeVisible()
      await expect(dialog().getByLabel(/^Clave/)).toHaveCount(0)
      await expect(dialog().getByText(keys.num)).toBeVisible()
      await expect(dialog().getByLabel('Valor máximo')).toHaveValue('100')
      await dialog().getByLabel(/^Etiqueta \(español\)/).fill(`${labels.num} (kg)`)
      await dialog().getByLabel('Valor máximo').fill('500')
      await save()
      await toastOf(`Campo actualizado: ${labels.num} (kg)`)
      await expect(row(keys.num)).toContainText(`${labels.num} (kg)`)

      await row(keys.num).getByRole('button', { name: 'Editar' }).click()
      await dialog().getByLabel(/^Tipo/).selectOption('TEXT')
      await save()
      await expect(dialog().getByText('No se puede cambiar el tipo de un campo que ya tiene valores guardados.')).toBeVisible()
      await shot(page, isMobile, 'ff1-cambio-de-tipo', 1250)
      await dialog().getByRole('button', { name: 'Cancelar' }).click()

      // ================= 8) Desactivar: desaparece de la tabla y de la ficha =================
      await row(keys.sel).getByRole('button', { name: 'Desactivar' }).click()
      await expect(dialog().getByText(/dejará de pedirse y de mostrarse en las fichas/)).toBeVisible()
      await shot(page, isMobile, 'ff1-desactivar', 700)
      await dialog().getByRole('button', { name: 'Desactivar' }).click()
      await toastOf(`Campo desactivado: ${labels.sel}`)
      await expect(row(keys.sel)).toHaveCount(0)
      await page.goto(`/catalog/clients?client=${client.publicId}`)
      await openClientPanels(page)
      const panel3 = page.locator('section.panel').filter({ has: page.getByRole('heading', { name: 'Campos personalizados' }) })
      await expect(panel3.getByLabel(labels.text)).toBeVisible()
      await expect(panel3.getByLabel(labels.sel)).toHaveCount(0)

      // ================= 9) Incluir inactivos y Reactivar =================
      await page.goto('/analytics/custom-fields')
      await expect(row(keys.sel)).toHaveCount(0)
      await page.getByRole('switch', { name: 'Incluir inactivos' }).check()
      await page.getByRole('combobox', { name: 'Filas por página' }).selectOption('100')   // las corridas anteriores dejan muchos campos inactivos: la fila de este recorrido puede quedar en la página 2
      await expect(row(keys.sel)).toContainText('Inactivo')
      await expect(row(keys.sel).getByRole('button', { name: 'Editar' })).toHaveCount(0)
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'ff1-inactivos', 760)
      await row(keys.sel).getByRole('button', { name: 'Reactivar' }).click()
      await toastOf(`Campo reactivado: ${labels.sel}`)
      await expect(row(keys.sel)).toContainText('Activo')
      await page.goto(`/catalog/clients?client=${client.publicId}`)
      await openClientPanels(page)
      const panel4 = page.locator('section.panel').filter({ has: page.getByRole('heading', { name: 'Campos personalizados' }) })
      await expect(panel4.getByLabel(labels.sel)).toHaveValue('ALTO') // el valor guardado se conservó
    } finally {
      // Que la corrida siguiente (y el otro proyecto) no vea estos campos.
      for (const id of created) await request.delete(`${API_URL}/api/v1/custom-fields/definitions/${id}`, { headers })
      if (listKey) await request.delete(`${API_URL}/api/v1/catalogs/lists/${listKey}`, { headers })
    }
  })
})
