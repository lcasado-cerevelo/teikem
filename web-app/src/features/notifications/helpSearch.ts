// Kemi busca (Lote 124): la búsqueda de ayuda sin IA, sobre el manual y la FAQ ya indexados (helpIndex.json, generado por scripts/build-help-index.mjs).
// Es la misma idea que Yudi en SEPHAS: se buscan las palabras de la pregunta (sin acentos ni plurales, sin las palabras vacías), las raras pesan más
// que las comunes (idf), coincidir en el título vale más que en el cuerpo, la FORMA de la pregunta decide el peso de la FAQ frente al manual
// («no me deja» es de la FAQ; «cómo se hace» es del manual) y por debajo de un suelo de calidad Kemi dice que no sabe en vez de adivinar.

export interface HelpEntry {
  /** 'f' = FAQ, 'm' = manual. */
  k: 'f' | 'm'
  /** Título del apartado o pregunta de la FAQ. */
  t: string
  /** Capítulo o tema donde vive. */
  c: string
  /** Texto limpio (la respuesta). */
  x: string
}

export interface HelpHit {
  entry: HelpEntry
  score: number
}

export type HelpIntent = 'blocked' | 'how' | 'any'

const VACIAS = new Set(
  ('de la el los las un una unos unas y o que no se en a al para con por es son su sus este esta ese esa lo le me mi te tu ha he hay como cuando donde muy mas' +
    ' segun sobre desde hasta entre ante bajo sin sino pero tambien tampoco aun cada ademas asi mientras pues quiero puedo puede hacer hago dime ayuda' +
    ' the of to an and or is are be this that you your it do does how why what where from with about into than then there these those will can should would')
    .split(' '),
)

/** Sin acentos ni mayúsculas, solo letras y números. */
export const norm = (s: string): string =>
  String(s ?? '')
    .toLowerCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9ñ ]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()

/** Raíz común entre singular y plural (se aplica igual al índice y a la pregunta, así que las dos formas se encuentran). */
const raiz = (w: string): string => {
  let r = w
  if (w.length > 4 && w.endsWith('es')) r = w.slice(0, -2)
  else if (w.length > 3 && w.endsWith('s')) r = w.slice(0, -1)
  // las formas del verbo y del sustantivo comparten el arranque («registro», «registrar», «registrada»; «factura», «facturación»): se compara por los primeros 7 caracteres...
  if (r.length > 7) r = r.slice(0, 7)
  // ...y sin la vocal final, para que «cliente» y «clientes», «entrega» y «entregó» caigan en la misma clave
  if (r.length > 4 && /[aeo]$/.test(r)) r = r.slice(0, -1)
  return r
}

export const palabras = (s: string): string[] =>
  norm(s)
    .split(' ')
    .filter((w) => w.length > 2 && !VACIAS.has(w))
    .map(raiz)

const BLOCKED = /(no me deja|no me permite|no puedo|no se puede|me dice|me sale|dice que|error|falla|bloque|por que no|porque no|rechaz|invalid|no aparece|no encuentro|que significa)/
const HOW = /(^| )(como|de que forma|donde (se|esta|puedo)|para que sirve|pasos|se hace|se crea|se registra|se configura|que es)( |$)/

/** La forma de la pregunta: un fallo («no me deja…», «qué significa…») es de la FAQ y un procedimiento («cómo se hace…») es del manual. */
export function intentOf(question: string): HelpIntent {
  const p = norm(question)
  const blocked = BLOCKED.test(p)
  const how = HOW.test(p)
  if (blocked && !how) return 'blocked'
  if (how && !blocked) return 'how'
  return 'any'
}

const WEIGHT: Record<HelpIntent, { f: number; m: number }> = {
  blocked: { f: 1, m: 0.6 },
  how: { f: 0.6, m: 1 },
  any: { f: 0.9, m: 0.9 },
}

/** Por debajo de esto Kemi no contesta con lo que encontró: «no lo sé» es mejor que algo que se parece. */
export const FLOOR = 0.18

interface Prepared {
  entry: HelpEntry
  body: Set<string>
  title: Set<string>
  /** Las palabras del capítulo o tema: los apartados genéricos («Qué hace») se encuentran por el nombre de su capítulo. */
  ctx: Set<string>
}

export interface HelpIndex {
  size: number
  search(question: string, limit?: number, skip?: ReadonlySet<HelpEntry>): { intent: HelpIntent; hits: HelpHit[] }
}

/** Arma el índice una vez (idf incluido); cada pregunta después solo puntúa. */
export function buildIndex(entries: readonly HelpEntry[]): HelpIndex {
  const prepared: Prepared[] = entries.map((entry) => ({ entry, body: new Set(palabras(`${entry.t} ${entry.c} ${entry.x}`)), title: new Set(palabras(entry.t)), ctx: new Set(palabras(entry.c)) }))
  const df = new Map<string, number>()
  for (const p of prepared) for (const w of p.body) df.set(w, (df.get(w) ?? 0) + 1)
  const n = prepared.length || 1
  const idf = new Map<string, number>()
  for (const [w, c] of df) idf.set(w, Math.log(1 + n / (1 + c)))

  function score(p: Prepared, query: Set<string>): number {
    let body = 0
    let title = 0
    let ctx = 0
    let total = 0
    for (const w of query) {
      const weight = idf.get(w) ?? 1
      total += weight
      if (p.body.has(w)) body += weight
      if (p.title.has(w)) title += weight
      else if (p.ctx.has(w)) ctx += weight
    }
    if (!body && !title) return 0
    return (body + title * 1.6 + ctx * 0.8) / ((total || 1) * 2.6)
  }

  return {
    size: prepared.length,
    search(question, limit = 5, skip) {
      const query = new Set(palabras(question))
      const intent = intentOf(question)
      if (query.size === 0) return { intent, hits: [] }
      const w = WEIGHT[intent]
      const scored: HelpHit[] = []
      for (const p of prepared) {
        if (skip?.has(p.entry)) continue
        let s = score(p, query)
        if (!s) continue
        s *= w[p.entry.k]
        // el título ES la pregunta: si coincide palabra por palabra, gana
        if (p.title.size === query.size && [...query].every((q) => p.title.has(q))) s *= 1.5
        if (s >= FLOOR) scored.push({ entry: p.entry, score: Number(s.toFixed(3)) })
      }
      scored.sort((a, b) => b.score - a.score)
      return { intent, hits: withBothSources(scored, limit) }
    },
  }
}

/** Si los primeros salen todos de una misma fuente, cede el último puesto a la otra (si la hay por encima del suelo): «documentos» a secas es explorar, y eso lo cuenta el manual. */
function withBothSources(scored: HelpHit[], limit: number): HelpHit[] {
  const cut = scored.slice(0, limit)
  if (cut.length < limit) return cut
  if (new Set(cut.map((h) => h.entry.k)).size > 1) return cut
  const other = scored.find((h) => h.entry.k !== cut[0].entry.k)
  return other ? [...cut.slice(0, limit - 1), other] : cut
}
