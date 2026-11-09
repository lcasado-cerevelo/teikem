// Lote 229 — bloque «Foto» del editor de producto: la foto propia del producto (también kits y kits de oferta) con vista previa, y subir / cambiar / quitar
// (`/api/v1/products/{id}/image`, multipart `file`; JPG, PNG o WEBP; el servidor la reduce a 1200 px). Ver: inventory.view o sales.view; cambiar: inventory.manage.
// Sin foto propia se muestra la que se usaría (la de la primera pieza con foto si es un kit) y se dice de dónde sale. Es autocontenido: no depende del formulario
// del editor (sus botones son `type="button"`) y, con un producto nuevo, solo avisa que se pone después de guardarlo.
import { useEffect, useRef, useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Spinner, toast } from '../../kernel/ui'
import { useProductImage, useProductImageActions } from './offerApi'
import { isProductPhoto, PRODUCT_PHOTO_ACCEPT } from './offerRules'
import { StoredImage } from './StoredImage'
import './offers.css'

/** Vista previa local mientras sube (sin ella, si el navegador no puede, se espera la foto guardada). */
function localUrl(file: File): string | null {
  try {
    return URL.createObjectURL(file)
  } catch {
    return null
  }
}

export interface ProductPhotoBlockProps {
  /** Producto ya guardado; null = producto nuevo (la foto se pone después de crearlo). */
  publicId: string | null
  /** Id del título del bloque (para `aria-labelledby`); por omisión uno propio. */
  titleId?: string
}

export function ProductPhotoBlock({ publicId, titleId }: ProductPhotoBlockProps) {
  const t = useT()
  const id = titleId ?? `pp-${publicId ?? 'new'}`
  return (
    <section className="pe-block pp-block" aria-labelledby={id}>
      <h3 id={id} className="pe-block-title">
        {t('warehouse.products.photo.title')}
      </h3>
      {publicId ? <PhotoBody publicId={publicId} /> : <p className="help">{t('warehouse.products.photo.saveFirst')}</p>}
    </section>
  )
}

function PhotoBody({ publicId }: { publicId: string }) {
  const t = useT()
  const { data, isLoading, error: loadError } = useProductImage(publicId)
  const actions = useProductImageActions(publicId)
  const fileRef = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const busy = actions.upload.isPending || actions.remove.isPending

  // la vista previa local se libera al cambiarla o al cerrar
  useEffect(() => () => {
    if (preview) URL.revokeObjectURL(preview)
  }, [preview])

  if (isLoading) return <Spinner block />
  const own = data?.imagePublicId ?? null
  const shown = data?.effectiveImagePublicId ?? own
  const fromPiece = !own && !!shown

  const upload = async (file: File) => {
    setError(null)
    if (!isProductPhoto(file)) return setError(t('warehouse.products.photo.badType'))
    setPreview(localUrl(file))
    try {
      await actions.upload.mutateAsync(file)
      toast.success(t('warehouse.products.photo.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    } finally {
      setPreview(null)
    }
  }
  const remove = async () => {
    setError(null)
    try {
      await actions.remove.mutateAsync()
      toast.success(t('warehouse.products.photo.removed'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <div className="pp-row">
      {preview ? (
        <img src={preview} alt={t('warehouse.products.photo.preview')} className="pp-img" />
      ) : (
        <StoredImage publicId={shown} alt={t('warehouse.products.photo.alt', { sku: data?.sku ?? '' })} className="pp-img" emptyClassName="pp-empty" emptyText={t('warehouse.products.photo.none')} />
      )}
      <div className="pp-side">
        {fromPiece && <p className="help">{t('warehouse.products.photo.fromPiece')}</p>}
        {loadError && !data && (
          <p className="ferr" role="alert">
            {applyProblemDetails(loadError).title}
          </p>
        )}
        <Can perm="inventory.manage">
          <p className="help">{t('warehouse.products.photo.help')}</p>
          <input
            ref={fileRef}
            type="file"
            hidden
            accept={PRODUCT_PHOTO_ACCEPT}
            aria-label={t('warehouse.products.photo.file')}
            onChange={(e) => {
              const file = e.target.files?.[0]
              e.target.value = ''
              if (file) void upload(file)
            }}
          />
          <div className="pp-actions">
            <button type="button" className="btn" disabled={busy} onClick={() => fileRef.current?.click()}>
              {busy && actions.upload.isPending ? t('common.loading') : own ? t('warehouse.products.photo.change') : t('warehouse.products.photo.add')}
            </button>
            {own && (
              <button type="button" className="btn danger" disabled={busy} onClick={() => void remove()}>
                {t('warehouse.products.photo.remove')}
              </button>
            )}
          </div>
        </Can>
        {error && (
          <p className="ferr" role="alert">
            {error}
          </p>
        )}
      </div>
    </div>
  )
}
