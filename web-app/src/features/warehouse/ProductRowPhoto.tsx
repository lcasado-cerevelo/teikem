// Lote 240 — la miniatura del producto en la lista de «Productos e inventario»: del alto de la fila (no una foto grande) y, con un clic, se amplía ahí mismo (ZoomPhoto).
// Sin foto, un recuadro vacío del mismo tamaño para que las filas no cambien de alto. (Solo se pinta si la compañía maneja fotos: la columna entera depende de eso.)
import { ZoomPhoto } from '../../kernel/ui'
import { useStoredFileUrl } from '../orders/api'

export function ProductRowPhoto({ publicId, name }: { publicId: string | null | undefined; name: string }) {
  const url = useStoredFileUrl(publicId).data
  if (publicId && url) return <ZoomPhoto src={url} alt={name} className="pl-rowphoto" />
  return <span className="pl-rowphoto pl-rowphoto-none" aria-hidden="true" />
}
