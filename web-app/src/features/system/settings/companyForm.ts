// Formulario de la compañía (Ajustes → General y alta de compañía nueva): valores, validación del alta y armado de lo que se manda al API (2026-10-26).
// El domicilio y la dirección postal son localizaciones de la compañía (tipos COMPANY / COMPANY_POSTAL); lo que se manda es TenantAddressInput.
import { z } from 'zod'
import type { components } from '../../../kernel/api/schema'

export type TenantAddressDto = components['schemas']['TenantAddressDto']
export type TenantAddressInput = components['schemas']['TenantAddressInput']

export const LINE_MAX = 200
export const CITY_MAX = 100
export const STATE_MAX = 100
export const POSTAL_MAX = 20
export const NAME_MAX = 200
export const PHONE_MAX = 30
export const DEFAULT_COUNTRY = 'PR'

export interface AddressValues {
  line1: string
  line2: string
  city: string
  state: string
  postalCode: string
  country: string
}

export const emptyAddress = (): AddressValues => ({ line1: '', line2: '', city: '', state: '', postalCode: '', country: DEFAULT_COUNTRY })

export const addressValuesOf = (a: TenantAddressDto | null | undefined): AddressValues =>
  a ? { line1: a.line1, line2: a.line2 ?? '', city: a.city, state: a.state ?? '', postalCode: a.postalCode ?? '', country: a.country || DEFAULT_COUNTRY } : emptyAddress()

/** ¿Escribió algo en la dirección? (el país por omisión no cuenta) */
export const addressHasContent = (v: AddressValues): boolean => [v.line1, v.line2, v.city, v.state, v.postalCode].some((s) => s.trim() !== '')

export const addressInputOf = (v: AddressValues): TenantAddressInput => ({
  line1: v.line1.trim(),
  line2: v.line2.trim() === '' ? null : v.line2.trim(),
  city: v.city.trim(),
  state: v.state.trim() === '' ? null : v.state.trim(),
  postalCode: v.postalCode.trim() === '' ? null : v.postalCode.trim(),
  country: v.country || null,
})

export interface CompanyMessages {
  nameRequired: string
  phoneRequired: string
  line1Required: string
  cityRequired: string
  max: (max: number) => string
}

const address = (m: CompanyMessages) =>
  z.object({
    line1: z.string().trim().min(1, m.line1Required).max(LINE_MAX, m.max(LINE_MAX)),
    line2: z.string().trim().max(LINE_MAX, m.max(LINE_MAX)),
    city: z.string().trim().min(1, m.cityRequired).max(CITY_MAX, m.max(CITY_MAX)),
    state: z.string().trim().max(STATE_MAX, m.max(STATE_MAX)),
    postalCode: z.string().trim().max(POSTAL_MAX, m.max(POSTAL_MAX)),
    country: z.string(),
  })

/** Alta de compañía nueva: nombre, teléfono y domicilio obligatorios; la dirección postal solo si no es la misma; dominio y subdominio opcionales (el API los valida). */
export function newCompanySchema(m: CompanyMessages) {
  return z
    .object({
      name: z.string().trim().min(1, m.nameRequired).max(NAME_MAX, m.max(NAME_MAX)),
      phoneNumber: z.string().trim().min(1, m.phoneRequired).max(PHONE_MAX, m.max(PHONE_MAX)),
      address: address(m),
      postalSame: z.boolean(),
      postalAddress: z.object({ line1: z.string(), line2: z.string(), city: z.string(), state: z.string(), postalCode: z.string(), country: z.string() }),
      customDomain: z.string().trim(),
      subdomain: z.string().trim(),
    })
    .superRefine((v, ctx) => {
      if (v.postalSame) return
      const r = address(m).safeParse(v.postalAddress)
      if (!r.success) for (const issue of r.error.issues) ctx.addIssue({ ...issue, path: ['postalAddress', ...issue.path] })
    })
}

export type NewCompanyValues = z.infer<ReturnType<typeof newCompanySchema>>

export const emptyNewCompany = (): NewCompanyValues => ({ name: '', phoneNumber: '', address: emptyAddress(), postalSame: true, postalAddress: emptyAddress(), customDomain: '', subdomain: '' })

export function buildProvisionRequest(v: NewCompanyValues): components['schemas']['TenantProvisionRequest'] {
  return {
    name: v.name.trim(),
    phoneNumber: v.phoneNumber.trim(),
    address: addressInputOf(v.address),
    postalAddress: v.postalSame ? undefined : addressInputOf(v.postalAddress),
    customDomain: v.customDomain.trim() || null,
    subdomain: v.subdomain.trim() || null,
  }
}
