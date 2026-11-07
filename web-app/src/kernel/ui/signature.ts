// Lote 223 — firma dibujada (pedido guiado; la puede usar cualquier pantalla que pida una firma): trazos en un lienzo lógico fijo de SIGNATURE_WIDTH ×
// SIGNATURE_HEIGHT unidades (el cuadro en pantalla se estira a su ancho sin cambiar la proporción), su dibujo SVG y su exportación a PNG con tinta negra sobre
// blanco (lo que acepta el servidor: PNG o JPG, y lo que se imprime). Lógica pura salvo `signatureToPng`, que usa un <canvas> del navegador.

export interface SignaturePoint {
  x: number
  y: number
}
export type SignatureStroke = SignaturePoint[]

export const SIGNATURE_WIDTH = 600
export const SIGNATURE_HEIGHT = 240
/** Trazo mínimo para que cuente como firma (en unidades del lienzo): un toque suelto no es una firma. */
export const SIGNATURE_MIN_INK = 12

/** Punto de la pantalla → punto del lienzo lógico (acotado a sus bordes). Un cuadro sin medir (0 × 0, p. ej. jsdom) usa la posición tal cual. */
export function toSignaturePoint(clientX: number, clientY: number, rect: { left: number; top: number; width: number; height: number }): SignaturePoint {
  const sx = rect.width > 0 ? SIGNATURE_WIDTH / rect.width : 1
  const sy = rect.height > 0 ? SIGNATURE_HEIGHT / rect.height : 1
  const clamp = (v: number, max: number) => Math.min(max, Math.max(0, Math.round(v * 10) / 10))
  return { x: clamp((clientX - rect.left) * sx, SIGNATURE_WIDTH), y: clamp((clientY - rect.top) * sy, SIGNATURE_HEIGHT) }
}

/** Atributo `d` de un trazo (un punto solo = un punto visible). */
export function strokePath(stroke: SignatureStroke): string {
  if (stroke.length === 0) return ''
  const [first, ...rest] = stroke
  if (rest.length === 0) return `M${first.x} ${first.y} l0.1 0`
  return `M${first.x} ${first.y}` + rest.map((p) => ` L${p.x} ${p.y}`).join('')
}

/** Largo total de la tinta (suma de los segmentos). */
export function inkLength(strokes: readonly SignatureStroke[]): number {
  let total = 0
  for (const s of strokes) for (let i = 1; i < s.length; i++) total += Math.hypot(s[i].x - s[i - 1].x, s[i].y - s[i - 1].y)
  return total
}

/** ¿Hay firma? Al menos `SIGNATURE_MIN_INK` unidades de tinta. */
export function hasInk(strokes: readonly SignatureStroke[]): boolean {
  return inkLength(strokes) >= SIGNATURE_MIN_INK
}

/** La firma como SVG independiente (tinta negra sobre blanco), del tamaño del lienzo lógico. */
export function signatureSvg(strokes: readonly SignatureStroke[], ink = '#000000', paper = '#FFFFFF'): string {
  const paths = strokes
    .filter((s) => s.length > 0)
    .map((s) => `<path d="${strokePath(s)}" fill="none" stroke="${ink}" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>`)
    .join('')
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${SIGNATURE_WIDTH}" height="${SIGNATURE_HEIGHT}" viewBox="0 0 ${SIGNATURE_WIDTH} ${SIGNATURE_HEIGHT}"><rect width="100%" height="100%" fill="${paper}"/>${paths}</svg>`
}

/** PNG de la firma (tinta negra sobre blanco) para subirla al servidor. Rechaza si el navegador no puede dibujar en un lienzo. */
export function signatureToPng(strokes: readonly SignatureStroke[]): Promise<Blob> {
  return new Promise((resolve, reject) => {
    const canvas = document.createElement('canvas')
    canvas.width = SIGNATURE_WIDTH
    canvas.height = SIGNATURE_HEIGHT
    const ctx = canvas.getContext('2d')
    if (!ctx) return reject(new Error('canvas'))
    ctx.fillStyle = '#FFFFFF'
    ctx.fillRect(0, 0, SIGNATURE_WIDTH, SIGNATURE_HEIGHT)
    ctx.strokeStyle = '#000000'
    ctx.lineWidth = 3
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'
    for (const s of strokes) {
      if (s.length === 0) continue
      ctx.beginPath()
      ctx.moveTo(s[0].x, s[0].y)
      if (s.length === 1) ctx.lineTo(s[0].x + 0.1, s[0].y)
      for (const p of s.slice(1)) ctx.lineTo(p.x, p.y)
      ctx.stroke()
    }
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('png'))), 'image/png')
  })
}
