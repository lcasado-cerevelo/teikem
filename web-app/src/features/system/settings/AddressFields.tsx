// Campos de una dirección de la compañía (domicilio o dirección postal): se usan en Ajustes → General y en el alta de compañía nueva. `prefix` es el nombre del grupo en el
// formulario y coincide con el campo con que el API responde los errores (address.line1, postalAddress.city…), así cada mensaje sale debajo de su campo.
import { useMemo } from 'react'
import { useLookups } from '../../../kernel/catalogs'
import { useT } from '../../../kernel/i18n'
import { Field, Select, TextInput } from '../../../kernel/ui'
import { CITY_MAX, LINE_MAX, POSTAL_MAX, STATE_MAX } from './companyForm'

export function AddressFields({ prefix, required = false }: { prefix: 'address' | 'postalAddress'; required?: boolean }) {
  const t = useT()
  const { data: countries = [] } = useLookups('Country')
  const options = useMemo(() => countries.map((o) => ({ value: o.code, label: o.label })), [countries])
  return (
    <>
      <Field name={`${prefix}.line1`} label={t('system.settings.company.line1')} required={required}>
        <TextInput maxLength={LINE_MAX} autoComplete="address-line1" />
      </Field>
      <Field name={`${prefix}.line2`} label={t('system.settings.company.line2')}>
        <TextInput maxLength={LINE_MAX} autoComplete="address-line2" />
      </Field>
      <div className="r3">
        <Field name={`${prefix}.city`} label={t('system.settings.company.city')} required={required}>
          <TextInput maxLength={CITY_MAX} autoComplete="address-level2" />
        </Field>
        <Field name={`${prefix}.state`} label={t('system.settings.company.state')}>
          <TextInput maxLength={STATE_MAX} autoComplete="address-level1" />
        </Field>
        <Field name={`${prefix}.postalCode`} label={t('system.settings.company.postalCode')}>
          <TextInput maxLength={POSTAL_MAX} className="mono" autoComplete="postal-code" />
        </Field>
      </div>
      <div style={{ maxWidth: 320 }}>
        <Field name={`${prefix}.country`} label={t('system.settings.company.country')}>
          <Select options={options} />
        </Field>
      </div>
    </>
  )
}
