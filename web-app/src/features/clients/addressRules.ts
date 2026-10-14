// Panel «Direcciones» de la ficha del cliente: reglas puras. Una dirección del cliente es una localización (dbo.Location) de tipo
// CORPORATE (la física, una sola), BILLING (la postal, una sola; si no hay, la postal es la física) o PICKUP (puntos de recogida, los que
// quiera). El punto de recogido por defecto es cualquiera de ellas (la física = sin elegir otro).
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']

export type AddressKind = 'physical' | 'postal' | 'pickup'
export type ClientAddress = Schemas['ClientAddressDto']

export const ADDRESS_TYPE: Record<AddressKind, string> = { physical: 'CORPORATE', postal: 'BILLING', pickup: 'PICKUP' }
export const NAME_MAX = 200
export const LINE_MAX = 200
export const CITY_MAX = 100
export const STATE_MAX = 100
export const POSTAL_MAX = 20
/** Valor del selector «Se recoge en» para la dirección física (= sin punto elegido: el servidor recoge en la corporativa). */
export const PICKUP_PHYSICAL = 'physical'
export const DEFAULT_COUNTRY = 'PR'

export interface AddressValues {
  name: string
  line1: string
  line2: string
  city: string
  state: string
  postalCode: string
  country: string
}

export function emptyAddressValues(): AddressValues {
  return { name: '', line1: '', line2: '', city: '', state: '', postalCode: '', country: DEFAULT_COUNTRY }
}

export function addressValuesOf(a: ClientAddress): AddressValues {
  return {
    name: a.name ?? '',
    line1: a.line1 ?? '',
    line2: a.line2 ?? '',
    city: a.city ?? '',
    state: a.state ?? '',
    postalCode: a.postalCode ?? '',
    country: a.country ?? DEFAULT_COUNTRY,
  }
}

/** Nombre interno de la localización cuando la dirección no lleva uno propio (la física y la postal). */
export function defaultAddressName(kind: AddressKind, clientName: string): string {
  const base = kind === 'physical' ? 'Dirección física' : 'Dirección postal'
  return `${base} — ${clientName}`.slice(0, NAME_MAX)
}

export interface AddressMessages {
  nameRequired: string
  line1Required: string
  cityRequired: string
  countryRequired: string
  max: (max: number) => string
}

/** Esquema del formulario; el nombre solo se exige en los puntos de recogida (la física y la postal usan uno automático). */
export function addressSchema(m: AddressMessages, needsName: boolean) {
  const text = (max: number) => z.string().max(max, m.max(max))
  return z.object({
    name: needsName ? z.string().trim().min(1, m.nameRequired).max(NAME_MAX, m.max(NAME_MAX)) : text(NAME_MAX),
    line1: z.string().trim().min(1, m.line1Required).max(LINE_MAX, m.max(LINE_MAX)),
    line2: text(LINE_MAX),
    city: z.string().trim().min(1, m.cityRequired).max(CITY_MAX, m.max(CITY_MAX)),
    state: text(STATE_MAX),
    postalCode: text(POSTAL_MAX),
    country: z.string().trim().min(1, m.countryRequired),
  })
}

const orNull = (s: string) => (s.trim() === '' ? null : s.trim())

/** Request de alta de la localización (POST /locations). */
export function buildAddressCreate(kind: AddressKind, clientPublicId: string, clientName: string, v: AddressValues): Schemas['LocationUpsertRequest'] {
  return {
    clientPublicId,
    locationType: ADDRESS_TYPE[kind],
    name: kind === 'pickup' ? v.name.trim() : defaultAddressName(kind, clientName),
    line1: v.line1.trim(),
    line2: orNull(v.line2),
    city: v.city.trim(),
    state: orNull(v.state),
    postalCode: orNull(v.postalCode),
    country: v.country.trim(),
  }
}

/** PATCH solo con lo que cambió (el nombre de la física y la postal no se toca). */
export function buildAddressPatch(kind: AddressKind, v: AddressValues, original: AddressValues): { request: Schemas['LocationPatchRequest']; changed: boolean } {
  const request: Schemas['LocationPatchRequest'] = {}
  if (kind === 'pickup' && v.name.trim() !== original.name.trim()) request.name = v.name.trim()
  if (v.line1.trim() !== original.line1.trim()) request.line1 = v.line1.trim()
  if (v.line2.trim() !== original.line2.trim()) request.line2 = v.line2.trim()
  if (v.city.trim() !== original.city.trim()) request.city = v.city.trim()
  if (v.state.trim() !== original.state.trim()) request.state = v.state.trim()
  if (v.postalCode.trim() !== original.postalCode.trim()) request.postalCode = v.postalCode.trim()
  if (v.country.trim() !== original.country.trim()) request.country = v.country.trim()
  return { request, changed: Object.keys(request).length > 0 }
}

/** Dirección en una línea para resúmenes: «Calle 1, Caguas, PR 00725». */
export function shortAddress(a: ClientAddress | null | undefined): string {
  if (!a) return ''
  const cityState = [a.city, [a.state, a.postalCode].filter(Boolean).join(' ')].filter(Boolean).join(', ')
  return [a.line1, cityState].filter(Boolean).join(', ')
}

export interface PickupOption {
  value: string
  kind: AddressKind
  label: string
}

/**
 * Opciones de «Se recoge en»: la física (siempre; es el valor por omisión), la postal si es una dirección propia (si no, es la
 * física) y cada punto de recogida. `labels` trae los textos ya traducidos.
 */
export function pickupOptions(
  client: { postalAddress?: ClientAddress | null; pickupLocations?: ClientAddress[] | null },
  labels: { physical: string; postal: string },
): PickupOption[] {
  const list: PickupOption[] = [{ value: PICKUP_PHYSICAL, kind: 'physical', label: labels.physical }]
  if (client.postalAddress?.publicId) list.push({ value: client.postalAddress.publicId, kind: 'postal', label: labels.postal })
  for (const p of client.pickupLocations ?? []) {
    if (p.publicId) list.push({ value: p.publicId, kind: 'pickup', label: [p.name, shortAddress(p)].filter(Boolean).join(' — ') })
  }
  return list
}

/** Valor del selector según la ficha: la física si recoge en la corporativa (o no hay punto elegido). */
export function currentPickupValue(client: { pickupAddress?: { publicId?: string; isDefaultFromCorporate?: boolean } | null }): string {
  const p = client.pickupAddress
  return p?.publicId && !p.isDefaultFromCorporate ? p.publicId : PICKUP_PHYSICAL
}
