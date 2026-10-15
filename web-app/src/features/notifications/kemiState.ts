// Reglas puras de cuándo Kemi avisa y dónde se guarda si está dormida (Lote 52). Sin React: se prueban solas.

/** Qué hacer con el contador de no leídos que acaba de llegar. */
export interface BubbleDecision {
  /** Cantidad con la que se muestra el globo (null = sin globo). */
  bubble: number | null
  /** Nuevo «último visto» para la próxima comparación. */
  seen: number
}

/**
 * - Primera lectura de la sesión: con avisos sin leer, UNA vez por sesión de navegador (`greeted` = ya saludó), Kemi avisa de todos.
 * - Después: solo cuando el contador SUBE (llegó algo nuevo); lo nuevo es la diferencia.
 * - Si baja (se leyeron) no hay globo y se anota el nuevo valor.
 * - Dormida nunca muestra globo (la campanita sigue con su contador).
 */
export function decideBubble(previous: number | null, count: number, greeted: boolean, asleep: boolean): BubbleDecision {
  if (previous === null) return { bubble: !asleep && !greeted && count > 0 ? count : null, seen: count }
  if (count > previous) return { bubble: asleep ? null : count - previous, seen: count }
  return { bubble: null, seen: count }
}

export function bubbleKey(n: number): 'one' | 'many' {
  return n === 1 ? 'one' : 'many'
}

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

/** Segundos que dura el globo de Kemi antes de esconderse solo. */
export const BUBBLE_SECONDS = 15
