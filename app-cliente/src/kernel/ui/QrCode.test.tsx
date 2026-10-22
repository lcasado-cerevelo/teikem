// El QR de la verificación en dos pasos: la matriz es cuadrada, trae los tres cuadros de ubicación de las esquinas y el componente la dibuja con su margen blanco.
import { render, screen } from '@testing-library/react-native'

import { QrCode, qrMatrix } from './QrCode'

const URI = 'otpauth://totp/Teikem:ana%40cliente.co?secret=JBSWY3DPEHPK3PXP&issuer=Teikem'

describe('QrCode', () => {
  it('la matriz es cuadrada y trae los cuadros de ubicación de tres esquinas', () => {
    const m = qrMatrix(URI)
    const n = m.length
    expect(n).toBeGreaterThanOrEqual(21)
    expect(m.every((row) => row.length === n)).toBe(true)
    // esquinas superior izquierda, superior derecha e inferior izquierda: el cuadro de 7×7 empieza oscuro en su esquina exterior
    expect(m[0][0] && m[0][n - 1] && m[n - 1][0]).toBe(true)
  })

  it('el mismo texto da siempre el mismo código', () => {
    expect(qrMatrix(URI)).toEqual(qrMatrix(URI))
    expect(qrMatrix(URI)).not.toEqual(qrMatrix(URI + 'x'))
  })

  it('se dibuja con su etiqueta de accesibilidad', async () => {
    await render(<QrCode value={URI} label="Código QR para su app de autenticación" />)
    expect(screen.getByLabelText('Código QR para su app de autenticación')).toBeTruthy()
    expect(screen.getByTestId('qr-code')).toBeTruthy()
  })
})
