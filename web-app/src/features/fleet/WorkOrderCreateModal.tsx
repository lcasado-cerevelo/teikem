// «Nueva orden de trabajo» (manual 04 §6.1): vehículo activo, tipo de mantenimiento (o un programa preventivo que aplique al vehículo, por
// él o por su tipo), fecha programada, odómetro, proveedor, costos y notas. El número OT-##### lo asigna el sistema; la orden nace «Abierta».
// Se abre desde la pestaña Órdenes de trabajo y desde cada renglón del panel «Mantenimiento preventivo» (ya con el vehículo y el programa).
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, NumberInput, Select, Spinner, TextArea, TextInput, toast } from '../../kernel/ui'
import { lookupOptions } from '../clients/contractRules'
import { FormModal } from '../clients/contractUi'
import { useCreateWorkOrder, useSchedules } from './maintenanceApi'
import { buildWorkOrderCreate, emptyWorkOrderCreateValues, MaintenanceTypes, schedulesForVehicle, workOrderCreateSchema, type WorkOrderCreateValues } from './maintenanceRules'
import { useVehicleChoices } from './vehicleChoices'

export interface WorkOrderCreateModalProps {
  /** Valores de partida (vehículo y programa cuando se crea desde el panel de vencimientos). */
  init?: Partial<WorkOrderCreateValues>
  onClose: () => void
  onCreated: (publicId: string, number: string) => void
}

export function WorkOrderCreateModal({ init, onClose, onCreated }: WorkOrderCreateModalProps) {
  const t = useT()
  const vehicles = useVehicleChoices()
  const schedules = useSchedules(false)
  const create = useCreateWorkOrder()
  const types = useLookups('MaintenanceType')
  const schema = useMemo(() => workOrderCreateSchema(t), [t])
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: emptyWorkOrderCreateValues({ ...init, maintenanceType: init?.scheduleId ? MaintenanceTypes.Preventive : (init?.maintenanceType ?? '') }),
  })
  const vehicleId = useWatch({ control: form.control, name: 'vehiclePublicId' })
  const scheduleId = useWatch({ control: form.control, name: 'scheduleId' })
  // los selects se pintan cuando ya traen sus opciones: así el vehículo, el tipo y el programa de partida quedan elegidos
  const ready = !vehicles.isPending && !schedules.isPending && !types.isPending
  const applicable = useMemo(() => schedulesForVehicle(schedules.data ?? [], vehicles.find(vehicleId)), [schedules.data, vehicles, vehicleId])

  // con un programa la orden es preventiva; si el vehículo cambia y el programa ya no le aplica, se quita
  useEffect(() => {
    if (scheduleId) form.setValue('maintenanceType', MaintenanceTypes.Preventive)
  }, [scheduleId, form])
  useEffect(() => {
    if (scheduleId && schedules.isSuccess && vehicles.vehicles.length > 0 && !applicable.some((s) => String(s.id) === scheduleId)) form.setValue('scheduleId', '')
  }, [scheduleId, schedules.isSuccess, vehicles.vehicles.length, applicable, form])

  return (
    <FormModal
      id="work-order-create"
      title={t('fleet.workOrders.create.title')}
      open
      size="md"
      onClose={onClose}
      form={form}
      onSubmit={async (v: WorkOrderCreateValues) => {
        const dto = await create.mutateAsync(buildWorkOrderCreate(v))
        toast.success(t('fleet.workOrders.created', { number: dto.number ?? '' }))
        onClose()
        onCreated(dto.publicId ?? '', dto.number ?? '')
      }}
    >
      {!ready ? (
        <Spinner block />
      ) : (
        <>
          <Field name="vehiclePublicId" label={t('fleet.workOrders.create.vehicle')} required>
            <Select options={vehicles.options} placeholder={t('fleet.workOrders.create.choose')} />
          </Field>
          <div className="r2">
            <Field name="maintenanceType" label={t('fleet.workOrders.create.type')} required>
              <Select options={lookupOptions(types.data ?? [])} placeholder={t('fleet.workOrders.create.choose')} />
            </Field>
            <Field name="scheduleId" label={t('fleet.workOrders.create.schedule')} help={t('fleet.workOrders.create.scheduleHelp')}>
              <Select options={applicable.map((s) => ({ value: String(s.id), label: s.name ?? '' }))} placeholder={t('fleet.workOrders.create.noSchedule')} />
            </Field>
          </div>
          <div className="r2">
            <Field name="scheduledDate" label={t('fleet.workOrders.create.scheduledDate')}>
              <DateInput />
            </Field>
            <Field name="odometerKm" label={t('fleet.workOrders.create.odometer')}>
              <NumberInput className="mono" min={0} />
            </Field>
          </div>
          <Field name="vendor" label={t('fleet.workOrders.create.vendor')}>
            <TextInput autoComplete="off" />
          </Field>
          <div className="r2">
            <Field name="laborCost" label={t('fleet.workOrders.create.labor')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="partsCost" label={t('fleet.workOrders.create.parts')}>
              <NumberInput className="mono" min={0} />
            </Field>
          </div>
          <p className="help">{t('fleet.workOrders.create.costsHelp')}</p>
          <Field name="notes" label={t('fleet.workOrders.create.notes')}>
            <TextArea rows={2} />
          </Field>
        </>
      )}
    </FormModal>
  )
}
