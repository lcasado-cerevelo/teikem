// Reglas de «Kemi con vida» (Lote 52): las puras viven en shared/kemi/rules.ts (las comparte el portal del cliente); aquí queda lo que guarda esta web (dónde dejó a Kemi, en localStorage).
import { DEFAULT_POS, isMoved, type KemiPos } from '../../../../shared/kemi/rules'
export { MICRO_MOODS, BIG_MOODS, BLINK_EVERY, MICRO_EVERY, BIG_EVERY, BLINK_MS, MICRO_MS, BIG_MS, between, pickMood, DEFAULT_POS, DRAG_THRESHOLD, clampPos, quadrant, anchorStyle, isMoved, pupilOffset } from '../../../../shared/kemi/rules'
export type { KemiMood, KemiPos } from '../../../../shared/kemi/rules'

const POS_KEY = 'teikem.kemi.pos'

/** La posición guardada de este usuario en este navegador; sin almacenamiento o con datos raros, la esquina de siempre. */
export function readPos(userId: number | string | null | undefined): KemiPos {
  try {
    const raw = globalThis.localStorage?.getItem(`${POS_KEY}.${userId ?? ''}`)
    if (!raw) return DEFAULT_POS
    const v = JSON.parse(raw) as Partial<KemiPos>
    if (typeof v.right === 'number' && typeof v.bottom === 'number' && Number.isFinite(v.right) && Number.isFinite(v.bottom)) return { right: v.right, bottom: v.bottom }
  } catch {
    /* datos dañados: la esquina de siempre */
  }
  return DEFAULT_POS
}

export function writePos(userId: number | string | null | undefined, pos: KemiPos): void {
  try {
    const key = `${POS_KEY}.${userId ?? ''}`
    if (isMoved(pos)) globalThis.localStorage?.setItem(key, JSON.stringify(pos))
    else globalThis.localStorage?.removeItem(key)
  } catch {
    /* sin almacenamiento: vale solo para esta pantalla abierta */
  }
}
