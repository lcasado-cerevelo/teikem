// Paleta de la marca (portada de la app ColaVora: azul marino #03204B sobre fondo blanco) con su versión OSCURA.
// El tema es Automático (sigue al teléfono), Claro u Oscuro; los colores NUNCA se escriben fijos en las pantallas: salen de useTheme().
// Los pares texto/fondo cumplen WCAG AA (4.5:1); lo verifica scripts/check-theme-contrast.mjs.

export interface Colors {
  primary: string;
  /** Texto sobre un fondo `primary` (botones). */
  primaryContrast: string;
  text: string;
  background: string;
  backgroundSecondary: string;
  /** Bordes y separadores. */
  gray: string;
  danger: string;
  /** Texto secundario. */
  muted: string;
  /** Estado «en línea» / «sin conexión» (texto blanco encima). */
  online: string;
  offline: string;
  /** Fondo de las capas modales (detrás de una hoja). */
  backdrop: string;
}

export const lightColors: Colors = {
  primary: '#03204B',
  primaryContrast: '#ffffff',
  text: '#1a1a1a',
  background: '#ffffff',
  backgroundSecondary: '#FAFBFC',
  gray: '#D9D9D9',
  danger: '#C62828',
  muted: '#5B5B5B',
  online: '#2E7D32',
  offline: '#5F6368',
  backdrop: 'rgba(0,0,0,0.4)',
};

export const darkColors: Colors = {
  primary: '#7FA8E8',
  primaryContrast: '#0B1626',
  text: '#F2F4F8',
  background: '#0F141C',
  backgroundSecondary: '#18202B',
  gray: '#3A4452',
  danger: '#FF8A80',
  muted: '#A8B0BD',
  online: '#2E7D32',
  offline: '#5F6368',
  backdrop: 'rgba(0,0,0,0.6)',
};

export type ThemeMode = 'auto' | 'light' | 'dark';
export const DEFAULT_THEME_MODE: ThemeMode = 'auto';
