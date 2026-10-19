// Paneles de tarifas del chofer (maqueta: «Tarifas por entrega e intento» y «Tarifas por viaje»; manual 04 §8). Son efectivo-fechadas:
// «Editar» abre una versión nueva desde una fecha (por defecto hoy; el servidor cierra la fila abierta ahí) y «Cerrar tarifa» cierra sin
// borrar. «Sin tarifa» (el nivel no tiene fila) es distinto de $0. El historial (filas cerradas) solo se ve con «Ver historial».
// Lee `driverpay.view`; escribe `driverpay.manage`; un chofer eliminado solo se consulta.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, DateInput, EmptyState, Field, Filters, Form, IconEdit, IconPlus, IconXCircle, NumberInput, Panel, Select, SearchSelect, Spinner, toast, useElementWidth, type DataColumn } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import { lookupOptions, renameErrors, rethrowRenamed } from '../clients/contractRules'
import { FormModal } from '../clients/contractUi'
import { useZoneGroups } from '../fleet/api'
import { useAddAttemptLevel, useDriverRates, useDriverTripTypes, useRateMutations, type AttemptRate, type DeliveryRate, type DriverDetail, type TripRate } from './api'
import {
  attemptCellKind,
  buildDeliveryRateCreate,
  buildRateUpdate,
  buildTripRateCreate,
  emptyRateValues,
  filterDeliveryRows,
  filterTripRows,
  isOpenRow,
  MAX_ATTEMPT_LEVELS,
  rateSchema,
  uniqueOptions,
  type RateValues,
} from './rateRules'

// ---- Editar una tarifa (nueva versión) o fijar la de un intento ----
interface EditTarget {
  title: string
  /** Qué tarifa es (servicio · paquete, tipo de viaje, intento). */
  subtitle: string
  rate: number | null
  /** true = no hay fila abierta que cerrar (alta del nivel de intento): la ayuda cambia. */
  creating: boolean
  submit: (v: RateValues) => Promise<unknown>
}

function RateEditModal({ target, onClose }: { target: EditTarget; onClose: () => void }) {
  const t = useT()
  const today = tenantToday()
  const schema = useMemo(() => rateSchema(t, { needs: false, needsDate: true, today }), [t, today])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { ...emptyRateValues(today), rate: target.rate } })
  return (
    <FormModal
      id="driver-rate-edit"
      title={target.title}
      open
      onClose={onClose}
      form={form}
      onSubmit={async (v: RateValues) => {
        await target.submit(v)
        toast.success(t('drivers.rates.saved'))
        onClose()
      }}
    >
      <p className="meta">{target.subtitle}</p>
      <p className="help">{target.creating ? t('drivers.rates.setHelp') : t('drivers.rates.editHelp')}</p>
      <div className="r2">
        <Field name="rate" label={t('drivers.rates.rate')} required>
          <NumberInput min={0} />
        </Field>
        <Field name="effectiveFrom" label={t('drivers.rates.effectiveFrom')} help={t('drivers.rates.effectiveHelp')} required>
          <DateInput min={today} />
        </Field>
      </div>
    </FormModal>
  )
}

// ---- Alta inline de una tarifa por entrega (Servicio, Paquete, Tarifa, Agregar) ----
function DeliveryAddForm({ publicId }: { publicId: string }) {
  const t = useT()
  const today = tenantToday()
  const mutations = useRateMutations(publicId)
  const { data: services = [] } = useLookups('ServiceType')
  const levels = useZoneGroups()
  const levelOptions = useMemo(() => (levels.data ?? []).map((g) => ({ value: g.code ?? '', label: g.name || g.code || '' })), [levels.data])
  const { data: packages = [] } = useLookups('PackageType')
  const schema = useMemo(() => rateSchema(t, { needs: 'delivery', needsDate: false, today }), [t, today])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: emptyRateValues(today) })
  return (
    <Form
      form={form}
      className="drv-addrow"
      onSubmit={async (v: RateValues) => {
        await mutations.addDelivery.mutateAsync(buildDeliveryRateCreate(v))
        toast.success(t('drivers.rates.added'))
        form.reset(emptyRateValues(today), { keepDirtyValues: false })
      }}
    >
      <Field name="serviceType" label={t('drivers.rates.service')}>
        <Select options={lookupOptions(services)} placeholder={t('drivers.choose')} />
      </Field>
      <Field name="packageType" label={t('drivers.rates.package')}>
        <Select options={lookupOptions(packages)} placeholder={t('drivers.choose')} />
      </Field>
      <Field name="zoneGroup" label={t('drivers.rates.level')}>
        <Select options={levelOptions} placeholder={t('drivers.rates.allLevels')} />
      </Field>
      <Field name="rate" label={t('drivers.rates.rate')}>
        <NumberInput min={0} step="0.01" />
      </Field>
      <button type="submit" className="btn" disabled={form.formState.isSubmitting}>
        <IconPlus /> {t('drivers.rates.add')}
      </button>
    </Form>
  )
}

