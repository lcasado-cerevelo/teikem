// Lote 222: distintivos de una orden de venta (urgente, entrega/recogido, forma de pago que cambió, consignación, espera aprobación). Los usan la lista y la ficha.
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'

export interface OrderBadgeInfo {
  isUrgent?: boolean
  deliveryMode?: string | null
  paymentChanged?: boolean
  isConsignment?: boolean
  awaitingClientApproval?: boolean
}

/** `showDelivery`: la lista solo pinta Entrega/Recogido cuando el servidor lo fijó (deliveryMode no nulo). */
export function OrderBadges({ info }: { info: OrderBadgeInfo }) {
  const t = useT()
  const mode = info.deliveryMode === 'PICKUP' || info.deliveryMode === 'DELIVERY' ? info.deliveryMode : null
  if (!info.isUrgent && !mode && !info.paymentChanged && !info.isConsignment && !info.awaitingClientApproval) return null
  return (
    <span className="sales-badges">
      {info.isUrgent && <Chip tone="fail">{t('sales.badge.urgent')}</Chip>}
      {info.awaitingClientApproval && <Chip tone="warn" title={t('sales.badge.awaitingHelp')}>{t('sales.badge.awaiting')}</Chip>}
      {mode && <Chip tone={mode === 'PICKUP' ? 'cap' : 'route'}>{t(mode === 'PICKUP' ? 'sales.badge.pickup' : 'sales.badge.delivery')}</Chip>}
      {info.isConsignment && <Chip tone="cod">{t('sales.badge.consignment')}</Chip>}
      {info.paymentChanged && <Chip tone="warn" title={t('sales.badge.paymentChangedHelp')}>{t('sales.badge.paymentChanged')}</Chip>}
    </span>
  )
}
