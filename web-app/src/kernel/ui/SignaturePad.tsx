// Lote 223 — cuadro para dibujar una firma con el dedo, el lápiz o el ratón (Pointer Events: un solo control para los tres). Controlado: `value` son los trazos
// en el lienzo lógico de `signature.ts` y `onChange` recibe los trazos al terminar cada uno (no en cada movimiento). En pantalla se pinta con los colores del tema
// (tinta `--text` sobre `--panel-2`); al subirla, `signatureToPng` la exporta en negro sobre blanco. «Borrar firma» la deja vacía.
import { useId, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { useT } from '../i18n/useT'
import { hasInk, SIGNATURE_HEIGHT, SIGNATURE_WIDTH, strokePath, toSignaturePoint, type SignatureStroke } from './signature'
import './signaturePad.css'

export interface SignaturePadProps {
  /** Trazos de la firma (vacío = sin firma). */
  value: readonly SignatureStroke[]
  onChange: (strokes: SignatureStroke[]) => void
  /** Nombre del cuadro (ya traducido): «Firma de quien autoriza». */
  label: string
  /** Texto del botón para borrar (por omisión «Borrar firma»). */
  clearLabel?: string
  required?: boolean
  disabled?: boolean
  invalid?: boolean
  /** id de la ayuda o del error que describe el cuadro. */
  describedBy?: string
  id?: string
}

export function SignaturePad({ value, onChange, label, clearLabel, required, disabled, invalid, describedBy, id }: SignaturePadProps) {
  const t = useT()
  const autoId = useId()
  const baseId = id ?? autoId
  const svgRef = useRef<SVGSVGElement>(null)
  const [current, setCurrent] = useState<SignatureStroke | null>(null)
  const drawing = useRef<SignatureStroke | null>(null)
  const signed = hasInk(value)

  const point = (e: ReactPointerEvent) => {
    const r = svgRef.current?.getBoundingClientRect() ?? { left: 0, top: 0, width: 0, height: 0 }
    return toSignaturePoint(e.clientX, e.clientY, r)
  }
  const start = (e: ReactPointerEvent<SVGSVGElement>) => {
    if (disabled || (e.pointerType === 'mouse' && e.button !== 0)) return
    e.preventDefault()
    try {
      e.currentTarget.setPointerCapture?.(e.pointerId)
    } catch {
      /* sin captura (jsdom): igual se dibuja */
    }
    drawing.current = [point(e)]
    setCurrent(drawing.current)
  }
  const move = (e: ReactPointerEvent<SVGSVGElement>) => {
    if (!drawing.current) return
    const p = point(e)
    const last = drawing.current[drawing.current.length - 1]
    if (last && last.x === p.x && last.y === p.y) return
    drawing.current = [...drawing.current, p]
    setCurrent(drawing.current)
  }
  const end = () => {
    const stroke = drawing.current
    drawing.current = null
    setCurrent(null)
    if (stroke && stroke.length > 0) onChange([...value, stroke])
  }

  const statusId = `${baseId}-status`
  return (
    <div className={`sigpad${invalid ? ' invalid' : ''}${disabled ? ' disabled' : ''}`} role="group" aria-labelledby={`${baseId}-label`} aria-describedby={[statusId, describedBy].filter(Boolean).join(' ')}>
      <div className="sigpad-head">
        <span id={`${baseId}-label`} className="sigpad-label">
          {label}
          {required && (
            <span className="req" aria-hidden="true">
              {' '}*
            </span>
          )}
        </span>
        <button type="button" className="btn sm" onClick={() => onChange([])} disabled={disabled || value.length === 0}>
          {clearLabel ?? t('ui.signature.clear')}
        </button>
      </div>
      <svg
        ref={svgRef}
        id={baseId}
        className="sigpad-box"
        viewBox={`0 0 ${SIGNATURE_WIDTH} ${SIGNATURE_HEIGHT}`}
        role="img"
        aria-label={signed ? t('ui.signature.signedAria', { label }) : t('ui.signature.emptyAria', { label })}
        aria-invalid={invalid || undefined}
        onPointerDown={start}
        onPointerMove={move}
        onPointerUp={end}
        onPointerCancel={end}
        onPointerLeave={(e) => {
          if (e.pointerType === 'mouse') end()
        }}
      >
        <line className="sigpad-baseline" x1={30} x2={SIGNATURE_WIDTH - 30} y1={SIGNATURE_HEIGHT - 50} y2={SIGNATURE_HEIGHT - 50} />
        {value.map((s, i) => (
          <path key={i} className="sigpad-ink" d={strokePath(s)} />
        ))}
        {current && <path className="sigpad-ink" d={strokePath(current)} />}
      </svg>
      <p id={statusId} className="help" aria-live="polite">
        {signed ? t('ui.signature.signed') : t('ui.signature.hint')}
      </p>
    </div>
  )
}
