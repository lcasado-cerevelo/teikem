import { useEffect } from 'react';
import { Redirect, Stack } from 'expo-router';
import { useAuth } from '@hooks/AuthContext';
import { useAuthSync, useAutoSync } from '@hooks/queries';
import { useAppStore } from '@/stores';

export default function AuthLayout() {
  const { isLoggedIn } = useAuth();
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

  if (!isLoggedIn) return <Redirect href="/(no-auth)/Login" />;

  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="OrdersList/index" />
      <Stack.Screen name="ShipmentDetails/index" options={{ presentation: 'modal' }} />
    </Stack>
  );
}
