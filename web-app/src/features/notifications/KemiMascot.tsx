// La mascota de Teikem (Kemi), del mismo dibujo de la maqueta (Diseño/teikem-mockups.html, `teikoMascot`). Dormida: ojos cerrados y una «z».
// `blinking` parpadea y se mueve un poco (se apaga solo con «reducir movimiento» del sistema, ver notifications.css).
import { useId } from 'react'

export function KemiMascot({ size = 38, sleeping = false, blinking = false }: { size?: number; sleeping?: boolean; blinking?: boolean }) {
  const clip = useId()
  return (
    <svg
      className={`kemi-svg${blinking ? ' blink' : ''}${sleeping ? ' sleep' : ''}`}
      width={size}
      height={size}
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
        <g className="kemi-eyes">
          <ellipse cx="49" cy="60" rx="11" ry="13" fill="#FFFFFF" />
          <ellipse cx="77" cy="60" rx="11" ry="13" fill="#FFFFFF" />
          <circle cx="51" cy="63" r="5" fill="#0B2C66" />
          <circle cx="79" cy="63" r="5" fill="#0B2C66" />
          <circle cx="48.5" cy="58" r="1.9" fill="#FFFFFF" />
          <circle cx="76.5" cy="58" r="1.9" fill="#FFFFFF" />
        </g>
      )}
      <path d="M52 88 Q63 97 74 88" fill="none" stroke="#FF6A1A" strokeWidth="4.5" strokeLinecap="round" />
      {sleeping && (
        <text x="92" y="30" fontSize="26" fontWeight="700" fill="#FF8B4D" fontFamily="system-ui, sans-serif">
          z
        </text>
      )}
    </svg>
  )
}
