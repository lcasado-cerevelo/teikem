// Reglas puras de Kemi, compartidas por la web del personal y el portal del cliente (Lote 218): sin React ni almacenamiento, se prueban solas.
// Cada app guarda por su cuenta lo que recuerda (posición, dormida, saludó); aquí solo se decide.

// Reglas puras de «Kemi con vida» (sin React, se prueban solas): cuándo parpadea, qué gestos hace y dónde se coloca al arrastrarla.
// Todo lo que se mueve es CSS (transform y opacity); aquí solo se decide cuándo y qué. Ver useKemiLife.ts y notifications.css.

/** Gestos de Kemi (atributo `data-mood` del dibujo). `calm` es la cara de reposo. */
export type KemiMood = 'calm' | 'look-l' | 'look-r' | 'wink' | 'happy' | 'surprised' | 'tongue' | 'worried' | 'alert'

/** Gestos cortos (de 1 a 2 segundos) que se hacen a cada rato. */
export const MICRO_MOODS: readonly KemiMood[] = ['look-l', 'look-r', 'wink', 'happy']
/** Muecas más grandes y raras. */
export const BIG_MOODS: readonly KemiMood[] = ['tongue', 'surprised', 'worried', 'happy']

/** Rangos de espera en milisegundos: siempre al azar, nunca un intervalo fijo (un reloj se nota). */
export const BLINK_EVERY: readonly [number, number] = [2500, 6000]
export const MICRO_EVERY: readonly [number, number] = [8000, 25000]
export const BIG_EVERY: readonly [number, number] = [60000, 180000]
export const BLINK_MS = 140
export const MICRO_MS: readonly [number, number] = [900, 1800]
export const BIG_MS: readonly [number, number] = [1500, 2500]

/** Número al azar entre min y max (rng devuelve [0, 1)). */
export function between(rng: () => number, [min, max]: readonly [number, number]): number {
  return Math.round(min + rng() * (max - min))
}

/** Elige un gesto de la lista que no sea el último que hizo (si hay más de uno). */
export function pickMood(rng: () => number, list: readonly KemiMood[], last: KemiMood | null): KemiMood {
  const options = list.length > 1 ? list.filter((m) => m !== last) : list
  return options[Math.min(options.length - 1, Math.floor(rng() * options.length))]
}

// ---------------------------------------------------------------- posición al arrastrarla

/** Distancia (en píxeles) de Kemi a los bordes derecho e inferior de la ventana, que es como se coloca. */
export interface KemiPos {
  right: number
  bottom: number
}

export const DEFAULT_POS: KemiPos = { right: 24, bottom: 24 }
/** Lo que se mueve el puntero antes de contar como arrastre (menos que esto es un clic). */
export const DRAG_THRESHOLD = 5
const MARGIN = 8

/** Mantiene a Kemi dentro de la ventana (con su tamaño y un margen). */
export function clampPos(pos: KemiPos, viewW: number, viewH: number, size = 58): KemiPos {
  const maxRight = Math.max(MARGIN, viewW - size - MARGIN)
  const maxBottom = Math.max(MARGIN, viewH - size - MARGIN)
  return {
    right: Math.min(maxRight, Math.max(MARGIN, Math.round(pos.right))),
    bottom: Math.min(maxBottom, Math.max(MARGIN, Math.round(pos.bottom))),
  }
}

/**
 * De qué lado de la ventana está Kemi, para abrir su globo y su panel hacia donde hay lugar: en la mitad de arriba se abren hacia abajo, en la de la izquierda se alinean a la izquierda.
 */
export function quadrant(pos: KemiPos, viewW: number, viewH: number, size = 58): { v: 'top' | 'bottom'; h: 'left' | 'right' } {
  const centerX = viewW - pos.right - size / 2
  const centerY = viewH - pos.bottom - size / 2
  return { v: centerY < viewH / 2 ? 'top' : 'bottom', h: centerX < viewW / 2 ? 'left' : 'right' }
}

/**
 * Estilo con el que se coloca el contenedor de Kemi: se ancla por el borde más cercano (arriba o abajo, izquierda o derecha) para que, al abrirse el globo o el panel hacia el lado
 * que tiene lugar, ella se quede donde está y no se corra.
 */
export function anchorStyle(pos: KemiPos, viewW: number, viewH: number, size = 58): { top: number | 'auto'; bottom: number | 'auto'; left: number | 'auto'; right: number | 'auto' } {
  const side = quadrant(pos, viewW, viewH, size)
  return {
    // el lado contrario va en «auto»: la hoja de estilos le da a Kemi bottom y right por omisión y, con los dos puestos, el contenedor se estiraría
    top: side.v === 'top' ? viewH - pos.bottom - size : 'auto',
    bottom: side.v === 'top' ? 'auto' : pos.bottom,
    left: side.h === 'left' ? viewW - pos.right - size : 'auto',
    right: side.h === 'left' ? 'auto' : pos.right,
  }
}

/** Si Kemi no está en su esquina de siempre. */
export function isMoved(pos: KemiPos): boolean {
  return pos.right !== DEFAULT_POS.right || pos.bottom !== DEFAULT_POS.bottom
}

/** Hacia dónde miran las pupilas (en unidades del dibujo, hasta 4) cuando el puntero está en (dx, dy) respecto al centro de Kemi. */
export function pupilOffset(dx: number, dy: number, max = 4): { x: number; y: number } {
  const len = Math.hypot(dx, dy)
  if (len === 0) return { x: 0, y: 0 }
  const k = Math.min(1, len / 40) * max / len
  return { x: Math.round(dx * k * 10) / 10, y: Math.round(dy * k * 10) / 10 }
}

// ---------------------------------------------------------------- cuándo avisa
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

/** Segundos que dura el globo de Kemi antes de esconderse solo. */
export const BUBBLE_SECONDS = 15

// ---------------------------------------------------------------- la cara, para quien la dibuja con código (el portal; la web lo hace con CSS)

export type KemiMouth = 'smile' | 'happy' | 'surprised' | 'tongue' | 'worried'

export interface KemiFace {
  eyeL: { sx: number; sy: number }
  eyeR: { sx: number; sy: number }
  /** Cuánto se corren las pupilas (unidades del dibujo). */
  pupil: { x: number; y: number }
  mouth: KemiMouth
}

/**
 * Qué cara pone Kemi según su gesto: lo mismo que dicen las reglas de CSS de la web (notifications.css), en forma de números.
 * `look` = hacia dónde mira cuando el puntero está encima (gana a los gestos de mirar). `excited` = llegó algo nuevo.
 */
export function faceOf(mood: KemiMood, blink: boolean, excited: boolean, look?: { x: number; y: number } | null): KemiFace {
  let sx = 1
  let sy = 1
  if (mood === 'happy') sy = 0.8
  if (mood === 'surprised' || excited) {
    sx = 1.12
    sy = 1.12
  }
  if (mood === 'alert') {
    sx = 1.1
    sy = 1.1
  }
  const eyeL = { sx, sy: blink ? 0.08 : sy }
  const eyeR = { sx, sy: blink || mood === 'wink' ? 0.08 : sy }
  let px = mood === 'look-l' ? -4 : mood === 'look-r' ? 4 : 0
  let py = mood === 'worried' ? -3 : 0
  if (look) {
    px = look.x
    py = look.y
  }
  const mouth: KemiMouth = excited || mood === 'happy' ? 'happy' : mood === 'surprised' ? 'surprised' : mood === 'tongue' ? 'tongue' : mood === 'worried' ? 'worried' : 'smile'
  return { eyeL, eyeR, pupil: { x: px, y: py }, mouth }
}
