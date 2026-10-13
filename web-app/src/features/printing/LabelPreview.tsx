// Vista previa del label (Lote F-G1): pinta en HTML el MISMO plan que el PDF (`planLabel`): tres renglones con sus tres celdas de
// texto y, debajo, los códigos de barras a todo el ancho (barras Code 128 reales en SVG). Proporción de la etiqueta elegida (4×6,
// 4×4 o 4×2), siempre blanco y negro como sale en la impresora térmica; el COD en caja negra.
import { useMemo } from 'react'
import { useT } from '../../kernel/i18n'
import { CODE128_QUIET_ZONE, encodeCode128 } from '../../kernel/ui/code128'
import { planLabel, type LabelLayout, type OrderLabelData } from '../../kernel/ui/orderLabelLayout'
import { useLabelTexts } from './labelTexts'
import { mediaInches } from './stationConfig'

const TEXT_PX = { sm: 10.5, md: 12.5, lg: 15.5 } as const
const BAR_PX = { sm: 22, md: 32, lg: 44 } as const

function Bars({ value }: { value: string }) {
  const symbol = useMemo(() => encodeCode128(value), [value])
  const total = symbol.width + 2 * CODE128_QUIET_ZONE
  return (
    <svg className="lbl-bars" viewBox={`0 0 ${total} 10`} preserveAspectRatio="none" aria-hidden="true">
      {symbol.bars.map((b) => (
        <rect key={b.x} x={b.x + CODE128_QUIET_ZONE} y={0} width={b.width} height={10} fill="#000" />
      ))}
    </svg>
  )
}

export interface LabelPreviewProps {
  layout: LabelLayout
  data: OrderLabelData
  mediaSize: string
  label?: string
}

export function LabelPreview({ layout, data, mediaSize, label }: LabelPreviewProps) {
  const t = useT()
  const texts = useLabelTexts()
  const rows = useMemo(() => planLabel(layout, data, texts), [layout, data, texts])
  const { widthIn, heightIn } = mediaInches(mediaSize)
  return (
    <div className="lbl-canvas" role="img" aria-label={label ?? t('printing.builder.preview')} style={{ aspectRatio: `${widthIn} / ${heightIn}` }} data-testid="label-preview">
      {rows.map((row) => (
        <div key={row.row} className={`lbl-row lbl-row-${row.row}`}>
          <div className="lbl-cells">
            {(['l', 'c', 'r'] as const).map((c) => (
              <div key={c} className={`lbl-cell lbl-cell-${c}`}>
                {row.cells[c].map((it) => (
                  <div key={it.key} className={`lbl-text${it.inverse ? ' lbl-inverse' : ''}${it.bold ? ' lbl-bold' : ''}`} style={{ fontSize: TEXT_PX[it.size] }} data-el={it.key}>
                    {it.lines.map((l, i) => (
                      <span key={i}>{l}</span>
                    ))}
                  </div>
                ))}
              </div>
            ))}
          </div>
          {row.barcodes.map((b) => (
            <div key={b.key} className="lbl-barcode" data-el={b.key} style={{ height: BAR_PX[b.size] + 14 }}>
              {b.encodable ? <Bars value={b.value} /> : <b className="lbl-nobars">{b.value}</b>}
              <span className="lbl-caption">{b.caption}</span>
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}
