// Lote 223 (P3) — «Cliente nuevo» del pedido guiado: el alta mínima (POST /clients/basic, clients.create_basic) con negocio, DBA, contacto, teléfonos, correo, SURI,
// dirección física (pueblo de la lista y código postal), postal opcional y forma de pago; luego sube las fotos del registro de comerciante y del certificado de
// revendedor (cámara o archivo). El cliente nace PROSPECTO: el pedido se solicita igual pero queda esperando su aprobación.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useLookups } from '../../../kernel/catalogs'
import { useT } from '../../../kernel/i18n'
import { Field, Form, Modal, PhoneInput, Select, TextInput, Toggle, toast } from '../../../kernel/ui'
import { PostalLocalityPickerInput } from '../../warehouse/PostalLocalityPicker'
import type { components } from '../../../kernel/api/schema'
import { useWizardMutations } from './wizardApi'
import { CLIENT_DOC_TYPES, CLIENT_PAYMENT_TERMS, emptyNewClient, newClientIssues, toBasicClientRequest, type ClientDocType, type NewClientValues } from './wizardRules'

type ClientDetailDto = components['schemas']['ClientDetailDto']

const FORM_ID = 'wz-new-client'

export function NewClientModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (client: ClientDetailDto) => void }) {
  const t = useT()
  const { createClient, uploadClientDoc } = useWizardMutations()
  const { data: terms = [] } = useLookups('PaymentTerm')
  const [files, setFiles] = useState<Partial<Record<ClientDocType, File>>>({})
  const schema = useMemo(
    () =>
      z
        .object({
          name: z.string(), legalName: z.string(), contactName: z.string(), phone: z.string(), phoneExtension: z.string(), phone2: z.string(), email: z.string(),
          merchantRegistration: z.string(), physicalLine1: z.string(), physicalLine2: z.string(), physicalCity: z.string(), physicalPostalCode: z.string(),
          physicalState: z.string(), physicalCountry: z.string(), hasPostal: z.boolean(), postalLine1: z.string(), postalLine2: z.string(), postalCity: z.string(),
          postalPostalCode: z.string(), postalState: z.string(), postalCountry: z.string(), paymentTerm: z.string(),
        })
        .superRefine((v, ctx) => {
          for (const [field, key] of Object.entries(newClientIssues(v as NewClientValues))) ctx.addIssue({ code: 'custom', path: [field], message: t(`salesWizard.newClient.errors.${key}`) })
        }),
    [t],
  )
  const form = useForm<NewClientValues>({ resolver: zodResolver(schema), defaultValues: emptyNewClient() })
  const hasPostal = useWatch({ control: form.control, name: 'hasPostal' })
  // las formas de pago del formulario de los vendedores, en su orden, con la etiqueta del catálogo de la compañía (solo las que existen)
  const termOptions = useMemo(
    () => CLIENT_PAYMENT_TERMS.map((code) => terms.find((o) => o.code.toUpperCase() === code)).filter((o): o is NonNullable<typeof o> => !!o).map((o) => ({ value: o.code, label: o.label })),
    [terms],
  )
  const busy = createClient.isPending || uploadClientDoc.isPending

  const close = () => {
    form.reset(emptyNewClient())
    setFiles({})
    onClose()
  }

  const submit = async (values: NewClientValues) => {
    const detail = await createClient.mutateAsync(toBasicClientRequest(values))
    // las fotos van después (el cliente ya existe): si alguna falla, el cliente queda creado y se avisa
    for (const type of CLIENT_DOC_TYPES) {
      const file = files[type]
      if (!file || !detail.publicId) continue
      try {
        await uploadClientDoc.mutateAsync({ clientPublicId: detail.publicId, file, documentType: type })
      } catch (e) {
        toast.error(t('salesWizard.newClient.docFailed', { doc: t(`salesWizard.newClient.docs.${type}`), error: applyProblemDetails(e).title }))
      }
    }
    toast.success(t('salesWizard.newClient.created', { name: detail.name ?? '' }))
    form.reset(emptyNewClient())
    setFiles({})
    onCreated(detail)
  }

  return (
    <Modal
      open={open}
      size="lg"
      title={t('salesWizard.newClient.title')}
      onClose={close}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={close} disabled={busy}>{t('common.cancel')}</button>
          <button type="submit" form={FORM_ID} className="btn flow" disabled={busy}>{busy ? t('common.loading') : t('salesWizard.newClient.save')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('salesWizard.newClient.help')}</p>
        <Form form={form} onSubmit={submit} id={FORM_ID} className="wz-form">
          <div className="r2">
            <Field name="name" label={t('salesWizard.newClient.name')} required><TextInput maxLength={200} autoComplete="organization" /></Field>
            <Field name="legalName" label={t('salesWizard.newClient.legalName')}><TextInput maxLength={250} /></Field>
          </div>
          <Field name="contactName" label={t('salesWizard.newClient.contact')} required><TextInput maxLength={150} autoComplete="name" /></Field>
          <div className="r3">
            <Field name="phone" label={t('salesWizard.newClient.phone')} required help={t('salesWizard.newClient.phoneHelp')}><PhoneInput /></Field>
            <Field name="phoneExtension" label={t('salesWizard.newClient.extension')}><TextInput inputMode="numeric" maxLength={6} /></Field>
            <Field name="phone2" label={t('salesWizard.newClient.phone2')}><PhoneInput /></Field>
          </div>
          <div className="r2">
            <Field name="email" label={t('salesWizard.newClient.email')}><TextInput type="email" autoComplete="email" /></Field>
            <Field name="merchantRegistration" label={t('salesWizard.newClient.suri')}><TextInput maxLength={50} /></Field>
          </div>
          <fieldset className="wz-fieldset">
            <legend>{t('salesWizard.newClient.physical')}</legend>
            <Field name="physicalLine1" label={t('salesWizard.newClient.line1')} required><TextInput maxLength={200} autoComplete="address-line1" /></Field>
            <Field name="physicalLine2" label={t('salesWizard.newClient.line2')}><TextInput maxLength={200} autoComplete="address-line2" /></Field>
            <div className="r2">
              <Field name="physicalCity" label={t('salesWizard.newClient.city')} required>
                <PostalLocalityPickerInput zipClass="DELIVERY" fields={{ postalCode: 'physicalPostalCode', state: 'physicalState', country: 'physicalCountry' }} />
              </Field>
              <Field name="physicalPostalCode" label={t('salesWizard.newClient.postalCode')}><TextInput maxLength={10} inputMode="numeric" autoComplete="postal-code" /></Field>
            </div>
          </fieldset>
          <Field name="hasPostal" label={t('salesWizard.newClient.hasPostal')}><Toggle text={t('salesWizard.newClient.hasPostalText')} /></Field>
          {hasPostal && (
            <fieldset className="wz-fieldset">
              <legend>{t('salesWizard.newClient.postal')}</legend>
              <Field name="postalLine1" label={t('salesWizard.newClient.line1')} required><TextInput maxLength={200} /></Field>
              <Field name="postalLine2" label={t('salesWizard.newClient.line2')}><TextInput maxLength={200} /></Field>
              <div className="r2">
                <Field name="postalCity" label={t('salesWizard.newClient.city')} required>
                  <PostalLocalityPickerInput fields={{ postalCode: 'postalPostalCode', state: 'postalState', country: 'postalCountry' }} />
                </Field>
                <Field name="postalPostalCode" label={t('salesWizard.newClient.postalCode')}><TextInput maxLength={10} inputMode="numeric" /></Field>
              </div>
            </fieldset>
          )}
          <Field name="paymentTerm" label={t('salesWizard.newClient.payment')}><Select options={termOptions} placeholder={t('salesWizard.newClient.paymentNone')} /></Field>
          <fieldset className="wz-fieldset">
            <legend>{t('salesWizard.newClient.docsTitle')}</legend>
            {CLIENT_DOC_TYPES.map((type) => (
              <div className="f" key={type}>
                <label htmlFor={`wz-doc-${type}`}>{t(`salesWizard.newClient.docs.${type}`)}</label>
                <input
                  id={`wz-doc-${type}`}
                  type="file"
                  accept="image/*,application/pdf"
                  onChange={(e) => {
                    const file = e.target.files?.[0]
                    setFiles((m) => ({ ...m, [type]: file }))
                  }}
                />
              </div>
            ))}
            <p className="help">{t('salesWizard.newClient.docsHelp')}</p>
          </fieldset>
        </Form>
      </div>
    </Modal>
  )
}
