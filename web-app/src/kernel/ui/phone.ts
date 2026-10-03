// Teléfono con la máscara de la COMPAÑÍA (Región y formatos, lote F9; Puerto Rico: "(###) ###-####"). La lógica vive en
// `kernel/format/phone.ts`; aquí se reexporta para el kit (`PhoneInput` y las pantallas).
export {
  formatPhone,
  formatPhoneInput,
  isValidPhone,
  maskDigitCount,
  normalizePhone,
  phoneDigits,
  phoneLocalDigits,
  phonePlaceholder,
} from '../format/phone'
export { formatPhone as normalizeStoredPhone } from '../format/phone'
