// Códigos de recuperación del portal: se pueden copiar, el archivo trae la cuenta y los códigos, y la casilla «Ya guardé» avisa al que la usa.
jest.mock('expo-clipboard', () => ({ setStringAsync: jest.fn().mockResolvedValue(true) }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import * as Clipboard from 'expo-clipboard'

import { setLang } from '../i18n/i18n'
import { RecoveryCodes, recoveryCodesFile } from './RecoveryCodes'

const CODES = ['aaaa-1111', 'bbbb-2222']

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('RecoveryCodes', () => {
  it('el archivo trae el encabezado con la cuenta, la fecha y un código por línea', () => {
    const text = recoveryCodesFile(CODES, 'ana@cliente.co', new Date('2026-10-22T10:00:00Z'))
    expect(text.split('\r\n')).toEqual(['Teikem — códigos de recuperación (ana@cliente.co)', 'Generados: 2026-10-22', '', 'aaaa-1111', 'bbbb-2222', ''])
  })

  it('muestra los códigos, copia los códigos al portapapeles y avisa', async () => {
    await render(<RecoveryCodes codes={CODES} saved={false} onSavedChange={jest.fn()} />)
    expect(screen.getByText('aaaa-1111')).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Copiar los códigos' }))
    await waitFor(() => expect(Clipboard.setStringAsync).toHaveBeenCalledWith('aaaa-1111\nbbbb-2222'))
    expect(await screen.findByText('Códigos copiados.')).toBeTruthy()
  })

  it('la casilla «Ya guardé» le avisa al que la usa', async () => {
    const onSaved = jest.fn()
    await render(<RecoveryCodes codes={CODES} saved={false} onSavedChange={onSaved} />)
    await fireEvent.press(screen.getByRole('checkbox', { name: 'Ya guardé estos códigos en un lugar seguro' }))
    expect(onSaved).toHaveBeenCalledWith(true)
  })
})
