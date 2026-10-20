// Inicio. Un consignatario con acceso propio (Lote 117) no tiene inicio ni órdenes: su espacio empieza en los avisos. Una compañía solo de almacén no tiene inicio de transporte: se entra por Almacén.
import { Redirect } from 'expo-router'

import { landingTab, useOffer } from '../../features/capabilities'
import { HomeScreen } from '../../features/home/HomeScreen'
import { useIsConsignee } from '../../kernel/auth/role'

export default function Home() {
  const consignee = useIsConsignee()
  const offer = useOffer()
  if (consignee) return <Redirect href="/notices" />
  if (offer.known && landingTab(offer) === 'warehouse') return <Redirect href="/warehouse" />
  return <HomeScreen />
}