// ---- Alta inline de una tarifa por viaje (Tipo de viaje, Tarifa, Agregar) ----
function TripRateAddForm({ publicId }: { publicId: string }) {
  const t = useT()
  const today = tenantToday()
  const mutations = useRateMutations(publicId)
  const types = useDriverTripTypes()
  const noCatalog = types.isSuccess && (types.data?.length ?? 0) === 0
  const schema = useMemo(() => rateSchema(t, { needs: 'trip', needsDate: false, today }), [t, today])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: emptyRateValues(today) })
  return (
    <Form
      form={form}
      className="drv-addrow trip"
      onSubmit={async (v: RateValues) => {
        try {
          await mutations.addTrip.mutateAsync(buildTripRateCreate(v))
        } catch (err) {
          // el servidor llama al campo specialServiceTypeId; el formulario lo llama tripTypeId
          rethrowRenamed(err, (e) => renameErrors(e, { specialServiceTypeId: 'tripTypeId' }))
        }
        toast.success(t('drivers.rates.added'))
        form.reset(emptyRateValues(today), { keepDirtyValues: false })
      }}
    >
      <Field name="tripTypeId" label={t('drivers.rates.tripType')}>
        <Select disabled={noCatalog} options={(types.data ?? []).map((x) => ({ value: String(x.id), label: x.name ?? '' }))} placeholder={noCatalog ? t('drivers.rates.noCatalog') : t('drivers.choose')} />
      </Field>
      <Field name="rate" label={t('drivers.rates.rate')}>
        <NumberInput min={0} step="0.01" />
      </Field>
      <button type="submit" className="btn" disabled={form.formState.isSubmitting || noCatalog}>
        <IconPlus /> {t('drivers.rates.add')}
      </button>
    </Form>
  )
}

type Closing = { kind: 'delivery'; row: DeliveryRate } | { kind: 'trip'; row: TripRate } | { kind: 'attempt'; row: AttemptRate }

