// Órdenes. El consignatario con acceso propio (Lote 117d) ve aquí sus envíos, solo lectura.
import { ConsigneeOrdersScreen } from '../../features/consigneeSpace/ConsigneeOrdersScreen'
import { OrdersScreen } from '../../features/orders/OrdersScreen'
import { useIsConsignee } from '../../kernel/auth/role'

export default function Orders() {
  return useIsConsignee() ? <ConsigneeOrdersScreen /> : <OrdersScreen />
}
