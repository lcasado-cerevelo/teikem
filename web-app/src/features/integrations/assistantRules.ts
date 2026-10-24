// Asistente de lectura (2026-10-24): arma las expresiones de un lector a partir de un renglón de muestra que la persona señala, para que nadie tenga que escribir expresiones regulares.
// Todo es lógica pura: separa un renglón en celdas, propone cómo reconocer los renglones parecidos y cuenta cuántos del texto de muestra coinciden.

/** Un dato que el asistente puede asignar a una celda: la posición de la columna de la plantilla y su nombre en lenguaje llano. */
export interface Datum {
  position: number
  label: string
}

export interface CellAssignment {
  /** Número de celda del renglón (1…n) o 0 = sin usar. */
  cell: number
  /** Si hay una segunda celda: el dato = celda ÷ esta celda (importe ÷ precio unitario = cantidad). 0 = no se divide. */
  divideBy: number
}

export interface BlockProposal {
  lineRegex: string
  columns: string
  cells: string[]
}

/** Texto → renglones no vacíos (conserva los tabuladores: así salen las columnas de un PDF). */
export function sampleLines(text: string): string[] {
  return text.split(/\r?\n/).map((l) => l.replace(/\s+$/, '')).filter((l) => l.trim() !== '')
}

export function usesTabs(line: string): boolean {
  return line.includes('\t')
}

/** Las celdas de un renglón: por tabulador si lo tiene; si no, por huecos de dos o más espacios. */
export function splitCells(line: string): string[] {
  const cells = usesTabs(line) ? line.split('\t') : line.trim().split(/\s{2,}/)
  return cells.map((c) => c.trim())
}

/** La forma que reconoce una celda: números enteros, importes con decimales, números sueltos o cualquier texto. */
function cellPattern(cell: string, tabbed: boolean): string {
  if (/^\d+$/.test(cell)) return '(\\d+)'
  if (/^[\d,]*\.\d+$/.test(cell)) return '([\\d,]*\\.\\d+)'
  if (/^[\d.,]+$/.test(cell)) return '([\\d.,]+)'
  return tabbed ? '([^\\t]*)' : '(.+?)'
}

/** Expresión que reconoce los renglones con la misma forma que el de muestra (mismo número de celdas y de qué tipo es cada una). */
export function lineRegexFor(sample: string): string {
  const tabbed = usesTabs(sample)
  const cells = splitCells(sample)
  const sep = tabbed ? '\\t' : '\\s{2,}'
  return `^\\s*${cells.map((c) => cellPattern(c, tabbed)).join(sep)}\\s*$`
}

/** «posición=celda» o «posición=celda/divisor» a partir de lo que la persona asignó (posición de la plantilla → celdas). */
export function columnsFor(assignments: Record<number, CellAssignment>): string {
  return Object.entries(assignments)
    .filter(([, a]) => a.cell > 0)
    .sort(([a], [b]) => Number(a) - Number(b))
    .map(([pos, a]) => (a.divideBy > 0 ? `${pos}=${a.cell}/${a.divideBy}` : `${pos}=${a.cell}`))
    .join(', ')
}

export function proposeBlock(sample: string, assignments: Record<number, CellAssignment>): BlockProposal {
  return { lineRegex: lineRegexFor(sample), columns: columnsFor(assignments), cells: splitCells(sample) }
}

/** Cuántos renglones del texto coinciden con la expresión (−1 si la expresión no es válida). */
export function countMatches(lines: string[], regex: string): number {
  try {
    const re = new RegExp(regex)
    return lines.filter((l) => re.test(l)).length
  } catch {
    return -1
  }
}

export function matchingLines(lines: string[], regex: string, max = 8): string[] {
  try {
    const re = new RegExp(regex)
    return lines.filter((l) => re.test(l)).slice(0, max)
  } catch {
    return []
  }
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * Expresión para encontrar un número que el documento declara («Total Amount USD 39,167.01»): el texto que lo antecede en su renglón (hasta tres palabras) y el número.
 * Devuelve null si el renglón no trae texto antes del número (no hay cómo reconocerlo).
 */
export function totalPatternFor(line: string, token: string): string | null {
  const at = line.lastIndexOf(token)
  if (at < 0 || token.trim() === '') return null
  const before = line.slice(0, at).replace(/[:\s\t]+$/, '')
  const words = before.split(/\s+/).filter(Boolean).slice(-3)
  if (words.length === 0) return null
  const number = /\.\d+$/.test(token) ? '([\\d,]+\\.\\d+)' : '([\\d,.]+)'
  return `${words.map(escapeRegex).join('\\s+')}\\s*:?\\s*${number}`
}

/** Dato suelto («Order #: 123»): expresión con el texto que antecede al valor señalado y lo que lo rodea hasta el fin del renglón. */
export function singlePatternFor(line: string, token: string): string | null {
  const at = line.indexOf(token)
  if (at < 0 || token.trim() === '') return null
  const before = line.slice(0, at).replace(/[:\s\t]+$/, '')
  const words = before.split(/\s+/).filter(Boolean).slice(-3)
  const body = /^\d+$/.test(token) ? '(\\d+)' : /^[\d.,]+$/.test(token) ? '([\\d.,]+)' : '(\\S+)'
  return words.length === 0 ? `^\\s*${body}` : `${words.map(escapeRegex).join('\\s+')}\\s*:?\\s*${body}`
}

/** «2=2, 3=4/3» → [{position:2, cell:2, divideBy:0}, …] para mostrarlo en palabras. */
export function parseColumnsText(text: string): { position: number; cell: number; divideBy: number }[] {
  const out: { position: number; cell: number; divideBy: number }[] = []
  for (const p of text.split(/[,;]/)) {
    const m = /^\s*(\d+)\s*=\s*(\d+)(?:\s*\/\s*(\d+))?\s*$/.exec(p)
    if (m) out.push({ position: Number(m[1]), cell: Number(m[2]), divideBy: m[3] ? Number(m[3]) : 0 })
  }
  return out
}

const NOTICE_DATUMS: Record<string, number[]> = { ARRIVAL_NOTICE: [1, 2, 3, 4, 5, 6], EXIT_NOTICE: [1, 2, 3, 4, 5], ORDER_COMPLETION: [1] }

/** Los datos que espera el destino, con su nombre; null = columnas libres de la plantilla. */
export function datumsOf(target: string, t: (k: string, p?: Record<string, string | number>) => string): Datum[] | null {
  const list = NOTICE_DATUMS[target]
  return list ? list.map((position) => ({ position, label: t(`integrations.assistant.datum.${target}.${position}`) })) : null
}
