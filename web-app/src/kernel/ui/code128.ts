// Lote F14 — codificador Code 128 (ISO/IEC 15417) puro, sin dependencias. Lo usa el reporte de códigos de barras
// (`barcodeReportPdf.ts`) para dibujar las barras como rectángulos vectoriales en el PDF.
// - Subconjunto B para ASCII imprimible (32–126) y subconjunto C (dos dígitos por símbolo) para las corridas de dígitos
//   en las que ahorra ancho, con las reglas de longitud mínima del anexo E de la norma:
//   · inicio en C si el valor es exactamente dos dígitos o empieza con 4 o más dígitos (si la corrida es impar, el último
//     dígito de la corrida va en B tras "Code B");
//   · estando en B, cambio a C ("Code C") ante 6 o más dígitos seguidos, o 4 o más si llegan al final del valor; con una
//     corrida impar, el primer dígito va en B y el cambio se hace después.
// - Dígito verificador módulo 103 (valor de inicio + Σ valor × posición), patrón de parada de 13 módulos.
// - El subconjunto A no se usa: solo haría falta para caracteres de control (0–31), que un SKU o un código de posición no
//   tienen y que no se imprimen. Fuera de 32–126 (acentos, ñ, emojis…) el valor no se puede codificar: `code128Unsupported`
//   dice cuáles caracteres sobran y `encodeCode128` lanza un error.
// Validado contra implementaciones independientes (ver docs/frontend/loteF14-decisiones.md) y con vectores fijos en
// code128.test.ts.

/**
 * Anchos de barra/espacio de cada símbolo (valores 0–106), en módulos, empezando por barra. Cada símbolo suma 11 módulos
 * (3 barras y 3 espacios); el de parada (106) suma 13 (4 barras y 3 espacios, con la barra final de 2 módulos).
 */
export const CODE128_PATTERNS: readonly string[] = [
  '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312', '132212', '221213', // 0–9
  '221312', '231212', '112232', '122132', '122231', '113222', '123122', '123221', '223211', '221132', // 10–19
  '221231', '213212', '223112', '312131', '311222', '321122', '321221', '312212', '322112', '322211', // 20–29
  '212123', '212321', '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313', // 30–39
  '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121', '313121', '211331', // 40–49
  '231131', '213113', '213311', '213131', '311123', '311321', '331121', '312113', '312311', '332111', // 50–59
  '314111', '221411', '431111', '111224', '111422', '121124', '121421', '141122', '141221', '112214', // 60–69
  '112412', '122114', '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111', // 70–79
  '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112', '421211', '212141', // 80–89
  '214121', '412121', '111143', '111341', '131141', '114113', '114311', '411113', '411311', '113141', // 90–99
  '114131', '311141', '411131', '211412', '211214', '211232', '2331112', // 100–106
]

/** Valores especiales de la tabla. */
export const CODE128 = {
  /** En A o B: cambia a C. */
  CODE_C: 99,
  /** En A o C: cambia a B. */
  CODE_B: 100,
  START_A: 103,
  START_B: 104,
  START_C: 105,
  STOP: 106,
} as const

/** Ancho (módulos) de un símbolo de datos y del de parada. */
export const CODE128_SYMBOL_MODULES = 11
export const CODE128_STOP_MODULES = 13
/** Zona de silencio mínima a cada lado (la norma pide 10 módulos). */
export const CODE128_QUIET_ZONE = 10

/** Una barra del símbolo: posición y ancho en módulos desde el inicio del símbolo (sin la zona de silencio). */
export interface Code128Bar {
  x: number
  width: number
}

export interface Code128Symbol {
  /** Valor codificado (tal cual se lee al escanear). */
  text: string
  /** Valores de los símbolos en orden: inicio, datos (y cambios de subconjunto), verificador; sin la parada. */
  values: number[]
  /** Dígito verificador (módulo 103). */
  checksum: number
  /** Anchos alternados barra/espacio de todo el símbolo, parada incluida (empieza por barra). */
  modules: number[]
  /** Barras negras con su posición y ancho en módulos. */
  bars: Code128Bar[]
  /** Ancho total del símbolo en módulos (sin las zonas de silencio): 11 × (símbolos + verificador) + 13. */
  width: number
}

