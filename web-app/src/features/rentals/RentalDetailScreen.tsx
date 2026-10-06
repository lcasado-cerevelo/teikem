// Lote F17 (Rentas F-R1) — ficha de una renta. `/warehouse/rentals/:publicId` (`rental.view` + RENTAL_EQUIPMENT por la ruta).
// Encabezado con número, cliente, estatus (insignia con su color) y vencimiento calculado; acciones según el estatus (las
// capacidades `canEdit`/`canSchedule`/`canDispatch`/`canExtend`/`canCancel` del servidor) y el permiso: Editar, Agregar
// equipos, Programar, Despachar y Cancelar (`rental.manage`), Extender (`rental.extend`); todas con confirmación y el
// mensaje exacto del servidor si falla. Datos (cliente, localidad, contacto, almacén, inicio, recogido vigente y pactado,
// contrato, transporte, enlaces vacíos a envío y factura, notas), Equipos por serie (posición de origen, tarifa vigente y
// estado; inactivos atenuados en una renta cancelada; tarifa y quitar antes del despacho), Extensiones (bitácora) e
// Historial de estatus (`/status/history/RENTAL/{id}`).
// Lote F18 (Rentas F-R2): "Registrar devolución" (`rental.return`, renta En renta con equipos sin devolver) y el panel
// Devoluciones de la renta (`GET /rental-returns?rentalPublicId=`) con enlace a la ficha de cada una (renta ↔ devolución).
import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, StatusHistory } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, EmptyState, Panel, Spinner, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconEdit, IconTrash } from '../../kernel/ui/actionIcons'
import { IconClock, IconDoc, IconWarehouse } from '../../kernel/ui/screenIcons'
import { useRental, useRentalAction, useRentalExtensions, useRentalReturns } from './api'
import { DueChip } from './DueChip'
import { AddEquipmentModal, RentalExtendModal, RentalRateModal, RentalStatusActionModal, type StatusActionKind } from './RentalDialogs'
import { RentalFormModal } from './RentalFormModal'
import { RentalReturnModal } from './RentalReturnModal'
import { lineState, RENTAL_ENTITY_TYPE, RENTAL_STATUS_DOMAIN, type RentalDto, type RentalExtensionDto, type RentalLineDto, type RentalLineRateDto } from './rentalRules'
import { canRegisterReturn, type RentalReturnListItemDto } from './returnRules'
import { useFrequencyLabel } from './useFrequencyOptions'
import '../warehouse/warehouse.css'
import './rentals.css'

const LINE_TONE = { inactive: 'neutral', returned: 'deliv', dispatched: 'route', pending: 'wh' } as const

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}

export default function RentalDetailScreen() {
  const t = useT()
  const { publicId = '' } = useParams()
  const { data: rental, isLoading, error } = useRental(publicId)

  if (isLoading) return <Spinner block />
  if (error || !rental?.rental) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('rentals.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/rentals">
            {t('rentals.back')}
          </Link>
        }
      />
    )
  }
  return <RentalDetail rental={rental} />
}

