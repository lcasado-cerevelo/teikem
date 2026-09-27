import { Outlet } from 'react-router-dom'
import { ReauthProvider } from '../kernel/auth/ReauthProvider'
import { useSession } from './session'
import { SessionProvider } from './SessionProvider'

function WithReauth() {
  const { me } = useSession()
  return (
    <ReauthProvider mfaEnabled={me?.mfaEnabled ?? false}>
      <Outlet />
    </ReauthProvider>
  )
}

/** Raíz del árbol de rutas: sesión (dentro del router, para poder navegar) y reautenticación. */
export default function Root() {
  return (
    <SessionProvider>
      <WithReauth />
    </SessionProvider>
  )
}
