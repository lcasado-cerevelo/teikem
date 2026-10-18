// Ficha de una orden: la persona del cliente ve la suya completa; el consignatario con acceso propio (Lote 117d), la de su envío sin precios.
import { ConsigneeOrderDetailScreen } from '../../features/consigneeSpace/ConsigneeOrderDetailScreen'
import { OrderDetailScreen } from '../../features/orders/OrderDetailScreen'
import { useIsConsignee } from '../../kernel/auth/role'

export default function OrderRoute() {
  return useIsConsignee() ? <ConsigneeOrderDetailScreen /> : <OrderDetailScreen />
}
