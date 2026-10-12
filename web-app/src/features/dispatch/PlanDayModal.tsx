// Lote F-E2a — «Planificar el día» (manual 05 §8): agrupa por zona las órdenes confirmadas sin ruta de una fecha, usando la ruta abierta de
// la zona o creando una con el chofer estándar. Es idempotente y no optimiza, no despacha ni asigna vehículo. El resultado se queda a la vista
// (rutas creadas, órdenes asignadas, omitidas y sin zona, más los avisos por zona).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Modal, SearchMultiSelectInput, Toggle } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import type { DispatchZone } from '../drivers/api'
import { usePlanDay } from './dispatchApi'
import { buildPlanDayRequest, planDateRange, planDaySchema, type PlanDayResult, type PlanDayValues } from './dispatchRules'
import { TripIssues } from './TripIssues'

export function PlanDayModal({
  date,
  zones,
  canFleet,
  onPlanned,
  onClose,
}: {
  date: string
  zones: readonly DispatchZone[]
  canFleet: boolean
  onPlanned: (planDate: string) => void
  onClose: () => void
}) {
  const t = useT()
  const f = useFormat()
  const today = tenantToday()
  const plan = usePlanDay()
  const [result, setResult] = useState<PlanDayResult | null>(null)
  const schema = useMemo(() => planDaySchema(t, today), [t, today])
  const range = planDateRange(today)
  const initial = date < range.min || date > range.max ? today : date
  const form = useForm<PlanDayValues>({ resolver: zodResolver(schema), defaultValues: { planDate: initial, zoneIds: [], createEmptyTrips: false } })
  const zoneOptions = zones.filter((z) => z.isActive !== false).map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') }))

  if (result) {
    const counts = [
      { key: 'tripsCreated', value: result.tripsCreated ?? 0 },
      { key: 'ordersAssigned', value: result.ordersAssigned ?? 0 },
      { key: 'ordersSkipped', value: result.ordersSkipped ?? 0 },
      { key: 'ordersWithoutZone', value: result.ordersWithoutZone ?? 0 },
    ]
    return (
      <Modal
        open
        size="md"
        title={t('dispatch.planDay.resultTitle', { date: f.date(result.planDate ?? '') })}
        onClose={onClose}
        footer={
          <button type="button" className="btn flow" onClick={onClose}>
            {t('dispatch.closeButton')}
          </button>
        }
      >
        <div className="disp-summary" role="group" aria-label={t('dispatch.planDay.summary')}>
          {counts.map((c) => (
            <div key={c.key}>
              <b>{f.number(c.value)}</b>
              <span>{t(`dispatch.planDay.${c.key}`)}</span>
            </div>
          ))}
        </div>
        {(result.zones ?? []).length === 0 && <p className="disp-note">{t('dispatch.planDay.noZones')}</p>}
        {(result.zones ?? []).map((z) => (
          <div key={z.dispatchZoneId} className="disp-zone-res">
            <b>{z.zoneCode}</b>
            {z.tripCode && (
              <>
                {' · '}
                {t(z.tripCreated ? 'dispatch.planDay.tripNew' : 'dispatch.planDay.tripExisting', { code: z.tripCode })}
              </>
            )}
            {' · '}
            {t('dispatch.planDay.zoneCounts', { assigned: f.number(z.ordersAssigned ?? 0), skipped: f.number(z.ordersSkipped ?? 0) })}
            <TripIssues issues={z.issues} />
          </div>
        ))}
      </Modal>
    )
  }

  return (
    <FormModal
      id="dispatch-plan-day"
      title={t('dispatch.planDay.title')}
      open
      size="md"
      onClose={onClose}
      form={form}
      saveLabel={t('dispatch.planDay.run')}
      onSubmit={async (v: PlanDayValues) => {
        const res = await plan.mutateAsync(buildPlanDayRequest(v))
        setResult(res)
        onPlanned(res.planDate ?? v.planDate)
      }}
    >
      <p className="d">{t('dispatch.planDay.hint')}</p>
      <Field name="planDate" label={t('dispatch.planDay.date')} required>
        <DateInput min={range.min} max={range.max} />
      </Field>
      {canFleet ? (
        <Field name="zoneIds" label={t('dispatch.planDay.zones')} help={t('dispatch.planDay.zonesHelp')}>
          <SearchMultiSelectInput options={zoneOptions} placeholder={t('dispatch.planDay.allZones')} />
        </Field>
      ) : (
        <p className="note" role="status">
          {t('dispatch.planDay.noFleetAccess')}
        </p>
      )}
      <Field name="createEmptyTrips" label={t('dispatch.planDay.empty')} help={t('dispatch.planDay.emptyHelp')}>
        <Toggle text={t('dispatch.planDay.emptyText')} />
      </Field>
    </FormModal>
  )
}
