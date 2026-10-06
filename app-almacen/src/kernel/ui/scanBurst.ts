// Lectura que llega como TECLAS (pedido del dueño 2026-10-06: «si le doy al gatillo, que le dé Aceptar»). Si el lector del Zebra no entrega por
// intent (perfil de DataWedge que no se aplicó, otro perfil con la salida por teclas, etc.), el código aparece de golpe en el campo y nadie toca
// Aceptar. Una persona no escribe 3+ caracteres de golpe ni a más de ~15 por segundo; un lector sí. Lógica pura (ScanField la usa).

/** Entre un carácter y el siguiente de una ráfaga del lector (una persona tarda mucho más). */
export const BURST_GAP_MS = 60
/** Silencio tras la ráfaga para darla por terminada y aceptar. */
export const BURST_QUIET_MS = 150
/** Mínimo de caracteres para considerar que es una lectura y no alguien tecleando. */
export const BURST_MIN_CHARS = 3

export interface BurstState {
  /** Caracteres acumulados en la ráfaga en curso. */
  chars: number
  /** Hora del último cambio que agregó texto (null = ninguno). */
  lastAt: number | null
}

export const NO_BURST: BurstState = { chars: 0, lastAt: null }

export interface BurstStep {
  state: BurstState
  /** true = es una lectura del lector: aceptar cuando pase BURST_QUIET_MS sin más cambios. */
  scanning: boolean
}

/** Un cambio del texto del campo (`prev` → `next`, a la hora `now`): avanza la ráfaga y dice si ya parece una lectura. */
export function stepBurst(state: BurstState, prev: string, next: string, now: number): BurstStep {
  const added = next.length - prev.length
  if (added <= 0) return { state: NO_BURST, scanning: false } // borrar o reemplazar no es una lectura
  const continues = state.lastAt !== null && now - state.lastAt <= BURST_GAP_MS
  const chars = added >= BURST_MIN_CHARS ? next.length : continues ? state.chars + added : added
  return { state: { chars, lastAt: now }, scanning: chars >= BURST_MIN_CHARS }
}
