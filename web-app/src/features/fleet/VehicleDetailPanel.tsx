// Ficha del vehículo (derecha de la lista): Ficha, Documentos del vehículo, Campos personalizados (VEHICLE), Contactos (VEHICLE) e
// Historial de estatus. Cada panel respeta su permiso (`fleet.*`, `contacts.manage`) y un vehículo dado de baja solo se consulta.
import { useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusHistory } from '../../kernel/catalogs'
import { CustomFieldsForm, useCustomFieldDefinitions, useSaveCustomFields } from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import { EmptyState, Form, IconClock, IconTag, IconTruck, Panel, Spinner, toast } from '../../kernel/ui'
import { ContactPointsPanel } from '../contacts/ContactPointsPanel'
import { contactKeys, useContactPoints } from '../contacts/api'
import { useVehicle, VEHICLE_ENTITY_TYPE, type VehicleDetail } from './api'
import { VehicleDocumentsPanel } from './VehicleDocumentsPanel'
import { VehicleProfilePanel } from './VehicleProfilePanel'

export const VEHICLE_STATUS_DOMAIN = 'VehicleStatus'

function VehicleCustomFieldsPanel({ vehicle }: { vehicle: VehicleDetail }) {
  const t = useT()
  const canEdit = useCan('fleet.manage') && vehicle.isTerminal !== true
  const definitions = useCustomFieldDefinitions(VEHICLE_ENTITY_TYPE)
  const { save } = useSaveCustomFields(VEHICLE_ENTITY_TYPE)
  const form = useForm<{ customFields: Record<string, unknown> }>({ defaultValues: { customFields: {} } })

  // sin definiciones (o sin permiso para leerlas) no hay nada que mostrar
  if (!definitions.data || definitions.data.length === 0) return null
  return (
    <Panel icon={<IconTag />} title={t('fleet.custom.title')}>
      <Form
        form={form}
        onSubmit={async () => {
          const problem = await save(vehicle.id, form)
          if (problem) {
            toast.error(problem.title)
            return
          }
          toast.success(t('fleet.vehicle.saved'))
        }}
      >
        <CustomFieldsForm entityType={VEHICLE_ENTITY_TYPE} entityId={vehicle.id} form={form} disabled={!canEdit} />
        {canEdit && (
          <div className="form-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
            </button>
          </div>
        )}
      </Form>
    </Panel>
  )
}

function VehicleContactsPanel({ vehicle }: { vehicle: VehicleDetail }) {
  const qc = useQueryClient()
  const contacts = useContactPoints(VEHICLE_ENTITY_TYPE, vehicle.id)
  // sin permiso para leer contactos (403) no hay nada que mostrar ni que editar
  if (contacts.isError) return null
  return (
    <ContactPointsPanel
      ownerEntity={VEHICLE_ENTITY_TYPE}
      ownerId={vehicle.id}
      points={contacts.data ?? []}
      perms={['contacts.manage', 'fleet.manage']}
      readOnly={vehicle.isTerminal === true}
      idPrefix="vehicle"
      emptyKey="fleet.contacts.empty"
      removeBodyKey="fleet.contacts.removeBody"
      onChanged={() => qc.invalidateQueries({ queryKey: contactKeys.of(VEHICLE_ENTITY_TYPE, vehicle.id) })}
    />
  )
}

export function VehicleDetailPanel({ publicId }: { publicId: string }) {
  const t = useT()
  const { data: vehicle, isPending, error } = useVehicle(publicId)

  if (isPending) {
    return (
      <Panel>
        <Spinner block />
      </Panel>
    )
  }
  if (error || !vehicle) {
    return (
      <Panel>
        <EmptyState icon={<IconTruck />} title={error ? applyProblemDetails(error).title : t('fleet.vehicle.notFound')} />
      </Panel>
    )
  }

  return (
    <div className="flt-stack">
      <VehicleProfilePanel vehicle={vehicle} />
      <VehicleDocumentsPanel vehicle={vehicle} />
      <VehicleCustomFieldsPanel vehicle={vehicle} />
      <VehicleContactsPanel vehicle={vehicle} />
      <Panel icon={<IconClock />} title={t('fleet.vehicle.history')}>
        <StatusHistory entityType={VEHICLE_ENTITY_TYPE} entityId={vehicle.id} domain={VEHICLE_STATUS_DOMAIN} />
      </Panel>
    </div>
  )
}
