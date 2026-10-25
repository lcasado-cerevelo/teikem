// Capturas del manual en inglés (2026-10-24).
// Con MANUAL_LANG=en, cada captura de documentación pasa la interfaz a inglés justo antes de la foto y la devuelve a español
// justo después, sin recargar: el selector de idioma del shell (`.langdd`) cambia el diccionario y el SessionProvider invalida
// las consultas, así que los datos del servidor (estatus, listas) también llegan en inglés. Los recorridos siguen encontrando
// sus textos en español y las fotos salen en inglés. En el portal del cliente (app-cliente, sin selector en la cabecera) el
// idioma se guarda en localStorage y se recarga la página. Con MANUAL_IMG_DIR las fotos van a otra carpeta (la del manual en
// inglés). Sin esas variables no cambia nada: las capturas salen como siempre.
import { appendFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, type ElementHandle, type Locator, type Page } from '@playwright/test'

export const MANUAL_LANG: 'es' | 'en' = process.env.MANUAL_LANG === 'en' ? 'en' : 'es'
const LOG = process.env.MANUAL_LANG_LOG ?? join(tmpdir(), 'teikem-manual-lang.log')
const PORTAL_LANG_KEY = 'teikem.cliente.lang'

/** Carpeta de las capturas: MANUAL_IMG_DIR (con separador final) o la de siempre. */
export function manualImgDir(defaultDir: string): string {
  const d = process.env.MANUAL_IMG_DIR
  if (!d) return defaultDir
  return /[\\/]$/.test(d) ? d : `${d}/`
}

function note(page: Page, what: string, why: string) {
  try {
    appendFileSync(LOG, `${what} | ${page.url()} | ${why}\n`)
  } catch {
    // sin bitácora no pasa nada
  }
}

/**
 * Elemento a fotografiar, resuelto ANTES de cambiar el idioma: muchos localizadores de los recorridos buscan por texto en
 * español y dejarían de encontrar el elemento una vez en inglés. El nodo del DOM sigue siendo el mismo tras el cambio.
 * Sin MANUAL_LANG=en devuelve lo mismo que recibió.
 */
export async function manualTarget<T extends Locator | Page | undefined>(target: T): Promise<T> {
  if (MANUAL_LANG !== 'en' || !target || 'goto' in target) return target
  const handle = await (target as Locator).elementHandle({ timeout: 15_000 })
  return (handle ?? target) as unknown as T
}

/** Tipo común de lo que devuelve manualTarget cuando no es la página. */
export type ManualShotTarget = Locator | ElementHandle

/**
 * Pasa la interfaz al idioma pedido usando el selector del shell (o el de las pantallas de acceso y legales). Solo actúa con
 * MANUAL_LANG=en. Usa dispatchEvent para que funcione aunque haya un diálogo modal abierto encima de la cabecera. En el portal
 * del cliente guarda el idioma y recarga. Si la pantalla no tiene selector, lo anota en la bitácora y la captura sale como esté.
 */
export async function manualLang(page: Page, lang: 'es' | 'en', what = '', opts: { refetch?: boolean } = {}): Promise<void> {
  if (MANUAL_LANG !== 'en') return
  try {
    const dd = page.locator('.langdd').first()
    if ((await dd.count()) === 0) {
      if (await isPortal(page)) {
        await portalLang(page, lang, opts.refetch === true)
        return
      }
      if (lang === 'en') note(page, what, 'sin selector de idioma: la captura queda en español')
      return
    }
    const btn = dd.locator('button[aria-haspopup="menu"]')
    const current = ((await btn.locator('.lbl').textContent()) ?? '').trim().toLowerCase()
    if (current === lang) return
    await btn.dispatchEvent('click')
    await dd
      .locator('button[role="menuitemradio"]')
      .filter({ hasText: lang === 'en' ? /English|Inglés/ : /Español|Spanish/ })
      .first()
      .dispatchEvent('click')
    await expect(btn.locator('.lbl')).toHaveText(lang.toUpperCase(), { timeout: 5_000 })
    // el SessionProvider invalida las consultas al cambiar de idioma: esperar a que vuelvan en el idioma nuevo
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {})
    await page.waitForTimeout(250)
  } catch (e) {
    note(page, what, `no se pudo cambiar a ${lang}: ${String(e).split('\n')[0]}`)
  }
}

