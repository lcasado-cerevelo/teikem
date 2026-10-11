// Pestaña «SLA»: un renglón por tipo de servicio (ServiceType) con horas máximas de tránsito, ventana de recogido (min), meta
// de puntualidad (%) y penalidad. Es un borrador local: «Guardar SLA» envía PUT …/service-levels con la lista completa
// (los renglones vacíos no se envían, y los tipos ausentes se dan de baja). Empieza vacío.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { EmptyState, Field, Form, NumberInput, Spinner, toast } from '../../kernel/ui'
import { IconClock } from '../../kernel/ui/screenIcons'
import { useSetServiceLevels } from './contractApi'
import { buildSlaRequest, remapSlaErrors, rethrowRenamed, slaRowsOf, slaSchema, type ContractDetail, type SlaValues } from './contractRules'


function SlaForm({ contract, clientPublicId, initial, canWrite }: { contract: ContractDetail; clientPublicId: string; initial: SlaValues; canWrite: boolean }) {
  const t = useT()
  const save = useSetServiceLevels(contract.publicId ?? '', clientPublicId)
  const schema = useMemo(
    () =>
      slaSchema({
        number: t('clients.sla.errors.number'),
        hoursInt: t('clients.sla.errors.hoursInt'),
        hoursMin: t('clients.sla.errors.hoursMin'),
        windowInt: t('clients.sla.errors.windowInt'),
        windowMin: t('clients.sla.errors.windowMin'),
        pctRange: t('clients.sla.errors.pctRange'),
        penaltyMin: t('clients.sla.errors.penaltyMin'),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        const { body, indexMap } = buildSlaRequest(v.levels)
        let updated: ContractDetail
        try {
          updated = await save.mutateAsync(body)
        } catch (err) {
          rethrowRenamed(err, (e) => remapSlaErrors(e, indexMap))
        }
        form.reset({ levels: slaRowsOf(initial.levels.map((r) => ({ code: r.serviceType, label: r.label })), updated.serviceLevels).levels }, { keepDirtyValues: false })
        toast.success(t('clients.sla.saved'))
      }}
    >
      <p className="help">{t('clients.sla.help')}</p>
      <fieldset className="cl-fs" disabled={!canWrite}>
        {initial.levels.map((row, i) => (
          <div key={row.serviceType} className="cl-sla-row" role="group" aria-label={row.label}>
            <div className="cl-sla-name">
              <b>{row.label}</b> <span className="meta mono">{row.serviceType}</span>
            </div>
            <div className="cl-sla-fields">
              <Field name={`levels.${i}.maxTransitHours`} label={t('clients.sla.maxTransitHours')}>
                <NumberInput min={1} step={1} />
              </Field>
              <Field name={`levels.${i}.pickupWindowMin`} label={t('clients.sla.pickupWindowMin')}>
                <NumberInput min={0} step={1} />
              </Field>
              <Field name={`levels.${i}.onTimeTargetPct`} label={t('clients.sla.onTimeTargetPct')}>
                <NumberInput min={0} max={100} />
              </Field>
              <Field name={`levels.${i}.penaltyAmount`} label={t('clients.sla.penaltyAmount')}>
                <NumberInput min={0} />
              </Field>
            </div>
          </div>
        ))}
      </fieldset>
      {canWrite && (
        <div className="form-acts">
          <button type="button" className="btn" disabled={form.formState.isSubmitting || !form.formState.isDirty} onClick={() => form.reset(initial, { keepDirtyValues: false })}>
            {t('clients.sla.discard')}
          </button>
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? t('common.loading') : t('clients.sla.save')}
          </button>
        </div>
      )}
    </Form>
  )
}

export function ContractSlaTab({ contract, clientPublicId }: { contract: ContractDetail; clientPublicId: string }) {
  const t = useT()
  const canWrite = useCan('contracts.update') && contract.canEdit
  const { data: types, isPending } = useLookups('ServiceType')
  const initial = useMemo(() => slaRowsOf((types ?? []).map((o) => ({ code: o.code, label: o.label })), contract.serviceLevels), [types, contract.serviceLevels])

  if (isPending) return <Spinner block />
  if (initial.levels.length === 0) return <EmptyState icon={<IconClock />} title={t('clients.sla.noTypes')} />
  return <SlaForm contract={contract} clientPublicId={clientPublicId} initial={initial} canWrite={canWrite} />
}
