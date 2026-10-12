// «Ficha del chofer» (maqueta: Código, Nombre, Zona, Área, Tope de paradas, Activo, botón rojo «Eliminar chofer»). Los campos se editan
// aquí mismo con un solo «Guardar cambios» (PATCH solo de lo cambiado + `rowVersion`; 409 → se relee y se conserva lo escrito).
// El estado del chofer son TRES cosas distintas y rotuladas como tales: la casilla «Activo» (deactivate/reactivate), la
// «Disponibilidad» (estatus Disponible / No disponible) y «Eliminar chofer» (baja definitiva). Un chofer eliminado solo se consulta.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useTenantSettings } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip, DateInput, Field, Form, IconTag, IconTrash, NumberInput, Panel, Select, TextInput, toast } from '../../kernel/ui'
import { driverKeys, useDispatchZones, useDriverStatusChange, useLinkableUsers, useRetireDriver, useSetDriverActive, useUpdateDriver, type DriverDetail } from './api'
import { CommentDialog } from './CommentDialog'
import { buildDriverPatch, driverSchema, driverState, driverValuesOf, DriverStatuses, linkableUsers, userLabel } from './driverRules'
import { zoneOptions } from './zoneOptions'

export function DriverProfilePanel({ driver }: { driver: DriverDetail }) {
  const t = useT()
  const qc = useQueryClient()
  const publicId = driver.publicId ?? ''
  const canManage = useCan('fleet.manage')
  const canLinkUser = useCan('admin.users')
  const retired = driver.isTerminal === true
  const editable = canManage && !retired
  const zones = useDispatchZones()
  const users = useLinkableUsers(canLinkUser && editable)
  const { data: settings } = useTenantSettings()
  const update = useUpdateDriver(publicId)
  const setActive = useSetDriverActive(publicId)
  const changeStatus = useDriverStatusChange(publicId)
  const retire = useRetireDriver(publicId)
  const [retiring, setRetiring] = useState(false)
  const [pendingActive, setPendingActive] = useState<boolean | null>(null)

  const schema = useMemo(() => driverSchema(t, { creating: false, hadHireDate: Boolean(driver.hireDate) }), [t, driver.hireDate])
  const initial = useMemo(() => driverValuesOf(driver), [driver])
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })
  const zoneId = form.watch('dispatchZoneId')
  const zoneList = zones.data
  const areaName = zoneId ? (zoneList?.find((z) => String(z.id) === zoneId)?.name ?? (zoneId === String(driver.dispatchZoneId ?? '') ? driver.area : '')) : ''
  const state = driverState(driver)

  const submit = async (v: ReturnType<typeof driverValuesOf>) => {
    const plan = buildDriverPatch(v, driver, { canLinkUser })
    if (!plan.changed) return
    try {
      const saved = await update.mutateAsync(plan.request)
      // lo guardado (recortado por el servidor) pasa a ser el valor de partida: el botón vuelve a apagarse
      form.reset(driverValuesOf(saved), { keepDirtyValues: false })
    } catch (err) {
      // 409: otro usuario guardó antes; se relee la ficha (y se conserva lo escrito) para tener el `rowVersion` nuevo
      if (applyProblemDetails(err).code === 'conflict') void qc.invalidateQueries({ queryKey: driverKeys.detail(publicId) })
      throw err
    }
    toast.success(t('drivers.saved'))
  }

  const toggleActive = async (next: boolean) => {
    setPendingActive(next)
    try {
      await setActive.mutateAsync(next)
      toast.success(t(next ? 'drivers.reactivated' : 'drivers.deactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    } finally {
      setPendingActive(null)
    }
  }

  const moveTo = async (toCode: string) => {
    try {
      await changeStatus.mutateAsync({ toCode })
      toast.success(t(toCode === DriverStatuses.Unavailable ? 'drivers.madeUnavailable' : 'drivers.madeAvailable'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const available = (driver.statusCode ?? '').toUpperCase() === DriverStatuses.Active
  // la casilla responde al clic de inmediato (mientras viaja la petición muestra lo pedido; si falla vuelve a lo que había)
  const activeShown = pendingActive ?? driver.isActive === true

  return (
    <Panel
      icon={<IconTag />}
      title={t('drivers.info')}
      actions={
        editable ? (
          <button type="button" className="btn sm drv-danger" onClick={() => setRetiring(true)}>
            <IconTrash /> {t('drivers.delete')}
          </button>
        ) : undefined
      }
    >
      {retired && (
        <p className="note drv-retired" role="status">
          {t('drivers.retiredNote')}
        </p>
      )}
      <Form form={form} onSubmit={submit}>
        <div className="drv-grid">
          <div className="f">
            <label htmlFor="drv-code">{t('drivers.code')}</label>
            <input id="drv-code" className="mono" value={driver.code ?? ''} disabled readOnly />
          </div>
          <Field name="fullName" label={t('drivers.name')} required>
            <TextInput disabled={!editable} autoComplete="off" />
          </Field>
          <Field name="dispatchZoneId" label={t('drivers.zone')}>
            <Select disabled={!editable} options={zoneOptions(zoneList, { id: driver.dispatchZoneId, code: driver.zoneCode, name: driver.area })} placeholder={t('drivers.noZone')} />
          </Field>
          <div className="f">
            <label id="drv-area-label">{t('drivers.area')}</label>
            <div className="drv-readonly" aria-labelledby="drv-area-label" data-testid="driver-area">
              {areaName || '—'}
            </div>
          </div>
          <Field
            name="maxStopsPerRoute"
            label={t('drivers.maxStops')}
            help={driver.maxStopsPerRoute == null && driver.effectiveMaxStops != null ? t('drivers.maxStopsEffective', { n: driver.effectiveMaxStops }) : t('drivers.maxStopsHelp')}
          >
            <NumberInput disabled={!editable} className="mono" min={1} step={1} placeholder={settings?.maxStopsPerRouteDefault != null ? String(settings.maxStopsPerRouteDefault) : undefined} />
          </Field>
          <Field name="hireDate" label={t('drivers.hireDate')}>
            <DateInput disabled={!editable} />
          </Field>
          {canLinkUser && editable ? (
            <Field name="userId" label={t('drivers.user')} help={t('drivers.userHelp')} className="wide">
              <Select options={linkableUsers(users.data, driver.user)} placeholder={t('drivers.noUser')} />
            </Field>
          ) : (
            <div className="f wide">
              <label>{t('drivers.user')}</label>
              <div className="drv-readonly">{driver.user ? userLabel(driver.user.fullName, driver.user.email) : t('drivers.noUser')}</div>
            </div>
          )}
        </div>
        {editable && (
          <div className="form-acts drv-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('drivers.saveChanges')}
            </button>
          </div>
        )}
      </Form>

      <div className="drv-state">
        <div className="f">
          <label>{t('drivers.active')}</label>
          <label className="sw">
            <input
              type="checkbox"
              role="switch"
              checked={activeShown}
              disabled={!editable || setActive.isPending}
              aria-label={t('drivers.active')}
              onChange={(e) => void toggleActive(e.target.checked)}
            />
            <span className="tk" aria-hidden="true" />
            <span>{activeShown ? t('drivers.active') : t('drivers.inactive')}</span>
          </label>
          <p className="help">{t('drivers.activeHelp')}</p>
        </div>
        <div className="f">
          <label id="drv-avail-label">{t('drivers.availability')}</label>
          {retired ? (
            <Chip tone="fail">{t('drivers.removed')}</Chip>
          ) : (
            <div className="seg" role="group" aria-labelledby="drv-avail-label">
              <button type="button" className={available ? 'on' : ''} aria-pressed={available} disabled={!editable || changeStatus.isPending} onClick={() => !available && void moveTo(DriverStatuses.Active)}>
                {t('drivers.available')}
              </button>
              <button type="button" className={!available ? 'on' : ''} aria-pressed={!available} disabled={!editable || changeStatus.isPending} onClick={() => available && void moveTo(DriverStatuses.Unavailable)}>
                {t('drivers.unavailable')}
              </button>
            </div>
          )}
          <p className="help">{state === 'inactive' ? t('drivers.availabilityInactive') : t('drivers.availabilityHelp')}</p>
        </div>
      </div>

      <Can perm="fleet.manage">
        <CommentDialog
          open={retiring}
          title={t('drivers.deleteTitle', { name: driver.fullName ?? '' })}
          confirmLabel={t('drivers.delete')}
          onClose={() => setRetiring(false)}
          onConfirm={async (comment) => {
            await retire.mutateAsync({ comment, driverId: driver.id })
            toast.success(t('drivers.deleted'))
          }}
        >
          <p>{t('drivers.deleteBody')}</p>
          <ul className="drv-effects">
            <li>{t('drivers.deleteEffect.user')}</li>
            <li>{t('drivers.deleteEffect.devices')}</li>
            <li>{t('drivers.deleteEffect.zone')}</li>
            <li>{t('drivers.deleteEffect.rates')}</li>
            <li>{t('drivers.deleteEffect.trips')}</li>
          </ul>
          <p>{t('drivers.deleteIrreversible')}</p>
        </CommentDialog>
      </Can>
    </Panel>
  )
}
