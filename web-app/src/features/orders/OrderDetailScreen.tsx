// Pantalla E (Lote F6) — Ficha de la orden (solo lectura). `/orders/:publicId`. Lectura: orders.view + LTL_GROUND
// (aplicado por la ruta). Sin pestañas de edición ni StatusPipeline interactivo: solo StatusHistory de lectura.
import { useMemo } from 'react'
import { Link, useParams } from 'react-router-dom'
import { parseApiDate } from '../../kernel/api/dates'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, StatusHistory } from '../../kernel/catalogs'
import { formatMoney, useLang, useT } from '../../kernel/i18n'
import { DataTable, EmptyState, Panel, Spinner, type DataColumn } from '../../kernel/ui'
import { useOrderReadonly, type OrderDetailDto } from '../warehouse/api'
import { IconLayers } from '../../kernel/ui/screenIcons'

const STATUS_DOMAIN = 'OrderStatus'
const ENTITY_TYPE = 'TRANSPORT_ORDER'

type Package = NonNullable<OrderDetailDto['packages']>[number]
type Stop = OrderDetailDto['pickup']

function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function money(v: number | null | undefined, currency: string | null | undefined, lang: string): string {
  if (v == null) return '—'
  return formatMoney(v, lang, { currency })
}

function StopCard({ title, stop, lang }: { title: string; stop: Stop | undefined; lang: string }) {
  const t = useT()
  if (!stop) return null
  return (
    <div className="panel" style={{ padding: 12 }}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      <p>{stop.name}</p>
      <p>
        {[stop.line1, stop.line2].filter(Boolean).join(', ')}
        {stop.city ? `, ${stop.city}` : ''} {stop.state ?? ''} {stop.postalCode ?? ''}
      </p>
      {(stop.windowStartUtc || stop.windowEndUtc) && (
        <p className="note">
          {t('orders.detail.window')}: {formatDateTime(stop.windowStartUtc, lang)} — {formatDateTime(stop.windowEndUtc, lang)}
        </p>
      )}
      {stop.notes && <p className="note">{stop.notes}</p>}
      {stop.status && <StatusChip domain="StopStatus" code={stop.status} label={stop.statusLabel} />}
    </div>
  )
}

export default function OrderDetailScreen() {
  const t = useT()
  const lang = useLang()
  const { publicId = '' } = useParams()
  const { data: order, isLoading, error } = useOrderReadonly(publicId)

  const packageColumns = useMemo<DataColumn<Package>[]>(
    () => [
      { id: 'packageNumber', header: t('orders.detail.packages.number'), cell: (p) => p.packageNumber ?? '—', card: 'title', sortValue: (p) => p.packageNumber },
      { id: 'type', header: t('orders.detail.packages.type'), cell: (p) => p.packageTypeLabel ?? p.packageType, sortValue: (p) => p.packageTypeLabel ?? p.packageType },
      { id: 'description', header: t('orders.detail.packages.description'), cell: (p) => p.description ?? '—', sortValue: (p) => p.description },
      { id: 'pieces', header: t('orders.detail.packages.pieces'), cell: (p) => p.pieces, align: 'end', sortValue: (p) => p.pieces },
      { id: 'weight', header: t('orders.detail.packages.weight'), cell: (p) => p.weightKg ?? '—', align: 'end', sortValue: (p) => p.weightKg },
      { id: 'volume', header: t('orders.detail.packages.volume'), cell: (p) => p.volumeM3 ?? '—', align: 'end', sortValue: (p) => p.volumeM3 },
    ],
    [t],
  )

  if (isLoading) return <Spinner block />
  if (error || !order) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('orders.detail.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/orders">
            {t('orders.detail.back')}
          </Link>
        }
      />
    )
  }

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{order.orderNumber}</span> · {order.clientName}
          </h1>
          <p>
            <StatusChip domain={STATUS_DOMAIN} code={order.status} label={order.statusLabel} /> {order.serviceTypeLabel}
          </p>
        </div>
      </div>

      <Panel icon={<IconLayers />} title={t('orders.detail.tabGeneral')}>
        <div className="r3">
          <div className="f">
            <label>{t('orders.detail.fields.invoice')}</label>
            <p>{order.clientInvoiceNumber ?? '—'}</p>
          </div>
          <div className="f">
            <label>{t('orders.detail.fields.packBatch')}</label>
            <p>{order.packBatchNumber ?? '—'}</p>
          </div>
          <div className="f">
            <label>{t('orders.detail.fields.priority')}</label>
            <p>{order.priority ?? '—'}</p>
          </div>
        </div>
        <div className="r3">
          <div className="f">
            <label>{t('orders.detail.fields.quotedAmount')}</label>
            <p>{money(order.quotedAmount, order.currency, lang)}</p>
          </div>
          <div className="f">
            <label>{t('orders.detail.fields.codAmount')}</label>
            <p>
              {money(order.codAmount, order.currency, lang)} {order.codStatusLabel ? `(${order.codStatusLabel})` : ''}
            </p>
          </div>
          <div className="f">
            <label>{t('orders.detail.fields.pieces')}</label>
            <p>
              {order.totalPieces} {t('orders.detail.fields.piecesUnit')}
              {order.totalWeightKg != null ? ` · ${order.totalWeightKg} kg` : ''}
              {order.totalVolumeM3 != null ? ` · ${order.totalVolumeM3} m³` : ''}
            </p>
          </div>
        </div>
        {order.assignedTripCode && (
          <div className="f">
            <label>{t('orders.detail.fields.trip')}</label>
            <p>
              {order.assignedTripCode} {order.assignedDriverName ? `· ${order.assignedDriverName}` : ''}
            </p>
          </div>
        )}
        {order.notes && (
          <div className="f">
            <label>{t('orders.detail.fields.notes')}</label>
            <p>{order.notes}</p>
          </div>
        )}
      </Panel>

      <div className="r2" style={{ marginTop: 14 }}>
        <StopCard title={t('orders.detail.tabPickup')} stop={order.pickup} lang={lang} />
        <StopCard title={t('orders.detail.tabDelivery')} stop={order.delivery} lang={lang} />
      </div>

      <div style={{ marginTop: 14 }}>
        <Panel flush icon={<IconLayers />} title={t('orders.detail.tabPackages')}>
          <DataTable
            label={t('orders.detail.tabPackages')}
            columns={packageColumns}
            rows={order.packages ?? []}
            rowKey={(p) => p.id ?? 0}
            empty={<EmptyState title={t('orders.detail.packages.empty')} />}
          />
        </Panel>
      </div>

      <div style={{ marginTop: 14 }}>
        <Panel icon={<IconLayers />} title={t('orders.detail.tabHistory')}>
          <StatusHistory entityType={ENTITY_TYPE} entityId={order.id ?? 0} domain={STATUS_DOMAIN} />
        </Panel>
      </div>
    </div>
  )
}