export function DriverRatesPanels({ driver }: { driver: DriverDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = driver.publicId ?? ''
  const canView = useCan('driverpay.view')
  const canManage = useCan('driverpay.manage')
  const [history, setHistory] = useState(false)
  const rates = useDriverRates(publicId, history, canView)
  const mutations = useRateMutations(publicId)
  const addLevel = useAddAttemptLevel()
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const [svcFilter, setSvcFilter] = useState<string[]>([])
  const [pkgFilter, setPkgFilter] = useState<string[]>([])
  const [tripFilter, setTripFilter] = useState<string[]>([])
  const [edit, setEdit] = useState<EditTarget | null>(null)
  const [closing, setClosing] = useState<Closing | null>(null)

  const data = rates.data
  const writable = canManage && data?.readOnly !== true && driver.isTerminal !== true
  const delivery = useMemo(() => data?.deliveryRates ?? [], [data?.deliveryRates])
  const attempts = useMemo(() => data?.attemptRates ?? [], [data?.attemptRates])
  const trips = useMemo(() => data?.tripRates ?? [], [data?.tripRates])
  const deliveryShown = useMemo(() => filterDeliveryRows(delivery, svcFilter, pkgFilter), [delivery, svcFilter, pkgFilter])
  const tripsShown = useMemo(() => filterTripRows(trips, tripFilter), [trips, tripFilter])
  const svcOptions = useMemo(() => uniqueOptions(delivery, (r) => ({ value: r.serviceTypeCode ?? '', label: r.serviceType ?? r.serviceTypeCode ?? '' })), [delivery])
  const pkgOptions = useMemo(() => uniqueOptions(delivery, (r) => ({ value: r.packageTypeCode ?? '', label: r.packageType ?? r.packageTypeCode ?? '' })), [delivery])
  const tripOptions = useMemo(() => uniqueOptions(trips, (r) => ({ value: String(r.specialServiceTypeId), label: r.tripType ?? '' })), [trips])
  const levels = data?.attemptLevels ?? 0

  const money = (n: number) => f.money(n, { unitPrice: true })
  const datesCols = <R extends { effectiveFrom?: string | null; effectiveTo?: string | null }>(): DataColumn<R>[] => [
    { id: 'from', header: t('drivers.rates.from'), cell: (r) => (r.effectiveFrom ? f.date(r.effectiveFrom) : '—'), sortValue: (r) => r.effectiveFrom ?? '' },
    ...(history ? [{ id: 'to', header: t('drivers.rates.to'), cell: (r: R) => (r.effectiveTo ? f.date(r.effectiveTo) : '—'), sortValue: (r: R) => r.effectiveTo ?? '' }] : []),
  ]

  const deliveryColumns: DataColumn<DeliveryRate>[] = [
    { id: 'service', header: t('drivers.rates.service'), cell: (r) => <b>{r.serviceType ?? r.serviceTypeCode}</b>, sortValue: (r) => r.serviceType ?? '', card: 'title' },
    { id: 'package', header: t('drivers.rates.package'), cell: (r) => r.packageType ?? r.packageTypeCode, sortValue: (r) => r.packageType ?? '' },
    { id: 'level', header: t('drivers.rates.level'), cell: (r) => r.zoneGroup ?? t('drivers.rates.allLevels'), sortValue: (r) => r.zoneGroup ?? '' },   // Lote 129
    { id: 'rate', header: t('drivers.rates.rate'), align: 'end', cell: (r) => <span className="mono">{money(r.rate)}</span>, sortValue: (r) => r.rate },
    ...datesCols<DeliveryRate>(),
  ]
  const tripColumns: DataColumn<TripRate>[] = [
    { id: 'type', header: t('drivers.rates.tripType'), cell: (r) => <b>{r.tripType}</b>, sortValue: (r) => r.tripType ?? '', card: 'title' },
    { id: 'rate', header: t('drivers.rates.rate'), align: 'end', cell: (r) => <span className="mono">{money(r.rate)}</span>, sortValue: (r) => r.rate },
    ...datesCols<TripRate>(),
  ]

  const deliveryName = (r: DeliveryRate) => `${r.serviceType ?? r.serviceTypeCode} · ${r.packageType ?? r.packageTypeCode}${r.zoneGroup ? ` · ${r.zoneGroup}` : ''}`

  const editDelivery = (r: DeliveryRate) =>
    setEdit({
      title: t('drivers.rates.editTitle'),
      subtitle: deliveryName(r),
      rate: r.rate,
      creating: false,
      submit: (v) => mutations.updateDelivery.mutateAsync({ id: r.id, body: buildRateUpdate(v) }),
    })
  const editTrip = (r: TripRate) =>
    setEdit({
      title: t('drivers.rates.editTitle'),
      subtitle: r.tripType ?? '',
      rate: r.rate,
      creating: false,
      submit: (v) => mutations.updateTrip.mutateAsync({ id: r.id, body: buildRateUpdate(v) }),
    })
  const editAttempt = (a: AttemptRate) =>
    setEdit({
      title: a.id == null ? t('drivers.rates.setAttemptTitle', { n: a.attemptNumber }) : t('drivers.rates.editAttemptTitle', { n: a.attemptNumber }),
      subtitle: t('drivers.rates.attempt', { n: a.attemptNumber }),
      rate: a.rate ?? null,
      creating: a.id == null,
      submit: (v) => mutations.setAttempt.mutateAsync({ attemptNumber: a.attemptNumber, body: buildRateUpdate(v) }),
    })

  if (!canView) return null

  const addLevelNow = async () => {
    try {
      await addLevel.mutateAsync()
      toast.success(t('drivers.rates.levelAdded'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const closingText =
    closing?.kind === 'delivery'
      ? t('drivers.rates.closeBody', { name: deliveryName(closing.row) })
      : closing?.kind === 'trip'
        ? t('drivers.rates.closeBody', { name: closing.row.tripType ?? '' })
        : closing?.kind === 'attempt'
          ? t('drivers.rates.closeBody', { name: t('drivers.rates.attempt', { n: closing.row.attemptNumber }) })
          : ''

  const historySwitch = (
    <label className="sw">
      <input type="checkbox" role="switch" checked={history} onChange={(e) => setHistory(e.target.checked)} />
      <span className="tk" aria-hidden="true" />
      <span>{t('drivers.rates.showHistory')}</span>
    </label>
  )

  if (rates.isPending) {
    return (
      <Panel icon={<IconCash />} title={t('drivers.rates.title')}>
        <Spinner block />
      </Panel>
    )
  }
  if (rates.isError) {
    return (
      <Panel icon={<IconCash />} title={t('drivers.rates.title')}>
        <EmptyState icon={<IconCash />} title={applyProblemDetails(rates.error).title} />
      </Panel>
    )
  }

  return (
    <div className="drv-stack" ref={ref}>
      <Panel icon={<IconCash />} title={t('drivers.rates.title')} actions={historySwitch}>
        {data?.readOnly && (
          <p className="note" role="status">
            {t('drivers.rates.retired')}
          </p>
        )}

        <section className="drv-sec" aria-label={t('drivers.rates.deliveryTitle')}>
          <div className="drv-sec-h">
            <h3>{t('drivers.rates.deliveryTitle')}</h3>
          </div>
          {delivery.length > 0 && (
            <div className="drv-filters">
              <Filters
                onClear={() => {
                  setSvcFilter([])
                  setPkgFilter([])
                }}
              >
                <SearchSelect label={t('drivers.rates.service')} options={svcOptions} value={svcFilter} onChange={setSvcFilter} />
                <SearchSelect label={t('drivers.rates.package')} options={pkgOptions} value={pkgFilter} onChange={setPkgFilter} />
              </Filters>
            </div>
          )}
          {delivery.length === 0 ? (
            <p className="drv-empty">{t('drivers.rates.noDelivery')}</p>
          ) : deliveryShown.length === 0 ? (
            <p className="drv-empty">{t('drivers.rates.noMatch')}</p>
          ) : (
            <DataTable
              label={t('drivers.rates.deliveryTitle')}
              columns={deliveryColumns}
              rows={deliveryShown}
              rowKey={(r) => String(r.id)}
              pagination={false}
              exportable={false}
              forceCards={width > 0 && width < 640}
              rowClassName={(r) => (r.isCurrent ? undefined : 'dim')}
              rowActions={[
                { key: 'edit', label: t('drivers.edit'), icon: <IconEdit />, onClick: editDelivery, visible: (r) => writable && isOpenRow(r) },
                { key: 'close', label: t('drivers.rates.close'), icon: <IconXCircle />, tone: 'danger', onClick: (r) => setClosing({ kind: 'delivery', row: r }), visible: (r) => writable && isOpenRow(r) },
              ]}
            />
          )}
          {writable && <DeliveryAddForm publicId={publicId} />}
        </section>

        <section className="drv-sec" aria-label={t('drivers.rates.attemptTitle')}>
          <div className="drv-sec-h">
            <h3>{t('drivers.rates.attemptTitle')}</h3>
            {writable && (
              <button type="button" className="btn sm" onClick={() => void addLevelNow()} disabled={addLevel.isPending || levels >= MAX_ATTEMPT_LEVELS} title={levels >= MAX_ATTEMPT_LEVELS ? t('drivers.rates.levelMax', { max: MAX_ATTEMPT_LEVELS }) : undefined}>
                {t('drivers.rates.addAttempt')}
              </button>
            )}
          </div>
          <ul className="drv-atts" aria-label={t('drivers.rates.attemptTitle')}>
            {attempts.map((a) => {
              const kind = attemptCellKind(a)
              return (
                <li key={`${a.attemptNumber}-${a.id ?? 'none'}`} className={`drv-att ${kind}`} data-kind={kind}>
                  <b>{t('drivers.rates.attempt', { n: a.attemptNumber })}</b>
                  {kind === 'none' || a.rate == null ? <span className="drv-none">{t('drivers.rates.noRate')}</span> : <span className="val mono">{money(a.rate)}</span>}
                  {kind !== 'none' && a.effectiveFrom && (
                    <span className="when">
                      {t('drivers.rates.sinceDate', { date: f.date(a.effectiveFrom) })}
                      {a.effectiveTo ? ` · ${t('drivers.rates.untilDate', { date: f.date(a.effectiveTo) })}` : ''}
                    </span>
                  )}
                  {writable && kind !== 'closed' && (
                    <span className="acts">
                      <button type="button" className="btn sm" onClick={() => editAttempt(a)}>
                        {kind === 'none' ? t('drivers.rates.setRate') : t('drivers.edit')}
                      </button>
                      {kind === 'open' && (
                        <button type="button" className="btn sm" onClick={() => setClosing({ kind: 'attempt', row: a })}>
                          {t('drivers.rates.close')}
                        </button>
                      )}
                    </span>
                  )}
                </li>
              )
            })}
          </ul>
          <p className="help">{t('drivers.rates.attemptHelp')}</p>
        </section>
      </Panel>

      <Panel icon={<IconCash />} title={t('drivers.rates.tripTitle')}>
        {trips.length > 0 && (
          <div className="drv-filters">
            <Filters onClear={() => setTripFilter([])}>
              <SearchSelect label={t('drivers.rates.tripType')} options={tripOptions} value={tripFilter} onChange={setTripFilter} />
            </Filters>
          </div>
        )}
        {trips.length === 0 ? (
          <p className="drv-empty">{t('drivers.rates.noTrips')}</p>
        ) : tripsShown.length === 0 ? (
          <p className="drv-empty">{t('drivers.rates.noMatch')}</p>
        ) : (
          <DataTable
            label={t('drivers.rates.tripTitle')}
            columns={tripColumns}
            rows={tripsShown}
            rowKey={(r) => String(r.id)}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 640}
            rowClassName={(r) => (r.isCurrent ? undefined : 'dim')}
            rowActions={[
              { key: 'edit', label: t('drivers.edit'), icon: <IconEdit />, onClick: editTrip, visible: (r) => writable && isOpenRow(r) },
              { key: 'close', label: t('drivers.rates.close'), icon: <IconXCircle />, tone: 'danger', onClick: (r) => setClosing({ kind: 'trip', row: r }), visible: (r) => writable && isOpenRow(r) },
            ]}
          />
        )}
        {writable && <TripRateAddForm publicId={publicId} />}
      </Panel>

      {edit && <RateEditModal key={`${edit.title}-${edit.subtitle}`} target={edit} onClose={() => setEdit(null)} />}
      <ConfirmDialog
        open={closing !== null}
        tone="danger"
        title={t('drivers.rates.closeTitle')}
        message={closingText}
        confirmLabel={t('drivers.rates.close')}
        onConfirm={async () => {
          if (!closing) return
          if (closing.kind === 'delivery') await mutations.closeDelivery.mutateAsync(closing.row.id)
          else if (closing.kind === 'trip') await mutations.closeTrip.mutateAsync(closing.row.id)
          else await mutations.closeAttempt.mutateAsync(closing.row.attemptNumber)
          toast.success(t('drivers.rates.closed'))
        }}
        onClose={() => setClosing(null)}
      />
    </div>
  )
}
