// Identificación de la compilación (2026-10-10): versión, código de versión de Android y sello (fecha y commit) puesto por el CI o por
// scripts/construir-apk.ps1 (TEIKEM_BUILD_STAMP). Inicio la muestra al pie para saber con certeza qué APK tiene instalado un aparato.
import Constants from 'expo-constants'

export function buildLabel(): string {
  const cfg = Constants.expoConfig
  const stamp = String((cfg?.extra as { buildStamp?: string } | undefined)?.buildStamp ?? 'local')
  const code = cfg?.android?.versionCode
  const variant = String((cfg?.extra as { appVariant?: string } | undefined)?.appVariant ?? '')
  // el APK de pruebas lo dice al pie de Inicio, para no confundirlo con el de producción
  return `${cfg?.version ?? '?'}${code != null ? ` (${code})` : ''} · ${stamp}${variant === 'staging' ? ' · STAGING' : ''}`
}
