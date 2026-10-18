// Pestaña «Avisos»: una persona del cliente ve los avisos que la compañía le manda; un consignatario con acceso propio, la bandeja de lo que se le avisó (Lote 117).
import { useIsConsignee } from '../../kernel/auth/role'
import { ConsigneeInboxScreen } from '../consigneeSpace/ConsigneeInboxScreen'
import { NoticesScreen } from './NoticesScreen'

export function NoticesTab() {
  return useIsConsignee() ? <ConsigneeInboxScreen /> : <NoticesScreen />
}
