// Lote F-E2a — «Reasignar por zona» (manual 05 §2): cambia el chofer de TODAS las rutas abiertas (borrador o planificadas) de una o varias
// zonas en una fecha. Cero rutas no es un error: se informa. El resultado trae los avisos de doble asignación del chofer.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import type { components } from '../../kernel/api/schema'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Modal, SearchMultiSelectInput, Select } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import type { DispatchZone } from '../drivers/api'
import { useReassignZone } from './dispatchApi'
import { buildReassignRequest, planDateRange, reassignSchema, type ReassignValues } from './dispatchRules'
import { useFleetChoices } from './fleetChoices'
import { TripIssues } from './TripIssues'

type ReassignResult = components['schemas']['ZoneReassignResultDto']

export function ReassignZoneModal({
  date,
  zones,
  canFleet,
  onDone,
  onClose,
}: {
  date: string
  zones: readonly DispatchZone[]
  canFleet: boolean
  onDone: (planDate: string) => void
  onClose: () => void
}) {
  const t = useT()
  const f = useFormat()
  const today = tenantToday()
  const reassign = useReassignZone()
  const [result, setResult] = useState<ReassignResult | null>(null)
  const schema = useMemo(() => reassignSchema(t, today), [t, today])
  const range = planDateRange(today)
  const initial = date < range.min || date > range.max ? today : date
  const form = useForm<ReassignValues>({ resolver: zodResolver(schema), defaultValues: { planDate: initial, zoneIds: [], driverPublicId: '' } })
  const planDate = useWatch({ control: form.control, name: 'planDate' })
  const choices = useFleetChoices(planDate, canFleet)
  const zoneOptions = zones.filter((z) => z.isActive !== false).map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') }))

  if (result) {
    const n = result.tripsUpdated ?? 0
    return (
      <Modal
        open
        size="md"
        title={t('dispatch.reassign.resultTitle')}
        onClose={onClose}
        footer={
          <button type="button" className="btn flow" onClick={onClose}>
            {t('dispatch.closeButton')}
          </button>
        }
      >
        <p role="status">
          {n === 0
            ? t('dispatch.reassign.none')
            : t('dispatch.reassign.updated', { count: f.number(n), driver: [result.driverCode, result.driverName].filter(Boolean).join(' · ') })}
        </p>
        {n > 0 && (
          <ul className="disp-results">
            {(result.trips ?? []).map((tr) => (
              <li key={tr.publicId}>
                <b className="mono">{tr.code}</b> · {tr.zoneCode}
              </li>
            ))}
          </ul>
        )}
        <TripIssues issues={result.issues} />
      </Modal>
    )
  }

  return (
    <FormModal
      id="dispatch-reassign"
      title={t('dispatch.reassign.title')}
      open
      size="md"
      onClose={onClose}
      form={form}
      saveLabel={t('dispatch.reassign.run')}
      onSubmit={async (v: ReassignValues) => {
        const res = await reassign.mutateAsync(buildReassignRequest(v))
        setResult(res)
        onDone(v.planDate)
      }}
    >
      <p className="d">{t('dispatch.reassign.hint')}</p>
      <Field name="planDate" label={t('dispatch.reassign.date')} required>
        <DateInput min={range.min} max={range.max} />
      </Field>
      {canFleet ? (
        <>
          <Field name="zoneIds" label={t('dispatch.reassign.zones')} required>
            <SearchMultiSelectInput options={zoneOptions} placeholder={t('dispatch.reassign.chooseZones')} />
          </Field>
          <Field name="driverPublicId" label={t('dispatch.reassign.driver')} required>
            {choices.isPending ? <Select options={[]} placeholder={t('common.loading')} disabled /> : <Select options={choices.drivers} placeholder={t('dispatch.reassign.chooseDriver')} />}
          </Field>
        </>
      ) : (
        <p className="note" role="status">
          {t('dispatch.reassign.noFleetAccess')}
        </p>
      )}
    </FormModal>
  )
}
