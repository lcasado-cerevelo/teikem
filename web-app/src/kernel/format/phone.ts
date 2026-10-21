// Teléfono con la máscara de la compañía (`Tenant.PhoneMask`, cada `#` es un dígito; Puerto Rico: "(###) ###-####") y su
// código de país (`PhoneCountryCode`, "+1"). Espejo de phoneDigitsOnly/fmtPhone/normPhone/isValidPhoneFmt de la maqueta.
// Se guardan solo los dígitos y se muestran con la máscara; un número que no tiene los dígitos de la máscara se deja tal
// cual se escribió. Lógica pura.
import type { FormatSettings } from './settings'
import { getFormatSettings } from './store'

/** Solo los dígitos de un texto. */
export function phoneDigits(v: string | null | undefined): string {
  return String(v ?? '').replace(/\D/g, '')
}

/** Cuántos dígitos pide la máscara (cantidad de `#`). */
export function maskDigitCount(mask: string): number {
  return (mask.match(/#/g) ?? []).length
}

/** Dígitos del número sin el código de país (si se escribió con él y sobra exactamente ese prefijo). */
export function phoneLocalDigits(v: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const d = phoneDigits(v)
  const cc = phoneDigits(s.phoneCountryCode)
  const need = maskDigitCount(s.phoneMask)
  return cc && d.length === need + cc.length && d.startsWith(cc) ? d.slice(cc.length) : d
}

/** Pone los dígitos en la máscara hasta donde alcancen (sin los caracteres fijos que van después del último dígito). */
function fillMask(digits: string, mask: string): string {
  if (!digits) return ''
  let out = ''
  let i = 0
  for (const ch of mask) {
    if (ch === '#') {
      if (i >= digits.length) break
      out += digits[i++]
    } else {
      if (i >= digits.length) break
      out += ch
    }
  }
  return out
}

/**
 * Máscara mientras se escribe (`PhoneInput`): hasta los dígitos de la máscara, parcial mientras no se completan.
 * "78" → "(78"; "7875" → "(787) 5"; "7875551234" → "(787) 555-1234". Se reformatea desde los dígitos.
 */
export function formatPhoneInput(v: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const need = maskDigitCount(s.phoneMask)
  return fillMask(phoneLocalDigits(v, s).slice(0, need), s.phoneMask)
}

/** Teléfono para mostrar: con los dígitos exactos de la máscara, con máscara; si no, tal cual se guardó ('' sin valor). */
export function formatPhone(v: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  if (!v) return ''
  const d = phoneLocalDigits(v, s)
  return d.length === maskDigitCount(s.phoneMask) ? fillMask(d, s.phoneMask) : String(v)
}

/** Vacío o con exactamente los dígitos de la máscara (con o sin el código de país delante). */
export function isValidPhone(v: string | null | undefined, s: FormatSettings = getFormatSettings()): boolean {
  if (phoneDigits(v).length === 0) return true
  return phoneLocalDigits(v, s).length === maskDigitCount(s.phoneMask)
}

/** Valor a guardar: un número válido para la máscara, solo sus dígitos; si no calza (o vacío), tal cual se escribió (recortado). */
export function normalizePhone(v: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const text = String(v ?? '').trim()
  return text && phoneDigits(text).length > 0 && isValidPhone(text, s) ? phoneLocalDigits(text, s) : text
}

// ---- Prefijo internacional por teléfono (2026-10-20): cada número lleva el suyo (`countryCallingCode`, solo dígitos y sin «+»).
// Con el prefijo de la compañía (o sin prefijo) rige la máscara de la compañía; con otro, el número se guarda y muestra con sus dígitos
// y el prefijo delante («+593 991234567»), porque la máscara de Puerto Rico no le aplica.

/** Prefijo por omisión de la compañía en dígitos ("+1" → "1"). */
export function defaultCallingCode(s: FormatSettings = getFormatSettings()): string {
  return phoneDigits(s.phoneCountryCode)
}

/** Prefijo en dígitos para guardar ("+593" → "593"); '' si está vacío. */
export function callingCodeDigits(cc: string | null | undefined): string {
  return phoneDigits(cc)
}

/** Vacío, o de 1 a 4 dígitos que no empiezan en 0 (con o sin «+» delante). */
export function isValidCallingCode(cc: string | null | undefined): boolean {
  const t = String(cc ?? '').trim()
  if (!t) return true
  return /^\+?[1-9][0-9]{0,3}$/.test(t.replace(/\s/g, ''))
}

/** ¿El prefijo es el de la compañía (o no se puso)? Entonces rige su máscara. */
export function usesCompanyMask(cc: string | null | undefined, s: FormatSettings = getFormatSettings()): boolean {
  const d = callingCodeDigits(cc)
  return d === '' || d === defaultCallingCode(s)
}

/** Teléfono con su prefijo: con el de la compañía, la máscara; con otro, de 7 dígitos hasta completar 15 con el prefijo. */
export function isValidPhoneFor(v: string | null | undefined, cc: string | null | undefined, s: FormatSettings = getFormatSettings()): boolean {
  if (phoneDigits(v).length === 0) return true
  if (!isValidCallingCode(cc)) return false
  if (usesCompanyMask(cc, s)) return isValidPhone(v, s)
  const n = phoneDigits(v).length
  return n >= 7 && n + callingCodeDigits(cc).length <= 15
}

/** Valor a guardar según el prefijo: con el de la compañía, `normalizePhone`; con otro, solo los dígitos. */
export function normalizePhoneFor(v: string | null | undefined, cc: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  return usesCompanyMask(cc, s) ? normalizePhone(v, s) : phoneDigits(v)
}

/** Para mostrar: con el prefijo de la compañía (o sin prefijo), la máscara; con otro, «+prefijo dígitos». */
export function formatPhoneFor(v: string | null | undefined, cc: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  if (!v) return ''
  return usesCompanyMask(cc, s) ? formatPhone(v, s) : `+${callingCodeDigits(cc)} ${phoneDigits(v)}`
}

/** Prefijo a mostrar en el campo: el del número, o el de la compañía ("+1"). */
export function callingCodeInput(cc: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const d = callingCodeDigits(cc) || defaultCallingCode(s)
  return d ? `+${d}` : ''
}

/** Ejemplo de la máscara para el placeholder: "(000) 000-0000". */
export function phonePlaceholder(s: FormatSettings = getFormatSettings()): string {
  return s.phoneMask.replace(/#/g, '0')
}
