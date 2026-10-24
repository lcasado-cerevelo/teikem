// Genera el índice de ayuda de Kemi (Lote 124) a partir del manual funcional y de la FAQ (docs/manual/*.md).
// Misma idea que Yudi en SEPHAS: el buscador corre dentro de la aplicación sobre un índice ya armado, sin servicio ni IA.
// Cada entrada: { k: 'f' (FAQ) | 'm' (manual), t: título/pregunta, c: contexto (capítulo o sección), x: texto limpio (la respuesta) }.
// Correr:  npm run help:index   (también lo corre `npm run check`). El resultado se versiona en src/features/notifications/helpIndex.json.
// Lote 218: DOS índices, uno por público. El del PERSONAL (web) lleva todo el manual y la FAQ; el del PORTAL del cliente (app-cliente/src/features/kemi/helpIndex.json) lleva SOLO lo que
// el cliente puede ver: los capítulos y las secciones de la FAQ marcados con el comentario <!-- kemi:portal --> (invisible al leer el markdown); un apartado o una pregunta con <!-- kemi:staff --> adentro se queda fuera, igual que los apartados «Endpoints». En un capítulo, el marcador va en
// cualquier línea y vale para todo el capítulo; en la FAQ va justo debajo del «## » y vale para esa sección. Lo que no se marca NO le llega al cliente (lo interno del personal no se filtra).
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const MANUAL = join(here, '..', '..', 'docs', 'manual')
const OUT = join(here, '..', 'src', 'features', 'notifications', 'helpIndex.json')
const OUT_PORTAL = join(here, '..', '..', 'app-cliente', 'src', 'features', 'kemi', 'helpIndex.json')
const PORTAL_MARK = /<!--\s*kemi:portal\s*-->/
const STAFF_MARK = /<!--\s*kemi:staff\s*-->/   // dentro de un apartado o una pregunta: esa entrada NO va al portal aunque su capítulo o sección sí
const MAX = 1100

