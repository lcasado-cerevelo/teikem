// Preferencias del aparato (no del servidor): «No confiar en esta computadora» (se RECUERDA para que el login la muestre marcada la próxima vez) y «Abrir con huella».
import { StoreKeys, storeGet, storeSet } from '../storage'

async function getFlag(key: string): Promise<boolean> {
  return (await storeGet(key)) === '1'
}

export const getNoTrust = () => getFlag(StoreKeys.noTrust)
export const setNoTrust = (value: boolean) => storeSet(StoreKeys.noTrust, value ? '1' : '0')
export const getBiometricEnabled = () => getFlag(StoreKeys.biometric)
export const setBiometricEnabled = (value: boolean) => storeSet(StoreKeys.biometric, value ? '1' : '0')
