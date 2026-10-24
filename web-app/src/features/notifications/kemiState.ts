// Dónde se guarda si Kemi está dormida y si ya saludó (Lote 52). Las reglas de cuándo avisa viven en shared/kemi/rules.ts (las comparte el portal del cliente).
export { BUBBLE_SECONDS, bubbleKey, decideBubble } from '../../../../shared/kemi/rules'
export type { BubbleDecision } from '../../../../shared/kemi/rules'

const ASLEEP_KEY = 'teikem.kemi.asleep'
const GREETED_KEY = 'teikem.kemi.greeted'

/** Si Kemi está dormida en este navegador (por usuario). El almacenamiento puede no existir o fallar: entonces está despierta. */
export function readAsleep(userId: number | string | null | undefined): boolean {
  try {
    return globalThis.localStorage?.getItem(`${ASLEEP_KEY}.${userId ?? ''}`) === '1'
  } catch {
    return false
  }
}

export function writeAsleep(userId: number | string | null | undefined, asleep: boolean): void {
  try {
    const key = `${ASLEEP_KEY}.${userId ?? ''}`
    if (asleep) globalThis.localStorage?.setItem(key, '1')
    else globalThis.localStorage?.removeItem(key)
  } catch {
    /* sin almacenamiento: vale solo para esta pantalla abierta */
  }
}

export function readGreeted(): boolean {
  try {
    return globalThis.sessionStorage?.getItem(GREETED_KEY) === '1'
  } catch {
    return false
  }
}

export function writeGreeted(): void {
  try {
    globalThis.sessionStorage?.setItem(GREETED_KEY, '1')
  } catch {
    /* ignorar */
  }
}
