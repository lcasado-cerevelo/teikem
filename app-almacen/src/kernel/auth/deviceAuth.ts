// Lote 8A-app — auth del aparato y del usuario que lo tiene en mano (docs/mobile/app-almacen-plan.md §2 pantalla 1).
import Constants from 'expo-constants'
import { Platform } from 'react-native'

import { api, unwrap } from '../api/client'
import { refreshTenantFormat } from '../format/tenantFormatApi'
import { removeCompany } from './companies'
import { addDeviceIdentity, dbNameFor, getSessionState, saveUserSession, updateDeviceIdentity } from './session'

const APP_VERSION = String(Constants.expoConfig?.version ?? '1.0.0')

/** Registra este aparato en una compañía con el código de un solo uso que crea el administrador en la web. Si el teléfono ya
 *  tiene registros, los manda para que el servidor rechace (409, sin gastar el código) uno de una compañía ya registrada. */
export async function enrollDevice(enrollCode: string): Promise<void> {
  const registered = getSessionState().devices.map((d) => d.devicePublicId)
  const enrolled = await unwrap(
    api.POST('/api/v1/devices/enroll', {
      body: {
        enrollCode: enrollCode.trim(),
        model: Platform.OS === 'android' ? 'Android' : Platform.OS,
        appVersion: APP_VERSION,
        registeredDevicePublicIds: registered.length > 0 ? registered : null,
      },
    }),
  )
  if (!enrolled.devicePublicId || !enrolled.deviceSecret) throw new Error('Respuesta de registro incompleta.')
  await addDeviceIdentity({
    dbName: dbNameFor(enrolled.devicePublicId),
    devicePublicId: enrolled.devicePublicId,
    deviceSecret: enrolled.deviceSecret,
    tenantName: enrolled.tenantName ?? '',
    defaultWarehousePublicId: enrolled.defaultWarehousePublicId ?? null,
    theme: enrolled.theme ?? null,
    defaultWarehouseReceivingMode: enrolled.defaultWarehouseReceivingMode ?? null,
  })
}

export interface DeviceUser {
  userId: number
  fullName: string
  initials: string
}

/** Lista de usuarios con acceso a este aparato (secreto del aparato, sin PIN todavía). */
export async function fetchDeviceUsers(devicePublicId: string, deviceSecret: string): Promise<DeviceUser[]> {
  const rows = await unwrap(api.POST('/api/v1/auth/device/users', { body: { devicePublicId, deviceSecret } }))
  return rows.map((r) => ({ userId: r.userId ?? 0, fullName: r.fullName ?? '', initials: r.initials ?? '' }))
}

/** Entra con el usuario elegido y su PIN: guarda la sesión (access + refresh) si el PIN es correcto. */
export async function loginWithPin(devicePublicId: string, deviceSecret: string, userId: number, pin: string, fullName: string): Promise<void> {
  const tokens = await unwrap(api.POST('/api/v1/auth/device/login', { body: { devicePublicId, deviceSecret, userId, pin: pin.trim() } }))
  if (!tokens.accessToken || !tokens.refreshToken) throw new Error('Respuesta de entrada incompleta.')
  await saveUserSession({
    accessToken: tokens.accessToken,
    accessExpiresAtUtc: tokens.accessExpiresAtUtc ?? '',
    refreshToken: tokens.refreshToken,
    refreshExpiresAtUtc: tokens.refreshExpiresAtUtc ?? '',
    tenantId: tokens.tenantId ?? 0,
    userId,
    fullName,
  })
  // Región y formatos de la compañía para esta sesión (se guardan en la base local); sin esperar: la entrada no se
  // detiene por esto y, si falla, se usan los guardados o los de Puerto Rico.
  void refreshTenantFormat()
}

/** Aviso periódico de vida (docs/mobile/app-almacen-plan.md §1): actualiza el almacén, el tema y (Lote 16) el modo de
 *  recepción del almacén por defecto guardados. Lo llama el motor de sincronización en cada pasada con señal
 *  (kernel/sync/engine.ts); solo reescribe la identidad si algo cambió. */
export async function sendHeartbeat(): Promise<{ isActive: boolean }> {
  const device = getSessionState().device
  if (!device) return { isActive: false }
  try {
    const beat = await unwrap(
      api.POST('/api/v1/devices/heartbeat', {
        body: { devicePublicId: device.devicePublicId, deviceSecret: device.deviceSecret, appVersion: APP_VERSION },
      }),
    )
    if (beat.isActive === false) {
      await removeCompany(device)
      return { isActive: false }
    }
    const next = {
      defaultWarehousePublicId: beat.defaultWarehousePublicId ?? device.defaultWarehousePublicId,
      theme: beat.theme ?? device.theme,
      defaultWarehouseReceivingMode: beat.defaultWarehouseReceivingMode ?? device.defaultWarehouseReceivingMode ?? null,
    }
    if (
      next.defaultWarehousePublicId !== device.defaultWarehousePublicId ||
      next.theme !== device.theme ||
      next.defaultWarehouseReceivingMode !== (device.defaultWarehouseReceivingMode ?? null)
    ) {
      // Si la web cambió el almacén por defecto, manda la web: se descarta el almacén que se había elegido en el aparato.
      const defaultChanged = next.defaultWarehousePublicId !== device.defaultWarehousePublicId
      await updateDeviceIdentity(defaultChanged ? { ...next, selectedWarehouse: null } : next)
    }
    return { isActive: true }
  } catch {
    // Sin red: el aparato sigue activo hasta que se demuestre lo contrario (no bloquea el trabajo sin señal).
    return { isActive: true }
  }
}
