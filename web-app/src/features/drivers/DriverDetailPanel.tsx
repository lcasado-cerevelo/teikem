// Ficha del chofer (derecha de la pantalla): Ficha del chofer, Licencias y certificaciones, Campos personalizados (DRIVER), Contactos,
// Tarifas por entrega e intento, Tarifas por viaje, Viajes pagados, Dispositivos e Historial de estatus. Cada panel respeta su propio
// permiso (`fleet.*`, `driverpay.*`, `contacts.manage`) y un chofer eliminado solo se consulta.
import { useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusHistory } from '../../kernel/catalogs'
import { CustomFieldsForm, useCustomFieldDefinitions, useSaveCustomFields } from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import { EmptyState, Form, IconClock, IconTag, IconUsers, Panel, Spinner, toast } from '../../kernel/ui'
import { ContactPointsPanel } from '../contacts/ContactPointsPanel'
import { contactKeys, useContactPoints } from '../contacts/api'
import { DRIVER_ENTITY_TYPE, useDriver, type DriverDetail } from './api'
import { DriverDevicesPanel } from './DriverDevicesPanel'
import { DriverDocumentsPanel } from './DriverDocumentsPanel'
import { DriverNoticePanel } from './DriverNoticePanel'
import { DRIVER_STATUS_DOMAIN } from './driverRules'
import { DriverProfilePanel } from './DriverProfilePanel'
import { DriverRatesPanels } from './DriverRatesPanels'
import { DriverTripsPanel } from './DriverTripsPanel'

function DriverCustomFieldsPanel({ driver }: { driver: DriverDetail }) {
  const t = useT()
  const canEdit = useCan('fleet.manage') && driver.isTerminal !== true
  const definitions = useCustomFieldDefinitions(DRIVER_ENTITY_TYPE)
  const { save } = useSaveCustomFields(DRIVER_ENTITY_TYPE)
  const form = useForm<{ customFields: Record<string, unknown> }>({ defaultValues: { customFields: {} } })

  // sin definiciones (o sin permiso para leerlas) no hay nada que mostrar
  if (!definitions.data || definitions.data.length === 0) return null
  return (
    <Panel icon={<IconTag />} title={t('drivers.custom.title')}>
      <Form
        form={form}
        onSubmit={async () => {
          const problem = await save(driver.id, form)
          if (problem) {
            toast.error(problem.title)
            return
          }
          toast.success(t('drivers.saved'))
        }}
      >
        <CustomFieldsForm entityType={DRIVER_ENTITY_TYPE} entityId={driver.id} form={form} disabled={!canEdit} />
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

function DriverContactsPanel({ driver }: { driver: DriverDetail }) {
  const qc = useQueryClient()
  const contacts = useContactPoints(DRIVER_ENTITY_TYPE, driver.id)
  // sin permiso para leer contactos (403) no hay nada que mostrar ni que editar
  if (contacts.isError) return null
  return (
    <ContactPointsPanel
      ownerEntity={DRIVER_ENTITY_TYPE}
      ownerId={driver.id}
      points={contacts.data ?? []}
      perms={['contacts.manage', 'fleet.manage']}
      readOnly={driver.isTerminal === true}
      idPrefix="driver"
      emptyKey="drivers.contacts.empty"
      removeBodyKey="drivers.contacts.removeBody"
      onChanged={() => qc.invalidateQueries({ queryKey: contactKeys.of(DRIVER_ENTITY_TYPE, driver.id) })}
    />
  )
}

export function DriverDetailPanel({ publicId }: { publicId: string }) {
  const t = useT()
  const { data: driver, isPending, error } = useDriver(publicId)

  if (isPending) {
    return (
      <Panel>
        <Spinner block />
      </Panel>
    )
  }
  if (error || !driver) {
    return (
      <Panel>
        <EmptyState icon={<IconUsers />} title={error ? applyProblemDetails(error).title : t('drivers.notFound')} />
      </Panel>
    )
  }

  return (
    <div className="drv-stack">
      <DriverProfilePanel driver={driver} />
      <DriverDocumentsPanel driver={driver} />
      <DriverCustomFieldsPanel driver={driver} />
      <DriverContactsPanel driver={driver} />
      <DriverRatesPanels driver={driver} />
      <DriverTripsPanel driver={driver} />
      <DriverDevicesPanel driver={driver} />
      <DriverNoticePanel driver={driver} />
      <Panel icon={<IconClock />} title={t('drivers.history')}>
        <StatusHistory entityType={DRIVER_ENTITY_TYPE} entityId={driver.id} domain={DRIVER_STATUS_DOMAIN} />
      </Panel>
    </div>
  )
}
