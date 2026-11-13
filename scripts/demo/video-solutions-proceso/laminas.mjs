// Genera las láminas del video (1920×1080) con Playwright: QuickBooks Desktop (menú de importación) y el contenido del archivo .iif exportado.
// Uso (desde web-app): node ../scripts/demo/video-solutions-proceso/laminas.mjs
import { readFileSync, mkdirSync } from 'node:fs'
import { chromium } from '@playwright/test'
const B = 'F:/Download/TeikemVideo/solutions-proceso'
mkdirSync(`${B}/laminas`, { recursive: true })
const iif = readFileSync(`${B}/datos/export-quickbooks.iif`, 'utf8').split(/\r?\n/).filter(Boolean)
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;')
const css = `body{margin:0;width:1920px;height:1080px;background:#0f2a4a;color:#fff;font-family:'Segoe UI',Arial,sans-serif;display:flex;flex-direction:column;justify-content:center;align-items:center}
h1{font-size:58px;margin:0 0 14px}p.s{font-size:30px;opacity:.8;margin:0 0 54px}
.m{display:flex;gap:22px;align-items:center}.b{background:#fff;color:#0f2a4a;border-radius:14px;padding:26px 40px;font-size:44px;font-weight:700}.b.f{background:#f28c28;color:#fff}.a{font-size:52px;opacity:.7}
.c{background:#fff;color:#111;border-radius:14px;padding:30px 36px;font-family:Consolas,monospace;font-size:27px;line-height:1.7;white-space:pre;max-width:1760px;overflow:hidden}
.h{color:#888}.t{color:#0b5cad;font-weight:700}.n{margin-top:34px;font-size:30px;opacity:.85}`
const l1 = `<style>${css}</style><h1>En QuickBooks Desktop</h1><p class="s">El archivo se importa desde el menú</p><div class="m"><div class="b">Archivo</div><div class="a">›</div><div class="b">Utilidades</div><div class="a">›</div><div class="b">Importar</div><div class="a">›</div><div class="b f">Archivos IIF</div></div>`
const rows = iif.map((r) => { const f = r.split('\t'); const cls = r.startsWith('!') ? 'h' : r.startsWith('TRNS') ? 't' : ''; return `<span class="${cls}">${esc(f.slice(0, 8).join('  ·  '))}</span>` }).join('\n')
const l2 = `<style>${css}</style><h1>El archivo que genera Teikem (.iif)</h1><p class="s">Una línea por la factura (TRNS) y una por cada producto (SPL)</p><div class="c">${rows}</div><div class="n">Es texto plano: QuickBooks lo lee tal cual.</div>`
const br = await chromium.launch(); const ctx = await br.newContext({ viewport: { width: 1920, height: 1080 } }); const p = await ctx.newPage()
for (const [n, h] of [['quickbooks-importar', l1], ['quickbooks-iif', l2]]) { await p.setContent(`<!doctype html><meta charset="utf-8">${h}`); await p.screenshot({ path: `${B}/laminas/${n}.png` }) }
await br.close(); console.log('láminas listas')
