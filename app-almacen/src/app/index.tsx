import { Redirect } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'

/** Puerta de entrada: sin aparato registrado → /enroll; aparato sin usuario → /login; con los dos → /home. */
export default function Index() {
  const { device, session } = useSession()
  if (!device) return <Redirect href="/enroll" />
  if (!session) return <Redirect href="/login" />
  return <Redirect href="/home" />
}