/** Quita el formato markdown y deja texto legible: párrafos, viñetas y filas de tabla con « · ». */
export function limpiar(md) {
  const lines = md.split('\n').map((l) => l.replace(/\r$/, ''))
  const out = []
  let enCodigo = false
  let enJson = false
  for (let l of lines) {
    // 2026-10-22 (Luis): Kemi es para el usuario, no para el programador — nada de JSON ni llamadas a la API en la respuesta
    if (/^\s*(```|~~~)/.test(l)) { enCodigo = !enCodigo; continue }
    if (enCodigo) continue
    if (/^\s*<!--.*-->\s*$/.test(l)) continue   // comentarios del markdown (marcadores de público): no son texto
    if (enJson) { if (l.includes('`')) enJson = false; continue }
    if (/`\{\s*"/.test(l) && (l.match(/`/g) ?? []).length % 2 === 1) { enJson = true; l = l.replace(/\s*[—-]*\s*`\{\s*".*$/, ''); if (!l.trim() || /^\s*[-*]\s*$/.test(l)) continue }
    if (/^\s*(GET|POST|PUT|PATCH|DELETE)\s+\/\S*/.test(l)) continue
    if (/^\s*[{}\[\]],?\s*$/.test(l) || /^\s*"[^"]+"\s*:/.test(l)) continue
    l = l.replace(/`[^`]*[{}][^`]*"[^`]*`/g, '').replace(/`(?:(?:GET|POST|PUT|PATCH|DELETE)\s+)?\/api\/[^`]*`/g, '').replace(/`(?:GET|POST|PUT|PATCH|DELETE)\s+\/[^`]*`/g, '')
    l = l.replace(/\b(?:GET|POST|PUT|PATCH|DELETE)(?:\/(?:GET|POST|PUT|PATCH|DELETE))*\s+\/[\w\-/{}?=&.:|]+/g, '').replace(/\/api\/v\d\/[\w\-/{}?=&.:|]*/g, '').replace(/\(\s*\)/g, '')
    if (/^\s*\|?[\s:|-]+\|?\s*$/.test(l) && l.includes('-')) continue // separador de tabla
    if (/^\s*\|/.test(l)) l = l.trim().replace(/^\||\|$/g, '').split('|').map((c) => c.trim()).filter(Boolean).join(' · ')
    l = l
      .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/\*\*([^*]+)\*\*/g, '$1')
      .replace(/(^|[^*])\*([^*\s][^*]*)\*/g, '$1$2')
      .replace(/`([^`]+)`/g, '$1')
      .replace(/^>\s?/, '')
      .replace(/^#{1,6}\s+/, '')
    out.push(l.trimEnd())
  }
  return out.join('\n').replace(/\n{3,}/g, '\n\n').trim()
}

function recortar(s) {
  if (s.length <= MAX) return s
  const cut = s.slice(0, MAX)
  const i = Math.max(cut.lastIndexOf('. '), cut.lastIndexOf('\n'), cut.lastIndexOf(' '))
  return `${cut.slice(0, i > MAX * 0.6 ? i : MAX).trimEnd()}…`
}

/** FAQ: «## Lote» (contexto), «### Tema» y preguntas en negrita que ocupan su propio párrafo; lo que sigue es la respuesta. */
export function indexarFaq(md) {
  const entries = []
  let ctx = ''
  let cur = null
  let portal = false   // la sección «##» actual está marcada <!-- kemi:portal -->
  const flush = () => {
    if (cur) {
      const x = recortar(limpiar(cur.body.join('\n')))
      if (x.length > 20) entries.push({ k: 'f', t: cur.t, c: cur.c, x, ...(cur.p ? { p: true } : {}), ...(cur.s ? { s: true } : {}) })
    }
    cur = null
  }
  const lines = md.replace(/\r/g, '').split('\n')
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i]
    const h = /^(#{2,3})\s+(.*)$/.exec(l)
    if (PORTAL_MARK.test(l)) { portal = true; if (cur) cur.p = true; continue }
    if (STAFF_MARK.test(l)) { if (cur) cur.s = true; continue }
    if (h) {
      flush()
      if (h[1] === '##') { ctx = h[2].trim(); portal = false }
      else ctx = `${ctx.split(' › ')[0]} › ${h[2].trim()}`
      continue
    }
    // pregunta: párrafo que empieza con ** y cuyo cierre ** termina una línea
    if (l.startsWith('**') && (i === 0 || lines[i - 1].trim() === '')) {
      let j = i
      let acc = l
      while (j < lines.length && !/\*\*\s*$/.test(lines[j]) && j - i < 4) acc += ` ${lines[++j] ?? ''}`
      if (/^\*\*[^*].*\*\*\s*$/.test(acc.trim()) && !/\*\*.+\*\*.+\*\*/.test(acc)) {
        flush()
        cur = { t: limpiar(acc), c: ctx.replace(/^Lote \d+[a-z]? — /, ''), body: [], p: portal }
        i = j
        continue
      }
    }
    if (cur) cur.body.push(l)
  }
  flush()
  return entries
}

/** Manual: un capítulo por archivo (h1); cada «##» o «###» con su texto hasta el siguiente encabezado. */
export function indexarManual(md) {
  const lines = md.replace(/\r/g, '').split('\n')
  const entries = []
  const portal = PORTAL_MARK.test(md)   // el capítulo entero es del portal del cliente
  let chapter = ''
  let h2 = ''
  let cur = null
  const flush = () => {
    if (cur) {
      const x = recortar(limpiar(cur.body.join('\n')))
      // un apartado genérico («Qué hace», «Casos frecuentes») se rotula con su capítulo para que se entienda solo
      const t = cur.t.length <= 24 && chapter ? `${chapter} — ${cur.t}` : cur.t
      if (x.length > 80) entries.push({ k: 'm', t, c: cur.c, x, ...(portal ? { p: true } : {}), ...(cur.s ? { s: true } : {}) })
    }
    cur = null
  }
  for (const l of lines) {
    if (STAFF_MARK.test(l)) { if (cur) cur.s = true; continue }
    const h = /^(#{1,3})\s+(.*)$/.exec(l)
    if (h) {
      flush()
      const title = limpiar(h[2])
      if (h[1] === '#') chapter = title.replace(/^(Capítulo )?\d+\s+—\s+/, '').replace(/\s*\(Lotes?[^)]*\)\s*$/, '')
      else if (h[1] === '##') { h2 = title; cur = { t: title, c: chapter, body: [] } }
      else cur = { t: title, c: `${chapter} › ${h2}`, body: [] }
      continue
    }
    if (cur) cur.body.push(l)
  }
  flush()
  return entries
}

export function construir() {
  const files = readdirSync(MANUAL).filter((f) => f.endsWith('.md') && f !== 'README.md').sort()
  const all = []
  for (const f of files) {
    const md = readFileSync(join(MANUAL, f), 'utf8')
    all.push(...(f === 'faq.md' ? indexarFaq(md) : indexarManual(md)))
  }
  return all
}

/** Un índice sin la marca interna `p` (lo que se versiona y se baja a la pantalla). */
export const sinMarca = (list) => list.map(({ p: _p, s: _s, ...e }) => e)

/** Lo que el cliente puede ver: lo marcado para el portal, sin lo marcado «staff» ni los apartados de endpoints (cosa de programadores). */
export const paraPortal = (list) => list.filter((e) => e.p && !e.s && !/\bEndpoints\s*$/i.test(e.t))

function escribir(path, list, etiqueta) {
  const json = JSON.stringify(sinMarca(list))
  let same = false
  try { same = readFileSync(path, 'utf8') === json } catch { /* primera vez */ }
  if (!same) { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, json) }
  const faq = list.filter((e) => e.k === 'f').length
  console.log(`help:index ${etiqueta} — ${list.length} entradas (${faq} de la FAQ, ${list.length - faq} del manual), ${(json.length / 1024).toFixed(0)} KB${same ? ' (sin cambios)' : ''}`)
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const all = construir()
  escribir(OUT, all, 'personal')
  escribir(OUT_PORTAL, paraPortal(all), 'portal')
}
