// Shell de la aplicación: barra lateral por grupos (filtrada por módulos y permisos), colapsable en escritorio y
// en cajón bajo 900 px; cabecera con tenant, idioma, usuario y salir. Cambiar idioma no desmonta nada: el grupo
// abierto y la pantalla actual se conservan.
import { Suspense, useMemo, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAccess } from '../kernel/access/accessContext'
import { applyProblemDetails } from '../kernel/api/problem'
import { useT } from '../kernel/i18n/useT'
import { IconChev, IconCollapse, IconLogout, IconMenu } from './icons'
import { LangSelect } from './LangSelect'
import { switchableMemberships } from './memberships'
import { visibleNav, type NavGroup, type NavGroupKey } from './navigation'
import { appRoutes, type AppRoute } from './routes'
import { useSession } from './session'
import { Splash } from './Splash'

const COLLAPSED_KEY = 'teikem.rail.collapsed'

function initials(name: string | null | undefined): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '·'
}

function readCollapsed(): boolean {
  try {
    return globalThis.localStorage?.getItem(COLLAPSED_KEY) === '1'
  } catch {
    return false
  }
}

/** Rutas visibles en el menú para los permisos y módulos del usuario, agrupadas. */
function useVisibleNav(): (NavGroup & { items: AppRoute[] })[] {
  const { permissions, modules } = useAccess()
  return useMemo(() => visibleNav(appRoutes, permissions, modules), [permissions, modules])
}

export function AppShell() {
  const t = useT()
  const { me, lang, setLang, logout, switchTenant } = useSession()
  const location = useLocation()
  const groups = useVisibleNav()
  const [collapsed, setCollapsed] = useState(readCollapsed)
  const [drawer, setDrawer] = useState(false)
  const activeGroup = groups.find((g) => g.items.some((r) => r.path === location.pathname))?.key
  const [openGroup, setOpenGroup] = useState<NavGroupKey | undefined>(activeGroup ?? groups[0]?.key)
  const [switchError, setSwitchError] = useState<string | null>(null)

  // Al navegar: cerrar el cajón y abrir el grupo de la pantalla actual (ajuste de estado durante el render).
  const [lastPath, setLastPath] = useState(location.pathname)
  if (lastPath !== location.pathname) {
    setLastPath(location.pathname)
    setDrawer(false)
    if (activeGroup) setOpenGroup(activeGroup)
  }

  function toggleCollapsed() {
    setCollapsed((c) => {
      try {
        globalThis.localStorage?.setItem(COLLAPSED_KEY, c ? '0' : '1')
      } catch {
        // sin almacenamiento
      }
      return !c
    })
  }

  async function onSwitchTenant(value: string) {
    setSwitchError(null)
    try {
      await switchTenant(Number(value))
    } catch (err) {
      setSwitchError(applyProblemDetails(err).title)
    }
  }

  // Solo las membresías a las que el API deja cambiar (ACTIVE/PLATFORM): una suspendida o invitada no se ofrece.
  const memberships = switchableMemberships(me?.memberships)
  const classes = ['app', collapsed ? 'col' : '', drawer ? 'drawer' : ''].filter(Boolean).join(' ')

  return (
    <div className={classes}>
      <aside className="rail" id="app-rail" aria-label={t('shell.menu')}>
        <Link to="/" className="rbrand" aria-label="Teikem">
          <span className="logo" aria-hidden="true">
            T
          </span>
          <span className="name">Teikem</span>
        </Link>
        <nav className="rnav">
          {groups.map((g) => {
            const GroupIcon = g.icon
            const isOpen = openGroup === g.key
            return (
              <div key={g.key} className={isOpen ? 'grp open' : 'grp'}>
                <button
                  type="button"
                  className="grp-h"
                  aria-expanded={isOpen}
                  title={t(g.labelKey)}
                  onClick={() => {
                    if (collapsed && !drawer && window.matchMedia?.('(min-width: 901px)').matches) setCollapsed(false)
                    setOpenGroup(isOpen ? undefined : g.key)
                  }}
                >
                  <span className="gi">
                    <GroupIcon />
                  </span>
                  <span className="lbl">{t(g.labelKey)}</span>
                  <span className="chev">
                    <IconChev />
                  </span>
                </button>
                <div className="grp-items">
                  {g.items.map((r) => (
                    <NavLink key={r.path} to={r.path} end className={({ isActive }) => (isActive ? 'navit on' : 'navit')}>
                      <span className="di" />
                      <span>{t(r.nav?.labelKey ?? r.path)}</span>
                    </NavLink>
                  ))}
                </div>
              </div>
            )
          })}
        </nav>
        <div className="rfoot">
          <button type="button" className="collapse" onClick={toggleCollapsed} aria-pressed={collapsed}>
            <IconCollapse />
            <span>{collapsed ? t('shell.expand') : t('shell.collapse')}</span>
          </button>
        </div>
      </aside>
      <div className="drawer-scrim" onClick={() => setDrawer(false)} aria-hidden="true" />

      <div className="main">
        <header className="bar">
          <button
            type="button"
            className="burger"
            aria-label={t('shell.openMenu')}
            aria-controls="app-rail"
            aria-expanded={drawer}
            onClick={() => setDrawer((d) => !d)}
          >
            <IconMenu />
          </button>
          <div className="tenant">
            {memberships.length > 1 ? (
              <select
                aria-label={t('shell.tenant')}
                value={String(me?.tenantId ?? '')}
                onChange={(e) => void onSwitchTenant(e.target.value)}
              >
                {memberships.map((m) => (
                  <option key={m.tenantId} value={String(m.tenantId)}>
                    {m.tenantName}
                  </option>
                ))}
              </select>
            ) : (
              <span className="tn" title={me?.tenantName ?? ''}>
                {me?.tenantName}
              </span>
            )}
            {switchError && (
              <span className="ferr" role="alert">
                {switchError}
              </span>
            )}
          </div>
          <div className="sp" />
          <LangSelect lang={lang} onChange={setLang} />
          <Link to="/account" className="who" title={t('shell.account')}>
            <span className="av" aria-hidden="true">
              {initials(me?.fullName ?? me?.email)}
            </span>
            <span className="nm">{me?.fullName ?? me?.email}</span>
          </Link>
          <button type="button" className="iconbtn" aria-label={t('shell.logout')} title={t('shell.logout')} onClick={() => void logout()}>
            <IconLogout />
          </button>
        </header>
        <main className="stage">
          <div className="wrap">
            <Suspense fallback={<Splash />}>
              <Outlet />
            </Suspense>
          </div>
        </main>
      </div>
    </div>
  )
}
