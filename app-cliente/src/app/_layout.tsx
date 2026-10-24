// Raíz de la app del cliente: arranque (idioma y sesión guardada) y la COMPUERTA que decide a qué pantalla va cada quien:
// sin sesión → login (o el paso de MFA / el enlace de confirmación de computadora); con sesión y la huella activa y bloqueada → /lock; con sesión sin aceptar el documento legal → /legal; si no, el inicio.
import { Stack, usePathname, useRouter } from 'expo-router'
import { StatusBar } from 'expo-status-bar'
import { useEffect, useState } from 'react'
import { View } from 'react-native'
import { SafeAreaProvider } from 'react-native-safe-area-context'

import { setLegalRequired, useLegalRequired } from '../features/legal/legalState'
import { getLegalStatus } from '../features/portalApi'
import { setAuthLostHandler } from '../kernel/api/client'
import { AppLockProvider, useAppLock } from '../kernel/auth/AppLock'
import { useIsConsignee } from '../kernel/auth/role'
import { loadSession, useSessionState } from '../kernel/auth/session'
import { initLang } from '../kernel/i18n/i18n'
import { initTheme } from '../kernel/ui/theme'
import { EnvironmentBanner, EnvironmentFooter } from '../kernel/ui/EnvironmentBanner'
import { useT } from '../kernel/i18n/useT'
import { clearPortalBrand, loadPortalBrand } from '../kernel/brand/portalBrand'
import { KemiFloating, KemiSleepButton } from '../features/kemi/KemiFloating'
import { KemiProvider } from '../features/kemi/KemiProvider'
import { TopBar } from '../features/shell/TopBar'
import { BrandBar } from '../kernel/ui/BrandBar'
import { useIsWide } from '../kernel/ui/layout'
import { Spinner, useColors } from '../kernel/ui/ui'

/** Pantallas que se ven sin sesión. */
const PUBLIC_PATHS = ['/login', '/mfa', '/confirm-browser', '/accept-invite']

export default function RootLayout() {
  const [ready, setReady] = useState(false)

  useEffect(() => {
    void Promise.all([initLang(), initTheme(), loadSession()]).then(() => setReady(true))
  }, [])

  return (
    <SafeAreaProvider>
      <AppLockProvider>
        <StatusBar style="light" />
        {ready ? <Gate /> : <Spinner />}
      </AppLockProvider>
    </SafeAreaProvider>
  )
}

function Gate() {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const pathname = usePathname()
  const { session } = useSessionState()
  const { locked } = useAppLock()
  const legalRequired = useLegalRequired()
  const consignee = useIsConsignee()
  const [, setTick] = useState(0)

  // la sesión no se pudo renovar: de vuelta al login
  useEffect(() => {
    setAuthLostHandler(() => setTick((n) => n + 1))
    return () => setAuthLostHandler(null)
  }, [])

  // con sesión el portal se viste con los colores y el logo de la compañía; sin sesión vuelve a la marca de siempre (2026-10-22)
  const wide = useIsWide()
  const signedIn = !!session
  useEffect(() => {
    if (signedIn) void loadPortalBrand()
    else clearPortalBrand()
  }, [signedIn])

  // con sesión se pregunta si falta aceptar el documento legal; sin sesión se olvida
  useEffect(() => {
    if (!session) {
      setLegalRequired(null)
      return
    }
    let alive = true
    getLegalStatus()
      .then((s) => alive && setLegalRequired(!!s.required && !s.accepted))
      .catch(() => alive && setLegalRequired(false))   // si no se puede leer, el servidor igual lo exige con 403
    return () => {
      alive = false
    }
  }, [session])

  useEffect(() => {
    // «¿Cómo va mi orden?» es pública: con o sin sesión se queda donde está
    if (pathname.startsWith('/track') || pathname.startsWith('/unsubscribe') || pathname === '/legal-view') return   // páginas públicas: seguimiento por enlace y baja del consignatario, y la política de privacidad / términos (los enlaces del pie, con o sin sesión)
    const isPublic = PUBLIC_PATHS.includes(pathname)
    if (!session) {
      if (!isPublic) router.replace('/login')
      return
    }
    if (locked) {
      if (pathname !== '/lock') router.replace('/lock')
      return
    }
    if (legalRequired === true) {
      if (pathname !== '/legal') router.replace('/legal')
      return
    }
    if (legalRequired === false && (isPublic || pathname === '/lock' || pathname === '/legal' || pathname === '/')) router.replace(consignee ? '/notices' : '/home')   // el consignatario con acceso propio empieza en sus avisos
  }, [session, locked, legalRequired, pathname, router, consignee])

  // Kemi (Lote 218): con sesión de una persona del cliente (no del consignatario), con el documento legal aceptado y sin el bloqueo de la huella
  const kemiOn = !!session && !consignee && legalRequired === false && !locked

  return (
    <KemiProvider>
    <View style={{ flex: 1, backgroundColor: colors.bg }}>
      <EnvironmentBanner />
      {!!session && (wide ? <TopBar /> : <BrandBar />)}
      <Stack screenOptions={{ headerShown: false, headerStyle: { backgroundColor: colors.brand }, headerTintColor: colors.onBrand, headerBackTitle: t('common.back') }}>
        <Stack.Screen name="(tabs)" />
        <Stack.Screen name="order/[id]" options={{ headerShown: !wide }} />
        <Stack.Screen name="order/new" options={{ headerShown: !wide, title: t('newOrder.title') }} />
        <Stack.Screen name="profile" options={{ headerShown: !wide, title: t('profile.title') }} />
        <Stack.Screen name="invoices" options={{ headerShown: !wide, title: t('invoices.title') }} />
        <Stack.Screen name="invoice/[id]" options={{ headerShown: !wide }} />
        <Stack.Screen name="notice-prefs" options={{ headerShown: !wide, title: t('noticePrefs.title') }} />
        <Stack.Screen name="consignee-notices" options={{ headerShown: !wide, title: t('consigneeNotices.title') }} />
        <Stack.Screen name="consignee-messages/index" options={{ headerShown: !wide, title: t('consigneeMessages.title') }} />
        <Stack.Screen name="consignee-messages/[id]" options={{ headerShown: !wide, title: t('consigneeMessages.title') }} />
      </Stack>
      <EnvironmentFooter />
      {!!session && legalRequired === null && (
        <View style={{ position: 'absolute', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: colors.bg, justifyContent: 'center' }}>
          <Spinner />
        </View>
      )}
      {kemiOn && <KemiFloating />}
      {kemiOn && !wide && <KemiSleepButton floating />}
    </View>
    </KemiProvider>
  )
}
