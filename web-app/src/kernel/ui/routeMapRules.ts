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
  /** Mercator del mundo entero: píxeles que mide el mundo (de 0 a 1 en x y en y) y dónde cae su esquina noroeste en el plano; con esto se colocan las teselas del mapa. */
  world: { scale: number; originX: number; originY: number }
}

/** Metros que mide el ecuador (el mundo entero en Mercator). */
const EQUATOR_METERS = 40_075_016.686
/** Alcance mínimo del plano en grados (≈ 1.1 km): con un solo punto, o puntos casi iguales, no se amplía sin fin. */
const MIN_SPAN_DEG = 0.01

/** Longitud → x del mundo (0 a 1) y latitud → y del mundo (0 arriba, 1 abajo), Web Mercator (el de las teselas de OpenStreetMap y Google). */
export const worldX = (lng: number): number => (lng + 180) / 360
export const worldY = (lat: number): number => 0.5 - Math.log(Math.tan(Math.PI / 4 + (lat * Math.PI) / 360)) / (2 * Math.PI)
const lngOf = (wx: number): number => wx * 360 - 180
const latOf = (wy: number): number => (2 * Math.atan(Math.exp((0.5 - wy) * 2 * Math.PI)) - Math.PI / 2) * (180 / Math.PI)

/**
 * Proyección Web Mercator ajustada al rectángulo que contiene todos los puntos, con `margin` px de margen (2026-10-26: antes equirectangular sin mapa de fondo; ahora
 * es la misma proyección de las teselas del mapa, así los pines caen sobre las calles). Conserva la proporción y deja el contenido centrado.
 * Sin puntos → null (no hay con qué ajustar).
 */
export function fitProjection(points: readonly GeoPoint[], width: number, height: number, margin = 28): Projection | null {
  if (points.length === 0 || width <= 0 || height <= 0) return null
  const xs = points.map((p) => worldX(p.lng))
  const ys = points.map((p) => worldY(p.lat))
  const xMin = Math.min(...xs)
  const xMax = Math.max(...xs)
  const yMin = Math.min(...ys)
  const yMax = Math.max(...ys)
  const lat0 = (Math.min(...points.map((p) => p.lat)) + Math.max(...points.map((p) => p.lat))) / 2
  const minSpan = MIN_SPAN_DEG / 360
  const spanX = Math.max(xMax - xMin, minSpan)
  const spanY = Math.max(yMax - yMin, minSpan)
  const availW = Math.max(width - 2 * margin, 1)
  const availH = Math.max(height - 2 * margin, 1)
  const scale = Math.min(availW / spanX, availH / spanY) // px que mide el mundo entero
  const offX = margin + (availW - (xMax - xMin) * scale) / 2
  const offY = margin + (availH - (yMax - yMin) * scale) / 2
  return {
    project: (p) => ({ x: offX + (worldX(p.lng) - xMin) * scale, y: offY + (worldY(p.lat) - yMin) * scale }),
    unproject: (x, y) => ({ lat: latOf(yMin + (y - offY) / scale), lng: lngOf(xMin + (x - offX) / scale) }),
    metersPerPx: (EQUATOR_METERS * Math.cos((lat0 * Math.PI) / 180)) / scale,
    world: { scale, originX: offX - xMin * scale, originY: offY - yMin * scale },
  }
}

/** Una tesela del mapa de fondo, ya colocada en píxeles del plano. */
export interface MapTile {
  key: string
  url: string
  x: number
  y: number
  size: number
}

/** Mapa de fondo: teselas de OpenStreetMap (como en RentComparable). Cambiar de proveedor es cambiar esta plantilla. */
export const TILE_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'

/** Teselas que cubren el plano (ancho × alto px). Zoom = el menor que da teselas nítidas (≤ 19); como mucho `maxTiles` (si no, no hay mapa de fondo). */
export function tilesFor(world: Projection['world'], width: number, height: number, maxTiles = 80): MapTile[] {
  const z = Math.min(19, Math.max(0, Math.ceil(Math.log2(Math.max(world.scale, 1) / 256))))
  const n = 2 ** z
  const size = world.scale / n
  const x0 = Math.floor((0 - world.originX) / size)
  const x1 = Math.floor((width - world.originX) / size)
  const y0 = Math.max(0, Math.floor((0 - world.originY) / size))
  const y1 = Math.min(n - 1, Math.floor((height - world.originY) / size))
  if ((x1 - x0 + 1) * (y1 - y0 + 1) > maxTiles) return []
  const out: MapTile[] = []
  for (let ty = y0; ty <= y1; ty++) {
    for (let tx = x0; tx <= x1; tx++) {
      const wrapped = ((tx % n) + n) % n
      out.push({ key: `${z}/${tx}/${ty}`, url: TILE_URL.replace('{z}', String(z)).replace('{x}', String(wrapped)).replace('{y}', String(ty)), x: world.originX + tx * size, y: world.originY + ty * size, size })
    }
  }
  return out
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
