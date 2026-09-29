// Íconos SVG mínimos del shell (trazo 2, 24×24). Solo los que usa el shell; las pantallas traen los suyos.
import type { ReactNode } from 'react'

function Svg({ children }: { children: ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

export function IconMenu() {
  return (
    <Svg>
      <path d="M4 6h16M4 12h16M4 18h16" />
    </Svg>
  )
}

export function IconChev() {
  return (
    <Svg>
      <path d="m9 6 6 6-6 6" />
    </Svg>
  )
}

export function IconCollapse() {
  return (
    <Svg>
      <path d="m15 6-6 6 6 6" />
    </Svg>
  )
}

export function IconLogout() {
  return (
    <Svg>
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" />
    </Svg>
  )
}

export function IconGlobe() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18" />
    </Svg>
  )
}

// Íconos de los grupos del menú: los de la maqueta (`NAV` en Diseño/teikem-mockups.html: box, layers, cash, users,
// chart, gear, users). Viven en el kit (`kernel/ui/screenIcons.tsx`) porque también los usa la cabecera de `Panel`.
export { IconBox, IconCash, IconChart, IconGear, IconLayers, IconUsers } from '../kernel/ui/screenIcons'

// Cabecera: buscador (lupa) y tema claro/oscuro.
export function IconSearch() {
  return (
    <Svg>
      <circle cx="11" cy="11" r="7" />
      <path d="m21 21-4.3-4.3" />
    </Svg>
  )
}

export function IconSun() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </Svg>
  )
}

export function IconMoon() {
  return (
    <Svg>
      <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
    </Svg>
  )
}
