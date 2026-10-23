import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

// Almacén de la sesión.
//  - Nativo: expo-secure-store (Keychain / Keystore).
//  - Web: localStorage (SecureStore no existe en la web).
//
// Se guarda lo mínimo: el par de tokens con sus vencimientos, la compañía activa y los datos para mostrar el nombre sin conexión.
// OJO: SecureStore en Android limita el valor a unos 2 KB; por eso el perfil guardado es chico.

const KEY = 'teikem.choferes.session';

export interface AuthUser {
  userId: number;
  fullName?: string | null;
  email?: string | null;
  tenantId: number;
  tenantName?: string | null;
  /** Compañías a las que pertenece (para cambiar de compañía). */
  memberships: { tenantId: number; tenantName: string }[];
}

export interface StoredSession {
  accessToken: string;
  /** Vencimiento del token de acceso (ms desde 1970). */
  expiresAt: number;
  refreshToken: string;
  /** Vencimiento de la SESIÓN (la fija la compañía: días o minutos); pasado este momento hay que entrar de nuevo. */
  refreshExpiresAt: number;
  tenantId: number;
  /** Para mostrar el nombre y las iniciales aunque no haya señal. */
  user?: AuthUser;
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
      return s.accessToken && s.refreshToken ? s : null;
    } catch {
      return null;
    }
  },
  async clear(): Promise<void> {
    await removeItem();
  },
};
