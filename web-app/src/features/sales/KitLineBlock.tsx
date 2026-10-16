// Lote 69 (kits en órdenes de venta) — lo que se ve de un kit dentro de una orden: en el borrador, debajo de la línea, los componentes (los de la orden guardada o, si la
// línea es nueva, los del catálogo) y, si el kit es «ajustable al pedir», sus cantidades por kit se pueden cambiar solo para esta orden; en la orden solicitada, una
// tabla por kit con lo pedido, reservado, en backorder y despachado de cada componente.
import { useMemo } from 'react'
import { useCan } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { useKit } from '../warehouse/api'
import type { KitPiece, LineDraft } from './salesRules'
import type { SalesOrderLineDto } from './salesApi'

type LineComponent = NonNullable<SalesOrderLineDto['components']>[number]

export function KitLineBlock({ line, saved, onPieces }: { line: LineDraft; saved: SalesOrderLineDto | undefined; onPieces: (pieces: KitPiece[] | null) => void }) {
  const t = useT()
  const canInventory = useCan('inventory.view')
  const kit = useKit(line.productPublicId, { enabled: canInventory })
  const adjustable = kit.data?.adjustable === true

  // lo que se muestra: las piezas que el vendedor ya cambió, o las de la orden guardada, o las del catálogo
  const shown: KitPiece[] = useMemo(() => {
    if (line.components) return line.components
    if (saved?.components?.length) return saved.components.map((c) => ({ productPublicId: c.productPublicId ?? '', sku: c.sku ?? '', name: c.name ?? '', quantity: String(c.qtyPerKit ?? 1) }))
    return (kit.data?.components ?? []).map((c) => ({ productPublicId: c.productPublicId ?? '', sku: c.sku ?? '', name: c.name ?? '', quantity: String(c.quantity ?? 1) }))
  }, [line.components, saved, kit.data])

  const change = (id: string, quantity: string) => onPieces(shown.map((p) => (p.productPublicId === id ? { ...p, quantity } : p)))

  return (
    <div className="sales-kit" style={{ gridColumn: '1 / -1' }}>
      <p className="help">
        <Chip tone="neutral">{t('sales.kit.badge')}</Chip> {saved ? t('sales.kit.savedHint') : t('sales.kit.newHint')}
      </p>
      {shown.length > 0 && (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }} aria-label={t('sales.kit.components')}>
          {shown.map((p) => (
            <li key={p.productPublicId} style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 4 }}>
              <span className="ref">{p.sku}</span> <span>{p.name}</span>
              {adjustable ? (
                <input
                  aria-label={`${t('sales.kit.perKit')} ${p.sku}`}
                  inputMode="decimal"
                  style={{ width: 80 }}
                  value={p.quantity}
                  onChange={(e) => change(p.productPublicId, e.target.value)}
                />
              ) : (
                <b>× {p.quantity}</b>
              )}
            </li>
          ))}
        </ul>
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
