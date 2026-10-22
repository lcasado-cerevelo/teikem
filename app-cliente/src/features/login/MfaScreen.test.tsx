// Inscripción del segundo paso en el portal: QR + clave, y los códigos de recuperación en una pantalla que ya trae el código de la app (como en la web del equipo): sin guardarlos no se puede terminar.
jest.mock('expo-clipboard', () => ({ setStringAsync: jest.fn().mockResolvedValue(true) }))
const mockRouter = { push: jest.fn(), replace: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../../kernel/auth/portalAuth', () => ({
  getChallenge: () => ({ token: 't', enrollmentRequired: true }),
  cancelMfa: jest.fn(),
  enrollMfa: jest.fn(),
  confirmMfa: jest.fn(),
  verifyMfa: jest.fn(),
}))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { confirmMfa, enrollMfa, verifyMfa } from '../../kernel/auth/portalAuth'
import { setLang } from '../../kernel/i18n/i18n'
import { MfaScreen } from './MfaScreen'

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  ;(enrollMfa as jest.Mock).mockResolvedValue({ secret: 'JBSWY3DPEHPK3PXP', otpAuthUri: 'otpauth://totp/Teikem:ana?secret=JBSWY3DPEHPK3PXP&issuer=Teikem' })
  ;(confirmMfa as jest.Mock).mockResolvedValue(['aaaa-1111', 'bbbb-2222'])
  ;(verifyMfa as jest.Mock).mockResolvedValue({ status: 'ok' })
})

async function toRecovery() {
  await render(<MfaScreen />)
  await fireEvent.press(screen.getByRole('button', { name: 'Empezar' }))
  expect(await screen.findByTestId('qr-code')).toBeTruthy()   // el código QR para escanear
  expect(screen.getByText('JBSWY3DPEHPK3PXP')).toBeTruthy()   // y la clave escrita por si no se puede escanear
  await fireEvent.changeText(screen.getByTestId('mfa-code'), '123456')
  await fireEvent.press(screen.getByRole('button', { name: 'Confirmar' }))
  expect(await screen.findByText('aaaa-1111')).toBeTruthy()
}

describe('MfaScreen — inscripción', () => {
  it('los códigos de recuperación traen en la misma pantalla el código de la app, y no se termina sin marcar «Ya guardé»', async () => {
    await toRecovery()
    expect(screen.getByRole('button', { name: 'Copiar los códigos' })).toBeTruthy()
    await fireEvent.changeText(screen.getByTestId('mfa-code'), '654321')
    const verify = screen.getByRole('button', { name: 'Verificar' })
    expect(verify.props.accessibilityState.disabled).toBe(true)   // falta marcar que ya los guardó
    await fireEvent.press(screen.getByRole('checkbox', { name: 'Ya guardé estos códigos en un lugar seguro' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Verificar' }).props.accessibilityState.disabled).toBe(false))
    await fireEvent.press(screen.getByRole('button', { name: 'Verificar' }))
    await waitFor(() => expect(verifyMfa).toHaveBeenCalledWith('654321'))
    expect(mockRouter.replace).toHaveBeenCalledWith('/home')
  })
})
