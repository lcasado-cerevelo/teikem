import { storage } from '@/offline';

// Preferencia persistida del usuario (SQLite/localStorage vía @/offline, la
// misma pieza que ya guarda el caché offline) — sobrevive a cerrar la app.
// El valor "vivo" en memoria se guarda en useAppStore (no aquí como useState):
// tanto el layout (que arma el timer) como Perfil (que lo cambia) deben leer y
// escribir el MISMO estado, o cambiarlo en Perfil no movería el timer que ya
// está corriendo hasta el próximo reinicio de la app.
const KEY = 'settings.syncIntervalMinutes';

export const DEFAULT_SYNC_INTERVAL_MINUTES = 20;
export const SYNC_INTERVAL_OPTIONS_MINUTES = [5, 10, 15, 20, 30, 60] as const;

export async function loadSyncIntervalMinutes(): Promise<number> {
  const raw = await storage.kvGet(KEY);
  const n = raw ? Number(raw) : DEFAULT_SYNC_INTERVAL_MINUTES;
  return Number.isFinite(n) && n > 0 ? n : DEFAULT_SYNC_INTERVAL_MINUTES;
}

export async function saveSyncIntervalMinutes(minutes: number): Promise<void> {
  await storage.kvSet(KEY, String(minutes));
}
