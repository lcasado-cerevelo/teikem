// Lote 8A-app — paleta y medidas del kit (pantalla del Zebra: 4", alto contraste, botones grandes para trabajar con
// guantes). Un solo tema por ahora (el tema por aparato/usuario, docs/mobile/app-almacen-plan.md §7, llega con A5).
export const colors = {
  bg: '#0B1220',
  panel: '#141D2E',
  panelAlt: '#1B263B',
  line: '#26344A',
  text: '#EAF1FB',
  muted: '#8CA0BE',
  brand: '#1F6FE5',
  brandDark: '#154FA6',
  ok: '#1FA971',
  error: '#E5484D',
  warn: '#E5A11F',
  // fondos de los avisos al escanear: con texto blanco, contraste ≥ 5.5:1 (más que el rojo y el verde de arriba)
  errorStrong: '#B42318',
  okStrong: '#0E7A4F',
  onStrong: '#FFFFFF',
} as const

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const

export const radius = { sm: 8, md: 12, lg: 16 } as const

export const touchTarget = 56

/** Tamaños de letra del kit (docs/mobile/mejoras-ux-zebra.md §4, usuarios no técnicos en una pantalla de 4"): ningún mensaje
 *  por debajo de 16. */
export const fontSize = {
  /** Título de una pantalla. */
  title: 20,
  /** Etiqueta de un campo o de una sección. */
  label: 16,
  /** Ayuda, error y cualquier mensaje (mínimo). */
  message: 16,
  /** Título y subtítulo de una fila de lista. */
  listTitle: 18,
  listSubtitle: 15,
  /** Etiqueta de un botón. */
  button: 20,
  /** Aviso que sale al escanear (encontrado / no encontrado). */
  scanMessage: 20,
} as const

/** Alto de un botón de acción de la pantalla principal (icono arriba, texto abajo, dos columnas). */
export const tileHeight = 88
