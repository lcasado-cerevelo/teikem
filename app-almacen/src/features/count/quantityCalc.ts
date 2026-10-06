// Calculadora de cantidad (pedido del dueño 2026-10-05): para contar estibas sin sumar de cabeza. Cada BLOQUE es filas × columnas (enteros) y se
// suma lo SUELTO (admite decimales, como la cantidad normal): «5 filas de 3 columnas y 10 sueltas» = (5 × 3) + 10 = 25. Solo el total sale de aquí: lo
// que se guarda y se manda es la cantidad (no se guarda la fórmula). Lógica pura, sin React.

export interface CalcBlock {
  rows: string
  cols: string
}

export interface CalcState {
  blocks: CalcBlock[]
  extra: string
}

/** Tope de la cantidad calculada (misma idea que el máximo del servidor: nada absurdo por un dedo de más). */
export const CALC_MAX = 9_999_999

export const EMPTY_BLOCK: CalcBlock = { rows: '', cols: '' }

export function emptyCalc(): CalcState {
  return { blocks: [{ ...EMPTY_BLOCK }], extra: '' }
}

/** Arranca la calculadora con lo que ya estaba escrito en la cantidad (queda como «sueltas»), para no perderlo al cambiar de modo. */
export function calcFromText(text: string): CalcState {
  const t = text.trim()
  return { blocks: [{ ...EMPTY_BLOCK }], extra: t }
}

function parseInteger(text: string): number | null {
  const t = text.trim()
  if (t === '') return null
  return /^\d+$/.test(t) ? Number(t) : Number.NaN
}

function parseExtra(text: string): number | null {
  const t = text.trim()
  if (t === '') return null
  const n = Number(t.replace(',', '.'))
  return Number.isFinite(n) && n >= 0 ? n : Number.NaN
}

export type CalcIssue = 'incompleteBlock' | 'invalid' | 'tooLarge' | 'empty'

export interface CalcResult {
  /** Total; null mientras no se pueda calcular (algo incompleto, inválido o todo en blanco). */
  total: number | null
  /** «(5 × 3) + (4 × 3) + 10»; vacío si no hay nada que mostrar. */
  expression: string
  issue: CalcIssue | null
}

/**
 * Suma los bloques con filas y columnas escritas y lo suelto. Un bloque con solo una de las dos medidas es un error (`incompleteBlock`); un bloque en
 * blanco se ignora; algo que no es un número, `invalid`; todo en blanco, `empty` (sin total todavía, no es error).
 */
export function calcTotal(state: CalcState): CalcResult {
  const parts: string[] = []
  let total = 0
  let any = false
  for (const b of state.blocks) {
    const r = parseInteger(b.rows)
    const c = parseInteger(b.cols)
    if (r === null && c === null) continue
    if (Number.isNaN(r) || Number.isNaN(c)) return { total: null, expression: parts.join(' + '), issue: 'invalid' }
    if (r === null || c === null) return { total: null, expression: parts.join(' + '), issue: 'incompleteBlock' }
    parts.push(`(${r} × ${c})`)
    total += (r as number) * (c as number)
    any = true
  }
  const extra = parseExtra(state.extra)
  if (Number.isNaN(extra)) return { total: null, expression: parts.join(' + '), issue: 'invalid' }
  if (extra !== null) {
    parts.push(formatPart(extra))
    total += extra
    any = true
  }
  if (!any) return { total: null, expression: '', issue: 'empty' }
  if (total > CALC_MAX) return { total: null, expression: parts.join(' + '), issue: 'tooLarge' }
  return { total: roundQty(total), expression: parts.join(' + '), issue: null }
}

/** La cantidad como texto del campo (sin ceros de más ni notación científica; hasta 3 decimales como el servidor). */
export function totalToText(total: number | null): string {
  return total === null ? '' : String(roundQty(total))
}

function roundQty(n: number): number {
  return Math.round(n * 1000) / 1000
}

function formatPart(n: number): string {
  return String(roundQty(n))
}

export function addBlock(state: CalcState): CalcState {
  return { ...state, blocks: [...state.blocks, { ...EMPTY_BLOCK }] }
}

export function removeBlock(state: CalcState, index: number): CalcState {
  if (state.blocks.length <= 1) return { ...state, blocks: [{ ...EMPTY_BLOCK }] }
  return { ...state, blocks: state.blocks.filter((_, i) => i !== index) }
}

export function setBlock(state: CalcState, index: number, patch: Partial<CalcBlock>): CalcState {
  return { ...state, blocks: state.blocks.map((b, i) => (i === index ? { ...b, ...patch } : b)) }
}
