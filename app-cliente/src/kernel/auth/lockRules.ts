// Reglas puras del bloqueo con huella: la app se bloquea si estuvo en segundo plano más de LOCK_AFTER_MINUTES (y al arrancar con una sesión guardada), siempre que «Abrir con huella» esté activo.

export const LOCK_AFTER_MINUTES = 2
/** Intentos fallidos de huella seguidos antes de pedir la contraseña de nuevo (se cierra la sesión del aparato). */
export const MAX_BIOMETRIC_FAILURES = 5

/** ¿Hay que bloquear al volver a primer plano? `backgroundedAt` = cuándo salió de primer plano (null = nunca salió). */
export function shouldLock(enabled: boolean, backgroundedAt: number | null, now: number, afterMinutes = LOCK_AFTER_MINUTES): boolean {
  if (!enabled || backgroundedAt === null) return false
  return now - backgroundedAt >= afterMinutes * 60_000
}

/** Qué puede hacer el aparato con la huella: no hay lector, hay lector sin huellas registradas, o listo. */
export type BiometricSupport = 'unavailable' | 'not_enrolled' | 'ready'

export function biometricSupport(hasHardware: boolean, isEnrolled: boolean): BiometricSupport {
  if (!hasHardware) return 'unavailable'
  return isEnrolled ? 'ready' : 'not_enrolled'
}
