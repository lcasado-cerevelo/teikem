// Alta y edición de un consignatario (modal de la maqueta `renderConsigneeAdminModalHtml`): número, nombre, dirección, pueblo,
// teléfono, cliente dueño y «Número de factura repetido». Lo que el servidor exige y la maqueta no trae (país, línea 2, estado,
// ZIP, ventana horaria, minutos de servicio y notas) va en la sección plegada «Opciones de entrega». POST (tipo fijo DELIVERY,
// país PR por omisión) o PATCH solo de lo cambiado con `rowVersion`; el teléfono es un ContactPoint de la localización
// (`contacts.manage` más `locations.update`). Errores del servidor por campo con `applyProblemDetails`.
// Lote F-B3: el selector de pueblo/ZIP ofrece solo ZIP de entrega (`zipClass=DELIVERY`: sin apartados postales ni militares).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { CallingCodeInput, Field, Form, Modal, NumberInput, PhoneInput, PhoneRow, Select, TextArea, TextInput, Toggle, toast } from '../../kernel/ui'
import { PostalLocalityPickerInput } from '../warehouse/PostalLocalityPicker'
import { useClientOptions, useCreateConsignee, useUpdateConsignee } from './api'
import { consigneeMessagesOf } from './consigneeMessages'
import {
  buildCreateRequest,
  buildPatchRequest,
  CODE_MAX,
  CONTACT_NAME_MAX,
  consigneeSchema,
  consigneeValuesOf,
  emptyConsigneeValues,
  LINE_MAX,
  NAME_MAX,
  NOTES_MAX,
  POSTAL_MAX,
  SHARED,
  STATE_MAX,
  type Consignee,
  type ConsigneeValues,
  type ContactPoint,
} from './consigneeRules'
import { useConsigneePhoneSync } from './useConsigneePhoneSync'

/** Campos de «Opciones de entrega»: si uno falla, la sección se abre para que el error se vea. */
const DELIVERY_FIELDS: ReadonlySet<string> = new Set(['line2', 'state', 'postalCode', 'country', 'defaultServiceMinutes', 'defaultWindowStart', 'defaultWindowEnd', 'accessNotes', 'deliveryNotes'])

export interface ConsigneeFormModalProps {
  open: boolean
  /** null = alta. */
  consignee: Consignee | null
  /** Teléfono actual del consignatario (edición). */
  phone: ContactPoint | undefined
  /** Dueño con el que arranca el alta (publicId del cliente del filtro; '' = compartido). */
  defaultOwner: string
  onClose: () => void
  /** Se llama con el publicId del consignatario guardado. */
  onSaved: (publicId: string) => void
}

