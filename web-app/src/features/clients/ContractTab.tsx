// Pestaña «Contrato»: estatus (StatusPipeline CONTRACT, POST …/status), datos generales (PATCH, con rowVersion y clearEndDate),
// modelo de facturación (los 5 checks, PATCH billing-model) con el cargo por despacho y el cargo por COD cuando su check está
// marcado, e historial de estatus. Todo se escribe con contracts.update y solo si el estatus permite editar (`canEdit`).
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useId, useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { StatusHistory, StatusPipeline, useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Form, NumberInput, Select, TextArea, TextInput, Toggle, toast } from '../../kernel/ui'
import { contractKeys, useBillingMutations, useTransitionContract, useUpdateContract } from './contractApi'
import {
  billingSchema,
  billingValuesOf,
  buildContractPatch,
  CONTRACT_ENTITY_TYPE,
  CONTRACT_NUMBER_MAX,
  CONTRACT_STATUS_DOMAIN,
  CONTRACT_TITLE_MAX,
  contractSchema,
  contractValuesOf,
  lookupOptions,
  planBillingSave,
  renameErrors,
  rethrowRenamed,
  type BillingValues,
  type ContractDetail,
} from './contractRules'


function GeneralForm({ contract, clientPublicId, canWrite }: { contract: ContractDetail; clientPublicId: string; canWrite: boolean }) {
  const t = useT()
  const qc = useQueryClient()
  const numberId = useId()
  const save = useUpdateContract(contract.publicId ?? '', clientPublicId)
  const { data: currencies = [] } = useLookups('Currency', { includeDisabled: true })
  const { data: triggers = [] } = useLookups('BillingModel', { includeDisabled: true })
  const original = useMemo(() => contractValuesOf(contract), [contract])
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
  const form = useForm({ resolver: zodResolver(schema), values: original, resetOptions: { keepDirtyValues: true } })

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        const updated = await save.mutateAsync(buildContractPatch(v, original, contract.rowVersion))
        form.reset(contractValuesOf(updated), { keepDirtyValues: false })
        toast.success(t('clients.saved'))
      }}
      onError={(p) => {
        // 409: otro usuario guardó antes; se relee el contrato para que el siguiente intento lleve su rowVersion
        if (p.code === 'conflict') void qc.invalidateQueries({ queryKey: contractKeys.detail(contract.publicId ?? '') })
      }}
    >
      <fieldset className="cl-fs" disabled={!canWrite}>
        <div className="r2">
          <div className="f">
            <label htmlFor={numberId}>{t('clients.contracts.fields.number')}</label>
            <input id={numberId} value={contract.contractNumber ?? ''} readOnly disabled />
          </div>
          <Field name="title" label={t('clients.contracts.fields.title')} required>
            <TextInput maxLength={CONTRACT_TITLE_MAX} />
          </Field>
        </div>
        <div className="r2">
          <Field name="startDate" label={t('clients.contracts.fields.since')} help={t('clients.contracts.fields.sinceHelp')} required>
            <DateInput />
          </Field>
          <Field name="endDate" label={t('clients.contracts.fields.end')} help={t('clients.contracts.fields.endHelp')}>
            <DateInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="currency" label={t('clients.contracts.fields.currency')}>
            <Select options={lookupOptions(currencies, contract.currency)} placeholder={t('clients.fields.none')} />
          </Field>
          <Field name="billingTrigger" label={t('clients.contracts.fields.trigger')}>
            <Select options={lookupOptions(triggers, contract.billingTrigger)} placeholder={t('clients.fields.none')} />
          </Field>
        </div>
        <Field name="autoRenew" label={t('clients.contracts.fields.autoRenew')} help={t('clients.contracts.fields.autoRenewHelp')}>
          <Toggle />
        </Field>
        <Field name="notes" label={t('clients.contracts.fields.notes')}>
          <TextArea rows={3} />
        </Field>
      </fieldset>
      {canWrite && (
        <div className="form-acts">
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
      )}
    </Form>
  )
}