function RentalDetail({ rental }: { rental: RentalDto }) {
  const t = useT()
  const f = useFormat()
  const r = rental.rental ?? {}
  const canManage = useCan('rental.manage')
  const frequencyLabel = useFrequencyLabel()
  const action = useRentalAction()
  const extensions = useRentalExtensions(r.publicId)
  const [editing, setEditing] = useState(false)
  const [adding, setAdding] = useState(false)
  const [extending, setExtending] = useState(false)
  const [statusAction, setStatusAction] = useState<StatusActionKind | null>(null)
  const [rateLine, setRateLine] = useState<RentalLineDto | null>(null)
  const [removing, setRemoving] = useState<RentalLineDto | null>(null)
  const [returning, setReturning] = useState(false)
  const returns = useRentalReturns({ rentalPublicId: r.publicId, skip: 0, take: 100 }, { enabled: Boolean(r.publicId) })

  const rateText = useCallback(
    (rate: RentalLineRateDto | null | undefined) =>
      rate?.frequencyCode
        ? `${f.money(rate.amount ?? 0, { currency: rate.currencyCode ?? undefined })} · ${frequencyLabel(rate.frequencyCode, rate.frequency)}`
        : t('rentals.rate.noRate'),
    [f, t, frequencyLabel],
  )

  const lineColumns = useMemo<DataColumn<RentalLineDto>[]>(
    () => [
      { id: 'serial', header: t('rentals.lines.serial'), cell: (l) => <span className="mono">{l.serialNumber}</span>, sortValue: (l) => l.serialNumber, card: 'title' },
      { id: 'sku', header: t('rentals.lines.sku'), cell: (l) => <span className="ref">{l.sku}</span>, sortValue: (l) => l.sku },
      { id: 'product', header: t('rentals.lines.product'), cell: (l) => l.productName, sortValue: (l) => l.productName },
      { id: 'lot', header: t('rentals.lines.lot'), cell: (l) => l.lotNumber ?? '', sortValue: (l) => l.lotNumber, card: 'hidden' },
      { id: 'fromBin', header: t('rentals.lines.fromBin'), cell: (l) => l.fromBinCode, sortValue: (l) => l.fromBinCode },
      { id: 'rate', header: t('rentals.lines.rate'), cell: (l) => rateText(l.rate), sortValue: (l) => l.rate?.amount ?? null, exportValue: (l) => rateText(l.rate) },
      {
        id: 'dispatchedAt',
        header: t('rentals.lines.dispatchedAt'),
        cell: (l) => (l.dispatchedAtUtc ? f.dateTime(l.dispatchedAtUtc) : ''),
        sortValue: (l) => l.dispatchedAtUtc,
      },
      {
        id: 'state',
        header: t('rentals.lines.state'),
        cell: (l) => {
          const s = lineState(l)
          return <Chip tone={LINE_TONE[s]}>{t(`rentals.lineStates.${s}`)}</Chip>
        },
        sortValue: (l) => t(`rentals.lineStates.${lineState(l)}`),
        exportValue: (l) => t(`rentals.lineStates.${lineState(l)}`),
      },
    ],
    [t, f, rateText],
  )

  const lineActions = useMemo<RowAction<RentalLineDto>[]>(
    () => [
      {
        key: 'rate',
        label: t('rentals.actions.editRate'),
        icon: <IconEdit />,
        perm: 'rental.manage',
        visible: (l) => rental.canEdit === true && l.isActive !== false,
        onClick: (l) => setRateLine(l),
      },
      {
        key: 'remove',
        label: t('rentals.actions.removeLine'),
        icon: <IconTrash />,
        tone: 'danger',
        perm: 'rental.manage',
        visible: (l) => rental.canEdit === true && l.isActive !== false,
        onClick: (l) => setRemoving(l),
      },
    ],
    [t, rental.canEdit],
  )

  const extensionColumns = useMemo<DataColumn<RentalExtensionDto>[]>(
    () => [
      { id: 'when', header: t('rentals.extensions.when'), cell: (e) => f.dateTime(e.createdAtUtc), sortValue: (e) => e.createdAtUtc, card: 'title' },
      { id: 'previous', header: t('rentals.extensions.previous'), cell: (e) => f.date(e.previousPickupDate), sortValue: (e) => e.previousPickupDate },
      { id: 'next', header: t('rentals.extensions.next'), cell: (e) => f.date(e.newPickupDate), sortValue: (e) => e.newPickupDate },
      { id: 'days', header: t('rentals.extensions.days'), cell: (e) => f.number(e.daysAdded ?? 0), sortValue: (e) => e.daysAdded ?? 0, align: 'end' },
      { id: 'reason', header: t('rentals.extensions.reason'), cell: (e) => e.reason, sortValue: (e) => e.reason },
      { id: 'user', header: t('rentals.extensions.user'), cell: (e) => e.createdByName ?? '', sortValue: (e) => e.createdByName },
      {
        id: 'rates',
        header: t('rentals.extensions.rates'),
        cell: (e) => (e.rates ?? []).map((x) => `${x.serialNumber}: ${rateText(x.rate)}`).join('; '),
        sortValue: (e) => (e.rates ?? []).length,
      },
    ],
    [t, f, rateText],
  )

  const returnColumns = useMemo<DataColumn<RentalReturnListItemDto>[]>(
    () => [
      {
        id: 'number',
        header: t('rentalReturns.columns.number'),
        cell: (x) => (
          <Link className="ref" to={`/warehouse/rental-returns/${x.publicId}`}>
            {x.number}
          </Link>
        ),
        sortValue: (x) => x.number,
        exportValue: (x) => x.number ?? '',
        card: 'title',
      },
      { id: 'returnedOn', header: t('rentalReturns.columns.returnedOn'), cell: (x) => f.date(x.returnedOn), sortValue: (x) => x.returnedOn, exportValue: (x) => f.date(x.returnedOn) },
      {
        id: 'reason',
        header: t('rentalReturns.columns.reason'),
        cell: (x) => <Chip tone={x.isEarly ? 'warn' : 'neutral'}>{x.reason ?? x.reasonCode}</Chip>,
        sortValue: (x) => x.reason ?? x.reasonCode,
        exportValue: (x) => x.reason ?? x.reasonCode ?? '',
      },
      { id: 'early', header: t('rentalReturns.columns.early'), cell: (x) => (x.isEarly ? t('rentalReturns.yes') : t('rentalReturns.no')), sortValue: (x) => (x.isEarly ? 1 : 0) },
      { id: 'units', header: t('rentalReturns.columns.units'), cell: (x) => f.number(x.units ?? 0), sortValue: (x) => x.units ?? 0, align: 'end' },
      { id: 'open', header: t('rentalReturns.columns.openProcesses'), cell: (x) => f.number(x.openProcesses ?? 0), sortValue: (x) => x.openProcesses ?? 0, align: 'end' },
    ],
    [t, f],
  )

  const lines = rental.lines ?? []
  const hasActive = lines.some((l) => l.isActive !== false)
  const transport =
    rental.estimatedDeliveryCost != null ? f.money(rental.estimatedDeliveryCost, { currency: rental.transportCurrencyCode ?? undefined }) : '—'

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{r.number}</span> · {r.clientName}
          </h1>
          <p className="ren-headline">
            <StatusChip domain={RENTAL_STATUS_DOMAIN} code={r.statusCode} label={r.status} />
            <DueChip rental={r} />
            <span>
              {t('rentals.fields.pickupDate')}: {f.date(r.pickupDate)}
            </span>
          </p>
        </div>
        <div className="act ren-act">
          <Link className="btn" to="/warehouse/rentals">
            {t('rentals.back')}
          </Link>
          <Can perm="rental.manage">
            {rental.canEdit && (
              <button type="button" className="btn" onClick={() => setEditing(true)}>
                {t('rentals.actions.edit')}
              </button>
            )}
            {rental.canEdit && (
              <button type="button" className="btn" onClick={() => setAdding(true)}>
                {t('rentals.actions.addEquipment')}
              </button>
            )}
            {rental.canSchedule && (
              <button type="button" className="btn flow" onClick={() => setStatusAction('schedule')}>
                {t('rentals.actions.schedule')}
              </button>
            )}
            {rental.canDispatch && (
              <button type="button" className="btn flow" onClick={() => setStatusAction('dispatch')}>
                {t('rentals.actions.dispatch')}
              </button>
            )}
          </Can>
          <Can perm="rental.return">
            {canRegisterReturn(rental) && (
              <button type="button" className="btn flow" onClick={() => setReturning(true)}>
                {t('rentalReturns.actions.register')}
              </button>
            )}
          </Can>
          <Can perm="rental.extend">
            {rental.canExtend && (
              <button type="button" className="btn" onClick={() => setExtending(true)}>
                {t('rentals.actions.extend')}
              </button>
            )}
          </Can>
          <Can perm="rental.manage">
            {rental.canCancel && (
              <button type="button" className="btn danger" onClick={() => setStatusAction('cancel')}>
                {t('rentals.actions.cancelRental')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <Panel icon={<IconWarehouse />} title={t('rentals.detail.data')}>
        <dl className="kx-facts">
          <Fact label={t('rentals.fields.client')}>{r.clientName}</Fact>
          <Fact label={t('rentals.fields.location')}>{[r.locationName, r.locationCity].filter(Boolean).join(' · ')}</Fact>
          <Fact label={t('rentals.fields.contact')}>{rental.clientContactName || '—'}</Fact>
          <Fact label={t('rentals.fields.warehouse')}>{r.warehouseCode}</Fact>
          <Fact label={t('rentals.fields.startDate')}>{f.date(r.startDate)}</Fact>
          <Fact label={t('rentals.fields.pickupDate')}>{f.date(r.pickupDate)}</Fact>
          <Fact label={t('rentals.fields.originalPickupDate')}>{f.date(r.originalPickupDate)}</Fact>
          <Fact label={t('rentals.fields.extensions')}>{f.number(r.extensionCount ?? 0)}</Fact>
          <Fact label={t('rentals.fields.contractNumber')}>{r.contractNumber || '—'}</Fact>
          <Fact label={t('rentals.fields.contractSignedOn')}>{rental.contractSignedOn ? f.date(rental.contractSignedOn) : '—'}</Fact>
          <Fact label={t('rentals.fields.deliveryCost')}>{transport}</Fact>
          <Fact label={t('rentals.fields.units')}>{f.number(r.units ?? 0)}</Fact>
          <Fact label={t('rentals.fields.dispatchedAt')}>{r.dispatchedAtUtc ? f.dateTime(r.dispatchedAtUtc) : '—'}</Fact>
          <Fact label={t('rentals.fields.closedAt')}>{r.closedAtUtc ? f.dateTime(r.closedAtUtc) : '—'}</Fact>
          <Fact label={t('rentals.fields.deliveryShipment')}>{rental.deliveryShipmentId ? `#${rental.deliveryShipmentId}` : t('rentals.detail.noShipment')}</Fact>
          <Fact label={t('rentals.fields.invoice')}>{rental.invoiceId ? `#${rental.invoiceId}` : t('rentals.detail.noInvoice')}</Fact>
        </dl>
        {rental.notes && (
          <>
            <h3 className="ren-h3">{t('rentals.fields.notes')}</h3>
            <p className="ren-notes">{rental.notes}</p>
          </>
        )}
      </Panel>

      <Panel flush icon={<IconWarehouse />} title={t('rentals.detail.equipment')} badge={lines.filter((l) => l.isActive !== false).length}>
        {r.statusCode === 'CANCELLED' && hasActive === false && lines.length > 0 && <p className="pb note">{t('rentals.detail.cancelledLines')}</p>}
        <DataTable
          label={t('rentals.detail.equipment')}
          columns={lineColumns}
          rows={lines}
          rowKey={(l) => l.id ?? 0}
          rowActions={canManage ? lineActions : undefined}
          rowClassName={(l) => (l.isActive === false ? 'dim' : undefined)}
          exportFileName={`${t('rentals.detail.equipment')} ${r.number ?? ''}`}
          empty={<EmptyState title={t('rentals.detail.noEquipment')} body={rental.canEdit && canManage ? t('rentals.detail.noEquipmentBody') : undefined} />}
        />
      </Panel>

      <Panel flush icon={<IconClock />} title={t('rentals.detail.extensions')} badge={extensions.data?.length}>
        {extensions.error ? (
          <p className="pb ferr" role="alert">
            {extensions.error.message}
          </p>
        ) : (
          <DataTable
            label={t('rentals.detail.extensions')}
            columns={extensionColumns}
            rows={extensions.data ?? []}
            rowKey={(e) => e.id ?? 0}
            loading={extensions.isLoading}
            exportFileName={`${t('rentals.detail.extensions')} ${r.number ?? ''}`}
            empty={<EmptyState title={t('rentals.extensions.empty')} />}
          />
        )}
      </Panel>

      <Panel
        flush
        icon={<IconDoc />}
        title={t('rentals.detail.returns')}
        badge={returns.data?.total}
        actions={
          (returns.data?.total ?? 0) > 0 ? (
            <Link className="btn sm" to={`/warehouse/rental-returns?rentalPublicId=${r.publicId}`}>
              {t('rentals.detail.returnsLink')}
            </Link>
          ) : undefined
        }
      >
        {returns.error ? (
          <p className="pb ferr" role="alert">
            {returns.error.message}
          </p>
        ) : (
          <DataTable
            label={t('rentals.detail.returns')}
            columns={returnColumns}
            rows={returns.data?.items ?? []}
            rowKey={(x) => x.publicId ?? String(x.id)}
            loading={returns.isLoading}
            exportFileName={`${t('rentals.detail.returns')} ${r.number ?? ''}`}
            empty={<EmptyState title={t('rentals.detail.noReturns')} />}
          />
        )}
      </Panel>

      <Panel icon={<IconDoc />} title={t('rentals.detail.history')}>
        <StatusHistory entityType={RENTAL_ENTITY_TYPE} entityId={r.id ?? 0} domain={RENTAL_STATUS_DOMAIN} />
      </Panel>

      {editing && <RentalFormModal open rental={rental} onClose={() => setEditing(false)} />}
      {adding && <AddEquipmentModal rental={rental} onClose={() => setAdding(false)} />}
      {extending && <RentalExtendModal rental={rental} onClose={() => setExtending(false)} />}
      {statusAction && <RentalStatusActionModal kind={statusAction} rental={rental} onClose={() => setStatusAction(null)} />}
      {returning && <RentalReturnModal rental={rental} onClose={() => setReturning(false)} />}
      {rateLine && <RentalRateModal rental={rental} line={rateLine} onClose={() => setRateLine(null)} />}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('rentals.confirm.removeTitle', { serial: removing?.serialNumber ?? '' })}
        message={t(r.statusCode === 'SCHEDULED' ? 'rentals.confirm.removeBodyScheduled' : 'rentals.confirm.removeBody')}
        confirmLabel={t('rentals.actions.removeLine')}
        onConfirm={async () => {
          await action.mutateAsync({ action: 'removeLine', publicId: r.publicId ?? '', lineId: removing?.id ?? 0 })
          toast.success(t('rentals.toast.lineRemoved', { serial: removing?.serialNumber ?? '' }))
        }}
        onClose={() => setRemoving(null)}
      />
    </div>
  )
}
