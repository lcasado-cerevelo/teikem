// Genera el índice de ayuda de Kemi (Lote 124) a partir del manual funcional y de la FAQ (docs/manual/*.md).
// Misma idea que Yudi en SEPHAS: el buscador corre dentro de la aplicación sobre un índice ya armado, sin servicio ni IA.
// Cada entrada: { k: 'f' (FAQ) | 'm' (manual), t: título/pregunta, c: contexto (capítulo o sección), x: texto limpio (la respuesta) }.
// Correr:  npm run help:index   (también lo corre `npm run check`). El resultado se versiona en src/features/notifications/helpIndex.json.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const MANUAL = join(here, '..', '..', 'docs', 'manual')
const OUT = join(here, '..', 'src', 'features', 'notifications', 'helpIndex.json')
const MAX = 1100

/** Quita el formato markdown y deja texto legible: párrafos, viñetas y filas de tabla con « · ». */
export function limpiar(md) {
  const lines = md.split('\n').map((l) => l.replace(/\r$/, ''))
  const out = []
  for (let l of lines) {
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
  const flush = () => {
    if (cur) {
      const x = recortar(limpiar(cur.body.join('\n')))
      if (x.length > 20) entries.push({ k: 'f', t: cur.t, c: cur.c, x })
    }
    cur = null
  }
  const lines = md.replace(/\r/g, '').split('\n')
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i]
    const h = /^(#{2,3})\s+(.*)$/.exec(l)
    if (h) {
      flush()
      if (h[1] === '##') ctx = h[2].trim()
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
        cur = { t: limpiar(acc), c: ctx.replace(/^Lote \d+[a-z]? — /, ''), body: [] }
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
  let chapter = ''
  let h2 = ''
  let cur = null
  const flush = () => {
    if (cur) {
      const x = recortar(limpiar(cur.body.join('\n')))
      // un apartado genérico («Qué hace», «Casos frecuentes») se rotula con su capítulo para que se entienda solo
      const t = cur.t.length <= 24 && chapter ? `${chapter} — ${cur.t}` : cur.t
      if (x.length > 80) entries.push({ k: 'm', t, c: cur.c, x })
    }
    cur = null
  }
  for (const l of lines) {
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

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const all = construir()
  const json = JSON.stringify(all)
  let same = false
  try { same = readFileSync(OUT, 'utf8') === json } catch { /* primera vez */ }
  if (!same) writeFileSync(OUT, json)
  const faq = all.filter((e) => e.k === 'f').length
  console.log(`help:index — ${all.length} entradas (${faq} de la FAQ, ${all.length - faq} del manual), ${(json.length / 1024).toFixed(0)} KB${same ? ' (sin cambios)' : ''}`)
}
