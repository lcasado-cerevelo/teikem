// Guardado del teléfono de un consignatario (ContactPoint de la localización), compartido por el modal de alta/edición y el formulario en
// línea de la captura de envíos (Lote 213). Solo con `contacts.manage` + `locations.update`. El teléfono va aparte del consignatario:
// un fallo aquí no deshace el consignatario ya guardado, se devuelve el título del problema para avisarlo.
import { applyProblemDetails } from '../../kernel/api/problem'
import { useCan } from '../../kernel/access'
import { callingCodeDigits, useFormat } from '../../kernel/format'
import { useSaveConsigneePhone } from './api'
import { PHONE_TYPE, type ContactPoint } from './consigneeRules'

export interface PhoneValues {
  /** Teléfono tal como se ve en el campo (con la máscara de la compañía). */
  phone: string
  /** Prefijo internacional con «+» ('' = el de la compañía). */
  countryCallingCode: string
}

export function useConsigneePhoneSync() {
  const f = useFormat()
  const canPhone = useCan('contacts.manage', 'locations.update')
  const savePhone = useSaveConsigneePhone()

  /** Crea, cambia o quita el teléfono según lo escrito frente a `current`. Devuelve el título del problema, o null si todo bien (o nada que hacer). */
  async function sync(locationId: number | null | undefined, v: PhoneValues, current: ContactPoint | undefined): Promise<string | null> {
    if (!canPhone || locationId == null) return null
    const next = f.normalizePhoneFor(v.phone, v.countryCallingCode)
    const nextCc = callingCodeDigits(v.countryCallingCode) || null
    const was = f.normalizePhoneFor(current?.value ?? '', current?.countryCallingCode)
    if (next === was && (next === '' || nextCc === (current?.countryCallingCode ?? null))) return null
    try {
      if (current && next === '') await savePhone.mutateAsync({ action: 'remove', id: current.id ?? 0, locationId })
      else if (current)
        await savePhone.mutateAsync({
          action: 'update',
          id: current.id ?? 0,
          locationId,
          body: { contactType: current.contactType ?? PHONE_TYPE, value: next, countryCallingCode: nextCc, extension: current.extension ?? null, label: current.label ?? null, isPrimary: current.isPrimary ?? true },
        })
      else await savePhone.mutateAsync({ action: 'create', locationId, body: { contactType: PHONE_TYPE, value: next, countryCallingCode: nextCc, isPrimary: true } })
      return null
    } catch (err) {
      return applyProblemDetails(err).title
    }
  }

  return { canPhone, sync }
}
