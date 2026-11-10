// Lote 235 — lo que el renglón de un producto dice del inventario al venderlo (pedido guiado y «Nueva orden»): «Disponible: N» en el almacén de la orden; si se
// pide de más, el distintivo «Backorder: N» y «Solo hay X disponibles en {almacén}; N quedan en backorder.» en el tono de falla del tema (no bloquea: se puede
// guardar y enviar); y, si otros almacenes activos tienen, la nota «Hay disponible en {almacén}: N». Sin datos (todavía cargando o sin acceso) no pinta nada.
// Lote 236: un KIT que se arma al pedir (el kit de oferta) dice «Se pueden armar: N» y debajo su desglose por pieza («52800 Tirillas × 6 — 14,022 disp.»), con la
// pieza que limita marcada («Limita»); con más de 3 piezas el desglose va plegado («Ver piezas (N) · limita 52800»). `pieces` = las piezas de la orden si se
// cambiaron (sustitución): el desglose usa la pieza real. Un kit armado no se desglosa.
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { KIT_PIECES_UNFOLDED, lineStock, shortHeadKey, warehouseLabel, type KitPieceInput, type KitStock, type StockInput } from './stockRules'
import './sales.css'

export interface StockInfoProps {
  stock: StockInput | null | undefined
  /** El almacén de la orden. */
  warehousePublicId: string | null | undefined
  productPublicId: string | null | undefined
  /** Texto de la casilla de cantidad (vacío = todavía no se pide). */
  quantity: string | number
  /** Solo el aviso de backorder (resumen del paso de entrega): sin «Disponible» ni la nota de otros almacenes. */
  onlyBackorder?: boolean
  /** Lote 236: las piezas del kit en la orden (cambiadas en pantalla o guardadas); sin ellas, las del catálogo. */
  pieces?: readonly KitPieceInput[] | null
}

/** El desglose del disponible de un kit por pieza: lo que lleva cada kit y lo que hay de cada una; la que limita va marcada. */
function KitPieces({ kit }: { kit: KitStock }) {
  const t = useT()
  const f = useFormat()
  if (kit.pieces.length === 0) return null
  const list = (
    <ul className="sales-kitstock" aria-label={t('sales.stock.kitPieces')}>
      {kit.pieces.map((p, i) => (
        <li key={`${p.productPublicId}-${i}`} className={p.limiting ? 'limit' : undefined}>
          <span className="sales-kitstock-what">
            <b className="mono">{p.sku}</b> {p.name} × {f.number(p.qtyPerKit)}
          </span>
          <span className="sales-kitstock-n">
            {t('sales.stock.pieceAvailable', { n: f.number(p.available) })}
            {p.limiting && (
              <>
                {' '}
                <Chip tone="warn">{t('sales.stock.kitLimit')}</Chip>
              </>
            )}
          </span>
        </li>
      ))}
    </ul>
  )
  if (kit.pieces.length <= KIT_PIECES_UNFOLDED) return list
  const limiting = kit.pieces.filter((p) => p.limiting).map((p) => p.sku).join(', ')
  return (
    <details className="sales-kitstock-more">
      <summary>
        {t('sales.stock.kitShow', { n: kit.pieces.length })}
        {limiting && ` · ${t('sales.stock.kitLimitedBy', { sku: limiting })}`}
      </summary>
      {list}
    </details>
  )
}

export function StockInfo({ stock, warehousePublicId, productPublicId, quantity, onlyBackorder = false, pieces }: StockInfoProps) {
  const t = useT()
  const f = useFormat()
  const info = lineStock(stock, productPublicId, warehousePublicId, quantity, pieces)
  if (!info) return null
  if (onlyBackorder && info.backorder <= 0) return null
  const warehouse = warehouseLabel(stock, warehousePublicId)
  return (
    <div className={`sales-stock${info.backorder > 0 ? ' short' : ''}`}>
      {!onlyBackorder && <span className="sales-stock-avail">{t(info.kit ? 'sales.stock.kitAvailable' : 'sales.stock.available', { n: f.number(info.available) })}</span>}
      {!onlyBackorder && info.kit && <KitPieces kit={info.kit} />}
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
