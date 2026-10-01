import { Redirect } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'

/** Puerta de entrada: sin ningún registro → /enroll; sin compañía elegida o sin usuario → /login; con los dos → /home. */
export default function Index() {
  const { devices, device, session } = useSession()
  if (devices.length === 0) return <Redirect href="/enroll" />
  if (!device) return <Redirect href="/login" />
  if (!session) return <Redirect href="/login" />
  return <Redirect href="/home" />
}