async function isPortal(page: Page): Promise<boolean> {
  const origin = new URL(page.url()).origin
  const portal = process.env.CLIENT_APP_URL ?? 'http://localhost:8081'
  return origin === new URL(portal).origin
}

/**
 * Portal del cliente. Sin recargar, si la página publica `window.__setLang` (el servidor estático de las capturas lo expone sin
 * tocar la app); si no, el idioma se guarda en localStorage y se recarga la pantalla (se pierde lo que estuviera abierto).
 */
async function portalLang(page: Page, lang: 'es' | 'en', refetch = false) {
  const current = await page.evaluate((k) => localStorage.getItem(k) ?? 'es', PORTAL_LANG_KEY)
  if (current === lang) return
  const hot = await page.evaluate((l) => {
    const w = window as unknown as { __setLang?: (l: string) => void }
    if (!w.__setLang) return false
    w.__setLang(l)
    return true
  }, lang)
  if (hot && !refetch) {
    await page.waitForTimeout(600)
    return
  }
  // refetch: las etiquetas que vienen del servidor (estatus, servicio, paquete…) se piden una sola vez; para que lleguen en el
  // idioma nuevo se recarga la pantalla (sesión y ruta se conservan; se pierde lo escrito en un formulario a medias)
  if (hot) {
    await page.waitForTimeout(300)
    await page.reload({ waitUntil: 'load' })
    await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => {})
    await page.waitForTimeout(700)
    return
  }
  await page.evaluate(([k, v]) => localStorage.setItem(k, v), [PORTAL_LANG_KEY, lang] as const)
  await page.reload({ waitUntil: 'load' })
  await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => {})
  await page.waitForTimeout(500)
}

/**
 * Manual en inglés: las etiquetas que resuelve el servidor (estatus, tipos, pasos) llegan en el idioma de la cabecera
 * `Accept-Language`. Con MANUAL_LANG=en esta función hace que TODAS las peticiones del API que salen de la página lleven
 * `Accept-Language: en`, para que esas etiquetas ya vengan en inglés aunque la pantalla se haya cargado en español.
 * Sin MANUAL_LANG=en no hace nada. Los recorridos que afirman etiquetas del servidor deben aceptar también la versión en inglés.
 */
export async function manualApiEnglish(page: Page): Promise<void> {
  if (MANUAL_LANG !== 'en') return
  await page.route('**/api/v1/**', (route) => route.continue({ headers: { ...route.request().headers(), 'accept-language': 'en' } }))
}

/**
 * Nombres de datos de la base de pruebas que, para capturar en inglés, se renombran a inglés en la base (indicadores, gráficos,
 * informes y zonas por omisión). Con MANUAL_LANG=en los recorridos buscan el nombre en inglés; sin la variable, el original.
 */
const NAMES_EN: Record<string, string> = {
  'Productos bajo mínimo': 'Products below minimum',
  'Actividad reciente': 'Recent activity',
  'Valor de inventario por categoría': 'Inventory value by category',
  'Movimientos de inventario por tipo': 'Inventory movements by type',
  'Cambios por acción': 'Changes by action',
  'Ventas por día': 'Sales by day',
  'Productos activos': 'Active products',
  'Inventario disponible': 'Available inventory',
  'Rentas por vencer (7 días)': 'Rentals expiring (7 days)',
  'Rentas vencidas': 'Overdue rentals',
  'Devoluciones de renta por motivo': 'Rental returns by reason',
  'Equipos en proceso': 'Units in process',
  'Equipos en renta por cliente': 'Rented units by client',
  'Cambios por usuario y acción': 'Changes by user and action',
  'Cambios por usuario': 'Changes by user',
  Clientes: 'Clients',
  Cuarentena: 'Quarantine',
  'En renta': 'Rented out',
  Reserva: 'Reserve',
  Recepción: 'Receiving',
  'Formato estándar': 'Standard format',
}
/** El almacén ALM-01 solo se renombra a inglés en la base de pruebas durante las corridas que lo piden (MANUAL_WH_EN=1). */
const WAREHOUSE_EN: Record<string, string> = { 'ALM-01 · Almacén principal': 'ALM-01 · Main warehouse', 'Almacén principal': 'Main warehouse' }
export function nm(es: string): string {
  if (MANUAL_LANG !== 'en') return es
  if (process.env.MANUAL_WH_EN === '1' && WAREHOUSE_EN[es]) return WAREHOUSE_EN[es]
  return NAMES_EN[es] ?? es
}

