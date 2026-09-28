import { act, render } from '@testing-library/react-native'
import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../db/database'
import { __resetLangForTests, setLang } from '../i18n/i18n'
import { BrandLockup } from './BrandLockup'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetLangForTests()
})

// @testing-library/react-native v14: render() es async (React 19).
describe('BrandLockup', () => {
  it('muestra el lockup del idioma activo y lo cambia sin recargar', async () => {
    const { getByLabelText, queryByTestId } = await render(<BrandLockup />)
    expect(getByLabelText('Teikem')).toBeTruthy()
    expect(queryByTestId('brand-lockup-es')).toBeTruthy()

    await act(async () => setLang('en'))
    expect(queryByTestId('brand-lockup-en')).toBeTruthy()
    expect(queryByTestId('brand-lockup-es')).toBeNull()
  })
})
