import { AndroidConfig, type ConfigPlugin, withAndroidManifest } from 'expo/config-plugins'
import type { ExpoConfig } from 'expo/config'

// Lote 8A-app — build 'development' (docs/mobile/app-almacen-plan.md §8): permite que el aparato apunte a un API por
// HTTP plano dentro de la red local (`http://<IP>:5000/`) sin certificado. La build 'production' nunca aplica este
// plugin y exige HTTPS (comportamiento por defecto de Android desde API 28). Inline en este archivo (no en plugins/)
// porque el cargador de configuración de Expo transpila app.config.ts pero no sigue imports .ts hacia otros archivos.
const withCleartextTraffic: ConfigPlugin = (config) =>
  withAndroidManifest(config, (mod) => {
    const application = AndroidConfig.Manifest.getMainApplicationOrThrow(mod.modResults)
    application.$['android:usesCleartextTraffic'] = 'true'
    return mod
  })

// Variante de compilación: 'development' (APK instalable directo, admite HTTP sin cifrar) o 'production' (exige HTTPS).
const variant = process.env.APP_VARIANT === 'production' ? 'production' : 'development'

// Lote F8a (P8) — marca Teikem. Los PNG de assets/ los genera scripts/brand-icons.mjs (`npm run brand:icons`) desde
// Logos/teikem-symbol.svg; no se editan a mano. Azul institucional de la marca:
const brandBlue = '#0B2C66'
// Splash nativo: en este SDK ya no hay clave `splash` de nivel superior en ExpoConfig; lo configura el plugin
// expo-splash-screen (lista `plugins` de abajo).
const splash = { image: './assets/splash-icon.png', backgroundColor: brandBlue, resizeMode: 'contain' as const }

const config: ExpoConfig = {
  name: variant === 'production' ? 'Teikem Almacén' : 'Teikem Almacén (dev)',
  slug: 'teikem-almacen',
  version: '1.0.0',
  orientation: 'portrait',
  userInterfaceStyle: 'automatic',
  scheme: 'teikem-almacen',
  icon: './assets/icon.png',
  android: {
    package: 'com.teikem.almacen',
    // Android solo deja instalar ENCIMA (sin perder la configuración del aparato) una versión con versionCode MAYOR que la instalada y firmada con la
    // misma llave. scripts/construir-apk.ps1 lo sube solo en cada compilación (TEIKEM_VERSION_CODE).
    versionCode: Number.parseInt(process.env.TEIKEM_VERSION_CODE ?? '', 10) || 1,
    adaptiveIcon: {
      backgroundColor: brandBlue,
      foregroundImage: './assets/android-icon-foreground.png',
      backgroundImage: './assets/android-icon-background.png',
      monochromeImage: './assets/android-icon-monochrome.png',
    },
    permissions: ['CAMERA'],
  },
  web: {
    favicon: './assets/favicon.png',
  },
  plugins: [
    'expo-router',
    ['expo-splash-screen', { ...splash, imageWidth: 160 }],
    'expo-sqlite',
    'expo-secure-store',
    ['expo-camera', { cameraPermission: 'Teikem Almacén necesita la cámara para escanear códigos sin lector.' }],
    './plugins/withDataWedge',
    './plugins/withAndroidReleaseSigning',
  ],
  extra: {
    appVariant: variant,
  },
}

export default variant === 'development' ? withCleartextTraffic(config) : config
