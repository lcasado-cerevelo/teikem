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
// chart, gear, users).
export function IconBox() {
  return (
    <Svg>
      <path d="M21 8l-9-5-9 5 9 5 9-5zM3 8v8l9 5 9-5V8M12 13v8" />
    </Svg>
  )
}

export function IconLayers() {
  return (
    <Svg>
      <path d="M12 3 2 8l10 5 10-5zM2 13l10 5 10-5M2 18l10 5 10-5" />
    </Svg>
  )
}

export function IconCash() {
  return (
    <Svg>
      <rect x="2" y="6" width="20" height="12" rx="2" />
      <circle cx="12" cy="12" r="2.5" />
    </Svg>
  )
}

export function IconUsers() {
  return (
    <Svg>
      <circle cx="9" cy="8" r="3" />
      <path d="M3.5 20a5.5 5.5 0 0 1 11 0M16 5.6a3 3 0 0 1 0 5.8M17 20a5.5 5.5 0 0 0-3-4.9" />
    </Svg>
  )
}

export function IconChart() {
  return (
    <Svg>
      <path d="M3 3v18h18M8 17v-5M13 17V8M18 17v-9" />
    </Svg>
  )
}

export function IconGear() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 13a7 7 0 0 0 0-2l1.8-1.4-2-3.4-2.1.9a7 7 0 0 0-1.7-1L15 3h-4l-.3 2.1a7 7 0 0 0-1.7 1l-2.1-.9-2 3.4L6.6 11a7 7 0 0 0 0 2l-1.8 1.4 2 3.4 2.1-.9a7 7 0 0 0 1.7 1L11 21h4l.3-2.1a7 7 0 0 0 1.7-1l2.1.9 2-3.4z" />
    </Svg>
  )
}

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
