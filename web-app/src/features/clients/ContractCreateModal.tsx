// Modal «Nuevo contrato» (POST /contracts, contracts.create): contrato adicional del cliente, en borrador con «Por servicio»
// encendido. El SLA no se pide aquí (tiene su pestaña). Errores del servidor por campo y 409 de número repetido.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useLookups } from '../../kernel/catalogs'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Select, TextArea, TextInput, Toggle, toast } from '../../kernel/ui'
import { useCreateContract } from './contractApi'
import { buildContractCreate, CONTRACT_NUMBER_MAX, CONTRACT_TITLE_MAX, contractSchema, emptyContractValues, lookupOptions, type ContractDetail } from './contractRules'
import { FormModal } from './contractUi'
import type { ClientDetail } from './clientRules'

export function ContractCreateModal({ client, open, onClose, onCreated }: { client: ClientDetail; open: boolean; onClose: () => void; onCreated: (c: ContractDetail) => void }) {
  const t = useT()
  const create = useCreateContract(client.publicId ?? '')
  const { data: currencies = [] } = useLookups('Currency')
  const { data: triggers = [] } = useLookups('BillingModel')
  const schema = useMemo(
    () =>
      contractSchema({
        titleRequired: t('clients.contracts.errors.titleRequired'),
        titleMax: t('clients.contracts.errors.titleMax', { max: CONTRACT_TITLE_MAX }),
        numberMax: t('clients.contracts.errors.numberMax', { max: CONTRACT_NUMBER_MAX }),
        startRequired: t('clients.contracts.errors.startRequired'),
        endBeforeStart: t('clients.contracts.errors.endBeforeStart'),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: emptyContractValues(tenantToday()) })

  const close = () => {
    form.reset(emptyContractValues(tenantToday()))
    onClose()
  }

  return (
    <FormModal
      id="contract-create"
      title={t('clients.contracts.createTitle')}
      open={open}
      onClose={close}
      form={form}
      size="md"
      onSubmit={async (v) => {
        const created = await create.mutateAsync(buildContractCreate(client.publicId ?? '', v))
        toast.success(t('clients.contracts.created'))
        form.reset(emptyContractValues(tenantToday()))
        onCreated(created)
      }}
    >
      <p className="help">{t('clients.contracts.createHelp')}</p>
      <div className="r2">
        <Field name="title" label={t('clients.contracts.fields.title')} required>
          <TextInput maxLength={CONTRACT_TITLE_MAX} />
        </Field>
        <Field name="contractNumber" label={t('clients.contracts.fields.number')} help={t('clients.contracts.fields.numberHelp')}>
          <TextInput maxLength={CONTRACT_NUMBER_MAX} spellCheck={false} />
        </Field>
      </div>
      <div className="r2">
        <Field name="startDate" label={t('clients.contracts.fields.since')} required>
          <DateInput />
        </Field>
        <Field name="endDate" label={t('clients.contracts.fields.end')}>
          <DateInput />
        </Field>
      </div>
      <div className="r2">
        <Field name="currency" label={t('clients.contracts.fields.currency')}>
          <Select options={lookupOptions(currencies)} placeholder={t('clients.fields.none')} />
        </Field>
        <Field name="billingTrigger" label={t('clients.contracts.fields.trigger')}>
          <Select options={lookupOptions(triggers)} placeholder={t('clients.fields.none')} />
        </Field>
      </div>
      <Field name="autoRenew" label={t('clients.contracts.fields.autoRenew')} help={t('clients.contracts.fields.autoRenewHelp')}>
        <Toggle />
      </Field>
      <Field name="notes" label={t('clients.contracts.fields.notes')}>
        <TextArea rows={3} />
      </Field>
    </FormModal>
  )
}
