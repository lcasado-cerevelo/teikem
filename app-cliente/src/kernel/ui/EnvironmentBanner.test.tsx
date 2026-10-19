import { render, screen } from '@testing-library/react-native'

import { EnvironmentBanner, EnvironmentFooter } from './EnvironmentBanner'

let mockVariant: string | undefined
jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { get expoConfig() { return { extra: { appVariant: mockVariant } } } },
}))

describe('EnvironmentBanner (portal del cliente)', () => {
  it('en la variante test muestra la cinta TEST arriba y la etiqueta al pie', async () => {
    mockVariant = 'test'
    await render(
      <>
        <EnvironmentBanner />
        <EnvironmentFooter />
      </>,
    )
    expect(screen.getByText('TEST · AMBIENTE DE PRUEBAS')).toBeTruthy()
    expect(screen.getByTestId('environment-footer')).toHaveTextContent(/^TEST · /)
  })

  it('en la variante staging dice STAGING', async () => {
    mockVariant = 'staging'
    await render(<EnvironmentBanner />)
    expect(screen.getByText('STAGING · AMBIENTE DE PRUEBAS')).toBeTruthy()
  })

  it('en producción y en desarrollo no muestra nada', async () => {
    for (const v of ['production', 'development', undefined]) {
      mockVariant = v
      const { toJSON, unmount } = await render(
        <>
          <EnvironmentBanner />
          <EnvironmentFooter />
        </>,
      )
      expect(toJSON()).toBeNull()
      await unmount()
    }
  })
})
