import { Platform } from 'react-native';
import * as Location from 'expo-location';
import { TRACKING_TASK } from './trackingTask';
import { trackingStore } from './trackingStore';
import { uploadTracking } from './trackingUpload';

/** Detiene el seguimiento y manda lo que quede (al terminar la ruta, al apagarlo o al cerrar sesión). */
export async function stopTracking(): Promise<void> {
  if (Platform.OS === 'web') return;
  try {
    if (await Location.hasStartedLocationUpdatesAsync(TRACKING_TASK)) await Location.stopLocationUpdatesAsync(TRACKING_TASK);
  } catch {
    /* no estaba corriendo */
  }
  await uploadTracking().catch(() => false);
}

/** Al cerrar sesión el teléfono queda limpio: se detiene y se borra lo que no se envió (era del chofer anterior). */
export async function resetTracking(): Promise<void> {
  if (Platform.OS === 'web') return;
  try {
    if (await Location.hasStartedLocationUpdatesAsync(TRACKING_TASK)) await Location.stopLocationUpdatesAsync(TRACKING_TASK);
  } catch {
    /* no estaba corriendo */
  }
  await trackingStore.clearAll();
}
