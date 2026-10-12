// «Ficha del vehículo» (maqueta: la fila de la tabla con el lápiz y la papelera). Los campos se editan aquí mismo con un solo «Guardar
// cambios» (PATCH solo de lo cambiado + `rowVersion`; 409 → se relee la ficha y se conserva lo escrito). El estado son TRES cosas
// distintas y rotuladas como tales: la casilla «Activo» (deactivate/reactivate), la «Disponibilidad» (estatus Disponible / En mantenimiento)
// y «Dar de baja definitiva» (estatus INACTIVE, terminal; en lugar de la papelera de la maqueta: el servidor no borra). Un vehículo dado de
// baja solo se consulta.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useT } from '../../kernel/i18n'
import { Chip, Form, IconTrash, IconTruck, Panel, toast } from '../../kernel/ui'
import { CommentDialog } from '../drivers/CommentDialog'
import { fleetKeys, useSetVehicleActive, useUpdateVehicle, useVehicleStatusChange, type VehicleDetail } from './api'
import { buildVehiclePatch, vehicleSchema, vehicleState, vehicleValuesOf, VehicleStatuses, type VehicleValues } from './fleetRules'
import { VehicleFields } from './VehicleFields'

export function VehicleProfilePanel({ vehicle }: { vehicle: VehicleDetail }) {
  const t = useT()
  const qc = useQueryClient()
  const publicId = vehicle.publicId ?? ''
  const canManage = useCan('fleet.manage')
  const retired = vehicle.isTerminal === true
  const editable = canManage && !retired
  const update = useUpdateVehicle(publicId)
  const setActive = useSetVehicleActive(publicId)
  const changeStatus = useVehicleStatusChange(publicId)
  const [retiring, setRetiring] = useState(false)
  const [pendingActive, setPendingActive] = useState<boolean | null>(null)

  const year = Number(tenantToday().slice(0, 4))
  const initial = useMemo(() => vehicleValuesOf(vehicle), [vehicle])
  const schema = useMemo(() => vehicleSchema(t, { creating: false, original: initial, currentYear: year }), [t, initial, year])
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })
  const state = vehicleState(vehicle)

  const submit = async (v: VehicleValues) => {
    const plan = buildVehiclePatch(v, vehicle)
    if (!plan.changed) return
    try {
      const saved = await update.mutateAsync(plan.request)
      // lo guardado (recortado y normalizado por el servidor) pasa a ser el valor de partida: el botón vuelve a apagarse
      form.reset(vehicleValuesOf(saved), { keepDirtyValues: false })
    } catch (err) {
      // 409: otro usuario guardó antes; se relee la ficha (y se conserva lo escrito) para tener el `rowVersion` nuevo
      if (applyProblemDetails(err).code === 'conflict') void qc.invalidateQueries({ queryKey: fleetKeys.detail(publicId) })
      throw err
    }
    toast.success(t('fleet.vehicle.saved'))
  }

  const toggleActive = async (next: boolean) => {
    setPendingActive(next)
    try {
      await setActive.mutateAsync(next)
      toast.success(t(next ? 'fleet.vehicle.reactivated' : 'fleet.vehicle.deactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    } finally {
      setPendingActive(null)
    }
  }

  const moveTo = async (toCode: string) => {
    try {
      await changeStatus.mutateAsync({ toCode })
      toast.success(t(toCode === VehicleStatuses.Maintenance ? 'fleet.vehicle.madeMaintenance' : 'fleet.vehicle.madeAvailable'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const available = (vehicle.statusCode ?? '').toUpperCase() === VehicleStatuses.Active
  // la casilla responde al clic de inmediato (mientras viaja la petición muestra lo pedido; si falla vuelve a lo que había)
  const activeShown = pendingActive ?? vehicle.isActive === true

  return (
    <Panel
      icon={<IconTruck />}
      title={t('fleet.vehicle.info')}
      actions={
        editable ? (
          <button type="button" className="btn sm flt-danger" onClick={() => setRetiring(true)}>
            <IconTrash /> {t('fleet.vehicle.retire')}
          </button>
        ) : undefined
      }
    >
      {retired && (
        <p className="note flt-retired" role="status">
          {t('fleet.vehicle.retiredNote')}
        </p>
      )}
      <Form form={form} onSubmit={submit}>
        <VehicleFields creating={false} code={vehicle.code ?? ''} disabled={!editable} current={initial} />
        {editable && (
          <div className="form-acts flt-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('fleet.vehicle.saveChanges')}
            </button>
          </div>
        )}
      </Form>

      <div className="flt-state">
        <div className="f">
          <label>{t('fleet.vehicle.active')}</label>
          <label className="sw">
            <input
              type="checkbox"
              role="switch"
              checked={activeShown}
              disabled={!editable || setActive.isPending}
              aria-label={t('fleet.vehicle.active')}
              onChange={(e) => void toggleActive(e.target.checked)}
            />
            <span className="tk" aria-hidden="true" />
            <span>{activeShown ? t('fleet.vehicle.active') : t('fleet.vehicle.inactive')}</span>
          </label>
          <p className="help">{t('fleet.vehicle.activeHelp')}</p>
        </div>
        <div className="f">
          <label id="flt-avail-label">{t('fleet.vehicle.availability')}</label>
          {retired ? (
            <Chip tone="fail">{t('fleet.vehicle.retired')}</Chip>
          ) : (
            <div className="seg" role="group" aria-labelledby="flt-avail-label">
              <button type="button" className={available ? 'on' : ''} aria-pressed={available} disabled={!editable || changeStatus.isPending} onClick={() => !available && void moveTo(VehicleStatuses.Active)}>
                {t('fleet.vehicle.available')}
              </button>
              <button type="button" className={!available ? 'on' : ''} aria-pressed={!available} disabled={!editable || changeStatus.isPending} onClick={() => available && void moveTo(VehicleStatuses.Maintenance)}>
                {t('fleet.vehicle.maintenance')}
              </button>
            </div>
          )}
          <p className="help">{state === 'inactive' ? t('fleet.vehicle.availabilityInactive') : t('fleet.vehicle.availabilityHelp')}</p>
        </div>
      </div>

      <Can perm="fleet.manage">
        <CommentDialog
          open={retiring}
          title={t('fleet.vehicle.retireTitle', { code: vehicle.code ?? '' })}
          confirmLabel={t('fleet.vehicle.retire')}
          onClose={() => setRetiring(false)}
          onConfirm={async (comment) => {
            await changeStatus.mutateAsync({ toCode: VehicleStatuses.Inactive, comment })
            toast.success(t('fleet.vehicle.retiredDone'))
          }}
        >
          <p>{t('fleet.vehicle.retireBody')}</p>
          <ul className="flt-effects">
            <li>{t('fleet.vehicle.retireEffect.inactive')}</li>
            <li>{t('fleet.vehicle.retireEffect.dispatch')}</li>
            <li>{t('fleet.vehicle.retireEffect.history')}</li>
          </ul>
          <p>{t('fleet.vehicle.retireIrreversible')}</p>
        </CommentDialog>
      </Can>
    </Panel>
  )
}
