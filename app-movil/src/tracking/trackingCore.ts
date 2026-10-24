// Reglas puras del seguimiento de ruta (sin Expo ni red, para poder probarlas con node).
// Se guarda un punto por MOVIMIENTO, no por reloj: al menos `minDistanceMeters` desde el último punto guardado, o `maxIntervalSeconds` si el chofer sí se está moviendo.
// Con el teléfono quieto no se guarda nada (ahorra batería y datos).

export interface TrackingConfig {
  minDistanceMeters: number;
  maxIntervalSeconds: number;
  uploadEverySeconds: number;
  retentionDays: number;
}

export const DEFAULT_TRACKING_CONFIG: TrackingConfig = { minDistanceMeters: 150, maxIntervalSeconds: 90, uploadEverySeconds: 120, retentionDays: 10 };

/** Por debajo de esta distancia se considera «quieto» (el GPS tiembla unos metros aun parado). */
export const STILL_METERS = 30;

/** Máximo de puntos que se guardan en el teléfono sin poder enviarlos (zona sin señal por horas); los más viejos se descartan. */
export const MAX_BUFFER = 2000;

export interface TrackPoint {
  latitude: number;
  longitude: number;
  capturedAtUtc: string;
  speedKmh?: number | null;
  headingDeg?: number | null;
}

export function distanceMeters(aLat: number, aLng: number, bLat: number, bLng: number): number {
  const R = 6371000;
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(bLat - aLat);
  const dLng = toRad(bLng - aLng);
  const s = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(aLat)) * Math.cos(toRad(bLat)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(s));
}

/** ¿Se guarda este punto? `last` = el último punto guardado (null = el primero siempre se guarda). */
export function shouldKeep(last: TrackPoint | null, lat: number, lng: number, atMs: number, cfg: TrackingConfig): boolean {
  if (!Number.isFinite(lat) || !Number.isFinite(lng) || (lat === 0 && lng === 0)) return false;
  if (!last) return true;
  const moved = distanceMeters(last.latitude, last.longitude, lat, lng);
  if (moved >= cfg.minDistanceMeters) return true;
  const elapsedS = (atMs - Date.parse(last.capturedAtUtc)) / 1000;
  return elapsedS >= cfg.maxIntervalSeconds && moved >= STILL_METERS;
}

/** Agrega al buffer sin pasar del tope (se descartan los más viejos). */
export function pushBounded(buffer: TrackPoint[], point: TrackPoint): TrackPoint[] {
  const next = [...buffer, point];
  return next.length > MAX_BUFFER ? next.slice(next.length - MAX_BUFFER) : next;
}

/** Trozos de a 500 (el máximo del servidor por envío). */
export function chunk<T>(items: T[], size = 500): T[][] {
  const out: T[][] = [];
  for (let i = 0; i < items.length; i += size) out.push(items.slice(i, i + size));
  return out;
}

export type TrackingEventType = 'STARTED' | 'STOPPED_BY_DRIVER' | 'PERMISSION_LOST' | 'RESUMED';
export interface TrackingEvent {
  type: TrackingEventType;
  occurredAtUtc: string;
}
