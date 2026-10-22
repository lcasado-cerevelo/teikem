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

/** Rayo ('bolt'): modo «Rápida» de la entrada de órdenes (Lote F-C3). */
export function IconBolt() {
  return (
    <Svg>
      <path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z" />
    </Svg>
  )
}

/** Formulario ('form'): modo «Detallada» de la entrada de órdenes (Lote F-C3). */
export function IconForm() {
  return (
    <Svg>
      <rect x="4" y="3" width="16" height="18" rx="2" />
      <path d="M8 8h8M8 12h8M8 16h4" />
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

// ===== Ajustes de la compañía (lote F9; `ic('pin')`, `ic('route')` e `ic('print')` de la maqueta) =====

/** 'pin' de la maqueta: pestaña y panel Región y formatos. */
export function IconPin() {
  return (
    <Svg>
      <path d="M12 21s7-6.3 7-11a7 7 0 1 0-14 0c0 4.7 7 11 7 11z" />
      <circle cx="12" cy="10" r="2.4" />
    </Svg>
  )
}

/** 'route' de la maqueta: pestaña Operación y pipeline de estatus de órdenes. */
export function IconRoute() {
  return (
    <Svg>
      <circle cx="6" cy="18" r="2.4" />
      <circle cx="18" cy="6" r="2.4" />
      <path d="M8.4 18H15a3 3 0 0 0 0-6H9a3 3 0 0 1 0-6h6.6" />
    </Svg>
  )
}

/** 'print' de la maqueta: panel Teléfono de Región y formatos. */
export function IconPhoneFormat() {
  return (
    <Svg>
      <path d="M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2M6 14h12v7H6z" />
    </Svg>
  )
}

/** 'print' de la maqueta (Lote F-G1): Impresoras y labels, Prueba e Imprimir. Mismo trazo que el del panel Teléfono. */
export const IconPrint = IconPhoneFormat

/** Ficha del cliente: edificio con cúpula y columnas (un Capitolio), para el nombre de la compañía. */
export function IconCapitol() {
  return (
    <Svg>
      <path d="M12 2v3M8 9a4 4 0 0 1 8 0M3 9h18M5 9v9M9.5 9v9M14.5 9v9M19 9v9M2 21h20M3 18h18" />
    </Svg>
  )
}

/** Personas de contacto: una persona y su tarjeta de contacto. */
export function IconContactPerson() {
  return (
    <Svg>
      <circle cx="8.5" cy="8" r="3" />
      <path d="M3 20a5.5 5.5 0 0 1 11 0M16 7h5M16 11h5M16 15h3" />
    </Svg>
  )
}

/** Equipo de servicio del cliente: audífonos con micrófono (quien lo atiende). */
export function IconSupportTeam() {
  return (
    <Svg>
      <path d="M4 14v-2a8 8 0 0 1 16 0v2M4 14h3v5H4zM17 14h3v5h-3zM20 19a4 4 0 0 1-4 3h-2" />
    </Svg>
  )
}

/** Impresión y portal del cliente: impresora y, al lado, una pantalla (lo que sale solo y lo que el cliente crea desde su portal). */
export function IconPrintPortal() {
  return (
    <Svg>
      <path d="M6 9V3h8v6M6 18H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2M6 14h8v7H6z" />
      <rect x="16" y="3" width="6" height="9" rx="1" />
      <path d="M18 9.5h2" />
    </Svg>
  )
}

/** 'scan' de la maqueta: Estación de escaneo (recuadro con barras). */
export function IconScan() {
  return (
    <Svg>
      <path d="M4 7V5a1 1 0 0 1 1-1h2M17 4h2a1 1 0 0 1 1 1v2M20 17v2a1 1 0 0 1-1 1h-2M7 20H5a1 1 0 0 1-1-1v-2M7 8v8M11 8v8M14 8v8M17 8v8" />
    </Svg>
  )
}

/** 'volume' de la maqueta: voz activada. */
export function IconVolume() {
  return (
    <Svg>
      <path d="M4 9v6h4l5 4V5L8 9H4Z" />
      <path d="M16 8a5 5 0 0 1 0 8" />
    </Svg>
  )
}

/** 'volumeOff' de la maqueta: voz silenciada. */
export function IconVolumeOff() {
  return (
    <Svg>
      <path d="M4 9v6h4l5 4V5L8 9H4Z" />
      <path d="m17 8 5 8M22 8l-5 8" />
    </Svg>
  )
}

/** 'truck' de la maqueta: Flota y mantenimiento (pantalla, vehículos, cuando se agregue Despacho también). */
export function IconTruck() {
  return (
    <Svg>
      <path d="M1 5h12v11H1zM13 9h4l3 3v4h-7" />
      <circle cx="5" cy="19" r="2" />
      <circle cx="16.5" cy="19" r="2" />
    </Svg>
  )
}

// ---- Lote 200 (2026-10-22): íconos propios para las secciones de la ficha del cliente, que repetían Users/Tag/Doc ----

/** Perfil del cliente: una tarjeta de identificación. */
export function IconIdCard() {
  return (
    <Svg>
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <circle cx="9" cy="11" r="2" />
      <path d="M6 16c.6-1.4 1.7-2 3-2s2.4.6 3 2M14 10h4M14 14h4" />
    </Svg>
  )
}

/** Consignatarios (los lugares a donde se entrega): un edificio. */
export function IconBuilding() {
  return (
    <Svg>
      <path d="M4 21V4a1 1 0 0 1 1-1h9a1 1 0 0 1 1 1v17M15 9h4a1 1 0 0 1 1 1v11M2 21h20M8 7h3M8 11h3M8 15h3" />
    </Svg>
  )
}

/** Numeración de documentos: el signo de número. */
export function IconHash() {
  return (
    <Svg>
      <path d="M5 9h15M4 15h15M10 3 8 21M16 3l-2 18" />
    </Svg>
  )
}

/** Teléfonos y correos del cliente: un teléfono. */
export function IconPhone() {
  return (
    <Svg>
      <path d="M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2z" />
    </Svg>
  )
}
