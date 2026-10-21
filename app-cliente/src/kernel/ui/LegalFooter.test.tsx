// Pie legal del portal: enlaces a privacidad y términos (abren la lectura en la parte pedida), derechos reservados y Powered by Teikem, en los dos idiomas.
const mockRouter = { push: jest.fn(), replace: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))

import { fireEvent, render, screen } from '@testing-library/react-native'

import { setLang } from '../i18n/i18n'
import { LegalFooter } from './LegalFooter'

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('LegalFooter', () => {
  it('muestra los derechos reservados del dueño y Powered by Teikem', async () => {
    await render(<LegalFooter year={2026} />)
    expect(screen.getByText('© 2026 Cerevelo LLC. Todos los derechos reservados.')).toBeTruthy()
    expect(screen.getByText('Powered by Teikem')).toBeTruthy()
    expect(screen.getByText('Privacidad')).toBeTruthy()
    expect(screen.getByText('Términos de uso')).toBeTruthy()
  })

  it('cada enlace abre el documento para leer en su parte', async () => {
    await render(<LegalFooter year={2026} />)
    await fireEvent.press(screen.getByTestId('legal-footer-privacy'))
    expect(mockRouter.push).toHaveBeenLastCalledWith({ pathname: '/legal-view', params: { part: 'privacy' } })
    await fireEvent.press(screen.getByTestId('legal-footer-terms'))
    expect(mockRouter.push).toHaveBeenLastCalledWith({ pathname: '/legal-view', params: { part: 'terms' } })
  })

  it('en inglés', async () => {
    setLang('en')
    await render(<LegalFooter year={2026} />)
    expect(await screen.findByText('© 2026 Cerevelo LLC. All rights reserved.')).toBeTruthy()
    expect(await screen.findByText('Privacy')).toBeTruthy()
    expect(await screen.findByText('Terms of use')).toBeTruthy()
  })
})
