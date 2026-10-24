// Logo de la compañía en los PDF (pedido de Luis, 2026-10-21): los reportes y las exportaciones a PDF llevan en el encabezado el logo que la compañía subió
// (Ajustes → Marca), no el de Teikem. Se toma del almacén de logos (`brandLogos.ts`, ya descargados por `TenantBrand`) y se rasteriza a PNG conservando su
// proporción (jsPDF no dibuja SVG). Sin logo subido, el reporte de marca conserva el símbolo de Teikem y las tablas van sin logo.
import { brandLockupSrc } from './brandAssets'
import { getCompanyLogos, pickLogoUrl } from './brandLogos'

export interface PdfLogo {
  /** PNG (data URL), con la proporción del logo original. */
  dataUrl: string
  width: number
  height: number
  /** Si la variante elegida no es la del fondo del PDF: el logo se dibuja sobre una placa de este color para que se vea. */
  plate: 'white' | 'navy' | null
}

/** Superficie donde se dibuja: la banda azul marino de los reportes (oscura) o la hoja blanca de las tablas (clara). */
export type PdfSurface = 'dark' | 'light'

/** ¿La compañía subió algún logo? (sin él no hay nada que cargar). */
export function hasCompanyLogo(): boolean {
  return Object.keys(getCompanyLogos().urls).length > 0
}

/** Rasteriza una imagen (SVG incluido) a PNG con su proporción, dentro de `maxW`×`maxH` px. null si no se puede (sin DOM, error, 3 s sin cargar). */
export function rasterizeLogo(src: string, maxW = 480, maxH = 160, timeoutMs = 3000): Promise<{ dataUrl: string; width: number; height: number } | null> {
  if (typeof document === 'undefined' || typeof Image === 'undefined') return Promise.resolve(null)
  return new Promise((resolve) => {
    const img = new Image()
    const timer = setTimeout(() => resolve(null), timeoutMs)
    img.onload = () => {
      clearTimeout(timer)
      try {
        // un SVG sin tamaño propio no da medidas: se le supone 3:1
        const nw = img.naturalWidth || 300
        const nh = img.naturalHeight || 100
        const scale = Math.min(maxW / nw, maxH / nh)
        const width = Math.max(1, Math.round(nw * scale))
        const height = Math.max(1, Math.round(nh * scale))
        const canvas = document.createElement('canvas')
        canvas.width = width
        canvas.height = height
        const ctx = canvas.getContext('2d')
        if (!ctx) return resolve(null)
        ctx.drawImage(img, 0, 0, width, height)
        resolve({ dataUrl: canvas.toDataURL('image/png'), width, height })
      } catch {
        resolve(null)
      }
    }
    img.onerror = () => {
      clearTimeout(timer)
      resolve(null)
    }
    img.src = src
  })
}

const cache = new Map<string, Promise<PdfLogo | null>>()

/** El logo de la compañía listo para el PDF (lockup; si no hay, la marca cuadrada); null = la compañía no subió ninguno o no se pudo leer. */
export function loadCompanyPdfLogo(surface: PdfSurface): Promise<PdfLogo | null> {
  const urls = getCompanyLogos().urls
  const theme = surface === 'dark' ? 'dark' : 'light'
  const url = pickLogoUrl(urls, 'lockup', theme) ?? pickLogoUrl(urls, 'mark', theme)
  if (!url) return Promise.resolve(null)
  const own = surface === 'dark' ? Object.entries(urls).find(([, u]) => u === url)?.[0]?.endsWith('-inverted') === true : Object.entries(urls).find(([, u]) => u === url)?.[0]?.endsWith('-inverted') !== true
  const key = `${surface}|${url}`
  let p = cache.get(key)
  if (!p) {
    p = rasterizeLogo(url).then((r) => {
      if (!r) cache.delete(key)
      return r ? { ...r, plate: own ? null : surface === 'dark' ? ('white' as const) : ('navy' as const) } : null
    })
    cache.set(key, p)
  }
  return p
}

/** Mide el logo dentro de una caja `maxW`×`maxH` (pt) sin deformarlo. */
export function fitLogo(logo: Pick<PdfLogo, 'width' | 'height'>, maxW: number, maxH: number): { w: number; h: number } {
  const scale = Math.min(maxW / logo.width, maxH / logo.height)
  return { w: logo.width * scale, h: logo.height * scale }
}

let teikemLogo: Promise<PdfLogo | null> | null = null

/** El logo de Teikem (lockup horizontal sin lema, versión para fondo claro) rasterizado para el PDF; null si no se pudo leer. */
export function loadTeikemPdfLogo(): Promise<PdfLogo | null> {
  teikemLogo ??= rasterizeLogo(brandLockupSrc('es', 'light', false)).then((r) => {
    if (!r) teikemLogo = null
    return r ? { ...r, plate: null } : null
  })
  return teikemLogo
}

/** Logo del encabezado de una tabla en hoja BLANCA (2026-10-23, pedido de Luis): el de la compañía y, si no subió ninguno, el de Teikem. */
export async function loadTableHeadingLogo(): Promise<PdfLogo | null> {
  return (hasCompanyLogo() ? await loadCompanyPdfLogo('light') : null) ?? (await loadTeikemPdfLogo())
}
