import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

// Almacén de la sesión.
//  - Nativo: expo-secure-store (Keychain / Keystore).
//  - Web: localStorage (SecureStore no existe en la web).
//
// Se guarda lo mínimo: el refresh token con el vencimiento de la sesión y los datos para mostrar el nombre sin conexión (los tokens de acceso van en memoria).
// OJO: SecureStore en Android limita el valor a unos 2 KB; por eso lo guardado es chico.

const KEY = 'teikem.choferes.session';

export interface AuthUser {
  userId: number;
  fullName?: string | null;
  email?: string | null;
}

export interface StoredSession {
  /** El refresh token VIGENTE de la sesión (se rota en cada renovación; el anterior queda revocado). Los tokens de acceso NO se guardan: viven en memoria. */
  refreshToken: string;
  /** Vencimiento ABSOLUTO de la sesión (ms desde 1970): la fija la compañía (la más permisiva si el chofer trabaja en varias); renovar no la alarga. */
  refreshExpiresAt: number;
  /** Para mostrar el nombre y las iniciales aunque no haya señal. */
  user?: AuthUser;
  /** Compañías donde puede usar la app (para mostrarlas sin conexión). */
  companies?: CompanyAccess[];
  /** Lote 213: usuario «solo chofer»: solo ve lo del chofer aunque tuviera otros permisos. */
  driverOnly?: boolean;
}

export interface CompanyAccess {
  tenantId: number;
  name: string;
  /** mfa_required: la compañía exige MFA y esta sesión no lo cumplió (no hay acceso hasta entrar de nuevo con contraseña y código). */
  blocked?: string | null;
  /** Lote 213: permisos efectivos en esa compañía (deciden qué muestra la app) y si allí es chofer activo. */
  permissions?: string[];
  isDriver?: boolean;
}

async function setItem(value: string): Promise<void> {
  if (Platform.OS === 'web') {
    localStorage.setItem(KEY, value);
    return;
  }
  await SecureStore.setItemAsync(KEY, value);
}

async function getItem(): Promise<string | null> {
  if (Platform.OS === 'web') return localStorage.getItem(KEY);
  return SecureStore.getItemAsync(KEY);
}

async function removeItem(): Promise<void> {
  if (Platform.OS === 'web') {
    localStorage.removeItem(KEY);
    return;
  }
  await SecureStore.deleteItemAsync(KEY);
}

export const sessionStorage = {
  async save(session: StoredSession): Promise<void> {
    await setItem(JSON.stringify(session));
  },
  async load(): Promise<StoredSession | null> {
    const raw = await getItem();
    if (!raw) return null;
    try {
      const s = JSON.parse(raw) as StoredSession;
      return s.refreshToken && s.refreshExpiresAt ? s : null;
    } catch {
      return null;
    }
  },
  async clear(): Promise<void> {
    await removeItem();
  },
};
