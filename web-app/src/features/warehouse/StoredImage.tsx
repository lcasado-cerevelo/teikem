// Lote 221 (P1) — foto de un archivo del almacén (oferta o renglón de lista) pedida con la sesión; sin foto o sin permiso, el recuadro vacío.
// Lote 240: la foto se amplía con un clic (ZoomPhoto) y, si la compañía no maneja fotos de productos, no se pinta NADA (ni el recuadro vacío).
import { useTenantSettings } from '../../kernel/catalogs'
import { ZoomPhoto } from '../../kernel/ui'
import { useStoredFileUrl } from '../orders/api'

/** ¿La compañía maneja fotos de productos? (Ajustes → Operación → Fotos; sí por omisión, también mientras cargan los ajustes de otra forma no se esconde nada.) */
export function useProductPhotosEnabled(): boolean {
  return useTenantSettings().data?.productPhotosEnabled !== false
}

export function StoredImage({ publicId, alt, className, emptyClassName = 'pl-thumb-empty', emptyText }: { publicId: string | null | undefined; alt: string; className: string; emptyClassName?: string; emptyText?: string }) {
  const enabled = useProductPhotosEnabled()
  const url = useStoredFileUrl(enabled ? publicId : null).data
  if (!enabled) return null
  if (publicId && url) return <ZoomPhoto src={url} alt={alt} className={className} />
  return (
    <span className={emptyClassName} aria-hidden={emptyText ? undefined : true}>
      {emptyText}
    </span>
  )
}
