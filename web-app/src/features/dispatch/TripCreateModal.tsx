// Lote F-E2a — «Nueva ruta» (manual 05 §1): fecha, zona, chofer, vehículo y hora de salida. Sin chofer, el servidor usa el chofer estándar de
// la zona; sin hora de salida, las 12:00 UTC de la fecha. Los errores del servidor (404, 409 de disponibilidad…) salen tal cual.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Select, TextInput, toast } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import type { DispatchZone } from '../drivers/api'
import { useCreateTrip } from './dispatchApi'
import { buildTripCreate, emptyTripCreateValues, planDateRange, tripCreateSchema, type TripCreateValues } from './dispatchRules'
import { useFleetChoices } from './fleetChoices'

export function TripCreateModal({
  date,
  zones,
  canFleet,
  onCreated,
  onClose,
}: {
  date: string
  zones: readonly DispatchZone[]
  /** `fleet.view` + CATALOG: sin ellos no hay lista de choferes ni vehículos y el servidor decide (chofer estándar de la zona). */
  canFleet: boolean
  onCreated: (publicId: string, planDate: string) => void
  onClose: () => void
}) {
  const t = useT()
  const today = tenantToday()
  const create = useCreateTrip()
  const schema = useMemo(() => tripCreateSchema(t, today), [t, today])
  const initial = date < planDateRange(today).min || date > planDateRange(today).max ? today : date
  const form = useForm<TripCreateValues>({ resolver: zodResolver(schema), defaultValues: emptyTripCreateValues(initial) })
  const planDate = useWatch({ control: form.control, name: 'planDate' })
  const choices = useFleetChoices(planDate, canFleet)
  const range = planDateRange(today)
  const zoneOptions = zones.filter((z) => z.isActive !== false).map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') }))
  const ready = !choices.isPending

  return (
    <FormModal
      id="dispatch-create"
      title={t('dispatch.create.title')}
      open
      size="md"
      onClose={onClose}
      form={form}
      saveLabel={t('dispatch.create.save')}
      onSubmit={async (v: TripCreateValues) => {
        const trip = await create.mutateAsync(buildTripCreate(v))
        toast.success(t('dispatch.create.done', { code: trip.code ?? '' }))
        onCreated(trip.publicId ?? '', trip.planDate ?? v.planDate)
        onClose()
      }}
    >
      <div className="r2">
        <Field name="planDate" label={t('dispatch.create.date')} required>
          <DateInput min={range.min} max={range.max} />
        </Field>
        <Field name="plannedStartUtc" label={t('dispatch.create.start')} help={t('dispatch.create.startHelp')}>
          <TextInput type="time" />
        </Field>
      </div>
      {canFleet ? (
        <>
          <Field name="dispatchZoneId" label={t('dispatch.create.zone')}>
            <Select options={zoneOptions} placeholder={t('dispatch.create.noZone')} />
          </Field>
          <div className="r2">
            <Field name="driverPublicId" label={t('dispatch.create.driver')} help={t('dispatch.create.driverHelp')}>
              {ready ? <Select options={choices.drivers} placeholder={t('dispatch.create.driverAuto')} /> : <Select options={[]} placeholder={t('common.loading')} disabled />}
            </Field>
            <Field name="vehiclePublicId" label={t('dispatch.create.vehicle')} help={t('dispatch.create.vehicleHelp')}>
              {ready ? <Select options={choices.vehicles} placeholder={t('dispatch.create.noVehicle')} /> : <Select options={[]} placeholder={t('common.loading')} disabled />}
            </Field>
          </div>
          {choices.isError && <p className="help" role="status">{t('dispatch.create.availabilityFailed')}</p>}
        </>
      ) : (
        <p className="note" role="status">
          {t('dispatch.create.noFleetAccess')}
        </p>
      )}
    </FormModal>
  )
}
