// Lote 69 (kits en órdenes de venta) — lo que se ve de un kit dentro de una orden: en el borrador, debajo de la línea, los componentes (los de la orden guardada o, si la
// línea es nueva, los del catálogo) y, si el kit es «ajustable al pedir», sus cantidades por kit se pueden cambiar solo para esta orden; en la orden solicitada, una
// tabla por kit con lo pedido, reservado, en backorder y despachado de cada componente.
// Lote 230: SUSTITUCIÓN de piezas. Con `sales.create`, las piezas salen de GET /sales-orders/kits/{id}/substitutes (también para quien no tiene inventory.view);
// cada pieza con sustitutos válidos (misma categoría y mismo precio) lleva «Cambiar», que lista SOLO esos productos (SKU, nombre, marca); al escoger, la pieza
// muestra la nueva y «en lugar de {SKU original}» con «Deshacer». Las piezas «no sustituibles» o sin sustitutos no llevan el botón. El precio de la línea no
// cambia. La línea manda todas sus piezas (la sustituta con `substitutesProductPublicId`). En la orden guardada se ve la pieza real con «en lugar de …».
import { useId, useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { useKit } from '../warehouse/api'
import { originalOf, substituteLabel, substituteOptions, substitutePiece, undoSubstitution, type KitSubstitutesDto } from './kitSubstitution'
import { useKitSubstitutes, type SalesOrderLineDto } from './salesApi'
import type { KitPiece, LineDraft } from './salesRules'
import './sales.css'

type LineComponent = NonNullable<SalesOrderLineDto['components']>[number]

/** Piezas de la orden guardada (con su sustitución, si la hay). */
function piecesOfSaved(saved: SalesOrderLineDto): KitPiece[] {
  return (saved.components ?? []).map((c) => ({
    productPublicId: c.productPublicId ?? '',
    sku: c.sku ?? '',
    name: c.name ?? '',
    quantity: String(c.qtyPerKit ?? 1),
    ...(c.substitutesProductPublicId ? { substitutesProductPublicId: c.substitutesProductPublicId, substitutesSku: c.substitutesSku ?? '', substitutesName: c.substitutesName ?? '' } : {}),
  }))
}

export function KitLineBlock({ line, saved, onPieces }: { line: LineDraft; saved: SalesOrderLineDto | undefined; onPieces: (pieces: KitPiece[] | null) => void }) {
  const t = useT()
  const canInventory = useCan('inventory.view')
  const canCreate = useCan('sales.create')
  const subs = useKitSubstitutes(line.productPublicId, canCreate)
  // sin sales.create (solo lectura del borrador) se cae al kit del almacén, como antes
  const kit = useKit(line.productPublicId, { enabled: canInventory && !canCreate })
  const adjustable = (subs.data?.adjustable ?? kit.data?.adjustable) === true

  // lo que se muestra: las piezas que el vendedor ya cambió, o las de la orden guardada, o las del catálogo
  const shown: KitPiece[] = useMemo(() => {
    if (line.components) return line.components
    if (saved?.components?.length) return piecesOfSaved(saved)
    const catalog = subs.data?.components ?? kit.data?.components ?? []
    return catalog.map((c) => ({ productPublicId: c.productPublicId ?? '', sku: c.sku ?? '', name: c.name ?? '', quantity: String(c.quantity ?? 1) }))
  }, [line.components, saved, subs.data, kit.data])

  const change = (id: string, quantity: string) => onPieces(shown.map((p) => (p.productPublicId === id ? { ...p, quantity } : p)))
  const subsError = subs.isError && !['forbidden', 'module_disabled'].includes(applyProblemDetails(subs.error).code ?? '') ? applyProblemDetails(subs.error).title : null

  return (
    <div className="sales-kit" style={{ gridColumn: '1 / -1' }}>
      <p className="help">
        <Chip tone="neutral">{t('sales.kit.badge')}</Chip> {saved ? t('sales.kit.savedHint') : t('sales.kit.newHint')}
      </p>
      {shown.length > 0 && (
        <ul className="sales-kit-pieces" aria-label={t('sales.kit.components')}>
          {shown.map((p) => (
            <KitPieceRow
              key={originalOf(p)}
              piece={p}
              pieces={shown}
              subs={subs.data}
              adjustable={adjustable}
              onQuantity={(q) => change(p.productPublicId, q)}
              onPieces={onPieces}
            />
          ))}
        </ul>
      )}
      {subsError && (
        <p className="help ferr" role="alert">
          {subsError}
        </p>
      )}
      {adjustable && (
        <p className="help">
          {t('sales.kit.adjustHelp')}{' '}
          {line.components && (
            <button type="button" className="btn ghost" onClick={() => onPieces(null)}>
              {t('sales.kit.reset')}
            </button>
          )}
        </p>
      )}
    </div>
  )
}

function KitPieceRow({
  piece: p,
  pieces,
  subs,
  adjustable,
  onQuantity,
  onPieces,
}: {
  piece: KitPiece
  pieces: KitPiece[]
  subs: KitSubstitutesDto | undefined
  adjustable: boolean
  onQuantity: (quantity: string) => void
  onPieces: (pieces: KitPiece[]) => void
}) {
  const t = useT()
  const listId = useId()
  const [open, setOpen] = useState(false)
  const original = originalOf(p)
  const options = substituteOptions(subs, pieces, p)
  const substituted = Boolean(p.substitutesProductPublicId)
  const originalSku = substituted ? (p.substitutesSku ?? '') : p.sku

  return (
    <li className="sales-kit-piece">
      <div className="sales-kit-piece-main">
        <span className="ref">{p.sku}</span> <span>{p.name}</span>
        {substituted && <span className="sales-kit-instead">{t('sales.kit.insteadOf', { sku: originalSku })}</span>}
      </div>
      {adjustable ? (
        <input
          className="sales-qty"
          aria-label={`${t('sales.kit.perKit')} ${p.sku}`}
          inputMode="decimal"
          value={p.quantity}
          onChange={(e) => onQuantity(e.target.value)}
        />
      ) : (
        <b>× {p.quantity}</b>
      )}
      <div className="sales-kit-piece-acts">
        {options.length > 0 && (
          <button
            type="button"
            className="btn sm"
            aria-label={t('sales.kit.changeOf', { sku: p.sku })}
            aria-expanded={open}
            aria-controls={open ? listId : undefined}
            onClick={() => setOpen((v) => !v)}
          >
            {t('sales.kit.change')}
          </button>
        )}
        {substituted && (
          <button
            type="button"
            className="btn sm ghost"
            aria-label={t('sales.kit.undoOf', { sku: originalSku })}
            onClick={() => {
              setOpen(false)
              onPieces(undoSubstitution(pieces, original))
            }}
          >
            {t('sales.kit.undo')}
          </button>
        )}
      </div>
      {open && options.length > 0 && (
        <div className="sales-kit-subs" id={listId}>
          <p className="help">{t('sales.kit.substituteHint')}</p>
          <ul aria-label={t('sales.kit.substitutesOf', { sku: originalSku })}>
            {options.map((s) => (
              <li key={s.productPublicId}>
                <button
                  type="button"
                  className="btn sm block"
                  aria-label={t('sales.kit.chooseSubstitute', { sku: s.sku ?? '', original: originalSku })}
                  onClick={() => {
                    setOpen(false)
                    onPieces(substitutePiece(pieces, original, s))
                  }}
                >
                  {substituteLabel(s)}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </li>
  )
}

/** Orden guardada: por cada línea de kit, sus componentes con pedido / reservado / en backorder / despachado (en piezas del componente). */
export function KitComponentsSection({ lines }: { lines: readonly SalesOrderLineDto[] }) {
  const t = useT()
  const f = useFormat()
  const kits = lines.filter((l) => l.isKit && (l.components?.length ?? 0) > 0)
  if (kits.length === 0) return null
  return (
    <section aria-label={t('sales.kit.sectionTitle')} style={{ marginTop: 12 }}>
      <h3>{t('sales.kit.sectionTitle')}</h3>
      {kits.map((l) => (
        <table key={l.id} className="tbl" aria-label={`${t('sales.kit.components')} ${l.sku}`} style={{ marginBottom: 10 }}>
          <caption style={{ textAlign: 'start' }}>
            <b>{l.sku}</b> · {l.productName} <Chip tone="neutral">{t('sales.kit.badge')}</Chip>
          </caption>
          <thead>
            <tr>
              <th>{t('sales.col.product')}</th>
              <th>{t('sales.kit.perKit')}</th>
              <th>{t('sales.kit.needed')}</th>
              <th>{t('sales.kit.reserved')}</th>
              <th>{t('sales.kit.backordered')}</th>
              <th>{t('sales.kit.shipped')}</th>
            </tr>
          </thead>
          <tbody>
            {(l.components ?? []).map((c: LineComponent) => (
              <tr key={c.productPublicId}>
                <td>
                  <b>{c.sku}</b> <span className="help">{c.name}</span>
                  {/* Lote 230: la pieza real y, si se sustituyó al pedir, cuál era la del kit */}
                  {c.substitutesProductPublicId && <span className="sales-kit-instead">{t('sales.kit.insteadOf', { sku: c.substitutesSku ?? '' })}</span>}
                </td>
                <td>{f.number(c.qtyPerKit ?? 0)}</td>
                <td>{f.number(c.needed ?? 0)}</td>
                <td>{f.number(c.reserved ?? 0)}</td>
                <td>{(c.backordered ?? 0) > 0 ? <Chip tone="warn">{f.number(c.backordered ?? 0)}</Chip> : '—'}</td>
                <td>{f.number(c.shipped ?? 0)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ))}
    </section>
  )
}
