import { storage } from '@/offline';
import { DEFAULT_TRACKING_CONFIG, type TrackPoint, type TrackingConfig, type TrackingEvent } from './trackingCore';

// Lo que el seguimiento guarda en el teléfono (kv de SQLite): puntos y avisos que todavía no se enviaron, el último punto guardado, la configuración del servidor
// y la preferencia del chofer. Sobrevive a cerrar la app: una zona sin señal por horas no pierde la ruta.

const K = {
  buffer: 'tracking.buffer',
  events: 'tracking.events',
  last: 'tracking.last',
  config: 'tracking.config',
  enabled: 'tracking.enabled',
  disclosed: 'tracking.disclosed',
  running: 'tracking.running',
};

async function getJson<T>(key: string, fallback: T): Promise<T> {
  try {
    const raw = await storage.kvGet(key);
    return raw ? (JSON.parse(raw) as T) : fallback;
  } catch {
    return fallback;
  }
}

const setJson = (key: string, value: unknown) => storage.kvSet(key, JSON.stringify(value));

export const trackingStore = {
  getBuffer: () => getJson<TrackPoint[]>(K.buffer, []),
  setBuffer: (v: TrackPoint[]) => setJson(K.buffer, v),
  getEvents: () => getJson<TrackingEvent[]>(K.events, []),
  setEvents: (v: TrackingEvent[]) => setJson(K.events, v),
  getLast: () => getJson<TrackPoint | null>(K.last, null),
  setLast: (v: TrackPoint | null) => setJson(K.last, v),
  getConfig: () => getJson<TrackingConfig>(K.config, DEFAULT_TRACKING_CONFIG),
  setConfig: (v: TrackingConfig) => setJson(K.config, v),
  /** El chofer puede apagar el envío de su ubicación; por omisión está encendido. */
  getEnabled: async () => (await getJson<boolean | null>(K.enabled, null)) ?? true,
  setEnabled: (v: boolean) => setJson(K.enabled, v),
  /** Ya se le mostró el aviso de qué se recoge y para qué (se muestra una vez antes de pedir el permiso). */
  getDisclosed: () => getJson<boolean>(K.disclosed, false),
  setDisclosed: () => setJson(K.disclosed, true),
  /** ¿El seguimiento estaba corriendo? (para avisar solo los cambios: STARTED, no repetirlo en cada apertura) */
  getRunning: () => getJson<boolean>(K.running, false),
  setRunning: (v: boolean) => setJson(K.running, v),
  async addEvent(type: TrackingEvent['type']): Promise<void> {
    const events = await trackingStore.getEvents();
    await trackingStore.setEvents([...events, { type, occurredAtUtc: new Date().toISOString() }].slice(-50));
  },
  async clearAll(): Promise<void> {
    await Promise.all([trackingStore.setBuffer([]), trackingStore.setEvents([]), trackingStore.setLast(null), trackingStore.setRunning(false)]);
  },
};
