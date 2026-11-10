// Lote 231 — editor de las PIEZAS de un kit, reutilizable (kit de oferta en Precios y ofertas; lo puede usar el kit armado de Productos): una pieza por renglón
// con su producto (buscador), cantidad por kit, casilla «No sustituible» (desmarcada = al pedir se puede cambiar por otra de la misma categoría y el mismo
// precio) y quitar; «Agregar pieza» al final. `readOnly` (o sin `onChange`) = la composición se muestra como lista y no se cambia (kit de oferta ya usado).
// Lógica pura en `kitPieces.ts`; textos en `warehouse.products.kit.*`. A 360 px cada renglón se apila (producto arriba; cantidad, casilla y quitar abajo).
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { newKitPiece, pieceQuantity, type KitPiece } from './kitPieces'
import { ProductPicker } from './pickers'
import './kitPieces.css'

const K = 'warehouse.products.kit'
/** Llave de los renglones nuevos: única aunque el editor se vuelva a montar con las mismas piezas. */
let nextPiece = 0

export interface KitPiecesEditorProps {
  pieces: readonly KitPiece[]
  /** Sin `onChange` el editor es de solo lectura. */
  onChange?: (pieces: KitPiece[]) => void
  readOnly?: boolean
  /** Prefijo de los id de los controles (único en la pantalla). */
  idPrefix: string
  /** Solo productos propios en el buscador (kit de oferta). */
  ownOnly?: boolean
  /** Solo productos de ese dueño en el buscador. */
  ownerClientPublicId?: string | null
  /** Título del grupo (por omisión «Piezas del kit»). */
  legend?: string
  /** Muestra la ayuda corta de «No sustituible» arriba (por omisión sí). */
  showHelp?: boolean
  /** Lote 232: false = sin la casilla «No sustituible» ni su ayuda (kit armado: sus piezas ya están dentro, no hay sustitución). Por omisión true. */
  substitutable?: boolean
}

export function KitPiecesEditor({ pieces, onChange, readOnly, idPrefix, ownOnly, ownerClientPublicId, legend, showHelp: showHelpProp = true, substitutable = true }: KitPiecesEditorProps) {
  const t = useT()
  const f = useFormat()
  const title = legend ?? t(`${K}.pieces`)
  const showHelp = showHelpProp && substitutable

  if (readOnly || !onChange) {
    const shown = pieces.filter((p) => p.productPublicId)
    return (
      <section className="kp-ro" aria-label={title}>
        <b className="kp-title">{title}</b>
        {shown.length === 0 ? (
          <p className="help">{t(`${K}.noPieces`)}</p>
        ) : (
          <ul>
            {shown.map((p) => (
              <li key={p.key}>
                <span className="ref">{p.sku}</span> <span className="kp-name">{p.name}</span> <b>× {f.number(pieceQuantity(p.quantity) ?? 0)}</b>
                {p.notSubstitutable && (
                  <>
                    {' '}
                    <Chip tone="neutral">{t(`${K}.notSubstitutable`)}</Chip>
                  </>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    )
  }

  const set = (key: string, patch: Partial<KitPiece>) => onChange(pieces.map((p) => (p.key === key ? { ...p, ...patch } : p)))
  return (
    <fieldset className="pe-fields kp-edit">
      <legend>
        <b>{title}</b>
      </legend>
      {showHelp && (
        <p className="help" id={`${idPrefix}-subst`}>
          {t(`${K}.substituteHelp`)}
        </p>
      )}
      {pieces.map((p, i) => (
        <div className="kp-row" key={p.key}>
          <div className="f">
            <label htmlFor={`${idPrefix}-${p.key}-p`}>{t(`${K}.piece`, { n: i + 1 })}</label>
            <ProductPicker
              id={`${idPrefix}-${p.key}-p`}
              ownOnly={ownOnly}
              ownerClientPublicId={ownerClientPublicId ?? undefined}
              value={p.productPublicId}
              onChange={(id, prod) => set(p.key, { productPublicId: id, sku: prod?.sku ?? '', name: prod?.name ?? '', salePrice: prod?.salePrice ?? null })}
            />
          </div>
          <div className="f">
            <label htmlFor={`${idPrefix}-${p.key}-q`}>{t(`${K}.quantity`)}</label>
            <input id={`${idPrefix}-${p.key}-q`} inputMode="decimal" value={p.quantity} onChange={(e) => set(p.key, { quantity: e.target.value })} />
          </div>
          {substitutable && (
            <label className="kp-ns">
              <input
                type="checkbox"
                checked={p.notSubstitutable}
                aria-label={t(`${K}.notSubstitutableOf`, { n: i + 1 })}
                aria-describedby={showHelp ? `${idPrefix}-subst` : undefined}
                onChange={(e) => set(p.key, { notSubstitutable: e.target.checked })}
              />
              <span aria-hidden="true">{t(`${K}.notSubstitutable`)}</span>
            </label>
          )}
          <button
            type="button"
            className="btn"
            aria-label={t(`${K}.removePiece`, { n: i + 1 })}
            disabled={pieces.length === 1}
            onClick={() => onChange(pieces.filter((x) => x.key !== p.key))}
          >
            ×
          </button>
        </div>
      ))}
      <p>
        <button type="button" className="btn" onClick={() => onChange([...pieces, newKitPiece(`kp-new-${++nextPiece}`)])}>
          {t(`${K}.addPiece`)}
        </button>
      </p>
    </fieldset>
  )
}
