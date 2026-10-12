import { render, screen } from '@testing-library/react-native'

import { EnvironmentBanner } from './EnvironmentBanner'

let mockVariant: string | undefined
jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { get expoConfig() { return { extra: { appVariant: mockVariant } } } },
}))

describe('EnvironmentBanner', () => {
  it('en la variante staging muestra siempre la cinta STAGING', async () => {
    mockVariant = 'staging'
    await render(<EnvironmentBanner />)
    expect(screen.getByText('STAGING · AMBIENTE DE PRUEBAS')).toBeTruthy()
  })

  it('en producción y en desarrollo no muestra nada', async () => {
    for (const v of ['production', 'development', undefined]) {
      mockVariant = v
      const { toJSON, unmount } = await render(<EnvironmentBanner />)
      expect(toJSON()).toBeNull()
      await unmount()
    }
  })
})
