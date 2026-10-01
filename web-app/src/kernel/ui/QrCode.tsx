import qrcode from 'qrcode-generator'
import { useMemo } from 'react'

/** Código QR generado en el navegador (el contenido nunca sale del equipo). Fondo blanco fijo para que la cámara lo lea
 *  también en tema oscuro. Uso: el enlace otpauth:// de la verificación en dos pasos. */
export function QrCode({ value, label, size = 184 }: { value: string; label: string; size?: number }) {
  const src = useMemo(() => {
    const qr = qrcode(0, 'M')
    qr.addData(value)
    qr.make()
    return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(qr.createSvgTag({ cellSize: 4, margin: 4 }))}`
  }, [value])
  return <img className="qr" src={src} width={size} height={size} alt={label} data-testid="qr-code" />
}
