import { Redirect } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'

/** Puerta de entrada: sin ningún registro → /enroll; sin compañía elegida o sin usuario → /login; con los dos → /home (o /lock si la sesión está bloqueada). */
export default function Index() {
  const { devices, device, session, locked } = useSession()
  if (devices.length === 0) return <Redirect href="/enroll" />
  if (!device) return <Redirect href="/login" />
  if (!session) return <Redirect href="/login" />
  if (locked) return <Redirect href="/lock" />
  return <Redirect href="/home" />
}
