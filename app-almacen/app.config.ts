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

// Variante de compilación: 'development' (APK instalable directo, admite HTTP sin cifrar), 'production' (exige HTTPS), 'staging'
// (2026-10-12: exige HTTPS, apunta al ambiente de pruebas y se instala AL LADO de producción: otro paquete, otro nombre y otro icono)
// o 'test' (2026-10-19: igual que staging pero apunta a test.advancelogisticspr.com, con su propio paquete, icono verde y cinta TEST).
type Variant = 'development' | 'production' | 'staging' | 'test'
const variant: Variant = process.env.APP_VARIANT === 'production' ? 'production' : process.env.APP_VARIANT === 'staging' ? 'staging' : process.env.APP_VARIANT === 'test' ? 'test' : 'development'
const staging = variant === 'staging'
const test = variant === 'test'
// Etiqueta del ambiente de pruebas (STAGING o TEST): nombre de la app, paquete, esquema e iconos la llevan.
const envTag = staging ? 'staging' : test ? 'test' : null
// Iconos de staging (fondo morado y cinta «STAGING») y de test (fondo verde y cinta «TEST»): los generan `npm run brand:icons:staging` y
// `npm run brand:icons:test`, no se editan a mano.
const icons = envTag ? `./assets/${envTag}` : './assets'

// Lote F8a (P8) — marca Teikem. Los PNG de assets/ los genera scripts/brand-icons.mjs (`npm run brand:icons`) desde
// Logos/teikem-symbol.svg; no se editan a mano. Azul institucional de la marca:
const brandBlue = '#0B2C66'
const stagingPurple = '#6D28D9'
const testGreen = '#047857'
// Splash nativo: en este SDK ya no hay clave `splash` de nivel superior en ExpoConfig; lo configura el plugin
// expo-splash-screen (lista `plugins` de abajo).
const splash = { image: './assets/splash-icon.png', backgroundColor: brandBlue, resizeMode: 'contain' as const }

const config: ExpoConfig = {
  name: variant === 'production' ? 'Teikem Almacén' : staging ? 'Teikem Almacén STAGING' : test ? 'Teikem Almacén TEST' : 'Teikem Almacén (dev)',
  slug: 'teikem-almacen',
  version: process.env.TEIKEM_APP_VERSION || '1.0.0',
  orientation: 'portrait',
  userInterfaceStyle: 'automatic',
  scheme: envTag ? `teikem-almacen-${envTag}` : 'teikem-almacen',
  icon: `${icons}/icon.png`,
  android: {
    // Staging y test llevan otro paquete (com.teikem.almacen.staging / .test): Android los trata como otra app y deja las tres instaladas a la vez.
    // El perfil de DataWedge sale del sufijo del paquete (TeikemAlmacenStaging / TeikemAlmacenTest), así que tampoco se pisan en los Zebra.
    package: envTag ? `com.teikem.almacen.${envTag}` : 'com.teikem.almacen',
    // Android solo deja instalar ENCIMA (sin perder la configuración del aparato) una versión con versionCode MAYOR que la instalada y firmada con la
    // misma llave. scripts/construir-apk.ps1 lo sube solo en cada compilación (TEIKEM_VERSION_CODE).
    versionCode: Number.parseInt(process.env.TEIKEM_VERSION_CODE ?? '', 10) || 1,
    adaptiveIcon: {
      backgroundColor: staging ? stagingPurple : test ? testGreen : brandBlue,
      foregroundImage: `${icons}/android-icon-foreground.png`,
      backgroundImage: `${icons}/android-icon-background.png`,
      monochromeImage: `${icons}/android-icon-monochrome.png`,
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
    // Identifica esta compilación (fecha y commit); Inicio la muestra al pie para saber qué APK tiene instalado el aparato
    buildStamp: process.env.TEIKEM_BUILD_STAMP ?? 'local',
  },
}

export default variant === 'development' ? withCleartextTraffic(config) : config
