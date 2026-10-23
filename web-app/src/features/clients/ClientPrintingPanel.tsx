// Panel «Impresión y portal»: lo que sale solo por la impresora al guardar un envío del cliente (labels de los paquetes y label de la factura) y lo que el cliente puede crear desde su portal
// (productos, avisos de llegada y de salida). Son banderas del cliente: mismo PATCH /profile con rowVersion que «Perfil fiscal» (solo manda lo que cambió; 409 = otro usuario guardó antes).
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { Can, ModuleGate, ModuleKeys, useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Field, Form, Panel, Toggle, toast } from '../../kernel/ui'
import { IconPrintPortal } from '../../kernel/ui/screenIcons'
import { clientKeys, useUpdateClientProfile } from './api'
import { buildProfileRequest, LEGAL_NAME_MAX, profileSchema, profileValuesOf, TAX_ID_MAX, type ClientDetail } from './clientRules'

export function ClientPrintingPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const qc = useQueryClient()
  const canEdit = useCan('clients.update')
  const publicId = client.publicId ?? ''
  const save = useUpdateClientProfile(publicId)
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
  const summary =
    [client.autoPrintLabels ? t('clients.printing.sumLabels') : null, client.autoPrintInvoice ? t('clients.printing.sumInvoice') : null].filter(Boolean).join(' · ') ||
    t('clients.printing.sumNone')

  return (
    <Panel icon={<IconPrintPortal />} title={t('clients.printing.title')} collapsible defaultCollapsed summary={summary}>
      <Form
        form={form}
        onSubmit={async (v) => {
          const updated = await save.mutateAsync(buildProfileRequest(v, original, client.rowVersion))
          form.reset(profileValuesOf(updated))
          toast.success(t('clients.saved'))
        }}
        onError={(p) => {
          if (p.code === 'conflict') void qc.invalidateQueries({ queryKey: clientKeys.detail(publicId) })
        }}
      >
        <fieldset className="cl-fs" disabled={!canEdit}>
          <Field name="autoPrintLabels" label={t('clients.profile.autoPrintLabels')} help={t('clients.profile.autoPrintLabelsHelp')}>
            <Toggle text={t('clients.profile.autoPrintLabelsText')} />
          </Field>
          <Field name="autoPrintInvoice" label={t('clients.profile.autoPrintInvoice')} help={t('clients.profile.autoPrintInvoiceHelp')}>
            <Toggle text={t('clients.profile.autoPrintInvoiceText')} />
          </Field>
          <h3 className="set-h3">{t('clients.profile.driverDeliveryTitle')}</h3>
          <p className="help">{t('clients.profile.driverDeliveryHelp')}</p>
          <Field name="requireDeliverySignature" label={t('clients.profile.requireSignature')}>
            <Toggle text={t('clients.profile.requireSignatureText')} />
          </Field>
          <Field name="requireDeliveryPhoto" label={t('clients.profile.requirePhoto')}>
            <Toggle text={t('clients.profile.requirePhotoText')} />
          </Field>
          <ModuleGate module={ModuleKeys.WmsLotSerial}>
            <h3 className="set-h3">{t('clients.profile.portalCreateTitle')}</h3>
            <p className="help">{t('clients.profile.portalCreateHelp')}</p>
            <Field name="portalCanCreateProducts" label={t('clients.profile.portalCreateProducts')}>
              <Toggle text={t('clients.profile.portalCreateProductsText')} />
            </Field>
            <Field name="portalCanCreateArrivals" label={t('clients.profile.portalCreateArrivals')}>
              <Toggle text={t('clients.profile.portalCreateArrivalsText')} />
            </Field>
            <Field name="portalCanCreateExits" label={t('clients.profile.portalCreateExits')}>
              <Toggle text={t('clients.profile.portalCreateExitsText')} />
            </Field>
          </ModuleGate>
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
