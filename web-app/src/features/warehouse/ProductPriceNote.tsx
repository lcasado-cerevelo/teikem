// Aviso informativo (no bloquea) cuando el precio escrito en la línea difiere del costo que guarda el producto, con la casilla
// «Actualizar el precio del producto con este» para quien puede administrar inventario (`inventory.manage`). Mismo criterio del servidor (ProductPriceRules).
import { useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'

/** El precio escrito difiere del guardado (ambos con valor mayor que cero). */
export const priceDiffers = (entered: number | null | undefined, stored: number | null | undefined) =>
  entered != null && entered > 0 && stored != null && stored > 0 && entered !== stored

/** Costo con el que se precarga el precio de una línea: el del producto, si lo tiene. */
export const preloadCost = (stored: number | null | undefined) => (stored != null && stored > 0 ? stored : null)

export function ProductPriceNote({ entered, stored, checked, onToggle }: { entered: number | null | undefined; stored: number | null | undefined; checked: boolean; onToggle: (v: boolean) => void }) {
  const t = useT()
  const canUpdate = useCan('inventory.manage')
  if (!priceDiffers(entered, stored)) return null
  return (
    <p className="help" role="note">
      <span aria-hidden="true">ⓘ </span>
      {t('warehouse.asns.lines.priceDiffers', { stored: String(stored) })}
      {canUpdate && (
        <label style={{ display: 'inline-flex', gap: 6, marginLeft: 10, alignItems: 'center' }}>
          <input type="checkbox" checked={checked} onChange={(e) => onToggle(e.target.checked)} />
          {t('warehouse.asns.lines.updateProductPrice')}
        </label>
      )}
    </p>
  )
}
