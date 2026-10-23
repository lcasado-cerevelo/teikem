import { useEffect, useRef } from 'react';
import { Platform } from 'react-native';
import * as Notifications from 'expo-notifications';
import Constants from 'expo-constants';
import { useRouter } from 'expo-router';
import { useQueryClient } from '@tanstack/react-query';
import { api, getCompanyTenantIds } from '@config/axios';
import { useAuth } from '@hooks/AuthContext';

// Avisos emergentes (Firebase). Con la app abierta el aviso se muestra igual; al tocarlo abre la pestaña de avisos.
Notifications.setNotificationHandler({
  handleNotification: async () => ({ shouldShowBanner: true, shouldShowList: true, shouldPlaySound: true, shouldSetBadge: false }),
});

/**
 * Registra el teléfono para recibir avisos: pide el permiso de notificaciones, crea el canal «avisos» y manda el token de FCM a TODAS las compañías del chofer
 * (cada una lo guarda para sus avisos). Se repite cuando cambian las compañías con sesión. Sin permiso o sin Firebase en esta marca, la app sigue: los avisos quedan en la bandeja.
 */
export function usePushRegistration() {
  const { companies, isLoggedIn } = useAuth();
  const router = useRouter();
  const qc = useQueryClient();
  const key = companies.filter((c) => !c.blocked).map((c) => c.tenantId).join(',');
  const done = useRef<string | null>(null);

  useEffect(() => {
    if (Platform.OS === 'web' || !isLoggedIn || !key || done.current === key) return;
    let cancelled = false;
    void (async () => {
      try {
        if (Platform.OS === 'android') {
          await Notifications.setNotificationChannelAsync('avisos', { name: 'Avisos', importance: Notifications.AndroidImportance.HIGH });
        }
        let perm = await Notifications.getPermissionsAsync();
        if (!perm.granted) perm = await Notifications.requestPermissionsAsync();
        if (!perm.granted || cancelled) return;
        const token = await Notifications.getDevicePushTokenAsync();
        if (cancelled || typeof token.data !== 'string') return;
        const body = { platform: Platform.OS === 'ios' ? 'IOS' : 'ANDROID', pushToken: token.data, appVersion: Constants.expoConfig?.version ?? null };
        await Promise.allSettled(getCompanyTenantIds().map((tenantId) => api.post('driver-app/device', body, { tenantId })));
        done.current = key;
      } catch {
        /* sin Firebase en esta marca o sin permiso: queda la bandeja */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [isLoggedIn, key]);

  useEffect(() => {
    if (Platform.OS === 'web') return;
    const received = Notifications.addNotificationReceivedListener(() => void qc.invalidateQueries({ queryKey: ['notices'] }));
    const tapped = Notifications.addNotificationResponseReceivedListener(() => {
      void qc.invalidateQueries({ queryKey: ['notices'] });
      router.push('/(auth)/(tabs)/notices');
    });
    return () => {
      received.remove();
      tapped.remove();
    };
  }, [qc, router]);
}
