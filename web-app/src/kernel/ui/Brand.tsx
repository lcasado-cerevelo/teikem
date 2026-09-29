// Marca Teikem (Lote F8a P7). Los archivos son los del paquete `Logos/` copiados a `public/brand/` (no se regeneran aquí),
// salvo las variantes `-inv`, ajustadas a la maqueta: sin rectángulo de fondo (transparentes sobre el degradado de la
// barra) y con el símbolo más pegado al borde izquierdo del lienzo;
// qué archivo toca por idioma y tema lo decide `brandAssets.ts`. Idioma y tema se leen de sus almacenes, así que el
// archivo cambia al instante sin recargar ni desmontar la pantalla.
import { useLang } from '../i18n/useT'
import { BRAND_SYMBOL_SRC, brandLockupSrc } from './brandAssets'
import { useTheme } from './theme'
import './ui.css'

export interface BrandLockupProps {
  /** Con lema ("Inteligencia de Entregas" / "Delivery Intelligence"); por defecto true. */
  tagline?: boolean
  className?: string
}

/** Lockup completo (símbolo + TEIKEM + lema) en `<img>`: ocupa el ancho de su contenedor con su proporción. */
export function BrandLockup({ tagline = true, className }: BrandLockupProps) {
  const lang = useLang()
  const theme = useTheme()
  return (
    <img
      src={brandLockupSrc(lang, theme, tagline)}
      alt="Teikem"
      // proporción del viewBox del SVG (reserva el alto antes de cargar)
      width={tagline ? 480 : 470}
      height={150}
      className={className ? `brand-lockup ${className}` : 'brand-lockup'}
      data-testid="brand-lockup"
      draggable={false}
    />
  )
}

export interface BrandMarkProps {
  /** Lado en px (cuadrado); por defecto 44, el de la barra colapsada. */
  size?: number
  /** Texto alternativo; `''` cuando es decorativa (ya hay un título al lado). */
  alt?: string
  className?: string
}

/** Solo el símbolo, para espacios chicos (barra colapsada, pantallas pendientes). Igual en ambos temas. */
export function BrandMark({ size = 44, alt = 'Teikem', className }: BrandMarkProps) {
  return (
    <img
      src={BRAND_SYMBOL_SRC}
      alt={alt}
      width={size}
      height={size}
      className={className ? `brand-mark ${className}` : 'brand-mark'}
      data-testid="brand-mark"
      draggable={false}
    />
  )
}
