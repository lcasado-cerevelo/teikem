// Panel «Viajes pagados» (manual 04 §9): lista con filtros de fecha y estatus, «Registrar viaje» (monto congelado con la tarifa por viaje
// vigente en la fecha; sin tarifa nace en $0 y se marca «Sin tarifa») y «Cancelar viaje». Un viaje que nace de una entrega especial no se
// cancela directo: el servidor responde 409 y el diálogo lo muestra. Lee `driverpay.view`; escribe `driverpay.manage`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, DateInput, DateRangeFilter, EMPTY_RANGE, Field, Filters, IconPlus, IconXCircle, Panel, Select, SelectFilter, Spinner, TextArea, toast, useElementWidth, type DataColumn, type DateRange } from '../../kernel/ui'
import { IconRoute } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { useDriverTrips, useDriverTripTypes, useTripMutations, type DriverDetail, type DriverTrip } from './api'
import { CommentDialog } from './CommentDialog'
import { buildTripQuery, buildTripRequest, canCancelTrip, emptyTripValues, tripSchema, TRIP_STATUS_DOMAIN, type TripValues } from './rateRules'

function TripModal({ publicId, onClose }: { publicId: string; onClose: () => void }) {
  const t = useT()
  const today = tenantToday()
  const types = useDriverTripTypes()
  const mutations = useTripMutations(publicId)
  const schema = useMemo(() => tripSchema(t, today), [t, today])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: emptyTripValues(today) })
  const noCatalog = types.isSuccess && (types.data?.length ?? 0) === 0
  return (
    <FormModal
      id="driver-trip"
      title={t('drivers.trips.addTitle')}
      open
      onClose={onClose}
      form={form}
      onSubmit={async (v: TripValues) => {
        await mutations.create.mutateAsync(buildTripRequest(v))
        toast.success(t('drivers.trips.saved'))
        onClose()
      }}
    >
      <p className="help">{t('drivers.trips.addHelp')}</p>
      <Field name="specialServiceTypeId" label={t('drivers.rates.tripType')} required>
        <Select disabled={noCatalog} options={(types.data ?? []).map((x) => ({ value: String(x.id), label: x.name ?? '' }))} placeholder={noCatalog ? t('drivers.rates.noCatalog') : t('drivers.choose')} />
      </Field>
      <Field name="tripDate" label={t('drivers.trips.date')} required>
        <DateInput max={today} />
      </Field>
      <Field name="notes" label={t('drivers.trips.notes')}>
        <TextArea rows={2} maxLength={600} />
      </Field>
    </FormModal>
  )
}

export function DriverTripsPanel({ driver }: { driver: DriverDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = driver.publicId ?? ''
  const canView = useCan('driverpay.view')
  const canManage = useCan('driverpay.manage') && driver.isTerminal !== true
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [status, setStatus] = useState('')
  const [adding, setAdding] = useState(false)
  const [cancelling, setCancelling] = useState<DriverTrip | null>(null)
  const statuses = useStatuses(TRIP_STATUS_DOMAIN)
  const mutations = useTripMutations(publicId)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const query = useMemo(() => buildTripQuery({ from: range.from, to: range.to, status }), [range, status])
  const trips = useDriverTrips(publicId, query, canView)
  const rows = useMemo(() => trips.data ?? [], [trips.data])

  const columns = useMemo<DataColumn<DriverTrip>[]>(
    () => [
      { id: 'date', header: t('drivers.trips.date'), cell: (r) => f.date(r.tripDate), sortValue: (r) => r.tripDate, card: 'title' },
      { id: 'type', header: t('drivers.rates.tripType'), cell: (r) => r.tripType, sortValue: (r) => r.tripType ?? '' },
      {
        id: 'amount',
        header: t('drivers.trips.amount'),
        align: 'end',
        cell: (r) => (
          <span className="mono">
            {f.money(r.amount)} {r.rateMissing && <Chip tone="warn" title={t('drivers.trips.noRateTitle')}>{t('drivers.trips.noRate')}</Chip>}
          </span>
        ),
        sortValue: (r) => r.amount,
      },
      {
        id: 'order',
        header: t('drivers.trips.order'),
        cell: (r) => (r.orderPublicId ? <Link to={`/orders/${r.orderPublicId}`}>{r.orderNumber ?? r.orderPublicId}</Link> : '—'),
        sortValue: (r) => r.orderNumber ?? '',
      },
      { id: 'status', header: t('drivers.trips.status'), cell: (r) => <StatusChip domain={TRIP_STATUS_DOMAIN} code={r.statusCode} label={r.status} />, sortValue: (r) => r.status ?? r.statusCode ?? '' },
      { id: 'notes', header: t('drivers.trips.notes'), cell: (r) => r.notes || '—', sortValue: (r) => r.notes ?? '' },
    ],
    [t, f],
  )

  if (!canView) return null

  return (
    <Panel
      icon={<IconRoute />}
      title={t('drivers.trips.title')}
      badge={trips.data ? rows.length : undefined}
      actions={
        canManage ? (
          <button type="button" className="btn sm flow" onClick={() => setAdding(true)}>
            <IconPlus /> {t('drivers.trips.add')}
          </button>
        ) : undefined
      }
    >
      <div ref={ref}>
        <div className="drv-tripfilters">
          <Filters
            onClear={() => {
              setRange(EMPTY_RANGE)
              setStatus('')
            }}
          >
            <DateRangeFilter label={t('drivers.trips.dates')} value={range} onChange={setRange} />
            <SelectFilter label={t('drivers.trips.status')} value={status} onChange={setStatus} allLabel={t('drivers.trips.openOnes')} options={(statuses.data ?? []).map((s) => ({ value: s.code, label: s.label }))} />
          </Filters>
        </div>
        {trips.isPending ? (
          <Spinner block />
        ) : trips.isError ? (
          <p className="ferr" role="alert">
            {applyProblemDetails(trips.error).title}
          </p>
        ) : rows.length === 0 ? (
          <p className="drv-empty">{t('drivers.trips.empty')}</p>
        ) : (
          <DataTable
            label={t('drivers.trips.title')}
            columns={columns}
            rows={rows}
            rowKey={(r) => r.publicId}
            exportable={false}
            forceCards={width > 0 && width < 640}
            rowClassName={(r) => (r.isActive ? undefined : 'dim')}
            rowActions={[{ key: 'cancel', label: t('drivers.trips.cancel'), icon: <IconXCircle />, tone: 'danger', onClick: (r) => setCancelling(r), visible: (r) => canManage && canCancelTrip(r) }]}
          />
        )}
      </div>
      {adding && <TripModal publicId={publicId} onClose={() => setAdding(false)} />}
      <CommentDialog
        open={cancelling !== null}
        title={t('drivers.trips.cancelTitle')}
        confirmLabel={t('drivers.trips.cancel')}
        onClose={() => setCancelling(null)}
        onConfirm={async (comment) => {
          if (!cancelling) return
          await mutations.cancel.mutateAsync({ tripPublicId: cancelling.publicId, comment })
          toast.success(t('drivers.trips.cancelled'))
        }}
      >
        <p>{t('drivers.trips.cancelBody', { date: cancelling ? f.date(cancelling.tripDate) : '', type: cancelling?.tripType ?? '' })}</p>
      </CommentDialog>
    </Panel>
  )
}
