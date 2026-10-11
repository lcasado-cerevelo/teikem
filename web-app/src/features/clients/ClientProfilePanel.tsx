// Panel «Perfil del cliente»: nombre (solo lectura), razón social, identificación fiscal, límite de crédito, término de pago,
// moneda y punto de recogido por defecto. PATCH /profile con rowVersion (409 = otro usuario guardó antes: se muestra el
// mensaje del servidor y se relee la ficha). Las direcciones son de solo lectura hasta el bloque de Consignatarios/localizaciones.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useId, useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { useLookups, type LookupOption } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Field, Form, NumberInput, Panel, Select, TextInput, toast } from '../../kernel/ui'
import { IconUsers } from '../../kernel/ui/screenIcons'
import { clientKeys, useUpdateClientProfile } from './api'
import {
  addressLine,
  buildProfileRequest,
  LEGAL_NAME_MAX,
  profileSchema,
  profileValuesOf,
  TAX_ID_MAX,
  type ClientDetail,
} from './clientRules'

/** Opciones de un catálogo: las habilitadas más el valor actual aunque ya esté deshabilitado. */
function lookupOptions(options: readonly LookupOption[], current: string | null | undefined) {
  return options.filter((o) => o.isEnabled !== false || o.code === current).map((o) => ({ value: o.code, label: o.label }))
}

function AddressBlock({ title, text, empty }: { title: string; text: string; empty: string }) {
  return (
    <div className="cl-addr">
      <b>{title}</b>
      <span>{text || empty}</span>
    </div>
  )
}

export function ClientProfilePanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const qc = useQueryClient()
  const canEdit = useCan('clients.update')
  const nameId = useId()
  const publicId = client.publicId ?? ''
  const save = useUpdateClientProfile(publicId)
  const { data: terms = [] } = useLookups('PaymentTerm', { includeDisabled: true })
  const { data: currencies = [] } = useLookups('Currency', { includeDisabled: true })
  const original = useMemo(() => profileValuesOf(client), [client])

  const schema = useMemo(
    () =>
      profileSchema(
        {
          legalNameMax: t('clients.errors.max', { max: LEGAL_NAME_MAX }),
          taxIdMax: t('clients.errors.max', { max: TAX_ID_MAX }),
          creditLimitNumber: t('clients.errors.creditLimitNumber'),
          creditLimitMin: t('clients.errors.creditLimitMin'),
          creditLimitRequired: t('clients.errors.creditLimitRequired'),
        },
        client.creditLimit != null,
      ),
    [t, client.creditLimit],
  )
  const form = useForm({ resolver: zodResolver(schema), values: original, resetOptions: { keepDirtyValues: true } })

  const pickupOptions = useMemo(() => {
    const list = (client.pickupLocations ?? []).map((l) => ({ value: l.publicId, label: [l.code, l.name].filter(Boolean).join(' · ') }))
    // el punto actual aunque ya no esté entre los ofrecidos
    const cur = client.pickupAddress
    if (cur?.publicId && !cur.isDefaultFromCorporate && !list.some((o) => o.value === cur.publicId)) list.push({ value: cur.publicId, label: [cur.code, cur.name].filter(Boolean).join(' · ') })
    return list
  }, [client.pickupLocations, client.pickupAddress])

  const physical = addressLine(client.physicalAddress)
  const postal = addressLine(client.postalAddress)

  return (
    <Panel icon={<IconUsers />} title={t('clients.profile.title')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          const updated = await save.mutateAsync(buildProfileRequest(v, original, client.rowVersion))
          form.reset(profileValuesOf(updated))
          toast.success(t('clients.saved'))
        }}
        onError={(p) => {
          // 409: otro usuario guardó antes; se relee la ficha para que el siguiente intento lleve su rowVersion
          if (p.code === 'conflict') void qc.invalidateQueries({ queryKey: clientKeys.detail(publicId) })
        }}
      >
        <fieldset className="cl-fs" disabled={!canEdit}>
          <div className="f">
            <label htmlFor={nameId}>{t('clients.fields.name')}</label>
            <input id={nameId} value={client.name ?? ''} readOnly disabled aria-describedby={`${nameId}-help`} />
            <p id={`${nameId}-help`} className="help">
              {t('clients.profile.nameReadOnly')}
            </p>
          </div>
          <div className="r2">
            <Field name="legalName" label={t('clients.fields.legalName')}>
              <TextInput maxLength={LEGAL_NAME_MAX} />
            </Field>
            <Field name="taxId" label={t('clients.fields.taxId')}>
              <TextInput maxLength={TAX_ID_MAX} />
            </Field>
          </div>
          <div className="r3">
            <Field name="creditLimit" label={t('clients.fields.creditLimit')}>
              <NumberInput min={0} />
            </Field>
            <Field name="paymentTerm" label={t('clients.fields.paymentTerm')}>
              <Select options={lookupOptions(terms, client.paymentTerm)} placeholder={t('clients.fields.none')} />
            </Field>
            <Field name="currency" label={t('clients.fields.currency')}>
              <Select options={lookupOptions(currencies, client.currency)} placeholder={t('clients.fields.none')} />
            </Field>
          </div>
          <Field name="pickup" label={t('clients.profile.pickup')} help={t('clients.profile.pickupHelp')}>
            <Select options={pickupOptions} placeholder={t('clients.profile.pickupNone')} />
          </Field>
        </fieldset>

        <div className="cl-addrs">
          <AddressBlock title={t('clients.profile.physicalAddress')} text={physical} empty={t('clients.profile.noAddress')} />
          <AddressBlock title={t('clients.profile.postalAddress')} text={client.postalAddress ? postal : physical ? t('clients.profile.samePostal') : ''} empty={t('clients.profile.noAddress')} />
        </div>
        <p className="help">{t('clients.profile.addressesReadOnly')}</p>

        <Can perm="clients.update">
          <div className="form-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
            </button>
          </div>
        </Can>
      </Form>
    </Panel>
  )
}
