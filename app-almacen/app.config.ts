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

const config: ExpoConfig = {
  name: variant === 'production' ? 'Teikem Almacén' : 'Teikem Almacén (dev)',
  slug: 'teikem-almacen',
  version: '1.0.0',
  orientation: 'portrait',
  userInterfaceStyle: 'automatic',
  scheme: 'teikem-almacen',
  icon: './assets/icon.png',
  newArchEnabled: true,
  android: {
    package: 'com.teikem.almacen',
    versionCode: 1,
    adaptiveIcon: {
      backgroundColor: '#0B1220',
      foregroundImage: './assets/android-icon-foreground.png',
    },
    permissions: ['CAMERA'],
  },
  plugins: [
    'expo-router',
    'expo-sqlite',
    'expo-secure-store',
    ['expo-camera', { cameraPermission: 'Teikem Almacén necesita la cámara para escanear códigos sin lector.' }],
    './plugins/withDataWedge',
  ],
  extra: {
    appVariant: variant,
  },
}

export default variant === 'development' ? withCleartextTraffic(config) : config
