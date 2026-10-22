// Código QR generado en el aparato o el navegador (el contenido nunca sale de él). Se dibuja con vistas simples, así que sirve igual en la web y en el celular, sin librerías nativas.
// Fondo blanco fijo (con su margen) para que la cámara lo lea también en tema oscuro. Uso: el enlace otpauth:// de la verificación en dos pasos.
import qrcode from 'qrcode-generator'
import { useMemo } from 'react'
import { View } from 'react-native'

const MARGIN = 4   // módulos de margen blanco alrededor, como pide el estándar

/** Matriz de módulos del QR (true = oscuro). Aparte del componente para poder probarla. */
export function qrMatrix(value: string): boolean[][] {
  const qr = qrcode(0, 'M')
  qr.addData(value)
  qr.make()
  const n = qr.getModuleCount()
  return Array.from({ length: n }, (_, r) => Array.from({ length: n }, (_, c) => qr.isDark(r, c)))
}

export function QrCode({ value, label, size = 200 }: { value: string; label: string; size?: number }) {
  const matrix = useMemo(() => qrMatrix(value), [value])
  const total = matrix.length + MARGIN * 2
  const cell = Math.max(1, Math.floor(size / total))
  const side = cell * total
  return (
    <View accessible accessibilityLabel={label} testID="qr-code" style={{ width: side, height: side, backgroundColor: '#FFFFFF', padding: cell * MARGIN, alignSelf: 'center' }}>
      {matrix.map((row, r) => (
        <View key={r} style={{ flexDirection: 'row' }}>
          {row.map((dark, c) => (
            <View key={c} style={{ width: cell, height: cell, backgroundColor: dark ? '#000000' : '#FFFFFF' }} />
          ))}
        </View>
      ))}
    </View>
  )
}