const isDigit = (c: number) => c >= 48 && c <= 57

/** Caracteres que Code 128 (subconjunto B) no admite: fuera de ASCII 32–126. Sin repetir, en el orden en que aparecen. */
export function code128Unsupported(value: string): string[] {
  const out: string[] = []
  for (const ch of value) {
    const c = ch.codePointAt(0) ?? 0
    if ((c < 32 || c > 126) && !out.includes(ch)) out.push(ch)
  }
  return out
}

/** true si el valor no está vacío y todos sus caracteres son ASCII imprimibles (32–126). */
export function isCode128Encodable(value: string): boolean {
  return value.length > 0 && code128Unsupported(value).length === 0
}

/** Largo de la corrida de dígitos que empieza en `i`. */
function digitRun(codes: readonly number[], i: number): number {
  let n = 0
  while (i + n < codes.length && isDigit(codes[i + n])) n++
  return n
}

/**
 * Valores de los símbolos (inicio + datos con los cambios de subconjunto), SIN verificador ni parada. Lanza si el valor
 * está vacío o tiene caracteres fuera de 32–126.
 */
export function code128Values(value: string): number[] {
  if (value.length === 0) throw new Error('Code 128: valor vacío.')
  const bad = code128Unsupported(value)
  if (bad.length > 0) throw new Error(`Code 128: caracteres no admitidos: ${bad.join(' ')}`)
  const codes = [...value].map((ch) => ch.charCodeAt(0))
  const out: number[] = []
  const firstRun = digitRun(codes, 0)
  let set: 'B' | 'C'
  if ((codes.length === 2 && firstRun === 2) || firstRun >= 4) {
    set = 'C'
    out.push(CODE128.START_C)
  } else {
    set = 'B'
    out.push(CODE128.START_B)
  }
  let i = 0
  while (i < codes.length) {
    if (set === 'C') {
      const run = digitRun(codes, i)
      if (run >= 2) {
        out.push((codes[i] - 48) * 10 + (codes[i + 1] - 48))
        i += 2
        continue
      }
      // un dígito suelto o un carácter que no es dígito: de vuelta a B
      out.push(CODE128.CODE_B)
      set = 'B'
      continue
    }
    const run = digitRun(codes, i)
    const atEnd = i + run === codes.length
    if (run >= 6 || (run >= 4 && atEnd)) {
      // corrida impar: el primer dígito va en B y el resto (par) en C
      if (run % 2 === 1) {
        out.push(codes[i] - 32)
        i++
      }
      out.push(CODE128.CODE_C)
      set = 'C'
      continue
    }
    out.push(codes[i] - 32)
    i++
  }
  return out
}

/** Dígito verificador: (valor de inicio + Σ valor × posición) módulo 103; `values` empieza por el de inicio. */
export function code128Checksum(values: readonly number[]): number {
  let sum = values[0] ?? 0
  for (let i = 1; i < values.length; i++) sum += values[i] * i
  return sum % 103
}

/** Codifica el valor completo: valores, verificador, anchos de módulos (con la parada) y barras. */
export function encodeCode128(value: string): Code128Symbol {
  const data = code128Values(value)
  const checksum = code128Checksum(data)
  const values = [...data, checksum]
  const modules: number[] = []
  for (const v of [...values, CODE128.STOP]) for (const w of CODE128_PATTERNS[v]) modules.push(Number(w))
  const bars: Code128Bar[] = []
  let x = 0
  modules.forEach((w, i) => {
    if (i % 2 === 0) bars.push({ x, width: w })
    x += w
  })
  return { text: value, values, checksum, modules, bars, width: x }
}

/** Ancho en módulos de un símbolo con `count` valores (inicio, datos y verificador) más la parada. */
export function code128WidthFor(count: number): number {
  return count * CODE128_SYMBOL_MODULES + CODE128_STOP_MODULES
}
