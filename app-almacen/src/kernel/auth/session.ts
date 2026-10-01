// Lote 8A-app — identidad del aparato (registro, `UserDevice`) y sesión del usuario que lo tiene en mano ahora mismo.
// Ambas viven en expo-secure-store (cifrado por el sistema; sobrevive a cerrar la app, se borra al desinstalar). El PIN
// del usuario nunca se guarda aquí: se manda al servidor en cada /auth/device/login y el servidor responde con tokens.
//
// 2026-09-30 (varias compañías): el teléfono guarda VARIOS registros, uno por compañía, cada uno con su propia base SQLite
// (`dbName`). `device` es el registro con el que se trabaja ahora; con más de uno, al salir el usuario se vuelve a elegir.
import * as SecureStore from 'expo-secure-store'

export interface DeviceIdentity {
  devicePublicId: string
  deviceSecret: string
  tenantName: string
  defaultWarehousePublicId: string | null
  theme: string | null
  /** Lote 16: modo de recepción del almacén por defecto ('PUTAWAY' | 'DIRECT'), del registro y de cada heartbeat.
   *  Opcional: un aparato registrado antes del lote no lo tiene guardado hasta su siguiente heartbeat. */
  defaultWarehouseReceivingMode?: string | null
  /** Archivo SQLite de esta compañía. Sin valor = la base única de antes de 2026-09-30 (`teikem_almacen.db`). */
  dbName?: string
}

export interface UserSession {
  accessToken: string
  accessExpiresAtUtc: string
  refreshToken: string
  refreshExpiresAtUtc: string
  tenantId: number
  userId: number
  fullName: string
}

interface State {
  /** Todos los registros del teléfono (uno por compañía). */
  devices: DeviceIdentity[]
  /** Registro activo (null con varios registros y ninguno elegido todavía). */
  device: DeviceIdentity | null
  session: UserSession | null
  hydrated: boolean
}

const KEYS = {
  devices: 'teikem.device.identities',
  active: 'teikem.device.active',
  /** Registro único de antes de 2026-09-30: se convierte en el primer elemento de la lista al arrancar. */
  legacyDevice: 'teikem.device.identity',
  session: 'teikem.user.session',
}

let state: State = { devices: [], device: null, session: null, hydrated: false }
const listeners = new Set<() => void>()
function emit(): void {
  listeners.forEach((l) => l())
}

export function subscribeSession(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function getSessionState(): State {
  return state
}

/** Nombre del archivo SQLite para un registro nuevo. */
export function dbNameFor(devicePublicId: string): string {
  return `teikem_almacen_${devicePublicId.replace(/[^0-9a-zA-Z]/g, '').slice(0, 32)}.db`
}

async function persist(devices: DeviceIdentity[], activeId: string | null): Promise<void> {
  await SecureStore.setItemAsync(KEYS.devices, JSON.stringify(devices))
  if (activeId) await SecureStore.setItemAsync(KEYS.active, activeId)
  else await SecureStore.deleteItemAsync(KEYS.active)
}

function pickActive(devices: DeviceIdentity[], activeId: string | null): DeviceIdentity | null {
  return devices.find((d) => d.devicePublicId === activeId) ?? (devices.length === 1 ? devices[0] : null)
}

/** Carga los registros y la sesión guardados. Se llama una vez al arrancar. */
export async function hydrateSession(): Promise<void> {
  const [devicesRaw, activeId, legacyRaw, sessionRaw] = await Promise.all([
    SecureStore.getItemAsync(KEYS.devices),
    SecureStore.getItemAsync(KEYS.active),
    SecureStore.getItemAsync(KEYS.legacyDevice),
    SecureStore.getItemAsync(KEYS.session),
  ])
  let devices = devicesRaw ? (JSON.parse(devicesRaw) as DeviceIdentity[]) : []
  let active = activeId
  if (legacyRaw) {
    // Instalación de antes: su registro y su base (sin dbName) pasan a ser el primero de la lista.
    const legacy = JSON.parse(legacyRaw) as DeviceIdentity
    if (!devices.some((d) => d.devicePublicId === legacy.devicePublicId)) devices = [legacy, ...devices]
    active = active ?? legacy.devicePublicId
    await persist(devices, active)
    await SecureStore.deleteItemAsync(KEYS.legacyDevice)
  }
  const device = pickActive(devices, active)
  state = {
    devices,
    device,
    // Una sesión sin registro activo no sirve (otra compañía o ninguna elegida).
    session: device && sessionRaw ? (JSON.parse(sessionRaw) as UserSession) : null,
    hydrated: true,
  }
  emit()
}

/** Registro nuevo (otra compañía): se agrega a la lista y pasa a ser el activo, sin usuario todavía. */
export async function addDeviceIdentity(device: DeviceIdentity): Promise<void> {
  const devices = [...state.devices.filter((d) => d.devicePublicId !== device.devicePublicId), device]
  await persist(devices, device.devicePublicId)
  await SecureStore.deleteItemAsync(KEYS.session)
  state = { ...state, devices, device, session: null }
  emit()
}

/** Cambia datos del registro activo (heartbeat: almacén, tema, modo de recepción) sin tocar la sesión. */
export async function updateDeviceIdentity(patch: Partial<DeviceIdentity>): Promise<void> {
  const current = state.device
  if (!current) return
  const device = { ...current, ...patch }
  const devices = state.devices.map((d) => (d.devicePublicId === current.devicePublicId ? device : d))
  await persist(devices, device.devicePublicId)
  state = { ...state, devices, device }
  emit()
}

/** Elige con qué compañía (registro) se trabaja; la sesión de usuario anterior se cierra. */
export async function selectDevice(devicePublicId: string | null): Promise<void> {
  const device = devicePublicId ? (state.devices.find((d) => d.devicePublicId === devicePublicId) ?? null) : null
  await persist(state.devices, device?.devicePublicId ?? null)
  await SecureStore.deleteItemAsync(KEYS.session)
  state = { ...state, device, session: null }
  emit()
}

/** Da de baja en este teléfono el registro ACTIVO (el servidor lo desactivó): los demás registros siguen. Su base SQLite
 *  no se borra aquí (puede tener pendientes); se pierde solo al desinstalar. */
export async function clearDeviceIdentity(): Promise<void> {
  const current = state.device
  const devices = current ? state.devices.filter((d) => d.devicePublicId !== current.devicePublicId) : state.devices
  const device = devices.length === 1 ? devices[0] : null
  await persist(devices, device?.devicePublicId ?? null)
  await SecureStore.deleteItemAsync(KEYS.session)
  state = { ...state, devices, device, session: null }
  emit()
}

export async function saveUserSession(session: UserSession): Promise<void> {
  await SecureStore.setItemAsync(KEYS.session, JSON.stringify(session))
  state = { ...state, session }
  emit()
}

/** Cambiar de usuario: el aparato sigue registrado, solo se pide PIN de nuevo. Con varias compañías, el siguiente usuario
 *  vuelve a elegir la suya. */
export async function clearUserSession(): Promise<void> {
  await SecureStore.deleteItemAsync(KEYS.session)
  if (state.devices.length > 1) {
    await persist(state.devices, null)
    state = { ...state, device: null, session: null }
  } else {
    state = { ...state, session: null }
  }
  emit()
}

export function __resetSessionForTests(): void {
  state = { devices: [], device: null, session: null, hydrated: false }
  listeners.clear()
}

/** Nombre de antes de 2026-09-30 (un solo registro); hoy agrega el registro como `addDeviceIdentity`. */
export const saveDeviceIdentity = addDeviceIdentity