export function ConsigneeFormModal({ open, consignee, phone, defaultOwner, onClose, onSaved }: ConsigneeFormModalProps) {
  const t = useT()
  const f = useFormat()
  const editing = consignee !== null
  const formId = 'consignee-form'
  const { canPhone, sync: syncPhone } = useConsigneePhoneSync()
  const create = useCreateConsignee()
  const update = useUpdateConsignee(consignee?.publicId ?? '')
  const { data: clients = [] } = useClientOptions(false)
  const { data: countries = [] } = useLookups('Country')
  const [moreOpen, setMoreOpen] = useState(false)

  const schema = useMemo(
    () =>
      consigneeSchema(
        consigneeMessagesOf(t),
        f.isValidPhoneFor,
      ),
    [t, f],
  )

  const initial = useMemo<ConsigneeValues>(
    () => {
      const cc = f.callingCodeInput(phone?.countryCallingCode)
      if (!consignee) return { ...emptyConsigneeValues(defaultOwner), countryCallingCode: cc }
      const shown = phone?.value ? (f.usesCompanyMaskFor(phone.countryCallingCode) ? f.phoneInput(phone.value) : phone.value) : ''
      return consigneeValuesOf(consignee, shown, cc)
    },
    [consignee, phone, defaultOwner, f],
  )
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })

  // un error en una opción de entrega abre la sección
  const deliveryError = Object.keys(form.formState.errors).some((k) => DELIVERY_FIELDS.has(k))

  const ownerOptions = useMemo(() => {
    const list = clients.map((c) => ({ value: c.publicId ?? '', label: c.name ?? c.code ?? '' }))
    // el dueño actual aunque no esté entre los clientes activos
    if (consignee && !consignee.isShared && consignee.clientPublicId && !list.some((o) => o.value === consignee.clientPublicId))
      list.push({ value: consignee.clientPublicId, label: consignee.clientName ?? '' })
    return [{ value: SHARED, label: t('consignees.shared') }, ...list]
  }, [clients, consignee, t])

  const countryOptions = useMemo(() => {
    const list = countries.map((o) => ({ value: o.code, label: o.label }))
    if (consignee?.country && !list.some((o) => o.value === consignee.country)) list.push({ value: consignee.country, label: consignee.countryLabel ?? consignee.country })
    return list
  }, [countries, consignee])

  const close = () => {
    form.reset(initial, { keepDirtyValues: false })
    setMoreOpen(false)
    onClose()
  }

  return (
    <Modal
      open={open}
      title={editing ? t('consignees.editTitle') : t('consignees.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn sm" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn sm flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          let saved: Consignee
          if (consignee) {
            const { request, changed } = buildPatchRequest(v, consigneeValuesOf(consignee, ''), consignee.rowVersion)
            saved = changed ? await update.mutateAsync(request) : consignee
          } else {
            saved = await create.mutateAsync(buildCreateRequest(v))
          }
          // el teléfono se guarda aparte (ContactPoint); un fallo aquí no deshace el consignatario ya guardado
          const phoneProblem = await syncPhone(saved.id, v, phone)
          if (phoneProblem) toast.error(t('consignees.phoneNotSaved', { reason: phoneProblem }))
          else toast.success(t(editing ? 'consignees.updated' : 'consignees.created', { name: saved.name ?? '' }))
          setMoreOpen(false)
          onClose()
          if (saved.publicId) onSaved(saved.publicId)
        }}
      >
        <div className="r2">
          <Field name="code" label={t('consignees.number')} help={t('consignees.numberHelp')}>
            <TextInput className="mono" maxLength={CODE_MAX} />
          </Field>
          <Field name="name" label={t('consignees.name')} required>
            <TextInput maxLength={NAME_MAX} />
          </Field>
        </div>
        <Field name="line1" label={t('consignees.address')} required>
          <TextInput maxLength={LINE_MAX} />
        </Field>
        <div className="r2">
          <Field name="city" label={t('consignees.town')} required help={t('consignees.townHelp')}>
            <PostalLocalityPickerInput zipClass="DELIVERY" />
          </Field>
          {canPhone ? (
            <PhoneRow>
              <Field name="countryCallingCode" label={t('ui.phone.countryCode')}>
                <CallingCodeInput />
              </Field>
              <Field name="phone" label={t('consignees.phone')}>
                <PhoneInput />
              </Field>
            </PhoneRow>
          ) : (
            <div className="f">
              <label>{t('consignees.phone')}</label>
              <input value={phone?.value ? f.phoneFor(phone.value, phone.countryCallingCode) : ''} readOnly disabled aria-label={t('consignees.phone')} />
              <p className="help">{t('consignees.phoneNoPermission')}</p>
            </div>
          )}
        </div>
        <Field name="contactName" label={t('consignees.contactName')} help={t('consignees.contactNameHelp')}>
          <TextInput maxLength={CONTACT_NAME_MAX} autoComplete="off" />
        </Field>
        <Field name="clientPublicId" label={t('consignees.scope')}>
          <Select options={ownerOptions} />
        </Field>
        <Field name="allowDupInvoice" label={t('consignees.dupInvTitle')} help={t('consignees.dupInvHint')}>
          <Toggle text={t('consignees.dupInvAllow')} />
        </Field>

        <details
          className="cs-more"
          open={moreOpen || deliveryError}
          onToggle={(e) => setMoreOpen((e.currentTarget as HTMLDetailsElement).open)}
        >
          <summary>{t('consignees.delivery.title')}</summary>
          <p className="help">{t('consignees.delivery.hint')}</p>
          <Field name="line2" label={t('consignees.delivery.line2')}>
            <TextInput maxLength={LINE_MAX} />
          </Field>
          <div className="r3">
            <Field name="state" label={t('consignees.delivery.state')}>
              <TextInput maxLength={STATE_MAX} />
            </Field>
            <Field name="postalCode" label={t('consignees.delivery.zip')}>
              <TextInput maxLength={POSTAL_MAX} />
            </Field>
            <Field name="country" label={t('consignees.delivery.country')} required>
              <Select options={countryOptions} />
            </Field>
          </div>
          <div className="r3">
            <Field name="defaultWindowStart" label={t('consignees.delivery.windowStart')}>
              <TextInput type="time" />
            </Field>
            <Field name="defaultWindowEnd" label={t('consignees.delivery.windowEnd')}>
              <TextInput type="time" />
            </Field>
            <Field name="defaultServiceMinutes" label={t('consignees.delivery.serviceMinutes')}>
              <NumberInput min={0} step={1} />
            </Field>
          </div>
          <Field name="accessNotes" label={t('consignees.delivery.accessNotes')}>
            <TextArea rows={2} maxLength={NOTES_MAX} />
          </Field>
          <Field name="deliveryNotes" label={t('consignees.delivery.deliveryNotes')}>
            <TextArea rows={2} maxLength={NOTES_MAX} />
          </Field>
        </details>
      </Form>
    </Modal>
  )
}
