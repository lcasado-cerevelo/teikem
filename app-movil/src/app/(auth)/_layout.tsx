import { useEffect } from 'react';
import { Redirect, Stack } from 'expo-router';
import { useAuth } from '@hooks/AuthContext';
import { useAuthSync, useAutoSync } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { usePushRegistration } from '@/push/usePushRegistration';
import { useRouteTracking } from '@/tracking/useRouteTracking';
import LockScreen from '@components/LockScreen';
import Legal from './Legal';

export default function AuthLayout() {
  const { isLoggedIn, locked, legalRequired } = useAuth();
  // Sincroniza las entregas del chofer apenas autentica (no-op si no hay sesión).
  useAuthSync();

  // Y después, cada N minutos (configurable en Perfil; 20 por defecto). El
  // valor vive en el store (compartido con Perfil) para que cambiarlo ahí
  // mueva de inmediato el timer que ya está corriendo aquí.
  const syncIntervalMinutes = useAppStore((s) => s.syncIntervalMinutes);
  const hydrateSyncIntervalMinutes = useAppStore((s) => s.hydrateSyncIntervalMinutes);
  useEffect(() => {
    void hydrateSyncIntervalMinutes();
  }, [hydrateSyncIntervalMinutes]);
  useAutoSync(syncIntervalMinutes);
  // Seguimiento de la ruta mientras haya una ruta en curso (la tarea en segundo plano se define en el layout raíz).
  const hydrateTrackingEnabled = useAppStore((s) => s.hydrateTrackingEnabled);
  useEffect(() => {
    void hydrateTrackingEnabled();
  }, [hydrateTrackingEnabled]);
  useRouteTracking();
  usePushRegistration();

  if (!isLoggedIn) return <Redirect href="/(no-auth)/Login" />;
  // con «Abrir con huella» activo, la app arranca y vuelve de segundo plano bloqueada
  if (locked) return <LockScreen />;

  // primer ingreso o documento nuevo: primero se acepta la política de privacidad y los términos de uso (sin aceptarlos no se ve nada más)
  if (legalRequired === true) return <Legal />;

  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="OrdersList/index" />
      <Stack.Screen name="Orders/index" />
      <Stack.Screen name="Trips/index" />
      <Stack.Screen name="Scan/index" />
      <Stack.Screen name="TripDetails/index" />
      <Stack.Screen name="StreetAssign/index" />
      <Stack.Screen name="AcceptManifest/index" />
      <Stack.Screen name="ShipmentDetails/index" options={{ presentation: 'modal' }} />
      <Stack.Screen name="Chat/index" />
      <Stack.Screen name="NewChat/index" />
    </Stack>
  );
}