/**
 * Documento legal (Lote 99): los datos del operador salen de Legal:* y, sin configurar, traen marcadores en español entre corchetes
 * («[RAZÓN SOCIAL DEL OPERADOR]»). Para las capturas en inglés se traducen los marcadores en la respuesta (solo con MANUAL_LANG=en).
 */
export async function manualLegalMarkers(page: Page): Promise<void> {
  if (MANUAL_LANG !== 'en') return
  await page.route('**/api/v1/legal/current*', async (route) => {
    const res = await route.fetch()
    const body = (await res.text())
      .replaceAll('[RAZÓN SOCIAL DEL OPERADOR]', '[OPERATOR LEGAL NAME]')
      .replaceAll('[DIRECCIÓN POSTAL DEL OPERADOR]', '[OPERATOR POSTAL ADDRESS]')
      .replaceAll('[CORREO DE PRIVACIDAD]', '[PRIVACY EMAIL]')
      .replaceAll('RAZ\u00d3N SOCIAL DEL OPERADOR', 'OPERATOR LEGAL NAME')
    await route.fulfill({ response: res, body })
  })
}

/**
 * Avisos (toasts) que la pantalla crea con t(): el aviso queda en el idioma que había al crearse. Con MANUAL_LANG=en esta ayuda
 * resuelve el elemento, pasa a inglés ANTES del clic, hace el clic, toma la captura enseguida (el aviso dura poco) y vuelve a
 * español. La captura queda registrada para que el `shot()` posterior del recorrido (alreadyShot) no la repita sin el aviso.
 * Sin MANUAL_LANG=en solo hace el clic.
 */
const takenShots = new Set<string>()
export async function manualClickShot(
  page: Page,
  locator: Locator,
  name: string,
  takeShot: () => Promise<void>,
  afterClick?: () => Promise<void>,
): Promise<void> {
  if (MANUAL_LANG !== 'en') {
    await locator.click()
    return
  }
  const handle = await locator.elementHandle({ timeout: 15_000 })
  await manualLang(page, 'en', name)
  await handle!.click()
  if (afterClick) await afterClick()
  await takeShot()
  takenShots.add(name)
  await manualLang(page, 'es')
}
export function alreadyShot(name: string): boolean {
  return takenShots.delete(name)
}

/**
 * Pide al servidor en inglés (Accept-Language: en) las respuestas que coinciden con `glob`, solo con MANUAL_LANG=en. Sirve para las etiquetas que
 * el servidor resuelve (servicio, paquete, estatus) en pantallas que se abren una vez en español y donde recargar perdería lo escrito.
 */
export async function manualServerEnglish(page: Page, glob: string): Promise<void> {
  if (MANUAL_LANG !== 'en') return
  await page.route(glob, async (route) => {
    await route.continue({ headers: { ...route.request().headers(), 'accept-language': 'en' } })
  })
}

/**
 * Manual en inglés: los avisos (toasts) duran 3,5 s y el cambio de idioma antes y después de pulsar consume buena parte de ese
 * tiempo. Con MANUAL_LANG=en, esto alarga a 9 s la vida de los avisos de la página (solo en la prueba; la aplicación no cambia)
 * para que sigan en pantalla cuando se toma la foto. Sin MANUAL_LANG=en no hace nada.
 */
export async function manualLongToasts(page: Page): Promise<void> {
  if (MANUAL_LANG !== 'en') return
  await page.addInitScript(() => {
    const orig = window.setTimeout.bind(window)
    window.setTimeout = ((fn: TimerHandler, ms?: number, ...args: unknown[]) =>
      orig(fn, ms === 3500 ? 9000 : ms, ...args)) as typeof window.setTimeout
  })
}

/**
 * Mensajes que el servidor devuelve en el idioma de la petición (errores de validación bajo un campo, resultados de una acción):
 * quedan en español si la acción se hizo con la interfaz en español. Con MANUAL_LANG=en esta ayuda vuelve a pulsar el mismo botón
 * (resuelto ANTES, en español) ya en inglés y deja la interfaz en inglés; el `shot()` siguiente toma la foto y devuelve el español.
 * Sin MANUAL_LANG=en no hace nada.
 */
export async function manualResubmit(page: Page, locator: Locator, what = ''): Promise<void> {
  if (MANUAL_LANG !== 'en') return
  const handle = await locator.elementHandle({ timeout: 15_000 })
  await manualLang(page, 'en', what)
  await handle!.click()
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {})
  await page.waitForTimeout(700)
}
