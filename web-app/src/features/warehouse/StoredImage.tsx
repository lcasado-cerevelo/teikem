// Lote 221 (P1) — foto de un archivo del almacén (oferta o renglón de lista) pedida con la sesión; sin foto o sin permiso, el recuadro vacío.
import { useStoredFileUrl } from '../orders/api'

export function StoredImage({ publicId, alt, className, emptyClassName = 'pl-thumb-empty', emptyText }: { publicId: string | null | undefined; alt: string; className: string; emptyClassName?: string; emptyText?: string }) {
  const url = useStoredFileUrl(publicId).data
  if (publicId && url) return <img src={url} alt={alt} className={className} />
  return (
    <span className={emptyClassName} aria-hidden={emptyText ? undefined : true}>
      {emptyText}
    </span>
  )
}
