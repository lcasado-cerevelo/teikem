// Alta y edición de un programa de mantenimiento (manual 04 §5): nombre, «Aplica a» (un vehículo O un tipo de vehículo), disparador
// (Kilometraje / Tiempo / Ambos), intervalos y, solo en programas de un vehículo, el último servicio. La edición manda solo lo cambiado.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, NumberInput, Select, Spinner, TextInput, toast } from '../../kernel/ui'
import { lookupOptions } from '../clients/contractRules'
import { FormModal } from '../clients/contractUi'
import {
  buildSchedulePatch,
  buildScheduleCreate,
  emptyScheduleValues,
  scheduleSchema,
  scheduleValuesOf,
  SCHEDULE_NAME_MAX,
  TRIGGER_CODES,
  type ScheduleValues,
} from './maintenanceRules'
import { useScheduleMutations, type MaintenanceSchedule } from './maintenanceApi'
import { type VehicleChoices } from './vehicleChoices'

export function ScheduleModal({ schedule, vehicles, onClose }: { schedule: MaintenanceSchedule | null; vehicles: VehicleChoices; onClose: () => void }) {
  const t = useT()
  const { create, update } = useScheduleMutations()
  const triggersQuery = useLookups('MaintenanceTrigger')
  const typesQuery = useLookups('VehicleType')
  const triggers = triggersQuery.data ?? []
  const types = typesQuery.data ?? []
  // los selects se pintan cuando ya traen sus opciones: así lo elegido (vehículo, tipo, disparador) queda seleccionado
  const ready = !vehicles.isPending && !triggersQuery.isPending && !typesQuery.isPending
  const today = tenantToday()
  const original = useMemo(() => (schedule ? scheduleValuesOf(schedule) : undefined), [schedule])
  const schema = useMemo(() => scheduleSchema(t, { today, original }), [t, today, original])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: original ?? emptyScheduleValues() })
  const target = useWatch({ control: form.control, name: 'target' })

  // los tres disparadores del servidor; las etiquetas, las del catálogo (si todavía no carga, el código)
  const triggerOptions = TRIGGER_CODES.map((code) => ({
    value: code,
    label: triggers.find((o) => o.code.toUpperCase() === code)?.label ?? t(`fleet.maintenance.basisValue.${code}`),
  }))
  const targetOptions = [
    { value: 'vehicle', label: t('fleet.maintenance.form.targetVehicle') },
    { value: 'type', label: t('fleet.maintenance.form.targetType') },
  ]

  return (
    <FormModal
      id="maintenance-schedule"
      title={schedule ? t('fleet.maintenance.programs.edit') : t('fleet.maintenance.programs.new')}
      open
      size="md"
      onClose={onClose}
      form={form}
      onSubmit={async (v: ScheduleValues) => {
        if (schedule) {
          const plan = buildSchedulePatch(v, schedule)
          if (plan.changed) await update.mutateAsync({ id: schedule.id ?? 0, body: plan.request })
        } else await create.mutateAsync(buildScheduleCreate(v))
        toast.success(t(schedule ? 'fleet.maintenance.programs.saved' : 'fleet.maintenance.programs.created'))
        onClose()
      }}
    >
      {!ready ? (
        <Spinner block />
      ) : (
        <>
          <Field name="name" label={t('fleet.maintenance.programs.name')} required>
            <TextInput autoComplete="off" maxLength={SCHEDULE_NAME_MAX + 20} />
          </Field>
          <div className="r2">
            <Field name="target" label={t('fleet.maintenance.form.target')} required>
              <Select options={targetOptions} />
            </Field>
            {target === 'vehicle' ? (
              <Field name="vehiclePublicId" label={t('fleet.maintenance.form.vehicle')} required>
                <Select options={vehicles.options} placeholder={t('fleet.maintenance.form.choose')} />
              </Field>
            ) : (
              <Field name="vehicleType" label={t('fleet.maintenance.form.vehicleType')} required>
                <Select options={lookupOptions(types, original?.vehicleType)} placeholder={t('fleet.maintenance.form.choose')} />
              </Field>
            )}
          </div>
          <Field name="trigger" label={t('fleet.maintenance.form.trigger')} required>
            <Select options={triggerOptions} />
          </Field>
          <div className="r2">
            <Field name="intervalKm" label={t('fleet.maintenance.form.intervalKm')} help={t('fleet.maintenance.form.intervalKmHelp')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="intervalDays" label={t('fleet.maintenance.form.intervalDays')} help={t('fleet.maintenance.form.intervalDaysHelp')}>
              <NumberInput className="mono" min={0} step={1} />
            </Field>
          </div>
          {target === 'vehicle' && (
            <>
              <div className="r2">
                <Field name="lastServiceKm" label={t('fleet.maintenance.form.lastServiceKm')}>
                  <NumberInput className="mono" min={0} />
                </Field>
                <Field name="lastServiceDate" label={t('fleet.maintenance.form.lastServiceDate')}>
                  <DateInput max={today} />
                </Field>
              </div>
              <p className="help">{t('fleet.maintenance.form.lastServiceHelp')}</p>
            </>
          )}
          {target === 'type' && <p className="help">{t('fleet.maintenance.form.lastServiceHelp')}</p>}
        </>
      )}
    </FormModal>
  )
}
