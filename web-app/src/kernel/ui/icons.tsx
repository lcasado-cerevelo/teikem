// Íconos SVG mínimos del kit (trazo 2, 24×24).
import type { ReactNode } from 'react'

function Svg({ children }: { children: ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

export function IconSearch() {
  return (
    <Svg>
      <circle cx="11" cy="11" r="7" />
      <path d="m20 20-3.5-3.5" />
    </Svg>
  )
}

export function IconClose() {
  return (
    <Svg>
      <path d="M18 6 6 18M6 6l12 12" />
    </Svg>
  )
}

export function IconCheck() {
  return (
    <Svg>
      <path d="m5 12 5 5L20 7" />
    </Svg>
  )
}

export function IconAlert() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 8v5M12 16.5v.5" />
    </Svg>
  )
}

export function IconInbox() {
  return (
    <Svg>
      <path d="M3 13h5l2 3h4l2-3h5" />
      <path d="M5 5h14l2 8v6H3v-6z" />
    </Svg>
  )
}

export function IconChevronDown() {
  return (
    <Svg>
      <path d="m6 9 6 6 6-6" />
    </Svg>
  )
}
