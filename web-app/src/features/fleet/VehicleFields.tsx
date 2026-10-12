// Campos del vehículo (maqueta `renderVehicleModalHtml`: Código, Placa, Tipo, Propiedad, Combustible, VIN, Odómetro, Peso máx, Volumen máx;
// el «Almacén base» de la maqueta se omite porque el servidor no lo guarda) más Marca, Modelo, Año y Tope de paradas. Los comparten el modal
// «Nuevo vehículo» y la ficha. Tipo, Propiedad y Combustible son selects de catálogo.
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Field, NumberInput, Select, TextInput } from '../../kernel/ui'
import { lookupOptions } from '../clients/contractRules'
import type { VehicleValues } from './fleetRules'

export interface VehicleFieldsProps {
  /** true = alta (el código se captura); false = ficha (el código se ve fijo y deshabilitado). */
  creating: boolean
  /** Código mostrado en la ficha. */
  code?: string
  /** Solo lectura (sin `fleet.manage` o vehículo dado de baja). */
  disabled?: boolean
  /** Valores guardados: sus códigos de catálogo siguen siendo elegibles aunque el catálogo los haya deshabilitado. */
  current?: Pick<VehicleValues, 'vehicleType' | 'ownership' | 'fuelType'>
}

export function VehicleFields({ creating, code, disabled = false, current }: VehicleFieldsProps) {
  const t = useT()
  const { data: types = [] } = useLookups('VehicleType')
  const { data: ownerships = [] } = useLookups('Ownership')
  const { data: fuels = [] } = useLookups('FuelType')
  return (
    <div className="flt-grid">
      {creating ? (
        <Field name="code" label={t('fleet.vehicle.code')} required help={t('fleet.vehicle.codeHelp')}>
          <TextInput className="mono" placeholder="V-04" maxLength={60} autoComplete="off" />
        </Field>
      ) : (
        <div className="f">
          <label htmlFor="flt-code">{t('fleet.vehicle.code')}</label>
          <input id="flt-code" className="mono" value={code ?? ''} disabled readOnly />
        </div>
      )}
      <Field name="plateNumber" label={t('fleet.vehicle.plate')}>
        <TextInput className="mono" disabled={disabled} autoComplete="off" />
      </Field>
      <Field name="vehicleType" label={t('fleet.vehicle.type')}>
        <Select disabled={disabled} options={lookupOptions(types, current?.vehicleType)} placeholder={t('fleet.vehicle.choose')} />
      </Field>
      <Field name="ownership" label={t('fleet.vehicle.ownership')}>
        <Select disabled={disabled} options={lookupOptions(ownerships, current?.ownership)} placeholder={t('fleet.vehicle.choose')} />
      </Field>
      <Field name="fuelType" label={t('fleet.vehicle.fuel')}>
        <Select disabled={disabled} options={lookupOptions(fuels, current?.fuelType)} placeholder={t('fleet.vehicle.choose')} />
      </Field>
      <Field name="modelYear" label={t('fleet.vehicle.year')}>
        <NumberInput disabled={disabled} className="mono" step={1} />
      </Field>
      <Field name="make" label={t('fleet.vehicle.make')}>
        <TextInput disabled={disabled} autoComplete="off" />
      </Field>
      <Field name="model" label={t('fleet.vehicle.model')}>
        <TextInput disabled={disabled} autoComplete="off" />
      </Field>
      <Field name="vin" label={t('fleet.vehicle.vin')} help={t('fleet.vehicle.vinHelp')} className="wide">
        <TextInput className="mono" disabled={disabled} autoComplete="off" />
      </Field>
      <Field name="currentOdometerKm" label={t('fleet.vehicle.odometer')} help={creating ? undefined : t('fleet.vehicle.odometerHelp')}>
        <NumberInput disabled={disabled} className="mono" min={0} />
      </Field>
      <Field name="maxStops" label={t('fleet.vehicle.maxStops')}>
        <NumberInput disabled={disabled} className="mono" min={1} step={1} />
      </Field>
      <Field name="maxWeightKg" label={t('fleet.vehicle.maxWeight')}>
        <NumberInput disabled={disabled} className="mono" min={0} />
      </Field>
      <Field name="maxVolumeM3" label={t('fleet.vehicle.maxVolume')}>
        <NumberInput disabled={disabled} className="mono" min={0} />
      </Field>
    </div>
  )
}
