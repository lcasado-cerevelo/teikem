import { AndroidConfig, type ConfigPlugin, withAndroidManifest } from 'expo/config-plugins'
import type { ExpoConfig } from 'expo/config'

// Lote 102 — app del cliente (portal de clientes, docs/p10-portal-diseno.md). Misma marca Teikem que la app de almacén (los PNG de assets/ son los de la marca; no se editan a mano).
// Variantes: 'development' (APK instalable directo, admite HTTP sin cifrar dentro de la red local), 'staging' (HTTPS, otro paquete y nombre para instalarse al lado) y 'production' (HTTPS).
const variant = process.env.APP_VARIANT === 'production' ? 'production' : process.env.APP_VARIANT === 'staging' ? 'staging' : 'development'
const staging = variant === 'staging'
const brandBlue = '#0B2C66'

const withCleartextTraffic: ConfigPlugin = (config) =>
  withAndroidManifest(config, (mod) => {
    const application = AndroidConfig.Manifest.getMainApplicationOrThrow(mod.modResults)
    application.$['android:usesCleartextTraffic'] = 'true'
    return mod
  })

const config: ExpoConfig = {
  name: variant === 'production' ? 'Teikem Clientes' : staging ? 'Teikem Clientes STAGING' : 'Teikem Clientes (dev)',
  slug: 'teikem-cliente',
  version: process.env.TEIKEM_APP_VERSION || '1.0.0',
  orientation: 'portrait',
  userInterfaceStyle: 'automatic',
  scheme: staging ? 'teikem-cliente-staging' : 'teikem-cliente',
  icon: './assets/icon.png',
  android: {
    package: staging ? 'com.teikem.cliente.staging' : 'com.teikem.cliente',
    versionCode: Number.parseInt(process.env.TEIKEM_VERSION_CODE ?? '', 10) || 1,
    adaptiveIcon: {
      backgroundColor: brandBlue,
      foregroundImage: './assets/android-icon-foreground.png',
      backgroundImage: './assets/android-icon-background.png',
      monochromeImage: './assets/android-icon-monochrome.png',
    },
    // la huella: expo-local-authentication agrega USE_BIOMETRIC / USE_FINGERPRINT
  },
  ios: { bundleIdentifier: staging ? 'com.teikem.cliente.staging' : 'com.teikem.cliente', supportsTablet: true },
  web: {
    bundler: 'metro',
    output: 'single',
    favicon: './assets/favicon.png',
    name: 'Teikem Clientes',
    shortName: 'Teikem',
    themeColor: brandBlue,
    backgroundColor: '#F1F3F8',
  },
  plugins: [
    'expo-router',
    ['expo-splash-screen', { image: './assets/splash-icon.png', backgroundColor: brandBlue, resizeMode: 'contain', imageWidth: 160 }],
    'expo-secure-store',
    ['expo-local-authentication', { faceIDPermission: 'Teikem Clientes usa Face ID para abrir el portal sin escribir su contraseña.' }],
  ],
  extra: {
    appVariant: variant,
    // URL del API de esta compilación (también se puede fijar con EXPO_PUBLIC_API_URL al arrancar Metro)
    apiUrl: process.env.TEIKEM_API_URL ?? undefined,
  },
}

export default variant === 'development' ? withCleartextTraffic(config) : config