function BillingForm({ contract, clientPublicId, canWrite }: { contract: ContractDetail; clientPublicId: string; canWrite: boolean }) {
  const t = useT()
  const mutations = useBillingMutations(contract.publicId ?? '', clientPublicId)
  const original = useMemo(() => billingValuesOf(contract), [contract])
  const schema = useMemo(
    () =>
      billingSchema(
        {
          number: t('clients.contracts.errors.number'),
          dispatchMin: t('clients.contracts.errors.dispatchMin'),
          dispatchRequired: t('clients.contracts.errors.dispatchRequired'),
          codFixedMin: t('clients.contracts.errors.codFixedMin'),
          codPercentRange: t('clients.contracts.errors.codPercentRange'),
          codRequired: t('clients.contracts.errors.codRequired'),
        },
        original,
      ),
    [t, original],
  )
  const form = useForm({ resolver: zodResolver(schema), values: original, resetOptions: { keepDirtyValues: true } })
  const dispatchOn = useWatch({ control: form.control, name: 'billDispatchFee' })
  const codOn = useWatch({ control: form.control, name: 'billCodFee' })
  const codType = useWatch({ control: form.control, name: 'codType' })

  const codTypes = [
    { value: 'FIXED', label: t('clients.contracts.billing.codFixed') },
    { value: 'PERCENT', label: t('clients.contracts.billing.codPercent') },
  ]
  const checks: { name: 'billPerService' | 'billExtraPiece' | 'billDispatchFee' | 'billCodFee' | 'billSpecialServices'; label: string }[] = [
    { name: 'billPerService', label: t('clients.contracts.billing.perService') },
    { name: 'billExtraPiece', label: t('clients.contracts.billing.extraPiece') },
    { name: 'billDispatchFee', label: t('clients.contracts.billing.dispatch') },
    { name: 'billCodFee', label: t('clients.contracts.billing.cod') },
    { name: 'billSpecialServices', label: t('clients.contracts.billing.special') },
  ]

  return (
    <Form
      form={form}
      onSubmit={async (v: BillingValues) => {
        const plan = planBillingSave(v, original)
        let last: ContractDetail | null = null
        try {
          if (plan.flags) last = await mutations.flags.mutateAsync(plan.flags)
          if (plan.dispatch) last = await mutations.dispatch.mutateAsync(plan.dispatch)
          if (plan.cod) last = await mutations.cod.mutateAsync(plan.cod)
        } catch (err) {
          rethrowRenamed(err, (e) => renameErrors(e, { amount: 'dispatchAmount', value: 'codValue', type: 'codType' }))
        }
        form.reset(last ? billingValuesOf(last) : v, { keepDirtyValues: false })
        toast.success(t('clients.saved'))
      }}
    >
      <p className="meta cl-summary">
        {t('clients.contracts.billing.summary')} <b>{contract.billingModel?.summary || '—'}</b>
      </p>
      <fieldset className="cl-fs" disabled={!canWrite}>
        <div className="cl-checks">
          {checks.map((c) => (
            <Field key={c.name} name={c.name} label={c.label}>
              <Toggle />
            </Field>
          ))}
        </div>
        <p className="help">{t('clients.contracts.billing.keepNote')}</p>
        {dispatchOn && (
          <div className="r3">
            <Field name="dispatchAmount" label={t('clients.contracts.billing.dispatchAmount')}>
              <NumberInput min={0} />
            </Field>
          </div>
        )}
        {codOn && (
          <div className="r3">
            <Field name="codType" label={t('clients.contracts.billing.codType')}>
              <Select options={codTypes} />
            </Field>
            <Field name="codValue" label={codType === 'PERCENT' ? t('clients.contracts.billing.codValuePct') : t('clients.contracts.billing.codValueFixed')} help={codType === 'PERCENT' ? t('clients.contracts.billing.codHelp') : undefined}>
              <NumberInput min={0} />
            </Field>
          </div>
        )}
      </fieldset>
      {canWrite && (
        <div className="form-acts">
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
      )}
    </Form>
  )
}

export function ContractTab({ contract, clientPublicId }: { contract: ContractDetail; clientPublicId: string }) {
  const t = useT()
  const canUpdate = useCan('contracts.update')
  const canWrite = canUpdate && contract.canEdit
  const transition = useTransitionContract(contract.publicId ?? '', clientPublicId)

  return (
    <div className="cl-ctr-tab">
      <StatusPipeline
        domain={CONTRACT_STATUS_DOMAIN}
        entityType={CONTRACT_ENTITY_TYPE}
        entityId={contract.id}
        currentCode={contract.status}
        disabled={!canUpdate}
        onTransition={async (toCode, comment) => {
          await transition.mutateAsync({ toCode, comment: comment || null })
          toast.success(t('clients.contracts.statusChanged'))
        }}
      />
      {canUpdate && !contract.canEdit && <p className="note cl-ctr-note">{t('clients.contracts.readOnlyStatus')}</p>}

      <h3 className="cl-ctr-h">{t('clients.contracts.generalTitle')}</h3>
      <GeneralForm contract={contract} clientPublicId={clientPublicId} canWrite={canWrite} />

      <h3 className="cl-ctr-h">{t('clients.contracts.billing.title')}</h3>
      <BillingForm contract={contract} clientPublicId={clientPublicId} canWrite={canWrite} />

      <h3 className="cl-ctr-h">{t('status.history')}</h3>
      <StatusHistory entityType={CONTRACT_ENTITY_TYPE} entityId={contract.id} domain={CONTRACT_STATUS_DOMAIN} />
    </div>
  )
}
