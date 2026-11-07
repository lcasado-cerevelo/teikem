// Panel «Perfil fiscal»: nombre (solo lectura), razón social (el nombre legal de la entidad, el de las facturas), identificación
// fiscal, límite de crédito, término de pago, moneda y tipos por omisión (lo de imprimir y lo del portal está en ClientPrintingPanel).
// PATCH /profile con rowVersion (409 = otro usuario guardó antes: se muestra el mensaje del servidor y se relee la ficha).
// Las direcciones y el punto de recogido por defecto viven en el panel «Direcciones» (ClientAddressesPanel).
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useId, useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { Can, ModuleKeys, useCan, useModule } from '../../kernel/access'
import { useLookups, type LookupOption } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Field, Form, NumberInput, Panel, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { IconIdCard } from '../../kernel/ui/screenIcons'
import { clientKeys, useUpdateClientProfile } from './api'
import {
  buildProfileRequest,
  LEGAL_NAME_MAX,
  profileSchema,
  SURI_MAX,
  profileValuesOf,
  TAX_ID_MAX,
  type ClientDetail,
} from './clientRules'

/** Opciones de un catálogo: las habilitadas más el valor actual aunque ya esté deshabilitado. */
function lookupOptions(options: readonly LookupOption[], current: string | null | undefined) {
  return options.filter((o) => o.isEnabled !== false || o.code === current).map((o) => ({ value: o.code, label: o.label }))
}

export function ClientProfilePanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const qc = useQueryClient()
  const canEdit = useCan('clients.update')
  const sales = useModule(ModuleKeys.SalesOrders)
  const nameId = useId()
  const publicId = client.publicId ?? ''
  const save = useUpdateClientProfile(publicId)
  const { data: terms = [] } = useLookups('PaymentTerm', { includeDisabled: true })
  const { data: currencies = [] } = useLookups('Currency', { includeDisabled: true })
  const { data: serviceTypes = [] } = useLookups('ServiceType', { includeDisabled: true })
  const { data: packageTypes = [] } = useLookups('PackageType', { includeDisabled: true })
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
          suriMax: t('clients.errors.max', { max: SURI_MAX }),
          shippingChargeRequired: t('clients.sales.chargeRequired'),
          shippingAmountMin: t('clients.sales.amountMin'),
        },
        client.creditLimit != null,
      ),
    [t, client.creditLimit],
  )
  const form = useForm({ resolver: zodResolver(schema), values: original, resetOptions: { keepDirtyValues: true } })
  const shippingMode = form.watch('shippingMode')

  return (
    <Panel
      icon={<IconIdCard />}
      title={t('clients.profile.title')}
      collapsible
      defaultCollapsed
      summary={[client.legalName, client.taxId, client.currencyLabel ?? client.currency, client.paymentTermLabel ?? client.paymentTerm].filter(Boolean).join(' · ') || t('clients.summary.empty')}
    >
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
            <Field name="legalName" label={t('clients.fields.legalName')} help={t('clients.profile.legalNameHelp')}>
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
          <div className="r2">
            <Field name="defaultServiceType" label={t('clients.profile.defaultService')} help={t('clients.profile.defaultsHelp')}>
              <Select options={lookupOptions(serviceTypes, client.defaultServiceType)} placeholder={t('clients.profile.companyDefault')} />
            </Field>
            <Field name="defaultPackageType" label={t('clients.profile.defaultPackage')}>
              <Select options={lookupOptions(packageTypes, client.defaultPackageType)} placeholder={t('clients.profile.companyDefault')} />
            </Field>
          </div>
          {sales && (
            <>
            <h3 className="set-h3">{t('clients.sales.title')}</h3>
            <Field name="merchantRegistration" label={t('clients.sales.suri')} help={t('clients.sales.suriHelp')}>
              <TextInput maxLength={SURI_MAX} autoComplete="off" />
            </Field>
            <Field name="shippingMode" label={t('clients.sales.shipping')} help={t('clients.sales.shippingHelp')}>
              <Select
                options={[
                  { value: 'FREE', label: t('clients.sales.shippingFree') },
                  { value: 'OWN', label: t('clients.sales.shippingOwn') },
                ]}
                placeholder={t('clients.sales.shippingCompany')}
              />
            </Field>
            {shippingMode === 'OWN' && (
              <div className="r2">
                <Field name="shippingCharge" label={t('clients.sales.charge')}>
                  <NumberInput min={0} />
                </Field>
                <Field name="shippingFreeFrom" label={t('clients.sales.freeFrom')} help={t('clients.sales.freeFromHelp')}>
                  <NumberInput min={0} />
                </Field>
              </div>
            )}
            <Field name="requireOrderSignature" label={t('clients.sales.requireSignature')} help={t('clients.sales.requireSignatureHelp')}>
              <Toggle text={t('clients.sales.requireSignatureText')} />
            </Field>
            </>
          )}
        </fieldset>

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
