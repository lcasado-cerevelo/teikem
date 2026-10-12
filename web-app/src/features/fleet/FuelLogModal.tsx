// «Registrar carga» / «Editar carga» (manual 04 §7): vehículo, fecha y hora (en la hora de la compañía), litros, costo total, odómetro y chofer
// opcionales y estación. El km/L y el costo/km no se escriben: los calcula el servidor. El vehículo de una carga no se cambia. La regla del
// odómetro (monótono por fecha dentro del vehículo) la aplica el servidor y su mensaje sale bajo el campo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, NumberInput, Select, Spinner, TextInput, toast } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import { useDrivers } from '../drivers/api'
import { useFuelMutations, type FuelLog } from './maintenanceApi'
import { buildFuelCreate, buildFuelPatch, emptyFuelValues, fuelSchema, fuelValuesOf, type FuelValues } from './maintenanceRules'
import { type VehicleChoices } from './vehicleChoices'

export function FuelLogModal({ log, vehicles, defaultVehicle, onClose }: { log: FuelLog | null; vehicles: VehicleChoices; defaultVehicle?: string; onClose: () => void }) {
  const t = useT()
  const mutations = useFuelMutations()
  const drivers = useDrivers(true)
  const today = tenantToday()
  // los selects se pintan cuando ya traen sus opciones: así el vehículo y el chofer de la carga quedan elegidos
  const ready = !vehicles.isPending && !drivers.isPending
  const schema = useMemo(() => fuelSchema(t, { creating: !log }), [t, log])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: log ? fuelValuesOf(log) : emptyFuelValues(new Date(), defaultVehicle) })
  const driverOptions = (drivers.data ?? []).map((d) => ({ value: d.publicId ?? '', label: [d.code, d.fullName].filter(Boolean).join(' · ') }))
  // el chofer de una carga vieja puede estar inactivo y no venir en la lista: sigue siendo elegible
  if (log?.driverPublicId && !driverOptions.some((o) => o.value === log.driverPublicId))
    driverOptions.push({ value: log.driverPublicId, label: log.driverName ?? log.driverPublicId })

  return (
    <FormModal
      id="fuel-log"
      title={log ? t('fleet.fuel.edit') : t('fleet.fuel.register')}
      open
      size="md"
      onClose={onClose}
      form={form}
      onSubmit={async (v: FuelValues) => {
        if (log) {
          const plan = buildFuelPatch(v, log)
          if (plan.changed) await mutations.update.mutateAsync({ id: log.id ?? 0, body: plan.request })
        } else await mutations.create.mutateAsync(buildFuelCreate(v))
        toast.success(t(log ? 'fleet.fuel.saved' : 'fleet.fuel.registered'))
        onClose()
      }}
    >
      {!ready ? (
        <Spinner block />
      ) : (
        <>
          <Field name="vehiclePublicId" label={t('fleet.fuel.vehicle')} required help={log ? t('fleet.fuel.vehicleLocked') : undefined}>
            <Select disabled={!!log} options={vehicles.options} placeholder={t('fleet.fuel.choose')} />
          </Field>
          <div className="r2">
            <Field name="date" label={t('fleet.fuel.date')} required>
              <DateInput max={today} />
            </Field>
            <Field name="time" label={t('fleet.fuel.time')} required>
              <TextInput type="time" />
            </Field>
          </div>
          <div className="r2">
            <Field name="liters" label={t('fleet.fuel.liters')} required>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="totalCost" label={t('fleet.fuel.totalCost')} required>
              <NumberInput className="mono" min={0} />
            </Field>
          </div>
          <Field name="odometerKm" label={t('fleet.fuel.odometer')} help={t('fleet.fuel.odometerHelp')}>
            <NumberInput className="mono" min={0} />
          </Field>
          <div className="r2">
            <Field name="driverPublicId" label={t('fleet.fuel.driver')}>
              <Select options={driverOptions} placeholder={t('fleet.fuel.noDriver')} />
            </Field>
            <Field name="station" label={t('fleet.fuel.station')}>
              <TextInput autoComplete="off" />
            </Field>
          </div>
        </>
      )}
    </FormModal>
  )
}
