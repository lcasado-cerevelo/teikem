// Íconos mínimos (trazo 2, 24×24) para acciones por fila de DataTable (RowAction.icon) — Lote F8a, pedido de Luis:
// "un icono a tono con la acción" en vez de un botón de texto, como ya definía Diseño/teikem-mockups.html (.rowbtn).
import type { ReactNode } from 'react'

function Svg({ children }: { children: ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

/** Editar (lápiz). */
export function IconEdit() {
  return (
    <Svg>
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </Svg>
  )
}

/** Eliminar (papelera). */
export function IconTrash() {
  return (
    <Svg>
      <path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
      <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
      <path d="M10 11v6M14 11v6" />
    </Svg>
  )
}

/** PIN / clave (llave). */
export function IconKey() {
  return (
    <Svg>
      <circle cx="7" cy="15" r="4" />
      <path d="M10.5 11.5 20 2M16 6l3 3M13 9l2 2" />
    </Svg>
  )
}

/** Activar / desactivar (encendido). */
export function IconPower() {
  return (
    <Svg>
      <path d="M12 2v10" />
      <path d="M18.36 6.64a9 9 0 1 1-12.73 0" />
    </Svg>
  )
}

/** Restaurar (volver atrás). */
export function IconRotateCcw() {
  return (
    <Svg>
      <path d="M3 3v6h6" />
      <path d="M3.51 15a9 9 0 1 0 2.13-9.36L3 9" />
    </Svg>
  )
}

/** Regenerar (código nuevo). */
export function IconRefreshCw() {
  return (
    <Svg>
      <path d="M21 4v6h-6" />
      <path d="M3 20v-6h6" />
      <path d="M20.49 9A9 9 0 0 0 5.6 5.6L3 8M3.51 15A9 9 0 0 0 18.4 18.4L21 16" />
    </Svg>
  )
}

/** Exigir/dejar de exigir MFA (escudo). */
export function IconShield() {
  return (
    <Svg>
      <path d="M12 2 4 5v6c0 5 3.4 8.6 8 10 4.6-1.4 8-5 8-10V5Z" />
      <path d="m9 12 2 2 4-4" />
    </Svg>
  )
}

/** Asignar a un usuario (persona con +). */
export function IconUserPlus() {
  return (
    <Svg>
      <circle cx="9" cy="7" r="4" />
      <path d="M2 21v-2a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v2M19 8v6M16 11h6" />
    </Svg>
  )
}

/** Iniciar (reproducir). */
export function IconPlay() {
  return (
    <Svg>
      <path d="m7 4 13 8-13 8Z" />
    </Svg>
  )
}

/** Completar (círculo con palomita). */
export function IconCheckCircle() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="9" />
      <path d="m8 12 3 3 5-6" />
    </Svg>
  )
}

/** Cancelar (círculo con equis). */
export function IconXCircle() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="9" />
      <path d="m15 9-6 6M9 9l6 6" />
    </Svg>
  )
}

/** Cerrar sesiones. */
export function IconLogOut() {
  return (
    <Svg>
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" />
    </Svg>
  )
}
