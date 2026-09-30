// Lote 8A-app — identidad del aparato (registro, `UserDevice`) y sesión del usuario que lo tiene en mano ahora mismo.
// Ambas viven en expo-secure-store (cifrado por el sistema; sobrevive a cerrar la app, se borra al desinstalar). El PIN
// del usuario nunca se guarda aquí: se manda al servidor en cada /auth/device/login y el servidor responde con tokens.
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
  device: DeviceIdentity | null
  session: UserSession | null
  hydrated: boolean
}

const KEYS = { device: 'teikem.device.identity', session: 'teikem.user.session' }

let state: State = { device: null, session: null, hydrated: false }
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

/** Carga el aparato y la sesión guardados. Se llama una vez al arrancar (kernel/nav/RootGate.tsx). */
export async function hydrateSession(): Promise<void> {
  const [deviceRaw, sessionRaw] = await Promise.all([
    SecureStore.getItemAsync(KEYS.device),
    SecureStore.getItemAsync(KEYS.session),
  ])
  state = {
    device: deviceRaw ? (JSON.parse(deviceRaw) as DeviceIdentity) : null,
    session: sessionRaw ? (JSON.parse(sessionRaw) as UserSession) : null,
    hydrated: true,
  }
  emit()
}

export async function saveDeviceIdentity(device: DeviceIdentity): Promise<void> {
  await SecureStore.setItemAsync(KEYS.device, JSON.stringify(device))
  state = { ...state, device }
  emit()
}

export async function updateDeviceIdentity(patch: Partial<DeviceIdentity>): Promise<void> {
  if (!state.device) return
  await saveDeviceIdentity({ ...state.device, ...patch })
}

/** Da de baja el aparato en este teléfono (código robado o error de registro): borra aparato y sesión de usuario. */
export async function clearDeviceIdentity(): Promise<void> {
  await SecureStore.deleteItemAsync(KEYS.device)
  await SecureStore.deleteItemAsync(KEYS.session)
  state = { ...state, device: null, session: null }
  emit()
}

export async function saveUserSession(session: UserSession): Promise<void> {
  await SecureStore.setItemAsync(KEYS.session, JSON.stringify(session))
  state = { ...state, session }
  emit()
}

/** Cambiar de usuario: el aparato sigue registrado, solo se pide PIN de nuevo. */
export async function clearUserSession(): Promise<void> {
  await SecureStore.deleteItemAsync(KEYS.session)
  state = { ...state, session: null }
  emit()
}

export function __resetSessionForTests(): void {
  state = { device: null, session: null, hydrated: false }
  listeners.clear()
}
