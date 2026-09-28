import { useEffect, useState } from 'react'
import { ActivityIndicator, StyleSheet, View } from 'react-native'
import { GestureHandlerRootView } from 'react-native-gesture-handler'
import { initialWindowMetrics, SafeAreaProvider, useSafeAreaInsets } from 'react-native-safe-area-context'
import { Slot, useRouter } from 'expo-router'
import { StatusBar } from 'expo-status-bar'

import { setAuthLostHandler } from '../kernel/api/client'
import { hydrateSession } from '../kernel/auth/session'
import { colors } from '../kernel/ui/theme'

/** Ninguna pantalla usa SafeAreaView (serían 9 archivos idénticos); el inset superior se aplica una sola vez aquí,
 *  para que el contenido nunca quede debajo de la barra de estado. `useSafeAreaInsets` necesita un descendiente de
 *  SafeAreaProvider, de ahí el componente aparte. */
function AppContent({ ready }: { ready: boolean }) {
  const insets = useSafeAreaInsets()
  return (
    <View style={[styles.fill, { paddingTop: insets.top }]}>
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
