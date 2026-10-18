// Inicio. Un consignatario con acceso propio (Lote 117) no tiene inicio ni órdenes: su espacio empieza en los avisos.
import { Redirect } from 'expo-router'

import { HomeScreen } from '../../features/home/HomeScreen'
import { useIsConsignee } from '../../kernel/auth/role'

export default function Home() {
  return useIsConsignee() ? <Redirect href="/notices" /> : <HomeScreen />
}
