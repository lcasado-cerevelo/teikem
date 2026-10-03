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

/** Ejemplo de la máscara para el placeholder: "(000) 000-0000". */
export function phonePlaceholder(s: FormatSettings = getFormatSettings()): string {
  return s.phoneMask.replace(/#/g, '0')
}
