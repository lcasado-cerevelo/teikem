import { Suspense } from 'react'
import { createBrowserRouter } from 'react-router-dom'
import { AppShell } from './AppShell'
import NotFound from './NotFound'
import { RequireAuth } from './RequireAuth'
import Root from './Root'
import { RouteGate } from './RouteGate'
import { appRoutes, publicRoutes } from './routes'
import { Splash } from './Splash'

export const router = createBrowserRouter([
  {
    element: <Root />,
    children: [
      ...publicRoutes.map((r) => ({
        path: r.path,
        element: (
          <Suspense fallback={<Splash full />}>
            <r.element />
          </Suspense>
        ),
      })),
      {
        element: (
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        ),
        children: [...appRoutes.map((r) => ({ path: r.path, element: <RouteGate route={r} /> })), { path: '*', element: <NotFound /> }],
      },
    ],
  },
])
