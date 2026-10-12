// Lote F-E2b — Reglas puras del plano de ruta esquemático (`RouteMap`): proyección al rectángulo del plano, separación de pines que caen
// uno encima de otro, escala y las claves de precisión y de estado. Sin React ni red: se prueban solas.
// No hay mapa real (sin teselas ni servidores externos): es un plano en SVG propio donde la posición relativa de los puntos es fiel, pero no hay calles.

export interface GeoPoint {
  lat: number
  lng: number
}

/** Precisión del pin: exacto (geocodificado), aproximado (centro del CP o del pueblo) o manual (puesto a mano). */
export type PinAccuracy = 'exact' | 'approximate' | 'manual'

/** Estado de la parada en el plano. */
export type PinState = 'pending' | 'current' | 'done' | 'failed'

/** Código de precisión del servidor (`GeocodeAccuracyCode`): MANUAL y EXACT; todo lo demás (centroide de CP o de pueblo, o sin dato) es aproximado. */
export function accuracyOf(code: string | null | undefined): PinAccuracy {
  const c = (code ?? '').toUpperCase()
  return c === 'MANUAL' ? 'manual' : c === 'EXACT' ? 'exact' : 'approximate'
}

/** Estatus de la parada del servidor (`RouteStopStatus`) → estado del pin. PENDING y lo desconocido son «pendiente». */
export function stateOf(statusCode: string | null | undefined): PinState {
  const c = (statusCode ?? '').toUpperCase()
  if (c === 'COMPLETED' || c === 'DELIVERED') return 'done'
  if (c === 'FAILED') return 'failed'
  if (c === 'ON_THE_WAY' || c === 'ARRIVED' || c === 'IN_PROGRESS') return 'current'
  return 'pending'
}

/** Un punto es dibujable si ambas coordenadas son números finitos dentro de los rangos geográficos. */
export function isGeoPoint(p: { lat?: number | null; lng?: number | null } | null | undefined): p is GeoPoint {
  return !!p && typeof p.lat === 'number' && typeof p.lng === 'number' && Number.isFinite(p.lat) && Number.isFinite(p.lng) && Math.abs(p.lat) <= 90 && Math.abs(p.lng) <= 180
}

export interface Projection {
  /** Punto geográfico → píxeles del plano. */
  project: (p: GeoPoint) => { x: number; y: number }
  /** Píxeles del plano → punto geográfico (inversa exacta de `project`). */
  unproject: (x: number, y: number) => GeoPoint
  /** Metros por píxel (a la latitud media): para la escala. */
  metersPerPx: number
}

/** Metros por grado de latitud. */
const METERS_PER_DEG = 111_195
/** Alcance mínimo del plano en grados (≈ 1.1 km): con un solo punto, o puntos casi iguales, no se amplía sin fin. */
const MIN_SPAN_DEG = 0.01

/**
 * Proyección equirectangular ajustada al rectángulo que contiene todos los puntos, con `margin` px de margen. Corrige la distorsión en longitud
 * con cos(latitud media) y conserva la proporción (un grado de latitud mide lo mismo que su equivalente en longitud). El contenido queda centrado.
 * Sin puntos → null (no hay con qué ajustar).
 */
export function fitProjection(points: readonly GeoPoint[], width: number, height: number, margin = 28): Projection | null {
  if (points.length === 0 || width <= 0 || height <= 0) return null
  const lats = points.map((p) => p.lat)
  const lngs = points.map((p) => p.lng)
  const latMin = Math.min(...lats)
  const latMax = Math.max(...lats)
  const lngMin = Math.min(...lngs)
  const lngMax = Math.max(...lngs)
  const lat0 = (latMin + latMax) / 2
  const kx = Math.max(Math.cos((lat0 * Math.PI) / 180), 0.01)
  const spanX = Math.max((lngMax - lngMin) * kx, MIN_SPAN_DEG)
  const spanY = Math.max(latMax - latMin, MIN_SPAN_DEG)
  const availW = Math.max(width - 2 * margin, 1)
  const availH = Math.max(height - 2 * margin, 1)
  const scale = Math.min(availW / spanX, availH / spanY) // px por grado de latitud
  const offX = margin + (availW - (lngMax - lngMin) * kx * scale) / 2
  const offY = margin + (availH - (latMax - latMin) * scale) / 2
  return {
    project: (p) => ({ x: offX + (p.lng - lngMin) * kx * scale, y: offY + (latMax - p.lat) * scale }),
    unproject: (x, y) => ({ lat: latMax - (y - offY) / scale, lng: lngMin + (x - offX) / (kx * scale) }),
    metersPerPx: METERS_PER_DEG / scale,
  }
}

export interface Placed {
  id: string
  x: number
  y: number
}

/**
 * Pines que caen uno encima de otro (varias órdenes a la misma dirección) se abren en abanico alrededor de su centro, para que todos se vean y se puedan
 * tocar. Devuelve la posición dibujada de cada id; un pin solo no se mueve.
 */
export function spreadOverlaps(items: readonly Placed[], minDist = 12): Map<string, { x: number; y: number }> {
  const clusters: Placed[][] = []
  for (const it of items) {
    const home = clusters.find((c) => Math.hypot(c[0].x - it.x, c[0].y - it.y) < minDist)
    if (home) home.push(it)
    else clusters.push([it])
  }
  const out = new Map<string, { x: number; y: number }>()
  for (const c of clusters) {
    if (c.length === 1) {
      out.set(c[0].id, { x: c[0].x, y: c[0].y })
      continue
    }
    const cx = c.reduce((s, i) => s + i.x, 0) / c.length
    const cy = c.reduce((s, i) => s + i.y, 0) / c.length
    const radius = Math.min(10 + 2 * c.length, 26)
    c.forEach((it, k) => {
      const angle = -Math.PI / 2 + (2 * Math.PI * k) / c.length
      out.set(it.id, { x: cx + radius * Math.cos(angle), y: cy + radius * Math.sin(angle) })
    })
  }
  return out
}

const NICE = [10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000]

/** Barra de escala: el mayor valor «redondo» (10 m, 50 m, 1 km…) que cabe en `maxPx`. */
export function niceScale(metersPerPx: number, maxPx: number): { meters: number; px: number } {
  let meters = NICE[0]
  for (const n of NICE) if (n / metersPerPx <= maxPx) meters = n
  return { meters, px: meters / metersPerPx }
}

/** «500 m» / «2 km» (el número ya formateado por quien llama, para respetar la región). */
export function scaleParts(meters: number): { value: number; unit: 'm' | 'km' } {
  return meters >= 1000 ? { value: meters / 1000, unit: 'km' } : { value: meters, unit: 'm' }
}
