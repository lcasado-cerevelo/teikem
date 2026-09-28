// Reglas puras del PIN, espejo de src/Teikem.Domain/Security/PinRules.cs (Lote 8A). Solo para dar el mensaje al instante
// en pantalla; el servidor vuelve a validar en /auth/device/login y es quien manda.
export const PIN_MIN_LENGTH = 4
export const PIN_MAX_LENGTH = 6

function isTrivial(digits: string): boolean {
  if (digits.length < 2) return true
  if ([...digits].every((c) => c === digits[0])) return true
  return hasStep(digits, 1) || hasStep(digits, -1)
}

function hasStep(p: string, step: number): boolean {
  for (let i = 1; i < p.length; i += 1) {
    if (p.charCodeAt(i) - p.charCodeAt(i - 1) !== step) return false
  }
  return true
}

/** Mensaje de error (clave de i18n) del PIN, o null si es válido. */
export function validatePin(pin: string): 'format' | 'trivial' | null {
  const p = pin.trim()
  if (p.length < PIN_MIN_LENGTH || p.length > PIN_MAX_LENGTH || !/^[0-9]+$/.test(p)) return 'format'
  return isTrivial(p) ? 'trivial' : null
}
