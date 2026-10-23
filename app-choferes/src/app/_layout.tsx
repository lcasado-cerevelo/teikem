import { DarkTheme, DefaultTheme, Stack, ThemeProvider as NavigationThemeProvider } from 'expo-router';
import { ActivityIndicator, Platform, View } from 'react-native';
import { NavigationBar } from 'expo-navigation-bar';
import { QueryClient } from '@tanstack/react-query';
import { AuthProvider, useAuth } from '@hooks/AuthContext';
import { ThemeProvider, useTheme } from '@hooks/ThemeContext';
import { OfflineProvider } from '@/offline';
import '@/lang/i18n'; // inicializa i18n (ES/EN)
import '@/tracking/trackingTask'; // define la tarea de ubicación en segundo plano (debe cargarse con la app)

// gcTime must be long for the persisted cache to be useful offline (a day's route).
// networkMode 'offlineFirst' makes queries serve cached data when there's no signal.
//
// refetchOnReconnect is OFF (react-query defaults it to true). With it on, ANY
// mounted query (e.g. the driver's shipment list on Home) silently refetches
// itself the instant connectivity returns — a SEPARATE, uncoordinated path
// from OfflineProvider's own reconnect handler. That refetch has no idea the
// outbox hasn't sent a just-completed delivery yet, so it can pull the
// server's still-stale "still open" list and stomp the correct local state.
// OfflineProvider.tsx now owns the ONLY reconnect-triggered refresh: drain the
// outbox FIRST, then invalidate — see catchUp() there.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 2,
      staleTime: 60_000,
      gcTime: 1000 * 60 * 60 * 24,
      refetchOnWindowFocus: false,
      refetchOnReconnect: false,
      networkMode: 'offlineFirst',
    },
    mutations: {
      networkMode: 'offlineFirst',
    },
  },
});

function RootNavigator() {
  const { isLoading } = useAuth();
  const { scheme, colors } = useTheme();
  const navTheme = {
    ...(scheme === 'dark' ? DarkTheme : DefaultTheme),
    colors: { ...(scheme === 'dark' ? DarkTheme : DefaultTheme).colors, primary: colors.primary, background: colors.background, card: colors.background, text: colors.text, border: colors.gray },
  };

  // While restoring the session, show a spinner so we don't flash Login
  // for users who are already logged in.
  if (isLoading) {
    return (
      <View style={{ flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: colors.background }}>
        <ActivityIndicator size="large" />
      </View>
    );
  }

  return (
    <NavigationThemeProvider value={navTheme}>
      <Stack screenOptions={{ headerShown: false }} />
    </NavigationThemeProvider>
  );
}

export default function RootLayout() {
  return (
    <OfflineProvider client={queryClient}>
      <ThemeProvider>
      <AuthProvider>
        {/* Immersive mode: keep the Android navigation bar hidden while driving
            the route; a swipe from the edge reveals it temporarily. */}
        {Platform.OS === 'android' && <NavigationBar hidden />}
        <RootNavigator />
      </AuthProvider>
      </ThemeProvider>
    </OfflineProvider>
  );
}
