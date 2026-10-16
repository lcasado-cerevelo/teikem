// La mascota de Teikem (Kemi), del mismo dibujo de la maqueta (Diseño/teikem-mockups.html, `teikoMascot`). Dormida: ojos cerrados y una «z».
// Es un solo SVG con todas las partes de sus gestos (ojos, pupilas, bocas); el CSS (notifications.css) enseña la que toca según `data-mood` / `data-blink`, que cambia useKemiLife.ts.
// `excited` = llegó un aviso: ojos grandes y boca abierta mientras el globo está a la vista (y Kemi brinca; se apaga con «reducir movimiento»).
import { useId, type RefObject } from 'react'

interface Props {
  size?: number
  sleeping?: boolean
  excited?: boolean
  svgRef?: RefObject<SVGSVGElement | null>
}

export function KemiMascot({ size = 38, sleeping = false, excited = false, svgRef }: Props) {
  const clip = useId()
  return (
    <svg
      ref={svgRef}
      className={`kemi-svg${excited ? ' excited' : ''}${sleeping ? ' sleep' : ''}`}
      width={size}
      height={size}
      style={{ width: size, height: size }}   // gana a la regla global svg{16px} (antes Kemi se veía diminuta)
      viewBox="0 0 124 132"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      focusable="false"
    >
      <defs>
        <clipPath id={clip}>
          <path d="M62 22 L102 44 V90 L62 112 L22 90 V44 Z" />
        </clipPath>
      </defs>
      <path d="M62 22 V10" stroke="#FF6A1A" strokeWidth="4" strokeLinecap="round" />
      <circle cx="62" cy="8" r="5.5" fill="#FF6A1A" />
      <path d="M62 22 L102 44 V90 L62 112 L22 90 V44 Z" fill="#0B2C66" />
      <g clipPath={`url(#${clip})`}>
        <rect x="22" y="22" width="40" height="90" fill="#1F6FE5" />
        <rect x="62" y="22" width="40" height="90" fill="#15347A" />
      </g>
      <path d="M62 22 L102 44 V90 L62 112 L22 90 V44 Z" fill="none" stroke="#FFFFFF" strokeWidth="2.5" />
      {sleeping ? (
        <g stroke="#FFFFFF" strokeWidth="4.5" strokeLinecap="round" fill="none">
          <path d="M39 62 Q49 70 59 62" />
          <path d="M67 62 Q77 70 87 62" />
        </g>
      ) : (
        <g className="k-eyes">
          <g className="k-eye k-eye-l">
            <ellipse cx="49" cy="60" rx="11" ry="13" fill="#FFFFFF" />
            <g className="k-pupil">
              <circle cx="51" cy="63" r="5" fill="#0B2C66" />
              <circle cx="48.5" cy="58" r="1.9" fill="#FFFFFF" />
            </g>
          </g>
          <g className="k-eye k-eye-r">
            <ellipse cx="77" cy="60" rx="11" ry="13" fill="#FFFFFF" />
            <g className="k-pupil">
              <circle cx="79" cy="63" r="5" fill="#0B2C66" />
              <circle cx="76.5" cy="58" r="1.9" fill="#FFFFFF" />
            </g>
          </g>
        </g>
      )}
      {sleeping ? (
        <path d="M52 88 Q63 97 74 88" fill="none" stroke="#FF6A1A" strokeWidth="4.5" strokeLinecap="round" />
      ) : (
        <g className="k-mouths">
          <path className="k-m k-m-smile" d="M52 88 Q63 97 74 88" fill="none" stroke="#FF6A1A" strokeWidth="4.5" strokeLinecap="round" />
          <path className="k-m k-m-happy" d="M50 86 Q63 106 76 86 Z" fill="#FF6A1A" stroke="#FF6A1A" strokeWidth="3" strokeLinejoin="round" />
          <ellipse className="k-m k-m-surprised" cx="63" cy="93" rx="5.5" ry="7" fill="#FF6A1A" />
          <path className="k-m k-m-tongue" d="M57 91 Q63 109 69 91 Z" fill="#FF8FA3" stroke="#FF8FA3" strokeWidth="2" strokeLinejoin="round" />
          <path className="k-m k-m-worried" d="M52 96 Q63 86 74 96" fill="none" stroke="#FF6A1A" strokeWidth="4.5" strokeLinecap="round" />
        </g>
      )}
      {sleeping && (
        <text x="92" y="30" fontSize="26" fontWeight="700" fill="#FF8B4D" fontFamily="system-ui, sans-serif">
          z
        </text>
      )}
    </svg>
  )
}
