// Órdenes. Un consignatario con acceso propio (Lote 117) no las ve: su espacio es avisos, mensajes y ajustes.
import { Redirect } from 'expo-router'

import { OrdersScreen } from '../../features/orders/OrdersScreen'
import { useIsConsignee } from '../../kernel/auth/role'

export default function Orders() {
  return useIsConsignee() ? <Redirect href="/notices" /> : <OrdersScreen />
}
