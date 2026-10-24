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
export async function manualLang(page: Page, lang: 'es' | 'en', what = ''): Promise<void> {
  if (MANUAL_LANG !== 'en') return
  try {
    const dd = page.locator('.langdd').first()
    if ((await dd.count()) === 0) {
      if (await isPortal(page)) {
        await portalLang(page, lang)
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

/** Portal del cliente: el idioma se lee de localStorage al arrancar; se guarda y se recarga la pantalla. */
async function portalLang(page: Page, lang: 'es' | 'en') {
  const current = await page.evaluate((k) => localStorage.getItem(k) ?? 'es', PORTAL_LANG_KEY)
  if (current === lang) return
  await page.evaluate(([k, v]) => localStorage.setItem(k, v), [PORTAL_LANG_KEY, lang] as const)
  await page.reload({ waitUntil: 'load' })
  await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => {})
  await page.waitForTimeout(500)
}
