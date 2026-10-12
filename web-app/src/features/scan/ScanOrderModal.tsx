// Lote F-E1 — «Ver orden» de la Estación de escaneo (`openOrderPeek` de la maqueta): un resumen de la orden en una ventana, con «Abrir»
// que lleva al expediente. Con `orders.view` lee la ficha (`GET /orders/{id}`); sin ese permiso, o si la ficha no llega, muestra solo
// lo que ya traía la fila (escaneo o pendiente) y no pinta «Abrir».
import { useNavigate } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { IconChevronRight, Modal, Spinner } from '../../kernel/ui'
import { useOrder } from '../orders/api'
import type { KnownOrder } from './scanRules'

export interface PeekTarget {
  orderPublicId: string
  /** Número de la orden (o lo escaneado). */
  ref: string
  tripCode?: string | null
  known: KnownOrder
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="scn-kv">
      <span>{label}</span>
      <b>{children}</b>
    </div>
  )
}

const dash = '—'

export function ScanOrderModal({ target, onClose }: { target: PeekTarget | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const canView = useCan('orders.view')
  const order = useOrder(target?.orderPublicId, { enabled: canView && !!target, handleAccessDenied: false })
  const o = order.data
  const k = target?.known ?? {}

  const client = o?.clientName ?? k.clientName
  const consignee = o?.delivery?.name ?? k.consigneeName
  const city = o?.delivery?.city ?? k.city
  const postal = o?.delivery?.postalCode ?? k.postalCode
  const town = [city, postal].filter(Boolean).join(' · ')
  const tripCode = o?.assignedTripCode ?? target?.tripCode
  const cod = o?.codAmount ?? null

  return (
    <Modal
      open={!!target}
      onClose={onClose}
      title={target?.ref ?? ''}
      size="sm"
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('scan.order.close')}
          </button>
          <Can perm="orders.view">
            <button
              type="button"
              className="btn flow"
              onClick={() => {
                if (target) navigate(`/orders/${target.orderPublicId}`)
              }}
            >
              <IconChevronRight /> {t('scan.order.open')}
            </button>
          </Can>
        </>
      }
    >
      {target && (
        <div className="scn-peek">
          {canView && order.isLoading && <Spinner label={t('scan.order.loading')} block />}
          {canView && order.isError && <p className="note">{t('scan.order.unavailable')}</p>}
          <Row label={t('scan.order.client')}>{client || dash}</Row>
          <Row label={t('scan.order.consignee')}>{consignee || dash}</Row>
          <Row label={t('scan.order.town')}>{town || dash}</Row>
          {(k.zoneCode || o) && <Row label={t('scan.order.zone')}>{k.zoneCode || dash}</Row>}
          {o && (
            <>
              <Row label={t('scan.order.service')}>{o.serviceTypeLabel || o.serviceType || dash}</Row>
              <Row label={t('scan.order.packages')}>
                {[o.packagesSummary, o.totalPieces ? t('scan.order.pieces', { n: o.totalPieces }) : null].filter(Boolean).join(' · ') || dash}
              </Row>
              <Row label={t('scan.order.cod')}>{cod ? <span className="cash">{f.money(cod)}</span> : dash}</Row>
              <Row label={t('scan.order.status')}>
                {o.status ? <StatusChip domain="OrderStatus" code={o.status} label={o.statusLabel ?? undefined} /> : dash}
              </Row>
            </>
          )}
          {!o && k.statusLabel && <Row label={t('scan.order.status')}>{k.statusLabel}</Row>}
          {tripCode && (
            <Row label={t('scan.order.trip')}>
              <span className="ref">{tripCode}</span>
            </Row>
          )}
        </div>
      )}
    </Modal>
  )
}
