// Ayudas para grabar videos: cursor falso, resaltado antes del clic y `paso()` que acompasa al audio (docs/video/REGLAS.md).
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { expect, request, type Locator, type Page } from '@playwright/test'
import { ADMIN, API_URL } from '../staffHelpers'

export const VIDEO_ID = 'solutions-proceso'

/** Inicia sesión FUERA de cámara por API en la compañía indicada (tenantId) y devuelve los tokens; la contraseña nunca se ve. */
export async function tokensFueraDeCamara(tenantId: number): Promise<Record<string, unknown>> {
  const ctx = await request.newContext()
  try {
    const res = await ctx.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId } })
    expect(res.ok(), `login ${res.status()}`).toBeTruthy()
    const body = (await res.json()) as { status?: string; tokens?: Record<string, unknown> }
    expect(body.status).toBe('ok')
    const headers = { Authorization: `Bearer ${String(body.tokens?.accessToken ?? '')}` }
    const st = (await (await ctx.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
    if (st.required) await ctx.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: st.currentVersion, lang: 'es' } })
    return { ...body.tokens, tenantId }
  } finally {
    await ctx.dispose()
  }
}

/** Deja la sesión puesta en la pestaña antes de que cargue la web (sessionStorage 'teikem.auth'). */
export async function ponerSesion(page: Page, tokens: Record<string, unknown>) {
  await page.addInitScript((t) => { try { sessionStorage.setItem('teikem.auth', JSON.stringify(t)) } catch { /* sin almacenamiento */ } }, tokens)
}

const CSS = `
#vid-cursor{position:fixed;z-index:2147483647;width:26px;height:26px;pointer-events:none;left:0;top:0;transform:translate(-4px,-2px);filter:drop-shadow(0 2px 3px rgba(0,0,0,.45))}
.vid-resalta{outline:3px solid #ff8a00 !important;outline-offset:3px !important;border-radius:6px;transition:outline .15s}
.vid-clic{position:fixed;z-index:2147483646;pointer-events:none;width:34px;height:34px;margin:-17px 0 0 -17px;border-radius:50%;border:3px solid #ff8a00;animation:vidclic .45s ease-out forwards}
@keyframes vidclic{from{transform:scale(.4);opacity:1}to{transform:scale(1.6);opacity:0}}
`

/** Inyecta el cursor falso (flecha) que sigue al ratón y un anillo al hacer clic. */
export async function prepararCursor(page: Page) {
  await page.addInitScript(({ css }) => {
    const montar = () => {
      if (document.getElementById('vid-cursor')) return
      const st = document.createElement('style'); st.textContent = css; document.head.appendChild(st)
      const c = document.createElement('div'); c.id = 'vid-cursor'
      c.innerHTML = '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M3 2l7 19 3-8 8-3z" fill="#111" stroke="#fff" stroke-width="1.6" stroke-linejoin="round"/></svg>'
      document.documentElement.appendChild(c)
      document.addEventListener('mousemove', (e) => { c.style.left = e.clientX + 'px'; c.style.top = e.clientY + 'px' }, true)
      document.addEventListener('mousedown', (e) => {
        const r = document.createElement('div'); r.className = 'vid-clic'; r.style.left = e.clientX + 'px'; r.style.top = e.clientY + 'px'
        document.documentElement.appendChild(r); setTimeout(() => r.remove(), 600)
      }, true)
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', montar); else montar()
  }, { css: CSS })
}

export const pausa = (page: Page, ms: number) => page.waitForTimeout(ms)

/** Mueve el cursor al centro del elemento en pasos suaves, lo resalta ~300 ms y hace clic. */
export const modo = { rapido: false }
export async function clicEn(page: Page, el: Locator, opts: { resaltar?: boolean } = {}) {
  await el.scrollIntoViewIfNeeded({ timeout: 8000 })
  await page.waitForTimeout(modo.rapido ? 120 : 500)   // que termine el desplazamiento suave antes de medir
  const box = await el.boundingBox()
  if (!box) throw new Error('clicEn: el elemento no tiene caja')
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: modo.rapido ? 8 : 25 })
  if (opts.resaltar !== false && !modo.rapido) {
    await el.evaluate((n) => n.classList.add('vid-resalta'))
    await page.waitForTimeout(300)
  }
  await page.mouse.down(); await page.mouse.up()
  await el.evaluate((n) => n.classList.remove('vid-resalta'), undefined, { timeout: 500 }).catch(() => {})
}

