import { Platform } from 'react-native';
import * as TaskManager from 'expo-task-manager';
import type * as Location from 'expo-location';
import { pushBounded, shouldKeep, type TrackPoint } from './trackingCore';
import { trackingStore } from './trackingStore';
import { uploadTracking } from './trackingUpload';

// Tarea de ubicación en segundo plano. Se DEFINE al cargar la app (la importa el layout raíz) para que el sistema pueda despertarla aunque la pantalla esté apagada.
// Aquí solo se filtra y se guarda en el teléfono; el envío va por lotes cada `uploadEverySeconds` (2 min).
export const TRACKING_TASK = 'teikem-route-tracking';

let lastUploadAttempt = 0;

if (Platform.OS !== 'web') {
  TaskManager.defineTask(TRACKING_TASK, async ({ data, error }: TaskManager.TaskManagerTaskBody<{ locations?: Location.LocationObject[] }>) => {
    if (error || !data?.locations?.length) return;
    const cfg = await trackingStore.getConfig();
    let last = await trackingStore.getLast();
    let buffer = await trackingStore.getBuffer();
    let changed = false;
    for (const loc of data.locations) {
      const at = loc.timestamp || Date.now();
      if (!shouldKeep(last, loc.coords.latitude, loc.coords.longitude, at, cfg)) continue;
      const point: TrackPoint = {
        latitude: Math.round(loc.coords.latitude * 1e6) / 1e6,
        longitude: Math.round(loc.coords.longitude * 1e6) / 1e6,
        capturedAtUtc: new Date(at).toISOString(),
        speedKmh: loc.coords.speed != null && loc.coords.speed >= 0 ? Math.round(loc.coords.speed * 3.6 * 10) / 10 : null,
        headingDeg: loc.coords.heading != null && loc.coords.heading >= 0 ? Math.round(loc.coords.heading) : null,
      };
      buffer = pushBounded(buffer, point);
      last = point;
      changed = true;
    }
    if (changed) {
      await trackingStore.setBuffer(buffer);
      await trackingStore.setLast(last);
    }
    if (Date.now() - lastUploadAttempt >= cfg.uploadEverySeconds * 1000) {
      lastUploadAttempt = Date.now();
      await uploadTracking().catch(() => false);
    }
  });
}
