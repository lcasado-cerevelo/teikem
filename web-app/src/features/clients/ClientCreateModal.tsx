// Alta de cliente (clients.create): POST /api/v1/clients con el contrato inicial opcional (por defecto encendido,
// «Contrato marco» desde hoy en la zona de la compañía). SLA, tarifas y servicios van en el bloque de contratos, no aquí.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Form, Modal, NumberInput, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { useCreateClient } from './api'
import {
  buildCreateRequest,
  CODE_MAX,
  CONTRACT_TITLE_MAX,
  createClientSchema,
  emptyCreateValues,
  LEGAL_NAME_MAX,
  NAME_MAX,
  TAX_ID_MAX,
} from './clientRules'

export interface ClientCreateModalProps {
  open: boolean
  onClose: () => void
  /** Se llama con el publicId del cliente creado (la lista lo selecciona). */
  onCreated: (publicId: string) => void
}

export function ClientCreateModal({ open, onClose, onCreated }: ClientCreateModalProps) {
  const t = useT()
  const create = useCreateClient()
  const { data: terms = [] } = useLookups('PaymentTerm')
  const { data: currencies = [] } = useLookups('Currency')
  const schema = useMemo(
    () =>
      createClientSchema({
        nameRequired: t('clients.errors.nameRequired'),
        nameMax: t('clients.errors.max', { max: NAME_MAX }),
        codeMax: t('clients.errors.codeMax'),
        legalNameMax: t('clients.errors.max', { max: LEGAL_NAME_MAX }),
        taxIdMax: t('clients.errors.max', { max: TAX_ID_MAX }),
        creditLimitNumber: t('clients.errors.creditLimitNumber'),
        creditLimitMin: t('clients.errors.creditLimitMin'),
        startRequired: t('clients.errors.startRequired'),
        titleMax: t('clients.errors.max', { max: CONTRACT_TITLE_MAX }),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: emptyCreateValues(tenantToday()) })
  const withContract = useWatch({ control: form.control, name: 'createContract' })
  const formId = 'client-create'

  const close = () => {
    form.reset(emptyCreateValues(tenantToday()))
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('clients.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const created = await create.mutateAsync(buildCreateRequest(v))
          toast.success(t('clients.created'))
          const publicId = created.publicId
          close()
          if (publicId) onCreated(publicId)
        }}
        onError={(p) => {
          // el duplicado es por CÓDIGO (409 sin campo): se muestra también bajo el código
          if (p.code === 'conflict') form.setError('code', { type: 'server', message: p.title })
        }}
      >
        <Field name="name" label={t('clients.fields.name')} required>
          <TextInput maxLength={NAME_MAX} placeholder={t('clients.fields.namePlaceholder')} />
        </Field>
        <div className="r2">
          <Field name="code" label={t('clients.fields.code')} help={t('clients.fields.codeHelp')}>
            <TextInput maxLength={CODE_MAX} />
          </Field>
          <Field name="taxId" label={t('clients.fields.taxId')}>
            <TextInput maxLength={TAX_ID_MAX} />
          </Field>
        </div>
        <Field name="legalName" label={t('clients.fields.legalName')}>
          <TextInput maxLength={LEGAL_NAME_MAX} />
        </Field>
        <div className="r3">
          <Field name="paymentTerm" label={t('clients.fields.paymentTerm')}>
            <Select options={terms.map((o) => ({ value: o.code, label: o.label }))} placeholder={t('clients.fields.none')} />
          </Field>
          <Field name="currency" label={t('clients.fields.currency')}>
            <Select options={currencies.map((o) => ({ value: o.code, label: o.label }))} placeholder={t('clients.fields.none')} />
          </Field>
          <Field name="creditLimit" label={t('clients.fields.creditLimit')}>
            <NumberInput min={0} />
          </Field>
        </div>

        <div className="cl-contract">
          <Field name="createContract" label={t('clients.create.contractSwitch')} help={t('clients.create.contractHelp')}>
            <Toggle text={withContract ? t('clients.create.yes') : t('clients.create.no')} />
          </Field>
          {withContract && (
            <div className="r2">
              <Field name="contract.startDate" label={t('clients.create.since')} required>
                <DateInput />
              </Field>
              <Field name="contract.title" label={t('clients.create.contractTitle')}>
                <TextInput maxLength={CONTRACT_TITLE_MAX} placeholder={t('clients.create.contractTitlePlaceholder')} />
              </Field>
            </div>
          )}
        </div>
      </Form>
    </Modal>
  )
}
