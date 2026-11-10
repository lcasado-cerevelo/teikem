// Lote 235 — lo que el renglón de un producto dice del inventario al venderlo (pedido guiado y «Nueva orden»): «Disponible: N» en el almacén de la orden; si se
// pide de más, el distintivo «Backorder: N» y «Solo hay X disponibles en {almacén}; N quedan en backorder.» en el tono de falla del tema (no bloquea: se puede
// guardar y enviar); y, si otros almacenes activos tienen, la nota «Hay disponible en {almacén}: N». Sin datos (todavía cargando o sin acceso) no pinta nada.
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { lineStock, shortHeadKey, warehouseLabel, type StockInput } from './stockRules'

export interface StockInfoProps {
  stock: StockInput | null | undefined
  /** El almacén de la orden. */
  warehousePublicId: string | null | undefined
  productPublicId: string | null | undefined
  /** Texto de la casilla de cantidad (vacío = todavía no se pide). */
  quantity: string | number
  /** Solo el aviso de backorder (resumen del paso de entrega): sin «Disponible» ni la nota de otros almacenes. */
  onlyBackorder?: boolean
}

export function StockInfo({ stock, warehousePublicId, productPublicId, quantity, onlyBackorder = false }: StockInfoProps) {
  const t = useT()
  const f = useFormat()
  const info = lineStock(stock, productPublicId, warehousePublicId, quantity)
  if (!info) return null
  if (onlyBackorder && info.backorder <= 0) return null
  const warehouse = warehouseLabel(stock, warehousePublicId)
  return (
    <div className={`sales-stock${info.backorder > 0 ? ' short' : ''}`}>
      {!onlyBackorder && <span className="sales-stock-avail">{t('sales.stock.available', { n: f.number(info.available) })}</span>}
      {info.backorder > 0 && (
        <span className="sales-stock-short" role="status">
          <Chip tone="fail">{t('sales.stock.backorderChip', { n: f.number(info.backorder) })}</Chip>{' '}
          {t(`sales.stock.${shortHeadKey(info.available)}`, { available: f.number(info.available), warehouse })}
          {'; '}
          {t(info.backorder === 1 ? 'sales.stock.backOne' : 'sales.stock.backMany', { n: f.number(info.backorder) })}
        </span>
      )}
      {!onlyBackorder && info.elsewhere.length === 1 && (
        <span className="sales-stock-else">{t('sales.stock.elsewhereOne', { warehouse: info.elsewhere[0].name, n: f.number(info.elsewhere[0].available) })}</span>
      )}
      {!onlyBackorder && info.elsewhere.length > 1 && (
        <span className="sales-stock-else">{t('sales.stock.elsewhereMany', { list: info.elsewhere.map((e) => `${e.name} ${f.number(e.available)}`).join(' · ') })}</span>
      )}
    </div>
  )
}
