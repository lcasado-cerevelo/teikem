// Fotos del chat: se reducen en el navegador antes de subirse (lado mayor 1280 px, JPEG al 80 %) para que pesen unos cientos de KB y no llenen el servidor.
// La parte pura (tamaño y reglas) se prueba sola; la que usa el lienzo del navegador queda fuera de las pruebas.

export const MAX_PHOTOS = 4
export const MAX_SIDE = 1280
export const JPEG_QUALITY = 0.8
export const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp']

/** El tamaño que queda al reducir para que el lado mayor no pase de `max` (nunca agranda). */
export function fitSize(width: number, height: number, max = MAX_SIDE): { width: number; height: number } {
  const side = Math.max(width, height)
  if (side <= max || side === 0) return { width, height }
  const scale = max / side
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) }
}

/** Cuántas fotos más caben en el mensaje. */
export function roomLeft(current: number, max = MAX_PHOTOS): number {
  return Math.max(0, max - current)
}

export function isAcceptedImage(file: { type: string }): boolean {
  return ACCEPTED_TYPES.includes(file.type.toLowerCase())
}

export interface ChatPhoto {
  fileName: string
  dataBase64: string
  /** Para la vista previa antes de enviar (data URL). */
  previewUrl: string
}

/** Reduce una foto y la deja lista para enviar. Lanza si el navegador no puede leerla. */
export async function shrinkImage(file: File): Promise<ChatPhoto> {
  const bitmap = await createImageBitmap(file)
  const { width, height } = fitSize(bitmap.width, bitmap.height)
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('canvas')
  ctx.drawImage(bitmap, 0, 0, width, height)
  bitmap.close?.()
  const dataUrl = canvas.toDataURL('image/jpeg', JPEG_QUALITY)
  const base = file.name.replace(/\.[^.]+$/, '') || 'foto'
  return { fileName: `${base}.jpg`, dataBase64: dataUrl.slice(dataUrl.indexOf(',') + 1), previewUrl: dataUrl }
}

/** Convierte un Blob en un data URL (para pintar la foto bajada con la sesión sin dejar URLs de objeto sueltas). */
export async function blobToDataUrl(blob: Blob): Promise<string> {
  const bytes = new Uint8Array(await blob.arrayBuffer())
  let binary = ''
  for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return `data:${blob.type || 'image/jpeg'};base64,${btoa(binary)}`
}
