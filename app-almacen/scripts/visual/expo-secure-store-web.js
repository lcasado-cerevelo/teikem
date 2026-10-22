// Sustituto de expo-secure-store SOLO para la verificación visual en el navegador: guarda en localStorage (lo que el aparato guarda en el almacén seguro del sistema).
export async function getItemAsync(key) { return globalThis.localStorage.getItem(key) }
export async function setItemAsync(key, value) { globalThis.localStorage.setItem(key, value) }
export async function deleteItemAsync(key) { globalThis.localStorage.removeItem(key) }
export function getItem(key) { return globalThis.localStorage.getItem(key) }
export function setItem(key, value) { globalThis.localStorage.setItem(key, value) }
export async function isAvailableAsync() { return true }
export const WHEN_UNLOCKED = 0
export const WHEN_UNLOCKED_THIS_DEVICE_ONLY = 1
export const AFTER_FIRST_UNLOCK = 2
