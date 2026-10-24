import { AxiosError } from 'axios';
import { api, getCompanyTenantIds } from '@config/axios';
import { chunk } from './trackingCore';
import { trackingStore } from './trackingStore';

// Envía lo juntado a TODAS las compañías del chofer con sesión: el servidor de cada una guarda los puntos solo si el chofer tiene allí una ruta en curso
// (y los avisos siempre). Es idempotente (la hora de captura no se repite), así que reintentar un lote que llegó a medias no duplica.
let inFlight: Promise<boolean> | null = null;

export function uploadTracking(): Promise<boolean> {
  if (!inFlight) inFlight = run().finally(() => (inFlight = null));
  return inFlight;
}

async function run(): Promise<boolean> {
  const tenants = getCompanyTenantIds();
  if (tenants.length === 0) return false; // sin sesión en memoria (p. ej. el proceso se reinició): se envía al abrir la app
  const [points, events] = await Promise.all([trackingStore.getBuffer(), trackingStore.getEvents()]);
  if (points.length === 0 && events.length === 0) return true;

  const batches = chunk(points, 500);
  if (batches.length === 0) batches.push([]);
  let allOk = true;
  for (const tenantId of tenants) {
    for (let i = 0; i < batches.length; i++) {
      try {
        await api.post(
          'driver-app/tracking',
          {
            points: batches[i].map((p) => ({ latitude: p.latitude, longitude: p.longitude, capturedAtUtc: p.capturedAtUtc, speedKmh: p.speedKmh ?? null, headingDeg: p.headingDeg ?? null })),
            events: i === 0 ? events : [],
          },
          { tenantId, timeout: 30_000 },
        );
      } catch (err) {
        const status = (err as AxiosError).response?.status;
        // 4xx (salvo 401/408/429): esa compañía no va a aceptarlo nunca (p. ej. ya no es chofer allí); no se reintenta para no acumular para siempre.
        const permanent = typeof status === 'number' && status >= 400 && status < 500 && ![401, 408, 429].includes(status);
        if (!permanent) allOk = false;
      }
    }
  }
  if (allOk) {
    // Solo se vacía lo que se envió: pudieron llegar puntos nuevos mientras tanto.
    const [nowPoints, nowEvents] = await Promise.all([trackingStore.getBuffer(), trackingStore.getEvents()]);
    await Promise.all([trackingStore.setBuffer(nowPoints.slice(points.length)), trackingStore.setEvents(nowEvents.slice(events.length))]);
  }
  return allOk;
}

/** Trae los valores globales del seguimiento (distancia, intervalos, retención) y los deja guardados. Sin señal quedan los últimos. */
export async function refreshTrackingConfig(): Promise<void> {
  const [tenantId] = getCompanyTenantIds();
  if (tenantId == null) return;
  try {
    const { data } = await api.get<{ minDistanceMeters: number; maxIntervalSeconds: number; uploadEverySeconds: number; retentionDays: number }>('driver-app/tracking/config', { tenantId });
    await trackingStore.setConfig(data);
  } catch {
    /* quedan los últimos valores */
  }
}
