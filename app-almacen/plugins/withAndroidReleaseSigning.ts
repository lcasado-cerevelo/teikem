import { type ConfigPlugin, withAppBuildGradle } from 'expo/config-plugins'

// Lote 8A-app — firma del APK con la llave propia del repositorio (decisión de Luis, 2026-09-28: llave propia, no EAS).
// El `android/` que genera `expo prebuild` trae por defecto un `signingConfigs.release` que apunta a la llave de
// depuración (comentario "Caution! In production, you need to generate your own keystore file."); este plugin agrega
// un `signingConfigs.release` de verdad que lee la llave y las contraseñas de variables de entorno (nunca del
// repositorio): en CI, el job `android` decodifica el secreto `TEIKEM_RELEASE_KEYSTORE_B64` a `android/app/
// release.keystore` después de este prebuild y exporta las demás. Sin esas variables (una compilación local sin la
// llave) el build de release cae de vuelta a la llave de depuración en vez de fallar.
const DEBUG_SIGNING_BLOCK_END = `            keyPassword 'android'
        }
    }`

const DEBUG_SIGNING_WITH_RELEASE = `            keyPassword 'android'
        }
        release {
            if (System.getenv('TEIKEM_RELEASE_KEYSTORE')) {
                storeFile file(System.getenv('TEIKEM_RELEASE_KEYSTORE'))
                storePassword System.getenv('TEIKEM_RELEASE_KEYSTORE_PASSWORD')
                keyAlias System.getenv('TEIKEM_RELEASE_KEY_ALIAS')
                keyPassword System.getenv('TEIKEM_RELEASE_KEY_PASSWORD')
            }
        }
    }`

// Único en el archivo (el comentario solo aparece en el buildType release generado por la plantilla de Expo).
const RELEASE_BUILD_TYPE_SIGNING = `            // Caution! In production, you need to generate your own keystore file.
            // see https://reactnative.dev/docs/signed-apk-android.
            signingConfig signingConfigs.debug`

const RELEASE_BUILD_TYPE_SIGNING_FIXED = `            // Firma con la llave propia (TEIKEM_RELEASE_KEYSTORE_*); sin esas variables, cae a la llave de depuración.
            signingConfig System.getenv('TEIKEM_RELEASE_KEYSTORE') ? signingConfigs.release : signingConfigs.debug`

export const withAndroidReleaseSigning: ConfigPlugin = (config) =>
  withAppBuildGradle(config, (mod) => {
    if (!mod.modResults.contents.includes(DEBUG_SIGNING_BLOCK_END)) {
      throw new Error(
        'withAndroidReleaseSigning: no se encontró el bloque signingConfigs.debug esperado en app/build.gradle; ' +
          'revisar si la plantilla de Expo cambió antes de seguir (docs/lote8A-app-decisiones.md).',
      )
    }
    if (!mod.modResults.contents.includes(RELEASE_BUILD_TYPE_SIGNING)) {
      throw new Error(
        'withAndroidReleaseSigning: no se encontró el signingConfig del buildType release esperado en app/build.gradle.',
      )
    }
    mod.modResults.contents = mod.modResults.contents
      .replace(DEBUG_SIGNING_BLOCK_END, DEBUG_SIGNING_WITH_RELEASE)
      .replace(RELEASE_BUILD_TYPE_SIGNING, RELEASE_BUILD_TYPE_SIGNING_FIXED)
    return mod
  })

export default withAndroidReleaseSigning
