// Lógica pura de `SplitPane` (dos paneles con barra arrastrable): límites de la proporción y lectura de lo guardado.
// (Se llama splitRatio.ts y no splitPane.ts: en Windows './SplitPane' resolvería a 'splitPane.ts' por no distinguir mayúsculas.)

/** Proporción por defecto del panel A (60/40). */
export const SPLIT_DEFAULT_RATIO = 0.6
export const SPLIT_MIN_RATIO = 0.35
export const SPLIT_MAX_RATIO = 0.75
/** Ancho mínimo en px de cada panel (A, B). */
export const SPLIT_MIN_PX: readonly [number, number] = [420, 320]
/** Bajo este ancho de ventana, una sola columna sin barra. */
export const SPLIT_STACK_BELOW = 900
/** Ancho de la barra arrastrable (columna central del grid; igual que ui.css). */
export const SPLIT_BAR_PX = 10
/** Paso del teclado (← / →). */
export const SPLIT_KEY_STEP = 0.05
/** Prefijo de la clave en localStorage: `teikem.split.<storageKey>`. */
export const SPLIT_STORAGE_PREFIX = 'teikem.split.'

export function splitStorageKey(storageKey: string): string {
  return `${SPLIT_STORAGE_PREFIX}${storageKey}`
}

/**
 * Límites efectivos `[lo, hi]` de la proporción del panel A: `[min, max]` estrechados para que cada panel conserve su
 * ancho mínimo (`minPx`) dentro de `width` (ancho útil sin la barra). Sin ancho medido (≤ 0) solo cuentan `min`/`max`.
 * Si el ancho no alcanza para los dos mínimos, los dos límites valen el reparto proporcional a los mínimos (acotado a
 * `[min, max]`): la barra queda fija.
 */
export function splitBounds(
  width: number,
  minPx: readonly [number, number] = SPLIT_MIN_PX,
  min = SPLIT_MIN_RATIO,
  max = SPLIT_MAX_RATIO,
): [number, number] {
  const lo0 = Math.min(min, max)
  const hi0 = Math.max(min, max)
  if (!(width > 0)) return [lo0, hi0]
  const lo = Math.max(lo0, minPx[0] / width)
  const hi = Math.min(hi0, 1 - minPx[1] / width)
  if (lo <= hi) return [lo, hi]
  const fixed = Math.min(hi0, Math.max(lo0, minPx[0] / (minPx[0] + minPx[1])))
  return [fixed, fixed]
}

/** Proporción del panel A acotada a `splitBounds` (un valor no numérico vale el límite inferior). */
export function clampSplitRatio(
  ratio: number,
  width: number,
  minPx: readonly [number, number] = SPLIT_MIN_PX,
  min = SPLIT_MIN_RATIO,
  max = SPLIT_MAX_RATIO,
): number {
  const [lo, hi] = splitBounds(width, minPx, min, max)
  if (!Number.isFinite(ratio)) return lo
  return Math.min(hi, Math.max(lo, ratio))
}

/** Lee lo guardado en localStorage: un número entre 0 y 1 (exclusivos); cualquier otra cosa (vacío, texto, fuera de
 *  rango) → `def`. El componente lo acota después con `clampSplitRatio`. */
export function readSplitRatio(raw: string | null | undefined, def: number = SPLIT_DEFAULT_RATIO): number {
  if (raw == null || raw.trim() === '') return def
  const n = Number(raw)
  return Number.isFinite(n) && n > 0 && n < 1 ? n : def
}

/** Proporción a partir de la posición del puntero: `x` relativo al borde izquierdo del contenedor de ancho `total`
 *  (incluida la barra); el centro de la barra queda bajo el puntero. */
export function ratioFromPointer(x: number, total: number, bar = SPLIT_BAR_PX): number {
  const useful = total - bar
  if (!(useful > 0)) return SPLIT_DEFAULT_RATIO
  return (x - bar / 2) / useful
}
