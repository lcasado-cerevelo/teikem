import { AndroidConfig, type ConfigPlugin, withAndroidManifest } from 'expo/config-plugins'
import type { ExpoConfig } from 'expo/config'

// Lote 102 — app del cliente (portal de clientes, docs/p10-portal-diseno.md). Misma marca Teikem que la app de almacén (los PNG de assets/ son los de la marca; no se editan a mano).
// Variantes: 'development' (APK instalable directo, admite HTTP sin cifrar dentro de la red local), 'staging' y 'test' (HTTPS, otro paquete, nombre e
// icono para instalarse al lado; 2026-10-19: test apunta a test.advancelogisticspr.com) y 'production' (HTTPS).
type Variant = 'development' | 'production' | 'staging' | 'test'
const variant: Variant = process.env.APP_VARIANT === 'production' ? 'production' : process.env.APP_VARIANT === 'staging' ? 'staging' : process.env.APP_VARIANT === 'test' ? 'test' : 'development'
const staging = variant === 'staging'
const test = variant === 'test'
const envTag = staging ? 'staging' : test ? 'test' : null
// Iconos: el logo de Advance (de app-movil/assets/brands/advance); staging y test le agregan una franja morada STAGING / verde TEST.
const icons = envTag ? `./assets/${envTag}` : './assets'
const brandBlue = '#0B2C66'
// Icono de instalación: el logo de Advance Logistics (2026-10-26); el fondo del icono adaptable es blanco en todas las variantes (las de staging/test llevan su franja de color)
const envBackground = '#FFFFFF'

const withCleartextTraffic: ConfigPlugin = (config) =>
  withAndroidManifest(config, (mod) => {
    const application = AndroidConfig.Manifest.getMainApplicationOrThrow(mod.modResults)
    application.$['android:usesCleartextTraffic'] = 'true'
    return mod
  })

const config: ExpoConfig = {
  name: variant === 'production' ? 'Teikem Portal' : staging ? 'Teikem Portal STAGING' : test ? 'Teikem Portal TEST' : 'Teikem Portal (dev)',
  slug: 'teikem-cliente',
  version: process.env.TEIKEM_APP_VERSION || '1.0.0',
  orientation: 'portrait',
  userInterfaceStyle: 'automatic',
  scheme: envTag ? `teikem-cliente-${envTag}` : 'teikem-cliente',
  icon: `${icons}/icon.png`,
  android: {
    package: envTag ? `com.teikem.cliente.${envTag}` : 'com.teikem.cliente',
    versionCode: Number.parseInt(process.env.TEIKEM_VERSION_CODE ?? '', 10) || 1,
    adaptiveIcon: {
      backgroundColor: envBackground,
      foregroundImage: `${icons}/android-icon-foreground.png`,
      backgroundImage: `${icons}/android-icon-background.png`,
    },
    // la huella: expo-local-authentication agrega USE_BIOMETRIC / USE_FINGERPRINT
  },
  ios: { bundleIdentifier: envTag ? `com.teikem.cliente.${envTag}` : 'com.teikem.cliente', supportsTablet: true },
  web: {
    bundler: 'metro',
    output: 'single',
    favicon: './assets/favicon.png',
    name: envTag ? `Teikem Portal ${envTag.toUpperCase()}` : 'Teikem Portal',
    shortName: 'Teikem',
    themeColor: brandBlue,
    backgroundColor: '#F1F3F8',
  },
  plugins: [
    'expo-router',
    ['expo-splash-screen', { image: './assets/splash-icon.png', backgroundColor: '#FFFFFF', resizeMode: 'contain', imageWidth: 160 }],
    'expo-secure-store',
    'expo-sharing',
    ['expo-local-authentication', { faceIDPermission: 'Teikem Portal usa Face ID para abrir el portal sin escribir su contraseña.' }],
    // Kemi (Lote 218): hablarle en vez de escribirle usa el micrófono y el reconocimiento de voz del aparato (en el navegador, el del propio navegador)
    ['expo-speech-recognition', { microphonePermission: 'Teikem Portal usa el micrófono para que le pregunte a Kemi hablando.', speechRecognitionPermission: 'Teikem Portal usa el reconocimiento de voz para entender lo que le pregunta a Kemi.', androidSpeechServicePackages: ['com.google.android.googlequicksearchbox'] }],
    // APK firmado con la llave propia (TEIKEM_RELEASE_KEYSTORE_*), igual que la app del almacén; sin las variables cae a la llave de depuración
    './plugins/withAndroidReleaseSigning',
  ],
  extra: {
    appVariant: variant,
    // URL del API de esta compilación (también se puede fijar con EXPO_PUBLIC_API_URL al arrancar Metro)
    apiUrl: process.env.TEIKEM_API_URL ?? undefined,
    // Identifica esta compilación (fecha y commit) para saber qué APK tiene instalado el aparato
    buildStamp: process.env.TEIKEM_BUILD_STAMP ?? 'local',
  },
}

export default variant === 'development' ? withCleartextTraffic(config) : config