/** Resalta un elemento unos ms sin tocarlo (para señalar un dato mientras se narra). */
export async function señalar(page: Page, el: Locator, ms = 1800) {
  await el.scrollIntoViewIfNeeded({ timeout: 8000 })
  const box = await el.boundingBox()
  if (box) await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 30 })
  await el.evaluate((n) => n.classList.add('vid-resalta'))
  await page.waitForTimeout(ms)
  await el.evaluate((n) => n.classList.remove('vid-resalta'), undefined, { timeout: 500 }).catch(() => {})
}

/** Escribe como una persona. */
export async function escribir(el: Locator, texto: string) {
  await el.pressSequentially(texto, { delay: 60 })
}

export interface Reloj {
  t0: number
  pasos: { nombre: string; ms: number; objetivoMs: number }[]
  audioSeg?: number
  inicios?: number[]
  tPagina?: number
}
export interface Cue { l: number; lead?: number }

/** Reloj de la escena. Con `escena` carga el audio medido (audio/<escena>.lineas.json): cada paso arranca cuando empieza su frase. `tPagina` = instante en que se creó la página (el video empieza ahí). */
export function nuevoReloj(escena?: string, tPagina?: number): Reloj {
  const r: Reloj = { t0: Date.now(), pasos: [], tPagina }
  if (escena) {
    const txt = readFileSync(`F:/Download/TeikemVideo/${VIDEO_ID}/audio/${escena}.lineas.json`, 'utf8').replace(/^﻿/, '')
    const j = JSON.parse(txt) as { audioSeg: number; inicios: number[] }
    r.audioSeg = j.audioSeg
    r.inicios = j.inicios
  }
  return r
}

/** Ejecuta la acción cuando le toca (si `objetivo` es una frase: `{ l: n }` = n-ésima frase, con 0.7 s de adelanto) y espera su resultado visible. Con un número, completa hasta esa duración (modo antiguo). */
export async function paso(page: Page, reloj: Reloj, nombre: string, objetivo: number | Cue, accion: () => Promise<void>) {
  if (typeof objetivo === 'object' && reloj.inicios) {
    const meta = (reloj.inicios[objetivo.l - 1] ?? 0) * 1000 - (objetivo.lead ?? 700)
    const ahora = Date.now() - reloj.t0
    if (ahora < meta) await page.waitForTimeout(meta - ahora)
  }
  const t = Date.now()
  console.log(`[${((t - reloj.t0) / 1000).toFixed(1)}s] >> ${nombre}`)
  await accion()
  if (typeof objetivo === 'number') { const g = Date.now() - t; if (g < objetivo) await page.waitForTimeout(objetivo - g) }
  reloj.pasos.push({ nombre, ms: Date.now() - t, objetivoMs: typeof objetivo === 'number' ? objetivo : 0 })
  console.log(`[${((Date.now() - reloj.t0) / 1000).toFixed(1)}s] ${nombre}: ${Date.now() - t} ms`)
}

/** Cierra la escena: espera a que termine la voz (+ extra) y escribe la ficha de la toma. */
export async function finEscena(page: Page, reloj: Reloj, escena: string, extraMs = 1800) {
  const meta = (reloj.audioSeg ?? 0) * 1000 + extraMs
  const ahora = Date.now() - reloj.t0
  if (ahora < meta) await page.waitForTimeout(meta - ahora)
  const tomaSeg = (Date.now() - reloj.t0) / 1000
  mkdirSync(`F:/Download/TeikemVideo/${VIDEO_ID}/tomas`, { recursive: true })
  writeFileSync(`F:/Download/TeikemVideo/${VIDEO_ID}/tomas/${escena}.json`, JSON.stringify({
    escena, audioSeg: reloj.audioSeg, tomaSeg, offsetSeg: reloj.tPagina ? (reloj.t0 - reloj.tPagina) / 1000 : null, pasos: reloj.pasos, estado: tomaSeg + 0.1 >= (reloj.audioSeg ?? 0) ? 'ok' : 'corta',
  }, null, 2))
  console.log(`toma ${tomaSeg.toFixed(1)} s, audio ${reloj.audioSeg} s`)
}

export { expect, API_URL }

/** Difumina (blur) la columna de una tabla por el texto de su encabezado: costos de compra, etc. (regla `> TAPAR:`). */
export async function taparColumna(page: Page, encabezado: string) {
  await page.evaluate((h) => {
    document.querySelectorAll('table').forEach((t) => {
      const ths = Array.from(t.querySelectorAll('thead th'))
      const i = ths.findIndex((th) => (th.textContent ?? '').trim().toLowerCase().startsWith(h.toLowerCase()))
      if (i < 0) return
      t.querySelectorAll('tbody tr').forEach((tr) => { const td = tr.children[i] as HTMLElement | undefined; if (td) td.style.filter = 'blur(7px)' })
    })
  }, encabezado)
}
