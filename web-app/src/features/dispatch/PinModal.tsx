// Lote F-E2b — «Poner pin» (manual 05 §4.3): fija a mano la latitud y la longitud de una parada. Lo principal son los dos campos (punto o coma decimal,
// con los mismos textos y rangos del servidor); si hay al menos otra parada con punto, un plano de referencia deja tomar una posición APROXIMADA con un clic.
// Al guardar, la precisión de la parada pasa a «manual» y el servidor recalcula las llegadas; una ruta ya optimizada conviene volver a optimizarla.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, RouteMap, TextInput, type RouteMapStop } from '../../kernel/ui'
import { isGeoPoint } from '../../kernel/ui/routeMapRules'
import { FormModal } from '../clients/contractUi'
import { buildPinRequest, coordText, parseCoordinate, pinSchema, pinValuesOf, type PinValues, type RouteStop } from './dispatchRules'

export function PinModal({
  stop,
  reference,
  onSave,
  onClose,
}: {
  stop: RouteStop
  /** Las demás paradas del plano (las que tienen punto sirven de referencia para el clic). */
  reference: readonly RouteMapStop[]
  onSave: (body: { lat: number; lng: number }) => Promise<unknown>
  onClose: () => void
}) {
  const t = useT()
  const [picked, setPicked] = useState<PinValues | null>(null)
  const schema = useMemo(() => pinSchema(t), [t])
  const form = useForm<PinValues>({ resolver: zodResolver(schema), defaultValues: pinValuesOf(stop) })
  const [lat, lng] = useWatch({ control: form.control, name: ['lat', 'lng'] })
  const others = useMemo(() => reference.filter((r) => String(r.id) !== String(stop.id) && isGeoPoint(r.point)), [reference, stop.id])
  const la = parseCoordinate(lat ?? '')
  const ln = parseCoordinate(lng ?? '')
  const draft = la !== null && ln !== null && isGeoPoint({ lat: la, lng: ln }) ? { lat: la, lng: ln } : null
  const number = stop.orderNumber ?? ''

  return (
    <FormModal
      id="dispatch-pin"
      title={t('dispatch.pin.title', { number })}
      open
      size="md"
      onClose={onClose}
      form={form}
      saveLabel={t('dispatch.pin.save')}
      onSubmit={async (v: PinValues) => {
        const body = buildPinRequest(v, null)
        await onSave({ lat: body.lat, lng: body.lng })
        onClose()
      }}
    >
      <p className="d">{t('dispatch.pin.hint', { name: [stop.consigneeName, stop.city].filter(Boolean).join(' · ') || number })}</p>
      <Field name="lat" label={t('dispatch.pin.lat')} required help={t('dispatch.pin.latHelp')}>
        <TextInput inputMode="decimal" autoComplete="off" />
      </Field>
      <Field name="lng" label={t('dispatch.pin.lng')} required help={t('dispatch.pin.lngHelp')}>
        <TextInput inputMode="decimal" autoComplete="off" />
      </Field>
      {others.length > 0 && (
        <div className="disp-pin-map">
          <h4>{t('dispatch.pin.pickTitle')}</h4>
          <RouteMap
            stops={others}
            draft={draft}
            height={200}
            showLegend={false}
            label={t('dispatch.pin.pickMap')}
            onPick={(p) => {
              const next = { lat: coordText(p.lat), lng: coordText(p.lng) }
              form.setValue('lat', next.lat, { shouldDirty: true, shouldValidate: form.formState.isSubmitted })
              form.setValue('lng', next.lng, { shouldDirty: true, shouldValidate: form.formState.isSubmitted })
              setPicked(next)
            }}
          />
          {picked && picked.lat === lat && picked.lng === lng && (
            <p className="note" role="status">
              {t('dispatch.pin.pickedApprox')}
            </p>
          )}
        </div>
      )}
      <p className="disp-note">{t('dispatch.pin.after')}</p>
    </FormModal>
  )
}
