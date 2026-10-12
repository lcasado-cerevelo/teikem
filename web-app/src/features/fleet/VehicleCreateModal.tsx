// «Nuevo vehículo» (maqueta `renderVehicleModalHtml`). POST /vehicles; los errores del servidor salen bajo cada campo (código repetido
// 409 «Ya existe un vehículo con ese código.», VIN repetido 409, catálogo desconocido 400…).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useT } from '../../kernel/i18n'
import { toast } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import { useCreateVehicle } from './api'
import { buildVehicleCreate, emptyVehicleValues, vehicleSchema, type VehicleValues } from './fleetRules'
import { VehicleFields } from './VehicleFields'

export function VehicleCreateModal({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: (publicId: string) => void }) {
  const t = useT()
  const create = useCreateVehicle()
  const year = Number(tenantToday().slice(0, 4))
  const schema = useMemo(() => vehicleSchema(t, { creating: true, currentYear: year }), [t, year])
  const initial = useMemo(() => emptyVehicleValues(), [])
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })

  const close = () => {
    form.reset(initial, { keepDirtyValues: false })
    onClose()
  }

  return (
    <FormModal
      id="vehicle-create"
      title={t('fleet.vehicle.new')}
      open={open}
      size="md"
      onClose={close}
      form={form}
      onSubmit={async (v: VehicleValues) => {
        const created = await create.mutateAsync(buildVehicleCreate(v))
        toast.success(t('fleet.vehicle.created'))
        form.reset(initial, { keepDirtyValues: false })
        onClose()
        if (created.publicId) onSaved(created.publicId)
      }}
    >
      <VehicleFields creating />
    </FormModal>
  )
}
