// Teléfono con la máscara de la compañía (`Tenant.PhoneMask`, cada `#` es un dígito; Puerto Rico: "(###) ###-####") y su
// código de país (`PhoneCountryCode`, "+1"). Mismo criterio que web-app/src/kernel/format/phone.ts: se GUARDAN solo los
// dígitos y se MUESTRAN con la máscara; un número que no calza con la máscara se muestra tal cual se guardó. Lógica pura.
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

function fillMask(digits: string, mask: string): string {
  let out = ''
  let i = 0
  for (const ch of mask) {
    if (i >= digits.length) break
    out += ch === '#' ? digits[i++] : ch
  }
  return out
}

/** Teléfono para mostrar: con los dígitos exactos de la máscara, con máscara; si no, tal cual se guardó ('' sin valor). */
export function formatPhone(v: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  if (!v) return ''
  const d = phoneLocalDigits(v, s)
  return d.length === maskDigitCount(s.phoneMask) ? fillMask(d, s.phoneMask) : String(v)
}

/** Valor a guardar: si calza con la máscara (con o sin código de país), solo sus dígitos; si no, tal cual (recortado). */
export function normalizePhone(v: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const text = String(v ?? '').trim()
  const d = phoneLocalDigits(text, s)
  return d.length > 0 && d.length === maskDigitCount(s.phoneMask) ? d : text
}
