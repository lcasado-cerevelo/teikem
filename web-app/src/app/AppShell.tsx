// Shell de la aplicación: barra lateral por grupos (filtrada por módulos y permisos), colapsable en escritorio y
// en cajón bajo 900 px; cabecera con "Buscar o ejecutar…" (paleta de comandos, atajos / y Ctrl/⌘+K; lupa en pantallas
// angostas), reloj "en vivo", compañía, tema claro/oscuro, idioma, usuario y salir. Cambiar idioma o tema no desmonta
// nada: el grupo abierto y la pantalla actual se conservan.
import { Suspense, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAccess } from '../kernel/access/accessContext'
import { applyProblemDetails } from '../kernel/api/problem'
import { useLang, useT } from '../kernel/i18n/useT'
import { BrandLockup, BrandMark } from '../kernel/ui/Brand'
import { CommandPalette } from '../kernel/ui/CommandPalette'
import { ExportCompanyProvider, FilterScope } from '../kernel/ui/FilterScope'
import {
  closeCommandPalette,
  openCommandPalette,
  useCommandPaletteOpen,
  useCommandPaletteShortcut,
  type CommandItem,
} from '../kernel/ui/commandPaletteStore'
import { setTheme, useTheme, type Theme } from '../kernel/ui/theme'
import { IconChev, IconCollapse, IconLogout, IconMenu, IconMoon, IconSearch, IconSun } from './icons'
import { LangSelect } from './LangSelect'
import { switchableMemberships } from './memberships'
import { navSubtitleKey, navTitleKey, visibleNav, type NavGroup, type NavGroupKey } from './navigation'
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

/** "● en vivo · HH:MM:SS" con la hora del idioma activo; se repinta solo él cada segundo (oculto bajo 600 px). */
function LiveClock() {
  const t = useT()
  const lang = useLang()
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(id)
  }, [])
  const fmt = useMemo(
    () => new Intl.DateTimeFormat(lang, { hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }),
    [lang],
  )
  const time = fmt.format(now)
  return (
    <div className="live" data-testid="live-clock">
      <span className="dot" aria-hidden="true" />
      <span className="lbl">{t('shell.live')}</span>
      <span className="lbl" aria-hidden="true">
        ·
      </span>
      <time className="mono" dateTime={now.toISOString()} aria-label={`${t('shell.clock')} ${time}`}>
        {time}
      </time>
    </div>
  )
}

/** Segmento ☀ / 🌙 de la maqueta: cambia `data-theme` en <html> y lo guarda (`teikem.theme`). */
function ThemeSwitch() {
  const t = useT()
  const theme = useTheme()
  const options: { value: Theme; icon: ReactNode }[] = [
    { value: 'light', icon: <IconSun /> },
    { value: 'dark', icon: <IconMoon /> },
  ]
  return (
    <div className="seg theme-seg" role="group" aria-label={t('shell.theme.label')}>
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          className={theme === o.value ? 'on' : undefined}
          aria-pressed={theme === o.value}
          aria-label={t(`shell.theme.${o.value}`)}
          title={t(`shell.theme.${o.value}`)}
          onClick={() => setTheme(o.value)}
        >
          {o.icon}
        </button>
      ))}
    </div>
  )
}

export function AppShell() {
  const t = useT()
  const { me, lang, setLang, logout, switchTenant } = useSession()
  const location = useLocation()
  const navigate = useNavigate()
  const groups = useVisibleNav()
  const [collapsed, setCollapsed] = useState(readCollapsed)
  const [drawer, setDrawer] = useState(false)
  const activeGroup = groups.find((g) => g.items.some((r) => r.path === location.pathname))?.key
  const [openGroup, setOpenGroup] = useState<NavGroupKey | undefined>(activeGroup ?? groups[0]?.key)
  const [switchError, setSwitchError] = useState<string | null>(null)
  const paletteOpen = useCommandPaletteOpen()
  useCommandPaletteShortcut(openCommandPalette)

  // Al navegar: cerrar el cajón y abrir el grupo de la pantalla actual (ajuste de estado durante el render).
  const [lastPath, setLastPath] = useState(location.pathname)
  if (lastPath !== location.pathname) {
    setLastPath(location.pathname)
    setDrawer(false)
    if (activeGroup) setOpenGroup(activeGroup)
  }

  // La paleta no sobrevive a la salida del shell (p. ej. cerrar sesión con la paleta abierta).
  useEffect(() => closeCommandPalette, [])

  // Destinos de la paleta: los mismos ítems visibles del menú, en su orden y agrupados igual.
  const commands = useMemo<(CommandItem & { path: string })[]>(
    () =>
      groups.flatMap((g) => {
        const GroupIcon = g.icon
        return g.items.map((r) => ({
          id: r.path,
          path: r.path,
          group: g.key,
          groupLabel: t(g.labelKey),
          title: t(navTitleKey(r.nav?.key ?? '')),
          subtitle: t(navSubtitleKey(r.nav?.key ?? '')),
          icon: <GroupIcon />,
        }))
      }),
    [groups, t],
  )

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
        {/* Marca (P7): lockup a todo el ancho; colapsada, la marca cuadrada de 44 px (en el cajón móvil, el lockup). */}
        <Link to="/" className="rbrand" aria-label="Teikem">
          <BrandLockup className="brand-full" />
          {collapsed && <BrandMark size={44} alt="" className="brand-mini" />}
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
                      <span>{t(navTitleKey(r.nav?.key ?? ''))}</span>
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
          <button
            type="button"
            className="cmd"
            onClick={openCommandPalette}
            aria-haspopup="dialog"
            aria-keyshortcuts="/ Control+K Meta+K"
          >
            <IconSearch />
            <span className="cmd-ph">{t('shell.palette.placeholder')}</span>
            <kbd aria-hidden="true">/</kbd>
          </button>
          <div className="sp" />
          <button
            type="button"
            className="iconbtn cmd-mini"
            aria-label={t('shell.palette.open')}
            title={t('shell.palette.open')}
            aria-haspopup="dialog"
            onClick={openCommandPalette}
          >
            <IconSearch />
          </button>
          <LiveClock />
          {/* compañía a la derecha, entre el reloj y el tema (como la maqueta) */}
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
          <ThemeSwitch />
          <LangSelect lang={lang} onChange={setLang} />
          {/* usuario: solo el círculo de iniciales (maqueta); el nombre completo va en el title y en el nombre accesible */}
          <Link
            to="/account"
            className="who"
            title={`${me?.fullName ?? me?.email ?? ''} · ${t('shell.account')}`}
            aria-label={`${t('shell.account')}: ${me?.fullName ?? me?.email ?? ''}`}
          >
            <span className="av" aria-hidden="true">
              {initials(me?.fullName ?? me?.email)}
            </span>
          </Link>
          <button type="button" className="iconbtn" aria-label={t('shell.logout')} title={t('shell.logout')} onClick={() => void logout()}>
            <IconLogout />
          </button>
        </header>
        <main className="stage">
          <div className="wrap">
            {/* exportaciones de tablas: compañía activa arriba del título y la oración de los filtros de la pantalla */}
            <ExportCompanyProvider company={me?.tenantName}>
              <FilterScope>
                <Suspense fallback={<Splash />}>
                  <Outlet />
                </Suspense>
              </FilterScope>
            </ExportCompanyProvider>
          </div>
        </main>
      </div>
      <CommandPalette
        open={paletteOpen}
        items={commands}
        onClose={closeCommandPalette}
        onSelect={(item) => {
          closeCommandPalette()
          navigate(item.path)
        }}
      />
    </div>
  )
}
