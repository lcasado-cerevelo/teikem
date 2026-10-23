import { useCallback, useEffect, useRef } from 'react';
import { Alert, AppState, Platform } from 'react-native';
import * as Location from 'expo-location';
import { useTranslation } from 'react-i18next';
import { useDriverWork } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { TRACKING_TASK } from './trackingTask';
import { trackingStore } from './trackingStore';
import { refreshTrackingConfig, uploadTracking } from './trackingUpload';
import { stopTracking } from './trackingControl';

// Seguimiento de la ruta: corre mientras el chofer tenga una ruta EN CURSO (manifiesto aceptado con envíos abiertos) y no lo haya apagado.
// Pide el permiso con un aviso previo claro (qué se recoge, para qué y cuándo); sin el permiso «siempre» sigue con la app abierta.
export function useRouteTracking() {
  const { t } = useTranslation();
  const { data } = useDriverWork();
  const enabled = useAppStore((s) => s.trackingEnabled);
  const routeActive = Boolean(data?.shipments.some((s) => s.manifestAccepted));
  const wanted = routeActive && enabled && Platform.OS !== 'web';
  const starting = useRef(false);

  const askDisclosure = useCallback(
    () =>
      new Promise<boolean>((resolve) => {
        Alert.alert(t('TRACKING.DISCLOSURE_TITLE'), t('TRACKING.DISCLOSURE_BODY'), [
          { text: t('TRACKING.DISCLOSURE_NO'), style: 'cancel', onPress: () => resolve(false) },
          { text: t('TRACKING.DISCLOSURE_YES'), onPress: () => resolve(true) },
        ]);
      }),
    [t],
  );

  const start = useCallback(async () => {
    if (starting.current) return;
    starting.current = true;
    try {
      if (await Location.hasStartedLocationUpdatesAsync(TRACKING_TASK)) return;
      let fg = await Location.getForegroundPermissionsAsync();
      if (!fg.granted) {
        if (!(await trackingStore.getDisclosed())) {
          if (!(await askDisclosure())) {
            useAppStore.getState().setTrackingEnabled(false);
            await trackingStore.addEvent('STOPPED_BY_DRIVER');
            return;
          }
          await trackingStore.setDisclosed();
        }
        fg = await Location.requestForegroundPermissionsAsync();
      }
      if (!fg.granted) {
        useAppStore.getState().setTrackingPermissionLost(true);
        await trackingStore.addEvent('PERMISSION_LOST');
        return;
      }
      useAppStore.getState().setTrackingPermissionLost(false);
      // El permiso «siempre» es el que deja seguir con la pantalla apagada; si no lo da, sigue mientras la app esté abierta.
      let bg = await Location.getBackgroundPermissionsAsync();
      if (!bg.granted) bg = await Location.requestBackgroundPermissionsAsync().catch(() => bg);
      await Location.startLocationUpdatesAsync(TRACKING_TASK, {
        accuracy: Location.Accuracy.Balanced,
        distanceInterval: 20,
        timeInterval: 30_000,
        pausesUpdatesAutomatically: true,
        showsBackgroundLocationIndicator: true,
        foregroundService: { notificationTitle: t('TRACKING.NOTIFICATION_TITLE'), notificationBody: t('TRACKING.NOTIFICATION_BODY'), killServiceOnDestroy: false },
      });
      if (!(await trackingStore.getRunning())) {
        await trackingStore.addEvent(enabledBefore.current === false ? 'RESUMED' : 'STARTED');
        await trackingStore.setRunning(true);
      }
      await refreshTrackingConfig();
    } catch {
      /* sin permiso o sin servicio de ubicación: se reintenta al volver a la app */
    } finally {
      starting.current = false;
    }
  }, [askDisclosure, t]);

  // Para distinguir «lo reanudó» de «arrancó»: ¿estaba apagado por el chofer antes?
  const enabledBefore = useRef<boolean | null>(null);
  useEffect(() => {
    if (enabledBefore.current === true && !enabled) void trackingStore.addEvent('STOPPED_BY_DRIVER').then(() => trackingStore.setRunning(false));
    enabledBefore.current = enabled;
  }, [enabled]);

  useEffect(() => {
    if (wanted) void start();
    else void stopTracking().then(() => (routeActive ? undefined : trackingStore.setRunning(false)));
  }, [wanted, routeActive, start]);

  // Envío por lotes mientras corre, y al volver a la app (también detecta que le quitaron el permiso).
  useEffect(() => {
    if (!wanted) return;
    let timer: ReturnType<typeof setInterval> | undefined;
    void trackingStore.getConfig().then((cfg) => {
      timer = setInterval(() => void uploadTracking().catch(() => false), cfg.uploadEverySeconds * 1000);
    });
    const sub = AppState.addEventListener('change', (s) => {
      if (s !== 'active') return;
      void (async () => {
        const fg = await Location.getForegroundPermissionsAsync();
        if (!fg.granted) {
          useAppStore.getState().setTrackingPermissionLost(true);
          if (await trackingStore.getRunning()) await trackingStore.addEvent('PERMISSION_LOST');
          await trackingStore.setRunning(false);
        } else void start();
        await uploadTracking().catch(() => false);
      })();
    });
    return () => {
      if (timer) clearInterval(timer);
      sub.remove();
    };
  }, [wanted, start]);
}
