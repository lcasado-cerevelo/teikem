import { __resetSecureStoreForTests } from 'expo-secure-store'

import { checkOfflinePin, clearOfflinePinVerifier, OFFLINE_MAX_FAILS, saveOfflinePinVerifier, sha256 } from './pinVerifier'

const hex = (b: Uint8Array) => Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('')
const bytes = (s: string) => new TextEncoder().encode(s)

beforeEach(() => __resetSecureStoreForTests())

describe('sha256', () => {
  it('coincide con los vectores conocidos', () => {
    expect(hex(sha256(bytes('')))).toBe('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855')
    expect(hex(sha256(bytes('abc')))).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')
    expect(hex(sha256(bytes('abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq')))).toBe(
      '248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1',
    )
  })
})

describe('PIN sin señal', () => {
  it('sin huella guardada no se puede desbloquear sin señal', async () => {
    expect(await checkOfflinePin('dev', 7, '2846')).toBe('none')
  })

  it('acepta el PIN correcto y rechaza el incorrecto', async () => {
    await saveOfflinePinVerifier('dev', 7, '2846')
    expect(await checkOfflinePin('dev', 7, '1111')).toBe('wrong')
    expect(await checkOfflinePin('dev', 7, '2846')).toBe('ok')
  })

  it('se bloquea tras los intentos fallidos aunque luego se escriba bien', async () => {
    await saveOfflinePinVerifier('dev', 7, '2846')
    for (let i = 0; i < OFFLINE_MAX_FAILS; i++) await checkOfflinePin('dev', 7, '0000')
    expect(await checkOfflinePin('dev', 7, '2846')).toBe('blocked')
  })

  it('un acierto reinicia el contador y borrar la huella la quita', async () => {
    await saveOfflinePinVerifier('dev', 7, '2846')
    await checkOfflinePin('dev', 7, '0000')
    await checkOfflinePin('dev', 7, '2846')
    for (let i = 0; i < OFFLINE_MAX_FAILS - 1; i++) expect(await checkOfflinePin('dev', 7, '0000')).toBe('wrong')
    await clearOfflinePinVerifier('dev', 7)
    expect(await checkOfflinePin('dev', 7, '2846')).toBe('none')
  })
})
