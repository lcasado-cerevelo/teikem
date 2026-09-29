// Íconos de pantalla (trazo 2, 24×24): los de los grupos del menú (`NAV` de Diseño/teikem-mockups.html: box, layers,
// cash, users, chart, gear) y los de cada pantalla (`ICONOF` de la maqueta), para la cabecera de `Panel` (`icon`).
// Cada pantalla usa el ícono que la maqueta le da en el menú; la que no existe en la maqueta usa el de su grupo.
// `app/icons.tsx` reexporta los de grupo para el shell.
import type { ReactNode } from 'react'

function Svg({ children }: { children: ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

// ===== Grupos del menú =====

/** Operación ('box'); en la maqueta también Portal → Inicio. */
export function IconBox() {
  return (
    <Svg>
      <path d="M21 8l-9-5-9 5 9 5 9-5zM3 8v8l9 5 9-5V8M12 13v8" />
    </Svg>
  )
}

/** Almacén ('layers'); en la maqueta también Órdenes y Productos e inventario. */
export function IconLayers() {
  return (
    <Svg>
      <path d="M12 3 2 8l10 5 10-5zM2 13l10 5 10-5M2 18l10 5 10-5" />
    </Svg>
  )
}

/** Contabilidad ('cash'). */
export function IconCash() {
  return (
    <Svg>
      <rect x="2" y="6" width="20" height="12" rx="2" />
      <circle cx="12" cy="12" r="2.5" />
    </Svg>
  )
}

/** Catálogo y Portal ('users'); en la maqueta también Clientes y Usuarios. */
export function IconUsers() {
  return (
    <Svg>
      <circle cx="9" cy="8" r="3" />
      <path d="M3.5 20a5.5 5.5 0 0 1 11 0M16 5.6a3 3 0 0 1 0 5.8M17 20a5.5 5.5 0 0 0-3-4.9" />
    </Svg>
  )
}

/** Análisis ('chart'); en la maqueta también Indicadores. */
export function IconChart() {
  return (
    <Svg>
      <path d="M3 3v18h18M8 17v-5M13 17V8M18 17v-9" />
    </Svg>
  )
}

/** Sistema ('gear'). */
export function IconGear() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 13a7 7 0 0 0 0-2l1.8-1.4-2-3.4-2.1.9a7 7 0 0 0-1.7-1L15 3h-4l-.3 2.1a7 7 0 0 0-1.7 1l-2.1-.9-2 3.4L6.6 11a7 7 0 0 0 0 2l-1.8 1.4 2 3.4 2.1-.9a7 7 0 0 0 1.7 1L11 21h4l.3-2.1a7 7 0 0 0 1.7-1l2.1.9 2-3.4z" />
    </Svg>
  )
}

// ===== Pantallas (`ICONOF` de la maqueta) =====

/** Almacenes ('warehouse'). */
export function IconWarehouse() {
  return (
    <Svg>
      <path d="M3 10 12 4l9 6v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-9Z" />
      <path d="M9 20v-6h6v6" />
    </Svg>
  )
}

/** Ubicaciones ('grid'): la pantalla, su panel y cada zona del río de ocupación. */
export function IconGrid() {
  return (
    <Svg>
      <rect x="3" y="3" width="7" height="7" rx="1.4" />
      <rect x="14" y="3" width="7" height="7" rx="1.4" />
      <rect x="3" y="14" width="7" height="7" rx="1.4" />
      <rect x="14" y="14" width="7" height="7" rx="1.4" />
    </Svg>
  )
}

/** Compras ('cart'). */
export function IconCart() {
  return (
    <Svg>
      <path d="M3 4h2l2.2 12a2 2 0 0 0 2 1.7h7a2 2 0 0 0 2-1.6L20.5 8H6.2" />
      <circle cx="9.5" cy="20" r="1.3" />
      <circle cx="17" cy="20" r="1.3" />
    </Svg>
  )
}

/** Recibo ('checkin'). */
export function IconCheckin() {
  return (
    <Svg>
      <path d="M4 8 12 4l8 4v9a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V8Z" />
      <path d="M4 8l8 4 8-4" />
      <path d="M9.5 13.5l1.7 1.7 3.3-3.4" />
    </Svg>
  )
}

/** Recolección y empaque ('basket'). */
export function IconBasket() {
  return (
    <Svg>
      <path d="M4 9h16l-1.6 9.3a2 2 0 0 1-2 1.7H7.6a2 2 0 0 1-2-1.7L4 9Z" />
      <path d="M8 9V6a4 4 0 0 1 8 0v3" />
      <path d="M9.5 13v3M14.5 13v3" />
    </Svg>
  )
}

/** Conteo cíclico ('clip'). */
export function IconClip() {
  return (
    <Svg>
      <rect x="5" y="4" width="14" height="17" rx="2" />
      <path d="M9 4V3a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1M8 11l2.5 2.5L16 8" />
    </Svg>
  )
}

/** Cruce de muelle ('swap'). */
export function IconSwap() {
  return (
    <Svg>
      <path d="M4 7h13l-3-3M4 7l3 3M20 17H7l3 3M20 17l-3-3" />
    </Svg>
  )
}

/** Kárdex de movimientos ('doc'). */
export function IconDoc() {
  return (
    <Svg>
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6" />
    </Svg>
  )
}

/** Etiqueta ('tag'): KPI "Con número de serie" de Productos e inventario. */
export function IconTag() {
  return (
    <Svg>
      <path d="M3 11V5a2 2 0 0 1 2-2h6l9 9-8 8z" />
      <circle cx="8" cy="8" r="1.4" />
    </Svg>
  )
}

/** Ajustes de inventario ('pencil'): nota de la pantalla y estado vacío "Selecciona una compra". Mismo trazo que `IconEdit`. */
export function IconPencil() {
  return (
    <Svg>
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </Svg>
  )
}

/** Actividad, pendientes, sesiones ('clock'). */
export function IconClock() {
  return (
    <Svg>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </Svg>
  )
}

/** Contraseña ('lock'). */
export function IconLock() {
  return (
    <Svg>
      <rect x="4" y="11" width="16" height="9" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
    </Svg>
  )
}
