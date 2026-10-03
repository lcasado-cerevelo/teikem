import { useEffect, useState } from 'react'
import { ActivityIndicator, StyleSheet, View } from 'react-native'
import { GestureHandlerRootView } from 'react-native-gesture-handler'
import { initialWindowMetrics, SafeAreaProvider, useSafeAreaInsets } from 'react-native-safe-area-context'
import { Slot, useRouter } from 'expo-router'
import { StatusBar } from 'expo-status-bar'

import { setAuthLostHandler } from '../kernel/api/client'
import { hydrateSession } from '../kernel/auth/session'
import { bottomPadding } from '../kernel/ui/insets'
import { colors } from '../kernel/ui/theme'

/** Ninguna pantalla usa SafeAreaView (serían 9 archivos idénticos); los márgenes del sistema se aplican una sola vez aquí:
 *  arriba, el de la barra de estado; abajo (docs/mobile/mejoras-ux-zebra.md §1), `max(inset, 32) + 8` para que la barra
 *  de navegación del aparato (3 botones o gestos) nunca tape el último botón. La barra NO se oculta (modo inmersivo):
 *  en un Zebra es la forma de salir de la app. `useSafeAreaInsets` necesita un descendiente de SafeAreaProvider, de ahí
 *  el componente aparte. */
function AppContent({ ready }: { ready: boolean }) {
  const insets = useSafeAreaInsets()
  return (
    <View testID="app-root" style={[styles.fill, { paddingTop: insets.top, paddingBottom: bottomPadding(insets.bottom) }]}>
      <StatusBar style="light" />
      {ready ? (
        <Slot />
      ) : (
        <View style={styles.loading}>
          <ActivityIndicator size="large" color={colors.brand} />
        </View>
      )}
    </View>
  )
}

/** Raíz de la app: hidrata el aparato y la sesión guardados antes de mostrar cualquier pantalla, y registra qué hacer
 *  si la sesión se pierde a mitad de una llamada (kernel/api/client.ts, 401 sin poder refrescar). */
export default function RootLayout() {
  const [ready, setReady] = useState(false)
  const router = useRouter()

  useEffect(() => {
    hydrateSession().then(() => setReady(true))
    setAuthLostHandler(() => router.replace('/login'))
    return () => setAuthLostHandler(null)
  }, [router])

  return (
    <GestureHandlerRootView style={styles.fill}>
      <SafeAreaProvider initialMetrics={initialWindowMetrics}>
        <AppContent ready={ready} />
      </SafeAreaProvider>
    </GestureHandlerRootView>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1, backgroundColor: colors.bg },
  loading: { flex: 1, alignItems: 'center', justifyContent: 'center' },
})
