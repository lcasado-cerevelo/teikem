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
} as const

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const

export const radius = { sm: 8, md: 12, lg: 16 } as const

export const touchTarget = 56
